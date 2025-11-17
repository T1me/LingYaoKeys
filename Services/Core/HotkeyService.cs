using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;
using System.ComponentModel;
using WpfApp.ViewModels;
using WpfApp.Services.Utils;
using WpfApp.Services.Models;

namespace WpfApp.Services.Core;

#region 接口定义

/// <summary>
/// 热键服务接口
/// </summary>
public interface IHotkeyService : IDisposable
{
    bool IsHotkeyControlEnabled { get; set; }
    bool IsInputFocused { get; set; }
    bool IsTargetWindowActive { get; set; }

    event Action? StartHotkeyPressed;
    event Action? StartHotkeyReleased;
    event Action? SequenceModeStarted;
    event Action? SequenceModeStopped;
    event Action<VirtualKeyCode>? KeyTriggered;

    bool RegisterHotkey(VirtualKeyCode keyCode, ModifierKeys modifiers, bool saveToConfig = true);
    void UnregisterHotkey(VirtualKeyCode keyCode, ModifierKeys modifiers);
    void StartSequence();
    void StopSequence();
    void SetKeySequence(List<KeyItemSettings> keySettings);
    void SetTargetWindows(IEnumerable<IntPtr> handles);
    bool IsMouseButton(VirtualKeyCode keyCode);
}

#endregion

/// <summary>
/// 热键服务实现
/// </summary>
public class HotkeyService : IHotkeyService, IDisposable
{
    #region 字段和属性

    private readonly HookManager _hookManager;
    private readonly WindowValidator _windowValidator;
    private readonly KeySequenceExecutor _executor;
    private readonly LyKeysService _lyKeysService;
    private readonly ISerilogManager _logger;
    private readonly IConfigManager _configManager;
    private readonly MainViewModel _mainViewModel;
    private readonly Window _mainWindow;

    private List<KeyItemSettings> _keySettings = new();
    private int _hotkeyVirtualKey;
    private VirtualKeyCode? _pendingHotkey;
    private bool _isKeyHeld;
    private bool _isInputFocused;
    private bool _isHotkeyControlEnabled = true;
    private bool _isRegisteringHotkey = false;

    public event Action? StartHotkeyPressed;
    public event Action? StartHotkeyReleased;
    public event Action? SequenceModeStarted;
    public event Action? SequenceModeStopped;
    public event Action<VirtualKeyCode>? KeyTriggered;

    public bool IsHotkeyControlEnabled
    {
        get => _isHotkeyControlEnabled;
        set
        {
            if (_isHotkeyControlEnabled != value)
            {
                _isHotkeyControlEnabled = value;
                if (!value && _executor.IsRunning)
                {
                    _executor.EmergencyStop();
                    StopSequence();
                }
            }
        }
    }

    public bool IsInputFocused
    {
        get => _isInputFocused;
        set => _isInputFocused = value;
    }

    public bool IsTargetWindowActive
    {
        get => _windowValidator.IsTargetWindowActive;
        set => _windowValidator.IsTargetWindowActive = value;
    }

    #endregion

    #region 构造函数和初始化

    public HotkeyService(ISerilogManager logger, Window mainWindow, KeySequenceExecutor executor, LyKeysService lyKeysService, IConfigManager configManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _lyKeysService = lyKeysService ?? throw new ArgumentNullException(nameof(lyKeysService));
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _mainViewModel = mainWindow.DataContext as MainViewModel ??
                         throw new ArgumentException("Window.DataContext must be of type MainViewModel", nameof(mainWindow));

        // 初始化组件
        _hookManager = new HookManager(logger);
        _windowValidator = new WindowValidator(logger);

        // 订阅钩子事件
        _hookManager.KeyboardEvent += OnKeyboardEvent;
        _hookManager.MouseButtonEvent += OnMouseButtonEvent;
        _hookManager.MouseWheelEvent += OnMouseWheelEvent;

        // 订阅配置变更事件
        _configManager.ConfigChanged += OnConfigChanged;
        _mainWindow.Closed += (s, e) => Dispose();

        // 加载初始状态
        LoadInitialState();

        // 安装钩子
        _hookManager.InstallHooks();
    }

    private void LoadInitialState()
    {
        try
        {
            _logger.Debug("HotkeyService 初始化完成，等待配置加载");
        }
        catch (Exception ex)
        {
            _logger.Error("加载初始状态失败", ex);
        }
    }

    #endregion

    #region 公共方法

