using System.Windows.Input;
using WpfApp.Services.Models;

namespace WpfApp.Services.Core.Hotkey;

/// <summary>
/// 热键注册服务接口
/// </summary>
public interface IHotkeyRegistry
{
    /// <summary>
    /// 当前注册的热键虚拟键码
    /// </summary>
    int HotkeyVirtualKey { get; }

    /// <summary>
    /// 当前待注册的热键
    /// </summary>
    VirtualKeyCode? PendingHotkey { get; }

    /// <summary>
    /// 是否正在注册热键模式
    /// </summary>
    bool IsRegisteringHotkey { get; set; }

    /// <summary>
    /// 注册热键
    /// </summary>
    /// <param name="keyCode">按键代码</param>
    /// <param name="modifiers">修饰键</param>
    /// <param name="saveToConfig">是否保存到配置文件</param>
    /// <returns>注册是否成功</returns>
    bool RegisterHotkey(VirtualKeyCode keyCode, ModifierKeys modifiers, bool saveToConfig = true);

    /// <summary>
    /// 注销热键
    /// </summary>
    /// <param name="keyCode">按键代码</param>
    /// <param name="modifiers">修饰键</param>
    void UnregisterHotkey(VirtualKeyCode keyCode, ModifierKeys modifiers);

    /// <summary>
    /// 检查是否为鼠标按键
    /// </summary>
    bool IsMouseButton(VirtualKeyCode keyCode);

    /// <summary>
    /// 检查指定虚拟键码是否为当前热键
    /// </summary>
    bool IsHotkey(int vkCode);

    /// <summary>
    /// 检查指定虚拟键码是否为当前热键
    /// </summary>
    bool IsHotkey(VirtualKeyCode keyCode);
}
