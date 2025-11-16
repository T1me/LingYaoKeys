using System.Collections.ObjectModel;
using System.Windows.Input;
using WpfApp.Services.Models;

namespace WpfApp.Services.Core;

/// <summary>
/// 按键配置管理服务接口
/// 负责管理多个按键配置方案的增删改查和激活切换
/// </summary>
public interface IKeyConfigurationService : IDisposable
{
    /// <summary>
    /// 配置列表变更事件
    /// </summary>
    event EventHandler? ConfigurationsChanged;

    /// <summary>
    /// 激活配置变更事件
    /// </summary>
    event EventHandler<KeyConfiguration?>? ActiveConfigurationChanged;

    /// <summary>
    /// 所有配置列表
    /// </summary>
    ObservableCollection<KeyConfiguration> Configurations { get; }

    /// <summary>
    /// 当前激活的配置
    /// </summary>
    KeyConfiguration? ActiveConfiguration { get; }

    /// <summary>
    /// 加载配置数据
    /// </summary>
    void LoadConfigurations(MultiKeyConfigData multiConfigData);

    /// <summary>
    /// 获取配置数据用于保存
    /// </summary>
    MultiKeyConfigData GetConfigData();

    /// <summary>
    /// 添加新配置
    /// </summary>
    KeyConfiguration AddConfiguration(string name);

    /// <summary>
    /// 删除配置
    /// </summary>
    bool RemoveConfiguration(Guid configId);

    /// <summary>
    /// 更新配置
    /// </summary>
    void UpdateConfiguration(KeyConfiguration config);

    /// <summary>
    /// 克隆配置
    /// </summary>
    KeyConfiguration CloneConfiguration(Guid configId);

    /// <summary>
    /// 设置激活配置
    /// </summary>
    void SetActiveConfiguration(Guid configId);

    /// <summary>
    /// 启用/禁用配置
    /// </summary>
    void SetConfigurationEnabled(Guid configId, bool isEnabled);

    /// <summary>
    /// 验证配置名称是否重复
    /// </summary>
    bool IsNameDuplicate(string name, Guid? excludeId = null);

    /// <summary>
    /// 验证热键是否冲突
    /// </summary>
    bool IsHotkeyConflict(VirtualKeyCode key, ModifierKeys mods, Guid? excludeId = null);

    /// <summary>
    /// 获取配置统计信息
    /// </summary>
    string GetStatistics();
}
