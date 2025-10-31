namespace WpfApp.Services.Core;

/// <summary>
/// 驱动接口 - 定义所有驱动必须实现的操作
/// </summary>
public interface IDriver : IDisposable
{
    bool Initialize();
    bool SendKeyDown(ushort vkCode);
    bool SendKeyUp(ushort vkCode);
    bool SendKeyPress(ushort vkCode, int duration = 100);
    bool MoveMouseAbsolute(int x, int y);
    bool SendMouseButton(MouseButtonType button, bool isDown);
    DeviceStatus GetLastStatus();
}

/// <summary>
/// 鼠标按键类型
/// </summary>
public enum MouseButtonType
{
    Left,
    Right,
    Middle,
    XButton1,
    XButton2,
    WheelUp,
    WheelDown
}

/// <summary>
/// 设备状态
/// </summary>
public enum DeviceStatus
{
    Unknown = 0,
    Ready = 1,
    Error = 2,
    NoKeyboard = 3,
    NoMouse = 4,
    InitFailed = 5
}
