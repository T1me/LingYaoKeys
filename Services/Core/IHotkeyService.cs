using WpfApp.Services.Models;

namespace WpfApp.Services.Core;

/// <summary>
/// 热键服务接口
/// </summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>
    /// 启用/禁用热键控制
    /// </summary>
    bool IsHotkeyControlEnabled { get; set; }

    /// <summary>
    /// 启动热键按下事件
    /// </summary>
    event Action? StartHotkeyPressed;

    /// <summary>
    /// 启动热键释放事件
    /// </summary>
    event Action? StartHotkeyReleased;

    /// <summary>
    /// 序列模式开始事件
    /// </summary>
    event Action? SequenceModeStarted;

    /// <summary>
    /// 序列模式停止事件
    /// </summary>
    event Action? SequenceModeStopped;

    /// <summary>
    /// 触发按键事件
    /// </summary>
    event Action<VirtualKeyCode>? KeyTriggered;
}
