using CommunityToolkit.Mvvm.ComponentModel;
using WpfApp.Services.Core;
using WpfApp.Services.Utils;

namespace WpfApp.ViewModels;

/// <summary>
/// ViewModel 基类
/// 提供通用的依赖服务和功能
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    /// <summary>
    /// 统一的日志管理器
    /// </summary>
    protected readonly ISerilogManager Logger;

    /// <summary>
    /// 统一的配置管理器
    /// </summary>
    protected readonly IConfigManager ConfigManager;

    /// <summary>
    /// 统一的异常处理器
    /// </summary>
    protected readonly ExceptionHandler ExceptionHandler = new();

    /// <summary>
    /// 构造函数：注入核心依赖服务
    /// </summary>
    protected ViewModelBase(ISerilogManager logger, IConfigManager configManager)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ConfigManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
    }
}
