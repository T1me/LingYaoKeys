using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;
using System.ComponentModel;
using WpfApp.ViewModels;
using WpfApp.Services.Core;
using WpfApp.Services.Utils;
using WpfApp.Services.Models;

// 提供快捷键服务
namespace WpfApp.Services.Core;

public class HotkeyService : IHotkeyService, IDisposable
{
    // Win32 API 函数
    // 统一的钩子回调委托
    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    // 统一的钩子安装函数
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    // 释放钩子
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    // 调用下一个钩子
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    // 获取模块句柄
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    // 获取前台窗口句柄
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    // 检查窗口是否有效
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    // Windows消息常量
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

    // 事件
    public event Action? StartHotkeyPressed; // 启动热键按下事件
    public event Action? StartHotkeyReleased; // 启动热键释放事件
    public event Action? SequenceModeStarted; // 序列模式开始事件
    public event Action? SequenceModeStopped; // 序列模式停止事件
    public event Action<VirtualKeyCode>? KeyTriggered; // 触发按键事件

    // 核心字段
    private readonly KeySequenceExecutor _executor;
    private readonly LyKeysService _lyKeysService;
    private readonly SerilogManager _logger = SerilogManager.Instance;
    private readonly IConfigManager _configManager;
    private readonly MainViewModel _mainViewModel;
    private readonly Window _mainWindow;
    private List<KeyItemSettings> _keySettings = new();

    // 热键状态
    private int _hotkeyVirtualKey;
    private VirtualKeyCode? _pendingHotkey;
    private bool _isKeyHeld;
    private bool _isInputFocused;
    private bool _isHotkeyControlEnabled = true;
    private bool _isRegisteringHotkey = false;

    // 保持回调函数的引用
    private readonly HookProc _mouseProcDelegate; // 鼠标钩子回调函数
    private readonly HookProc _keyboardProcDelegate; // 键盘钩子回调函数
    private IntPtr _mouseHookHandle; // 鼠标钩子句柄
    private IntPtr _keyboardHookHandle; // 键盘钩子句柄
    private readonly object _hookLock = new(); // 钩子锁

    private HashSet<IntPtr> _targetWindowHandles = new();
    private bool _isTargetWindowActive;

    // 窗口状态枚举
    private enum WindowState
    {
        NoTargetWindow, // 未选择目标窗口
        WindowInvalid, // 窗口无效
        WindowInactive, // 窗口未激活
        WindowActive // 窗口激活
    }

    // 构造函数
    public HotkeyService(Window mainWindow, KeySequenceExecutor executor, LyKeysService lyKeysService, IConfigManager configManager)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _lyKeysService = lyKeysService ?? throw new ArgumentNullException(nameof(lyKeysService));
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _mainViewModel = mainWindow.DataContext as MainViewModel ??
                         throw new ArgumentException("Window.DataContext must be of type MainViewModel",
                             nameof(mainWindow));

        // 初始化回调委托
        _mouseProcDelegate = MouseHookCallback;
        _keyboardProcDelegate = KeyboardHookCallback;

        // 订阅事件
        _configManager.ConfigChanged += OnConfigChanged;
        _mainWindow.Closed += (s, e) => Dispose();

        // 从配置加载初始状态
        LoadInitialState();

