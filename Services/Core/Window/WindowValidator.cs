using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using WpfApp.Services.Utils;
using static WpfApp.Services.Core.Window.IWindowValidator;

namespace WpfApp.Services.Core.Window;

/// <summary>
/// 窗口验证器实现 - 通过 Win32 API 检查目标窗口状态
/// </summary>
public class WindowValidator : IWindowValidator
{
    #region Win32 API 声明

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    #endregion

    #region 字段

    private HashSet<IntPtr> _targetWindowHandles = new();
    private readonly ISerilogManager _logger;

    #endregion

    #region 属性

    public bool IsTargetWindowActive { get; set; }

    #endregion

    #region 构造函数

    public WindowValidator(ISerilogManager logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #endregion

    #region 公共方法

    // 设置目标窗口句柄列表
    public void SetTargetWindows(IEnumerable<IntPtr> handles)
    {
        _targetWindowHandles = new HashSet<IntPtr>(handles.Where(h => h != IntPtr.Zero));
        _logger.Debug($"已设置 {_targetWindowHandles.Count} 个目标窗口");
    }

    // 检查是否有有效的目标窗口
    public bool HasValidWindows()
    {
        return _targetWindowHandles.Count > 0;
    }

    // 获取当前窗口状态
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

    // 检查窗口状态是否有效（允许热键执行）
    public bool IsWindowStateValid(WindowState state)
    {
        return state == WindowState.WindowActive || state == WindowState.NoTargetWindow;
    }

    // 检查是否可以触发热键
    public bool CanTriggerHotkey(bool isHotkeyControlEnabled, bool isRegisteringHotkey)
    {
        if (isRegisteringHotkey)
            return true;

        if (!isHotkeyControlEnabled)
            return false;

        var state = GetWindowState();
        return state == WindowState.NoTargetWindow || state == WindowState.WindowActive;
    }

    #endregion
}
