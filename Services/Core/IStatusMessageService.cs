namespace WpfApp.Services.Core;

/// <summary>
/// 状态消息服务接口
/// 用于解耦 HotkeyService 和 MainViewModel 的依赖关系
/// 支持窗口级和全局级 Growl 通知
/// </summary>
/// <remarks>
/// 设计说明：
/// 1. ShowMessage: 窗口级通知，简单映射（isError 决定类型）
/// 2. ShowXxxGlobal: 全局/桌面级通知，跨窗口显示
/// 3. IsCoordinateEditMode: 用于检查是否禁止热键触发
/// </remarks>
public interface IStatusMessageService
{
    #region 窗口级通知

    /// <summary>
    /// 显示窗口级消息通知（简单映射）
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <param name="isError">是否为错误消息（true=Error, false=Info）</param>
    void ShowMessage(string message, bool isError = false);

    #endregion

    #region 全局通知（桌面级）

    /// <summary>
    /// 显示全局信息通知（蓝色）
    /// </summary>
    /// <param name="message">消息内容</param>
    void ShowInfoGlobal(string message);

    /// <summary>
    /// 显示全局成功通知（绿色）
    /// </summary>
    /// <param name="message">消息内容</param>
    void ShowSuccessGlobal(string message);

    /// <summary>
    /// 显示全局警告通知（橙色）
    /// </summary>
    /// <param name="message">消息内容</param>
    void ShowWarningGlobal(string message);

    /// <summary>
    /// 显示全局错误通知（红色）
    /// </summary>
    /// <param name="message">消息内容</param>
    void ShowErrorGlobal(string message);

    /// <summary>
    /// 显示全局严重错误通知（需手动关闭）
    /// </summary>
    /// <param name="message">消息内容</param>
    void ShowFatalGlobal(string message);

    #endregion

    #region 状态查询

    /// <summary>
    /// 获取当前是否处于坐标编辑模式
    /// 坐标编辑模式下禁止触发热键
    /// </summary>
    bool IsCoordinateEditMode { get; }

    #endregion
}