    public bool RegisterHotkey(VirtualKeyCode keyCode, ModifierKeys modifiers, bool saveToConfig = true)
    {
        try
        {
            _isRegisteringHotkey = true;
            _hotkeyVirtualKey = (int)keyCode;
            _pendingHotkey = keyCode;

            if (saveToConfig)
            {
                SaveHotkeyConfig(keyCode, modifiers);
            }

            _isRegisteringHotkey = false;
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"注册热键失败: KeyCode={keyCode}, Modifiers={modifiers}", ex);
            _isRegisteringHotkey = false;
            ShowMessage($"热键注册失败: {ex.Message}", true);
            throw new InvalidOperationException($"无法注册热键 {keyCode}", ex);
        }
    }

    public void UnregisterHotkey(VirtualKeyCode keyCode, ModifierKeys modifiers)
    {
        try
        {
            if (_pendingHotkey == keyCode)
            {
                _pendingHotkey = null;
                _hotkeyVirtualKey = 0;
                _logger.Info($"已注销热键: {keyCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"注销热键失败: KeyCode={keyCode}, Modifiers={modifiers}", ex);
            throw;
        }
    }

    public void StartSequence()
    {
        try
        {
            if (_mainViewModel.KeyMappingViewModel?.IsCoordinateEditMode == true)
            {
                ShowMessage("坐标编辑模式下禁止触发热键", true);
                _logger.Debug("坐标编辑模式下禁止触发热键");
                return;
            }

            if (!_isRegisteringHotkey && !_windowValidator.CanTriggerHotkey(_isHotkeyControlEnabled, _isRegisteringHotkey))
                return;

            if (_keySettings == null || _keySettings.Count == 0)
            {
                _logger.Warning("按键列表为空，无法启动序列");
                ShowMessage("按键列表为空，无法启动序列，请至少添加一个按键或坐标", true);
                return;
            }

            SequenceModeStarted?.Invoke();

            var tempConfig = new KeyConfiguration("临时配置")
            {
                SoundEnabled = true,
                AutoSwitchToEnglishIME = true,
                IsReduceKeyStuck = true
            };

            _executor.Start(_keySettings, _lyKeysService.IsHoldMode, tempConfig, () =>
            {
                SequenceModeStopped?.Invoke();
            });
        }
        catch (Exception ex)
        {
            _logger.Error("启动序列时发生异常", ex);
            ShowMessage("启动序列失败，请检查日志", true);
        }
    }

    public void StopSequence()
    {
        _executor.Stop();
    }

    public void SetKeySequence(List<KeyItemSettings> keySettings)
    {
        try
        {
            if (keySettings == null)
                keySettings = new List<KeyItemSettings>();

            _keySettings = keySettings.ToList();
        }
        catch (Exception ex)
        {
            _logger.Error("设置按键序列失败", ex);
        }
    }

    public void SetTargetWindows(IEnumerable<IntPtr> handles)
    {
        _windowValidator.SetTargetWindows(handles);

        if (!_windowValidator.HasValidWindows() && _executor.IsRunning)
        {
            _executor.EmergencyStop();
            StopSequence();
        }
    }

    public bool IsMouseButton(VirtualKeyCode keyCode)
    {
        return keyCode == VirtualKeyCode.VK_LBUTTON ||
               keyCode == VirtualKeyCode.VK_RBUTTON ||
               keyCode == VirtualKeyCode.VK_MBUTTON ||
               keyCode == VirtualKeyCode.VK_XBUTTON1 ||
               keyCode == VirtualKeyCode.VK_XBUTTON2;
    }

    #endregion

    #region 事件处理

    private void OnKeyboardEvent(int vkCode, bool isDown)
    {
        if (_isInputFocused) return;

        try
        {
            var isHotkey = vkCode == _hotkeyVirtualKey;

            // 热键注册模式
            if (_isRegisteringHotkey && isHotkey)
            {
                if (isDown && !_isKeyHeld)
                {
                    _isKeyHeld = true;
                    StartHotkeyPressed?.Invoke();
                }
                else if (!isDown && _isKeyHeld)
                {
                    _isKeyHeld = false;
                    StartHotkeyReleased?.Invoke();
                }
                return;
            }

            // 窗口状态检查
            var windowState = _windowValidator.GetWindowState();
            if (_executor.IsRunning && !_windowValidator.IsWindowStateValid(windowState))
            {
                _executor.EmergencyStop();
                StopSequence();
                return;
            }

            // 热键触发逻辑
            if (isHotkey)
            {
                if (isDown && !_isKeyHeld && _windowValidator.CanTriggerHotkey(_isHotkeyControlEnabled, _isRegisteringHotkey))
                {
                    _isKeyHeld = true;
                    StartHotkeyPressed?.Invoke();

                    if (_lyKeysService.IsHoldMode)
                    {
                        StartSequence();
                    }
                    else
                    {
                        if (_executor.IsRunning) StopSequence();
                        else StartSequence();
                    }
                }
                else if (!isDown && _isKeyHeld)
                {
                    _isKeyHeld = false;
                    StartHotkeyReleased?.Invoke();

                    if (_lyKeysService.IsHoldMode)
                    {
                        StopSequence();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("键盘钩子回调异常", ex);
            _isKeyHeld = false;
            StopSequence();
        }
    }

    private void OnMouseButtonEvent(VirtualKeyCode button, bool isDown)
    {
        if (_isInputFocused) return;

        try
        {
            var isHotkey = button == _pendingHotkey;

            // 热键注册模式
            if (isHotkey && _isRegisteringHotkey)
            {
                if (isDown && !_isKeyHeld)
                {
                    _isKeyHeld = true;
                    StartHotkeyPressed?.Invoke();
                }
                else if (!isDown && _isKeyHeld)
                {
                    _isKeyHeld = false;
                    StartHotkeyReleased?.Invoke();
                }
                return;
            }

            // 窗口状态检查
            if (!_windowValidator.IsTargetWindowActive && _executor.IsRunning)
            {
                _executor.EmergencyStop();
                StopSequence();
                return;
            }

            // 热键触发逻辑
            if (isHotkey)
            {
                if (_lyKeysService.IsHoldMode)
                {
                    if (isDown && !_isKeyHeld)
                    {
                        _isKeyHeld = true;
                        StartHotkeyPressed?.Invoke();
                        StartSequence();
                    }
                    else if (!isDown && _isKeyHeld)
                    {
                        _isKeyHeld = false;
                        StartHotkeyReleased?.Invoke();
                        StopSequence();
                    }
                }
                else
                {
                    if (isDown && !_isKeyHeld)
                    {
                        _isKeyHeld = true;
                        StartHotkeyPressed?.Invoke();
                        if (_executor.IsRunning) StopSequence();
                        else StartSequence();
                    }
                    else if (!isDown && _isKeyHeld)
                    {
                        _isKeyHeld = false;
                        StartHotkeyReleased?.Invoke();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("鼠标钩子回调异常", ex);
            _isKeyHeld = false;
            StopSequence();
        }
    }

    private void OnMouseWheelEvent(VirtualKeyCode direction)
    {
        if (_isInputFocused) return;

        try
        {
            var isHotkey = direction == _pendingHotkey;

            if (isHotkey && _isRegisteringHotkey)
            {
                if (!_isKeyHeld)
                {
                    _isKeyHeld = true;
                    StartHotkeyPressed?.Invoke();
                    _isKeyHeld = false;
                    StartHotkeyReleased?.Invoke();
                }
                return;
            }

            if (isHotkey && !_isKeyHeld)
            {
                _isKeyHeld = true;
                StartHotkeyPressed?.Invoke();
                if (_executor.IsRunning) StopSequence();
                else StartSequence();
            }
        }
        catch (Exception ex)
        {
            _logger.Error("滚轮事件异常", ex);
        }
    }

    private void OnConfigChanged(object? sender, ConfigEventArgs e)
    {
        try
        {
            switch (e.ChangeType)
            {
                case ConfigChangeType.MultiKey:
                    if (e.MultiKeyConfigData != null)
                    {
                        var activeConfig = e.MultiKeyConfigData.GetActiveConfiguration();
                        if (activeConfig != null)
                            ReloadKeyConfiguration(activeConfig);
                    }
                    break;

                case ConfigChangeType.Global:
                    if (e.GlobalConfigData != null)
                        UpdateGlobalSettings(e.GlobalConfigData);
                    break;

                case ConfigChangeType.All:
                    if (e.GlobalConfigData != null)
                        UpdateGlobalSettings(e.GlobalConfigData);
                    if (e.MultiKeyConfigData != null)
                    {
                        var activeConfig = e.MultiKeyConfigData.GetActiveConfiguration();
                        if (activeConfig != null)
                            ReloadKeyConfiguration(activeConfig);
                    }
                    break;

                default:
                    _logger.Warning($"未处理的配置变更类型: {e.ChangeType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("处理配置变更事件时发生异常", ex);
        }
    }

    #endregion

    #region 私有方法

    private void SaveHotkeyConfig(VirtualKeyCode keyCode, ModifierKeys modifiers)
    {
        try
        {
            _configManager.UpdateMultiKeyConfig(multiConfig =>
            {
                var activeConfig = multiConfig.GetActiveConfiguration();
                if (activeConfig != null)
                {
                    activeConfig.StartKey = keyCode;
                    activeConfig.StartMods = modifiers;
                    activeConfig.StopKey = keyCode;
                    activeConfig.StopMods = modifiers;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"保存热键配置失败: KeyCode={keyCode}, Modifiers={modifiers}", ex);
            throw new InvalidOperationException("无法保存热键配置到文件", ex);
        }
    }

    /// <summary>
    /// 重新加载按键配置
    /// </summary>
    private void ReloadKeyConfiguration(KeyConfiguration keyConfig)
    {
        if (keyConfig == null)
        {
            _logger.Warning("按键配置为空，无法重新加载");
            return;
        }

        try
        {
            if (keyConfig.StartKey.HasValue)
            {
                RegisterHotkey(keyConfig.StartKey.Value, keyConfig.StartMods, saveToConfig: false);
            }
            else
            {
                _pendingHotkey = null;
                _hotkeyVirtualKey = 0;
            }

            if (keyConfig.Keys?.Count > 0)
            {
                var selectedItems = keyConfig.Keys.Where(k => k.IsSelected).ToList();
                if (selectedItems.Count > 0)
                {
                    var operations = new List<KeyItemSettings>();
                    foreach (var item in selectedItems)
                    {
                        if (item.Type == KeyItemType.Keyboard && item.Code.HasValue)
                        {
                            operations.Add(KeyItemSettings.CreateKeyboard(item.Code.Value, item.KeyInterval));
                        }
                        else if (item.Type == KeyItemType.Coordinates)
                        {
                            operations.Add(KeyItemSettings.CreateCoordinates(item.X.Value, item.Y.Value, item.KeyInterval));
                        }
                    }
                    SetKeySequence(operations);
                }
                else
                {
                    SetKeySequence(new List<KeyItemSettings>());
                }
            }
            else
            {
                SetKeySequence(new List<KeyItemSettings>());
            }

            _lyKeysService.IsHoldMode = keyConfig.ExecutionMode == KeyExecutionMode.Hold;
        }
        catch (Exception ex)
        {
            _logger.Error("重新加载热键配置失败", ex);
            throw;
        }
    }

    private void UpdateGlobalSettings(GlobalConfig globalConfig)
    {
        if (globalConfig == null)
        {
            _logger.Warning("全局配置为空，无法更新全局设置");
            return;
        }

        try
        {
            var previousState = _isHotkeyControlEnabled;
            _isHotkeyControlEnabled = globalConfig.isHotkeyControlEnabled ?? true;

            if (previousState != _isHotkeyControlEnabled)
            {
                if (!_isHotkeyControlEnabled && _executor.IsRunning)
                {
                    _executor.EmergencyStop();
                    StopSequence();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("更新全局设置失败", ex);
            throw;
        }
    }

    private void ShowMessage(string message, bool isError = false)
    {
        _mainViewModel.UpdateStatusMessage(message, isError);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        try
        {
            StopSequence();
        }
        catch (Exception ex)
        {
            _logger.Error("停止序列失败", ex);
        }

        try
        {
            _configManager.ConfigChanged -= OnConfigChanged;
        }
        catch (Exception ex)
        {
            _logger.Error("移除事件订阅失败", ex);
        }

        try
        {
            _hookManager?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Error("释放钩子管理器失败", ex);
        }
    }

    #endregion

    #region 私有嵌套类：HookManager

    /// <summary>
    /// Win32 钩子管理器
    /// </summary>
    private class HookManager : IDisposable
    {
        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP = 0x020C;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEWHEEL = 0x020A;

        private IntPtr _keyboardHookHandle;
        private IntPtr _mouseHookHandle;
        private readonly HookProc _keyboardProcDelegate;
        private readonly HookProc _mouseProcDelegate;
        private readonly ISerilogManager _logger;
        private readonly object _hookLock = new();

        public event Action<int, bool>? KeyboardEvent;
        public event Action<VirtualKeyCode, bool>? MouseButtonEvent;
        public event Action<VirtualKeyCode>? MouseWheelEvent;

        public HookManager(ISerilogManager logger)
        {
            _logger = logger;
            _keyboardProcDelegate = KeyboardHookCallback;
            _mouseProcDelegate = MouseHookCallback;
        }

        public void InstallHooks()
        {
            lock (_hookLock)
            {
                try
                {
                    using (var curProcess = Process.GetCurrentProcess())
                    using (var curModule = curProcess.MainModule!)
                    {
                        var hModule = GetModuleHandle(curModule.ModuleName);

                        _keyboardHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProcDelegate, hModule, 0);
                        _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseProcDelegate, hModule, 0);

                        if (_keyboardHookHandle == IntPtr.Zero || _mouseHookHandle == IntPtr.Zero)
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("安装钩子失败", ex);
                    throw;
                }
            }
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    var hookStruct = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT))!;
                    var wParamInt = (int)wParam;
                    var isDown = wParamInt == WM_KEYDOWN || wParamInt == WM_SYSKEYDOWN;

                    KeyboardEvent?.Invoke((int)hookStruct.vkCode, isDown);
                }
                catch (Exception ex)
                {
                    _logger.Error("键盘钩子回调异常", ex);
                }
            }

            return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    var hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT))!;
                    var wParamInt = (int)wParam;

                    switch (wParamInt)
                    {
                        case WM_XBUTTONDOWN:
                        case WM_XBUTTONUP:
                            var xButton = (int)((hookStruct.mouseData >> 16) & 0xFFFF);
                            var xButtonCode = xButton == 1 ? VirtualKeyCode.VK_XBUTTON1 : VirtualKeyCode.VK_XBUTTON2;
                            MouseButtonEvent?.Invoke(xButtonCode, wParamInt == WM_XBUTTONDOWN);
                            break;

                        case WM_MBUTTONDOWN:
                        case WM_MBUTTONUP:
                            MouseButtonEvent?.Invoke(VirtualKeyCode.VK_MBUTTON, wParamInt == WM_MBUTTONDOWN);
                            break;

                        case WM_MOUSEWHEEL:
                            var wheelDelta = (short)((hookStruct.mouseData >> 16) & 0xFFFF);
                            MouseWheelEvent?.Invoke(wheelDelta > 0 ? VirtualKeyCode.VK_WHEELUP : VirtualKeyCode.VK_WHEELDOWN);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("鼠标钩子回调异常", ex);
                }
            }

            return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            lock (_hookLock)
            {
                if (_keyboardHookHandle != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_keyboardHookHandle);
                    _keyboardHookHandle = IntPtr.Zero;
                }

                if (_mouseHookHandle != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_mouseHookHandle);
                    _mouseHookHandle = IntPtr.Zero;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }
    }

    #endregion

    #region 私有嵌套类：WindowValidator

    /// <summary>
    /// 窗口验证器
    /// </summary>
    private class WindowValidator
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        public enum WindowState
        {
            NoTargetWindow,
            WindowInvalid,
            WindowInactive,
            WindowActive
        }

        private HashSet<IntPtr> _targetWindowHandles = new();
        private readonly ISerilogManager _logger;

        public bool IsTargetWindowActive { get; set; }

        public WindowValidator(ISerilogManager logger)
        {
            _logger = logger;
        }

        public void SetTargetWindows(IEnumerable<IntPtr> handles)
        {
            _targetWindowHandles = new HashSet<IntPtr>(handles.Where(h => h != IntPtr.Zero));
        }

        public bool HasValidWindows()
        {
            return _targetWindowHandles.Count > 0;
        }

        public WindowState GetWindowState()
        {
            try
            {
                if (_targetWindowHandles.Count == 0)
                    return WindowState.NoTargetWindow;

                var activeWindow = GetForegroundWindow();
                if (_targetWindowHandles.Contains(activeWindow))
                    return WindowState.WindowActive;

                bool anyValid = _targetWindowHandles.Any(h => IsWindow(h));
                if (!anyValid)
                    return WindowState.WindowInvalid;

                return WindowState.WindowInactive;
            }
            catch (Exception ex)
            {
                _logger.Error("检查窗口状态时发生异常", ex);
                return WindowState.WindowInvalid;
            }
        }

        public bool IsWindowStateValid(WindowState state)
        {
            return state == WindowState.WindowActive || state == WindowState.NoTargetWindow;
        }

        public bool CanTriggerHotkey(bool isHotkeyControlEnabled, bool isRegisteringHotkey)
        {
            if (isRegisteringHotkey)
                return true;

            if (!isHotkeyControlEnabled)
                return false;

            var state = GetWindowState();
            return state == WindowState.NoTargetWindow || state == WindowState.WindowActive;
        }
    }

    #endregion
}
