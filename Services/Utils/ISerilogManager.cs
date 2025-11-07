using System.Runtime.CompilerServices;
using Serilog.Events;
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

    /// <summary>
    /// 记录调试日志
    /// </summary>
    void Debug(string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0);

    /// <summary>
    /// 记录信息日志
    /// </summary>
    void Info(string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0);

    /// <summary>
    /// 记录警告日志
    /// </summary>
    void Warning(string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0);

    /// <summary>
    /// 记录错误日志
    /// </summary>
    void Error(string message, Exception? ex = null,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0);
}
