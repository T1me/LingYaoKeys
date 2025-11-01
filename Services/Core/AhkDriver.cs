using WpfApp.Services.Utils;

namespace WpfApp.Services.Core;

/// <summary>
/// AHK 驱动空实现（测试阶段）
/// </summary>
public class AhkDriver : IDriver
{
    private static readonly SerilogManager _logger = SerilogManager.Instance;

    public bool Initialize()
    {
        _logger.Debug("AHK 驱动初始化（测试模式）");
        return true;
    }

    public bool SendKeyDown(ushort vkCode)
    {
        _logger.Debug($"AHK SendKeyDown: {vkCode}（测试模式）");
        return true;
    }

    public bool SendKeyUp(ushort vkCode)
    {
        _logger.Debug($"AHK SendKeyUp: {vkCode}（测试模式）");
        return true;
    }

    public bool SendKeyPress(ushort vkCode, int duration = 100)
    {
        _logger.Debug($"AHK SendKeyPress: {vkCode}, duration: {duration}（测试模式）");
        return true;
    }

    public bool MoveMouseAbsolute(int x, int y)
    {
        _logger.Debug($"AHK MoveMouseAbsolute: ({x}, {y})（测试模式）");
        return true;
    }

    public bool SendMouseButton(MouseButtonType button, bool isDown)
    {
        _logger.Debug($"AHK SendMouseButton: {button}, isDown: {isDown}（测试模式）");
        return true;
    }

    public DeviceStatus GetLastStatus()
    {
        return DeviceStatus.Ready;
    }

    public void Dispose()
    {
        _logger.Debug("AHK 驱动释放（测试模式）");
    }
}
