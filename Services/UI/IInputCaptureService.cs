using WpfApp.Services.Core;
using WpfApp.Services.Models;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;

namespace WpfApp.Services.UI;

/// <summary>
/// 输入捕获服务接口 - 负责键盘和鼠标输入的捕获和转换
/// </summary>
public interface IInputCaptureService
{
    /// <summary>
    /// 捕获键盘输入
    /// </summary>
    /// <param name="e">键盘事件参数</param>
    /// <returns>虚拟键码，如果无法捕获则返回 null</returns>
    VirtualKeyCode? CaptureKeyboardInput(KeyEventArgs e);

    /// <summary>
    /// 捕获鼠标按键输入
    /// </summary>
    /// <param name="e">鼠标按键事件参数</param>
    /// <returns>虚拟键码，如果无法捕获则返回 null</returns>
    VirtualKeyCode? CaptureMouseInput(MouseButtonEventArgs e);

    /// <summary>
    /// 捕获鼠标滚轮输入
    /// </summary>
    /// <param name="e">鼠标滚轮事件参数</param>
    /// <returns>虚拟键码（VK_WHEELUP 或 VK_WHEELDOWN）</returns>
    VirtualKeyCode? CaptureMouseWheel(MouseWheelEventArgs e);

    /// <summary>
    /// 判断是否为修饰键
    /// </summary>
    /// <param name="keyCode">虚拟键码</param>
    /// <returns>是否为修饰键（Ctrl、Alt、Shift）</returns>
    bool IsModifierKey(VirtualKeyCode keyCode);
}
