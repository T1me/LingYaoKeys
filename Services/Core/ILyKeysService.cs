using WpfApp.Services.Events;
using WpfApp.Services.Models;

namespace WpfApp.Services.Core;

/// <summary>
/// 驱动服务接口
/// </summary>
public interface ILyKeysService : IDisposable
{
    /// <summary>
    /// 初始化驱动
    /// </summary>
    bool Initialize(string driverPath);

    /// <summary>
    /// 重新加载驱动
    /// </summary>
    bool ReloadDriver(IDriver newDriver, string driverPath);

    /// <summary>
    /// 驱动是否已初始化
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// 按键间隔（毫秒）
    /// </summary>
    int KeyInterval { get; set; }

    /// <summary>
    /// 按键按下时长（毫秒）
    /// </summary>
    int KeyPressInterval { get; set; }

    /// <summary>
    /// 是否按压模式
    /// </summary>
    bool IsHoldMode { get; set; }

    /// <summary>
    /// 是否降低按键卡位
    /// </summary>
    bool IsReduceKeyStuck { get; set; }

    /// <summary>
    /// 发送按键按下
    /// </summary>
    bool SendKeyDown(VirtualKeyCode keyCode);

    /// <summary>
    /// 发送按键释放
    /// </summary>
    bool SendKeyUp(VirtualKeyCode keyCode);

    /// <summary>
    /// 发送按键按下并释放
    /// </summary>
    bool SendKeyPress(VirtualKeyCode keyCode, int duration = 100);

    /// <summary>
    /// 模拟组合键
    /// </summary>
    void SimulateKeyCombo(params VirtualKeyCode[] keyCodes);

    /// <summary>
    /// 移动鼠标到指定位置
    /// </summary>
    bool MoveMouseToPosition(int? x, int? y);

    /// <summary>
    /// 获取按键描述
    /// </summary>
    string GetKeyDescription(VirtualKeyCode keyCode);

    /// <summary>
    /// 初始化状态变更事件
    /// </summary>
    event EventHandler<bool>? InitializationStatusChanged;

    /// <summary>
    /// 状态消息变更事件
    /// </summary>
    event EventHandler<StatusMessageEventArgs>? StatusMessageChanged;

    /// <summary>
    /// 输入法服务
    /// </summary>
    InputMethodService InputMethodService { get; }
}
