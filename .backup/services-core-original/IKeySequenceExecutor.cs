using WpfApp.Services.Models;

namespace WpfApp.Services.Core;

/// <summary>
/// 按键序列执行器接口
/// </summary>
public interface IKeySequenceExecutor
{
    /// <summary>
    /// 启动按键序列执行
    /// </summary>
    /// <param name="operations">按键操作列表</param>
    /// <param name="isHoldMode">是否为按压模式</param>
    /// <param name="config">配置信息</param>
    /// <param name="onCompleted">完成回调</param>
    void Start(List<KeyItemSettings> operations, bool isHoldMode, KeyConfiguration config, Action? onCompleted = null);

    /// <summary>
    /// 停止按键序列执行
    /// </summary>
    void Stop();

    /// <summary>
    /// 紧急停止
    /// </summary>
    void EmergencyStop();
}
