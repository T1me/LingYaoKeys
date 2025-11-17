using System;
using WpfApp.Services.Models;

namespace WpfApp.Services.Core.Hooks;

/// <summary>
/// Win32 钩子管理器接口
/// </summary>
public interface IHookManager : IDisposable
{
    /// <summary>
    /// 键盘事件 - 参数: (virtualKeyCode, isKeyDown)
    /// </summary>
    event Action<int, bool>? KeyboardEvent;

    /// <summary>
    /// 鼠标按键事件 - 参数: (button, isButtonDown)
    /// </summary>
    event Action<VirtualKeyCode, bool>? MouseButtonEvent;

    /// <summary>
    /// 鼠标滚轮事件 - 参数: (滚轮方向)
    /// </summary>
    event Action<VirtualKeyCode>? MouseWheelEvent;

    /// <summary>
    /// 安装 Win32 钩子
    /// </summary>
    void InstallHooks();

    /// <summary>
    /// 卸载 Win32 钩子
    /// </summary>
    void UninstallHooks();
}
