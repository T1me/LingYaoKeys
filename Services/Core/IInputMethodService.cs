namespace WpfApp.Services.Core;

/// <summary>
/// 输入法服务接口
/// </summary>
public interface IInputMethodService
{
    /// <summary>
    /// 保存当前输入法状态
    /// </summary>
    void StoreCurrentLayout();

    /// <summary>
    /// 切换到英文输入法
    /// </summary>
    void SwitchToEnglish();

    /// <summary>
    /// 恢复之前的输入法状态
    /// </summary>
    void RestorePreviousLayout();
}