        InstallHooks();
    }

    // 加载初始状态
    private void LoadInitialState()
    {
        try
        {
            // 在多配置架构中，初始状态由 KeyConfigurationService 管理
            // HotkeyService 不再直接加载配置，而是等待 KeyConfigurationService 注册热键
            _logger.Debug("HotkeyService 初始化完成，等待配置加载");
        }
        catch (Exception ex)
        {
            _logger.Error("加载初始状态失败", ex);
        }
    }

    /// <summary>
    /// 统一的消息显示接口
    /// </summary>
    private void ShowMessage(string message, bool isError = false)
    {
        _mainViewModel.UpdateStatusMessage(message, isError);
    }

    // 释放资源
    public void Dispose()
    {
        lock (_hookLock)
        {

            // 1. 停止序列
            try
            {
                StopSequence();
            }
            catch (Exception ex)
            {
                _logger.Error("停止序列失败", ex);
            }

            // 2. 移除事件订阅
            try
            {
                _configManager.ConfigChanged -= OnConfigChanged;
            }
            catch (Exception ex)
            {
                _logger.Error("移除事件订阅失败", ex);
            }

            // 3. 卸载钩子
            try
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
            catch (Exception ex)
            {
                _logger.Error("卸载钩子失败", ex);
            }

        }
    }

    // 注册热键
    public bool RegisterHotkey(VirtualKeyCode keyCode, ModifierKeys modifiers, bool saveToConfig = true)
    {
        try
        {
            _isRegisteringHotkey = true; // 进入热键注册模式
            
            
            // 更新内部状态
            _hotkeyVirtualKey = GetVirtualKeyFromLyKey(keyCode);
            _pendingHotkey = keyCode;
            
            // 根据参数决定是否保存到配置文件
            if (saveToConfig)
            {
                SaveHotkeyConfig(keyCode, modifiers);
            }
            else
            {
            }
            
            _isRegisteringHotkey = false;
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"注册热键失败: KeyCode={keyCode}, Modifiers={modifiers}", ex);
            _isRegisteringHotkey = false;
            
            // 通知用户热键注册失败
            ShowMessage($"热键注册失败: {ex.Message}", true);
            
            // 抛出异常以便调用者处理
            throw new InvalidOperationException($"无法注册热键 {keyCode}", ex);
        }
    }

    // 注销热键
    public void UnregisterHotkey(VirtualKeyCode keyCode, ModifierKeys modifiers)
    {
        try
        {
            // 由于使用全局钩子而不是 RegisterHotKey API，
            // 这里只需要清空当前热键状态即可
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

    // 保存热键配置
    private void SaveHotkeyConfig(VirtualKeyCode keyCode, ModifierKeys modifiers)
    {
        try 
        {
            
            // 直接调用配置管理器保存热键配置
            _configManager.UpdateKeyConfig(keyConfig => {
                keyConfig.startKey = keyCode;
                keyConfig.startMods = modifiers;
                keyConfig.stopKey = keyCode;
                keyConfig.stopMods = modifiers;
            });
            
        }
        catch (Exception ex)
        {
            _logger.Error($"保存热键配置失败: KeyCode={keyCode}, Modifiers={modifiers}, Error={ex.Message}", ex);
            // 重新抛出异常，让调用者处理
            throw new InvalidOperationException($"无法保存热键配置到文件", ex);
        }
    }

    // 启动序列控制
    public void StartSequence()
    {
        try
        {
            // 检查是否在坐标编辑模式
            if (_mainViewModel.KeyMappingViewModel?.IsCoordinateEditMode == true)
            {
                ShowMessage("坐标编辑模式下禁止触发热键", true);
                _logger.Debug("坐标编辑模式下禁止触发热键");
                return;
            }

            if (!_isRegisteringHotkey && !CanTriggerHotkey()) return;

            if (_keySettings == null || _keySettings.Count == 0)
            {
                _logger.Warning("按键列表为空，无法启动序列，请至少添加一个按键或坐标");
                ShowMessage("按键列表为空，无法启动序列，请至少添加一个按键或坐标", true);
                return;
            }


            SequenceModeStarted?.Invoke();

            // TODO: 从 KeyConfigurationService 获取当前激活的配置
            // 临时使用默认配置
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

    // 停止序列控制
    public void StopSequence()
    {
        _executor.Stop();
    }

    // 键盘钩子回调处理
    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_isInputFocused)
            try
            {
                var hookStruct = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT))!;
                var isHotkey = hookStruct.vkCode == _hotkeyVirtualKey;

                // 如果正在注册热键，直接处理不进行窗口状态检查
                if (_isRegisteringHotkey && isHotkey)
                {
                    // 处理热键注册逻辑
                    switch ((int)wParam)
                    {
                        case WM_KEYDOWN:
                        case WM_SYSKEYDOWN:
                            if (!_isKeyHeld)
                            {
                                _isKeyHeld = true;
                                StartHotkeyPressed?.Invoke();
                            }
                            break;
                        case WM_KEYUP:
                        case WM_SYSKEYUP:
                            if (_isKeyHeld)
                            {
                                _isKeyHeld = false;
                                StartHotkeyReleased?.Invoke();
                            }
                            break;
                    }
                    return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
                }

                // 获取当前窗口状态
                var windowState = GetWindowState();

                // 如果序列正在运行，但窗口状态异常，则停止序列
                if (_executor.IsRunning && windowState != WindowState.WindowActive &&
                    windowState != WindowState.NoTargetWindow)
                {
                    _executor.EmergencyStop();
                    StopSequence();
                    return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
                }

                if (isHotkey)
                {
                    bool isKeyDown = (int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN;
                    bool isKeyUp = (int)wParam == WM_KEYUP || (int)wParam == WM_SYSKEYUP;

                    if (isKeyDown && !_isKeyHeld && CanTriggerHotkey())
                    {
                        _isKeyHeld = true;
                        StartHotkeyPressed?.Invoke();

                        if (_lyKeysService.IsHoldMode)
                        {
                            StartSequence();
                            return new IntPtr(1);
                        }
                        else
                        {
                            if (_executor.IsRunning) StopSequence();
                            else StartSequence();
                        }
                    }
                    else if (isKeyUp && _isKeyHeld)
                    {
                        _isKeyHeld = false;
                        StartHotkeyReleased?.Invoke();

                        if (_lyKeysService.IsHoldMode)
                        {
                            StopSequence();
                            return new IntPtr(1);
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

        return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    // 鼠标钩子回调处理
    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_isInputFocused)
            try
            {
                var hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT))!;
                var wParamInt = (int)wParam;
                
                // 如果正在注册热键模式，直接处理鼠标事件不进行窗口状态检查
                if (_isRegisteringHotkey)
                {
                    switch (wParamInt)
                    {
                        case WM_XBUTTONDOWN:
                        case WM_MBUTTONDOWN:
                            HandleMouseButtonDown(wParamInt, hookStruct);
                            break;

                        case WM_XBUTTONUP:
                        case WM_MBUTTONUP:
                            HandleMouseButtonUp(wParamInt, hookStruct);
                            break;

                        case WM_MOUSEWHEEL:
                            HandleMouseWheel(hookStruct);
                            break;
                    }
                    return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
                }
                
                // 获取当前活动窗口
                var activeWindow = GetForegroundWindow();
                // 修改判断逻辑：如果没有设置目标窗口，则允许在任何窗口触发
                var isTargetWindowActive = _targetWindowHandles.Count == 0 || _targetWindowHandles.Contains(activeWindow);

                // 如果目标窗口未激活，停止当前执行
                if (!isTargetWindowActive && _executor.IsRunning)
                {
                    _executor.EmergencyStop();
                    StopSequence();
                    return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
                }

                // 如果是鼠标热键且窗口未激活，直接返回
                if ((wParamInt == WM_XBUTTONDOWN || wParamInt == WM_MBUTTONDOWN ||
                     wParamInt == WM_XBUTTONUP || wParamInt == WM_MBUTTONUP ||
                     wParamInt == WM_MOUSEWHEEL) && !isTargetWindowActive)
                    return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);

                switch (wParamInt)
                {
                    case WM_XBUTTONDOWN:
                    case WM_MBUTTONDOWN:
                        HandleMouseButtonDown(wParamInt, hookStruct);
                        break;

                    case WM_XBUTTONUP:
                    case WM_MBUTTONUP:
                        HandleMouseButtonUp(wParamInt, hookStruct);
                        break;

                    case WM_MOUSEWHEEL:
                        HandleMouseWheel(hookStruct);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error("鼠标钩子回调异常", ex);
                _isKeyHeld = false;
                StopSequence();
            }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    // 处理鼠标按键按下
    private void HandleMouseButtonDown(int wParam, MSLLHOOKSTRUCT hookStruct)
    {
        var buttonCode = GetMouseButtonCode(wParam, hookStruct);
        var isHotkey = buttonCode == _pendingHotkey;

        // 如果是在注册热键模式，只触发事件但不执行序列控制
        if (isHotkey && _isRegisteringHotkey)
        {
            if (!_isKeyHeld)
            {
                _isKeyHeld = true;
                StartHotkeyPressed?.Invoke();
            }
            return;
        }

        if (isHotkey)
        {
            if (_lyKeysService.IsHoldMode)
            {
                if (!_isKeyHeld)
                {
                    _isKeyHeld = true;
                    StartHotkeyPressed?.Invoke();
                    StartSequence();
                }
            }
            else
            {
                if (!_isKeyHeld)
                {
                    _isKeyHeld = true;
                    if (_executor.IsRunning)
                    {
                        StartHotkeyPressed?.Invoke();
                        StopSequence();
                    }
                    else
                    {
                        StartHotkeyPressed?.Invoke();
                        StartSequence();
                    }
                }
            }
        }
    }

    // 处理鼠标按键释放
    private void HandleMouseButtonUp(int wParam, MSLLHOOKSTRUCT hookStruct)
    {
        var buttonCode = GetMouseButtonCode(wParam, hookStruct);
        var isHotkey = buttonCode == _pendingHotkey;

        // 如果是在注册热键模式，只触发事件不执行序列控制
        if (isHotkey && _isRegisteringHotkey)
        {
            if (_isKeyHeld)
            {
                _isKeyHeld = false;
                StartHotkeyReleased?.Invoke();
            }
            return;
        }

        if (isHotkey && _isKeyHeld)
        {
            _isKeyHeld = false;
            StartHotkeyReleased?.Invoke();
            if (_lyKeysService.IsHoldMode) StopSequence();
        }
    }

    // 处理滚轮事件
    private void HandleMouseWheel(MSLLHOOKSTRUCT hookStruct)
    {
        // 获取滚轮方向（向上为正，向下为负）
        var wheelDelta = (short)((hookStruct.mouseData >> 16) & 0xFFFF);
        var buttonCode = wheelDelta > 0 ? VirtualKeyCode.VK_WHEELUP : VirtualKeyCode.VK_WHEELDOWN;

        var isHotkey = buttonCode == _pendingHotkey;

        // 如果是在注册热键模式，只触发事件不执行序列控制
        if (isHotkey && _isRegisteringHotkey)
        {
            if (!_isKeyHeld)
            {
                _isKeyHeld = true;
                StartHotkeyPressed?.Invoke();
                
                // 由于滚轮事件是瞬时的，立即触发释放事件
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

    // 获取鼠标按键代码
    private VirtualKeyCode GetMouseButtonCode(int wParam, MSLLHOOKSTRUCT hookStruct)
    {
        if (wParam == WM_MBUTTONDOWN || wParam == WM_MBUTTONUP)
        {
            return VirtualKeyCode.VK_MBUTTON;
        }
        else if (wParam == WM_MOUSEWHEEL)
        {
            var wheelDelta = (short)((hookStruct.mouseData >> 16) & 0xFFFF);
            return wheelDelta > 0 ? VirtualKeyCode.VK_WHEELUP : VirtualKeyCode.VK_WHEELDOWN;
        }

        var xButton = (int)((hookStruct.mouseData >> 16) & 0xFFFF);
        return xButton == 1 ? VirtualKeyCode.VK_XBUTTON1 : VirtualKeyCode.VK_XBUTTON2;
    }

    // 工具方法
    private int GetVirtualKeyFromLyKey(VirtualKeyCode lyKeyCode)
    {
        // 直接使用VirtualKeyCode的值作为虚拟键码
        return (int)lyKeyCode;
    }


    // 配置变更事件处理
    private void OnConfigChanged(object? sender, ConfigEventArgs e)
    {
        try
        {

            switch (e.ChangeType)
            {
                case ConfigChangeType.Key:
                    // 按键配置变更 - 重新加载热键和按键序列
                    if (e.KeyConfigData != null)
                    {
                        ReloadKeyConfiguration(e.KeyConfigData);
                    }
                    break;

                case ConfigChangeType.Global:
                    // 全局配置变更 - 更新全局设置
                    if (e.GlobalConfigData != null)
                    {
                        UpdateGlobalSettings(e.GlobalConfigData);
                    }
                    break;

                case ConfigChangeType.All:
                    // 所有配置变更 - 同时更新全局和按键配置
                    if (e.GlobalConfigData != null)
                    {
                        UpdateGlobalSettings(e.GlobalConfigData);
                    }
                    if (e.KeyConfigData != null)
                    {
                        ReloadKeyConfiguration(e.KeyConfigData);
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

    // 重新加载热键配置
    private void ReloadKeyConfiguration(KeyConfigData keyConfig)
    {
        if (keyConfig == null)
        {
            _logger.Warning("按键配置为空，无法重新加载");
            return;
        }

        try
        {

            // 1. 重新注册热键
            if (keyConfig.startKey.HasValue)
            {
                RegisterHotkey(keyConfig.startKey.Value, keyConfig.startMods, saveToConfig: false);
            }
            else
            {
                _pendingHotkey = null;
                _hotkeyVirtualKey = 0;
            }

            // 2. 更新按键序列
            if (keyConfig.keys?.Count > 0)
            {
                var selectedItems = keyConfig.keys.Where(k => k.IsSelected).ToList();

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

            // 3. 更新目标窗口信息
            UpdateTargetWindow(keyConfig);

            // 4. 更新按键模式
            _lyKeysService.IsHoldMode = keyConfig.keyMode != 0;

        }
        catch (Exception ex)
        {
            _logger.Error("重新加载热键配置失败", ex);
            throw;
        }
    }

    // 更新目标窗口信息
    private void UpdateTargetWindow(KeyConfigData keyConfig)
    {
        try
        {
            // 多窗口配置由 WindowManagementService 处理
            // HotkeyService 只负责使用已设置的窗口句柄集合
        }
        catch (Exception ex)
        {
            _logger.Error("更新目标窗口信息失败", ex);
        }
    }

    // 更新全局设置
    private void UpdateGlobalSettings(GlobalConfig globalConfig)
    {
        if (globalConfig == null)
        {
            _logger.Warning("全局配置为空，无法更新全局设置");
            return;
        }

        try
        {

            // 更新热键总开关状态
            var previousState = _isHotkeyControlEnabled;
            _isHotkeyControlEnabled = globalConfig.isHotkeyControlEnabled ?? true;

            if (previousState != _isHotkeyControlEnabled)
            {

                // 如果热键总开关被禁用，停止当前运行的序列
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

    // 输入焦点控制
    public bool IsInputFocused
    {
        get => _isInputFocused;
        set => _isInputFocused = value;
    }

    // 热键总开关属性
    public bool IsHotkeyControlEnabled
    {
        get => _isHotkeyControlEnabled;
        set
        {
            if (_isHotkeyControlEnabled != value)
            {
                _isHotkeyControlEnabled = value;

                // 如果禁用热键总开关，同时停止当前执行的序列
                if (!value && _executor.IsRunning)
                {
                    _executor.EmergencyStop();
                    StopSequence();
                }
            }
        }
    }

    // 结构体定义
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

    // 设置按键序列
    public void SetKeySequence(List<KeyItemSettings> keySettings)
    {
        try
        {
            if (keySettings == null)
                keySettings = new List<KeyItemSettings>();

            _keySettings = keySettings.ToList();

            int keyboardCount = keySettings.Count(k => k.Type == KeyItemType.Keyboard);
            int coordinatesCount = keySettings.Count(k => k.Type == KeyItemType.Coordinates);
        }
        catch (Exception ex)
        {
            _logger.Error("设置按键序列失败", ex);
        }
    }

    // 判断是否为鼠标按键
    public bool IsMouseButton(VirtualKeyCode keyCode)
    {
        return keyCode == VirtualKeyCode.VK_LBUTTON ||
               keyCode == VirtualKeyCode.VK_RBUTTON ||
               keyCode == VirtualKeyCode.VK_MBUTTON ||
               keyCode == VirtualKeyCode.VK_XBUTTON1 ||
               keyCode == VirtualKeyCode.VK_XBUTTON2;
    }

    // 添加钩子安装方法
    private void InstallHooks()
    {
        try
        {
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule!)
            {
                var hModule = GetModuleHandle(curModule.ModuleName);

                // 安装键盘钩子
                _keyboardHookHandle = SetWindowsHookEx(
                    WH_KEYBOARD_LL,
                    _keyboardProcDelegate,
                    hModule,
                    0);

                // 安装鼠标钩子
                _mouseHookHandle = SetWindowsHookEx(
                    WH_MOUSE_LL,
                    _mouseProcDelegate,
                    hModule,
                    0);

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

    // 设置目标窗口句柄集合
    public void SetTargetWindows(IEnumerable<IntPtr> handles)
    {
        _targetWindowHandles = new HashSet<IntPtr>(handles.Where(h => h != IntPtr.Zero));

        // 如果没有有效窗口且正在执行，停止执行
        if (_targetWindowHandles.Count == 0 && _executor.IsRunning)
        {
            _executor.EmergencyStop();
            StopSequence();
        }
    }

    // 获取目标窗口是否激活
    public bool IsTargetWindowActive
    {
        get => _isTargetWindowActive;
        set => _isTargetWindowActive = value;
    }

    // 获取窗口状态
    private WindowState GetWindowState()
    {
        try
        {
            // 1. 如果没有目标窗口，允许全局触发
            if (_targetWindowHandles.Count == 0)
                return WindowState.NoTargetWindow;

            // 2. 检查是否有任意一个窗口激活
            var activeWindow = GetForegroundWindow();
            if (_targetWindowHandles.Contains(activeWindow))
                return WindowState.WindowActive;

            // 3. 检查是否有窗口有效
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

    // 总开关：判断是否可以触发热键
    private bool CanTriggerHotkey()
    {
        // 如果正在注册热键，跳过窗口状态检查
        if (_isRegisteringHotkey)
            return true;

        // 首先检查热键总开关是否启用
        if (!_isHotkeyControlEnabled)
        {
            return false;
        }

        // 继续检查窗口状态
        var state = GetWindowState();

        switch (state)
        {
            case WindowState.NoTargetWindow:
                return true; // 未选择窗口时允许全局触发


            case WindowState.WindowInvalid:
                ShowMessage("目标窗口无效，请重新选择窗口", true);
                return false;

            case WindowState.WindowInactive:
                ShowMessage("请先激活目标窗口", true);
                return false;

            case WindowState.WindowActive:
                return true;

            default:
                _logger.Error($"未处理的窗口状态: {state}");
                return false;
        }
    }
}