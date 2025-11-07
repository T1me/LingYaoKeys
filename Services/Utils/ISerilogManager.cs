using WpfApp.Services.Models;

namespace WpfApp.Services.Utils;

/// <summary>
/// 日志管理服务接口
/// </summary>
public interface ISerilogManager : IDisposable
{
    /// <summary>
    /// 初始化日志系统
    /// </summary>
    void Initialize(DebugConfig debugConfig);

    /// <summary>
    /// 设置日志基础目录
    /// </summary>
    void SetBaseDirectory(string baseDirectory);

    /// <summary>
    /// 更新日志配置
    /// </summary>
    void UpdateLoggerConfig(DebugConfig debugConfig);
}
