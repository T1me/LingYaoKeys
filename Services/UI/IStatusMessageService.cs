namespace WpfApp.Services.UI;

/// <summary>
/// 状态消息服务接口 - 用于解耦 HotkeyService 对 MainViewModel 的直接依赖
/// </summary>
public interface IStatusMessageService
{
    /// <summary>
    /// 更新状态栏消息
    /// </summary>
    /// <param name="message">消息文本</param>
    /// <param name="isError">是否为错误消息</param>
    void UpdateStatusMessage(string message, bool isError = false);

    /// <summary>
    /// 更新状态栏消息（带颜色）
    /// </summary>
    /// <param name="message">消息文本</param>
    /// <param name="color">消息颜色</param>
    void UpdateStatusMessage(string message, System.Windows.Media.Brush color);
}
