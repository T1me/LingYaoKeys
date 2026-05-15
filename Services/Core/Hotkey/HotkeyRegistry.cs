using System;
using System.Windows.Input;
using WpfApp.Services.Models;
using WpfApp.Services.UI;
using WpfApp.Services.Utils;

namespace WpfApp.Services.Core.Hotkey;

/// <summary>
/// 热键注册服务实现
/// </summary>
public class HotkeyRegistry : IHotkeyRegistry
{
    private readonly ISerilogManager _logger;
    private readonly IConfigManager _configManager;
    private readonly IStatusMessageService _statusMessageService;

    private int _hotkeyVirtualKey;
    private VirtualKeyCode? _pendingHotkey;
    private int _stopHotkeyVirtualKey;
    private VirtualKeyCode? _pendingStopHotkey;
    private bool _isRegisteringHotkey;

    public int HotkeyVirtualKey => _hotkeyVirtualKey;
    public VirtualKeyCode? PendingHotkey => _pendingHotkey;
    public int StopHotkeyVirtualKey => _stopHotkeyVirtualKey;
    public VirtualKeyCode? PendingStopHotkey => _pendingStopHotkey;

    public bool IsRegisteringHotkey
    {
        get => _isRegisteringHotkey;
        set => _isRegisteringHotkey = value;
    }

    public HotkeyRegistry(
        ISerilogManager logger,
        IConfigManager configManager,
        IStatusMessageService statusMessageService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _statusMessageService = statusMessageService ?? throw new ArgumentNullException(nameof(statusMessageService));
    }

    public bool RegisterHotkey(VirtualKeyCode keyCode, ModifierKeys modifiers, bool saveToConfig = true)
    {
        try
        {
            _isRegisteringHotkey = true;
            _hotkeyVirtualKey = (int)keyCode;
            _pendingHotkey = keyCode;

            if (saveToConfig)
            {
                SaveStartHotkeyConfig(keyCode, modifiers);
            }

            _logger.Info($"开始热键已注册: {keyCode} (修饰键: {modifiers})");
            _isRegisteringHotkey = false;
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"注册开始热键失败: KeyCode={keyCode}, Modifiers={modifiers}", ex);
            _isRegisteringHotkey = false;
            _statusMessageService.UpdateStatusMessage($"热键注册失败: {ex.Message}", true);
            throw new InvalidOperationException($"无法注册热键 {keyCode}", ex);
        }
    }

    public bool RegisterStopHotkey(VirtualKeyCode keyCode, ModifierKeys modifiers, bool saveToConfig = true)
    {
        try
        {
            _stopHotkeyVirtualKey = (int)keyCode;
            _pendingStopHotkey = keyCode;

            if (saveToConfig)
            {
                SaveStopHotkeyConfig(keyCode, modifiers);
            }

            _logger.Info($"停止热键已注册: {keyCode} (修饰键: {modifiers})");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"注册停止热键失败: KeyCode={keyCode}, Modifiers={modifiers}", ex);
            _statusMessageService.UpdateStatusMessage($"停止热键注册失败: {ex.Message}", true);
            throw new InvalidOperationException($"无法注册停止热键 {keyCode}", ex);
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

    public bool IsMouseButton(VirtualKeyCode keyCode)
    {
        return keyCode == VirtualKeyCode.VK_LBUTTON ||
               keyCode == VirtualKeyCode.VK_RBUTTON ||
               keyCode == VirtualKeyCode.VK_MBUTTON ||
               keyCode == VirtualKeyCode.VK_XBUTTON1 ||
               keyCode == VirtualKeyCode.VK_XBUTTON2;
    }

    public bool IsHotkey(int vkCode)
    {
        return vkCode == _hotkeyVirtualKey;
    }

    public bool IsHotkey(VirtualKeyCode keyCode)
    {
        return keyCode == _pendingHotkey;
    }

    public bool IsStopHotkey(int vkCode)
    {
        return vkCode == _stopHotkeyVirtualKey;
    }

    public bool IsStopHotkey(VirtualKeyCode keyCode)
    {
        return keyCode == _pendingStopHotkey;
    }

    private void SaveStartHotkeyConfig(VirtualKeyCode keyCode, ModifierKeys modifiers)
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
                }
            });

            _logger.Debug($"开始热键配置已保存: {keyCode}");
        }
        catch (Exception ex)
        {
            _logger.Error($"保存开始热键配置失败: KeyCode={keyCode}, Modifiers={modifiers}", ex);
            throw new InvalidOperationException("无法保存开始热键配置到文件", ex);
        }
    }

    private void SaveStopHotkeyConfig(VirtualKeyCode keyCode, ModifierKeys modifiers)
    {
        try
        {
            _configManager.UpdateMultiKeyConfig(multiConfig =>
            {
                var activeConfig = multiConfig.GetActiveConfiguration();
                if (activeConfig != null)
                {
                    activeConfig.StopKey = keyCode;
                    activeConfig.StopMods = modifiers;
                }
            });

            _logger.Debug($"停止热键配置已保存: {keyCode}");
        }
        catch (Exception ex)
        {
            _logger.Error($"保存停止热键配置失败: KeyCode={keyCode}, Modifiers={modifiers}", ex);
            throw new InvalidOperationException("无法保存停止热键配置到文件", ex);
        }
    }
}
