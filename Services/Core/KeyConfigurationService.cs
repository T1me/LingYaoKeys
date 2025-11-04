using System.Collections.ObjectModel;
using System.Windows.Input;
using WpfApp.Services.Models;
using WpfApp.Services.Utils;


namespace WpfApp.Services.Core;

/// <summary>
/// 按键配置管理服务
/// 负责管理多个按键配置方案的增删改查和激活切换
/// </summary>
public class KeyConfigurationService : IDisposable
{
    private readonly HotkeyService _hotkeyService;
    private MultiKeyConfigData _multiConfigData;
    private KeyConfiguration? _activeConfiguration;
    private readonly SerilogManager Logger = SerilogManager.Instance;

    /// <summary>
    /// 配置列表变更事件
    /// </summary>
    public event EventHandler? ConfigurationsChanged;

    /// <summary>
    /// 激活配置变更事件
    /// </summary>
    public event EventHandler<KeyConfiguration?>? ActiveConfigurationChanged;

    /// <summary>
    /// 所有配置列表
    /// </summary>
    public ObservableCollection<KeyConfiguration> Configurations { get; }

    /// <summary>
    /// 当前激活的配置
    /// </summary>
    public KeyConfiguration? ActiveConfiguration
    {
        get => _activeConfiguration;
        private set
        {
            if (_activeConfiguration != value)
            {
                _activeConfiguration = value;
                ActiveConfigurationChanged?.Invoke(this, value);
            }
        }
    }

    public KeyConfigurationService(HotkeyService hotkeyService)
    {
        _hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
        _multiConfigData = new MultiKeyConfigData();
        Configurations = new ObservableCollection<KeyConfiguration>();
    }

    /// <summary>
    /// 加载配置数据
    /// </summary>
    public void LoadConfigurations(MultiKeyConfigData multiConfigData)
    {
        if (multiConfigData == null)
        {
            Logger.Warning("加载的配置数据为空，使用默认配置");
            multiConfigData = new MultiKeyConfigData();

            // 创建默认配置
            var defaultConfig = new KeyConfiguration("默认配置");
            multiConfigData.AddConfiguration(defaultConfig);
        }

        _multiConfigData = multiConfigData;
        Configurations.Clear();

        foreach (var config in _multiConfigData.Configurations)
        {
            Configurations.Add(config);
        }

        // 设置激活配置
        var activeConfig = _multiConfigData.GetActiveConfiguration();
        if (activeConfig != null)
        {
            SetActiveConfiguration(activeConfig.Id);
        }

        Logger.Info($"已加载 {Configurations.Count} 个配置");
    }

    /// <summary>
    /// 从旧版本配置迁移
    /// </summary>
    public void MigrateFromLegacyConfig(KeyConfigData legacyConfig, GlobalConfig? globalConfig = null)
    {
        Logger.Info("检测到旧版本配置，开始迁移...");

        var multiConfig = MultiKeyConfigData.FromLegacyConfig(legacyConfig, globalConfig);
        LoadConfigurations(multiConfig);

        Logger.Info("配置迁移完成");
    }

    /// <summary>
    /// 获取配置数据用于保存
    /// </summary>
    public MultiKeyConfigData GetConfigData()
    {
        _multiConfigData.Configurations = Configurations.ToList();
        _multiConfigData.ActiveConfigurationId = ActiveConfiguration?.Id;
        return _multiConfigData;
    }

    /// <summary>
    /// 添加新配置
    /// </summary>
    public KeyConfiguration AddConfiguration(string name)
    {
        var config = new KeyConfiguration(name);
        Configurations.Add(config);
        _multiConfigData.AddConfiguration(config);

        // 如果是第一个配置，自动激活
        if (Configurations.Count == 1)
        {
            SetActiveConfiguration(config.Id);
        }

        ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
        Logger.Info($"已添加配置: {name}");

        return config;
    }

    /// <summary>
    /// 删除配置
    /// </summary>
    public bool RemoveConfiguration(Guid configId)
    {
        var config = Configurations.FirstOrDefault(c => c.Id == configId);
        if (config == null)
        {
            Logger.Warning($"未找到要删除的配置: {configId}");
            return false;
        }

        // 不允许删除最后一个配置
        if (Configurations.Count == 1)
        {
            Logger.Warning("不能删除最后一个配置");
            return false;
        }

        // 如果删除的是激活配置，先切换到其他配置
        if (ActiveConfiguration?.Id == configId)
        {
            var nextConfig = Configurations.FirstOrDefault(c => c.Id != configId);
            if (nextConfig != null)
            {
                SetActiveConfiguration(nextConfig.Id);
            }
        }

        // 注销该配置的热键
        if (config.StartKey.HasValue)
        {
            try
            {
                _hotkeyService.UnregisterHotkey(config.StartKey.Value, config.StartMods);
            }
            catch (Exception ex)
            {
                Logger.Error($"注销热键失败: {ex.Message}", ex);
            }
        }

        Configurations.Remove(config);
        _multiConfigData.RemoveConfiguration(configId);

        ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
        Logger.Info($"已删除配置: {config.Name}");

        return true;
    }

    /// <summary>
    /// 更新配置
    /// </summary>
    public void UpdateConfiguration(KeyConfiguration config)
    {
        var existingConfig = Configurations.FirstOrDefault(c => c.Id == config.Id);
        if (existingConfig == null)
        {
            Logger.Warning($"未找到要更新的配置: {config.Id}");
            return;
        }

        // 更新配置属性
        var index = Configurations.IndexOf(existingConfig);
        Configurations[index] = config;

        // 如果是激活配置，重新注册热键
        if (ActiveConfiguration?.Id == config.Id)
        {
            RegisterHotkeyForConfiguration(config);
        }

        ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
        Logger.Info($"已更新配置: {config.Name}");
    }

    /// <summary>
    /// 克隆配置
    /// </summary>
    public KeyConfiguration CloneConfiguration(Guid configId)
    {
        var config = Configurations.FirstOrDefault(c => c.Id == configId);
        if (config == null)
        {
            throw new ArgumentException($"未找到要克隆的配置: {configId}");
        }

        var clonedConfig = config.Clone();
        Configurations.Add(clonedConfig);
        _multiConfigData.AddConfiguration(clonedConfig);

        ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
        Logger.Info($"已克隆配置: {config.Name} -> {clonedConfig.Name}");

        return clonedConfig;
    }

    /// <summary>
    /// 设置激活配置
    /// </summary>
    public void SetActiveConfiguration(Guid configId)
    {
        var config = Configurations.FirstOrDefault(c => c.Id == configId);
        if (config == null)
        {
            Logger.Warning($"未找到要激活的配置: {configId}");
            return;
        }

        // 注销旧配置的热键
        if (ActiveConfiguration != null && ActiveConfiguration.StartKey.HasValue)
        {
            try
            {
                _hotkeyService.UnregisterHotkey(ActiveConfiguration.StartKey.Value, ActiveConfiguration.StartMods);
            }
            catch (Exception ex)
            {
                Logger.Error($"注销旧热键失败: {ex.Message}", ex);
            }
        }

        // 设置新的激活配置
        ActiveConfiguration = config;
        _multiConfigData.SetActiveConfiguration(configId);

        // 注册新配置的热键
        if (config.IsEnabled)
        {
            RegisterHotkeyForConfiguration(config);
        }

        Logger.Info($"已激活配置: {config.Name}");
    }

    /// <summary>
    /// 启用/禁用配置
    /// </summary>
    public void SetConfigurationEnabled(Guid configId, bool isEnabled)
    {
        var config = Configurations.FirstOrDefault(c => c.Id == configId);
        if (config == null)
        {
            Logger.Warning($"未找到配置: {configId}");
            return;
        }

        config.IsEnabled = isEnabled;

        // 如果是激活配置，更新热键注册状态
        if (ActiveConfiguration?.Id == configId)
        {
            if (isEnabled)
            {
                RegisterHotkeyForConfiguration(config);
            }
            else
            {
                if (config.StartKey.HasValue)
                {
                    try
                    {
                        _hotkeyService.UnregisterHotkey(config.StartKey.Value, config.StartMods);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"注销热键失败: {ex.Message}", ex);
                    }
                }
            }
        }

        Logger.Info($"配置 {config.Name} 已{(isEnabled ? "启用" : "禁用")}");
    }

    /// <summary>
    /// 为配置注册热键
    /// </summary>
    private void RegisterHotkeyForConfiguration(KeyConfiguration config)
    {
        if (!config.StartKey.HasValue)
        {
            Logger.Warning($"配置 {config.Name} 没有设置热键");
            return;
        }

        try
        {
            // 准备按键操作列表
            var operations = config.Keys
                .Where(k => k.IsSelected)
                .Select(k => new KeyItemSettings
                {
                    Type = k.Type,
                    KeyCode = k.Code,
                    X = k.X,
                    Y = k.Y,
                    Interval = k.KeyInterval
                })
                .ToList();

            // 注册热键
            _hotkeyService.RegisterHotkey(
                config.StartKey.Value,
                config.StartMods,
                saveToConfig: false
            );

            // 设置按键序列
            _hotkeyService.SetKeySequence(operations);

            Logger.Info($"已为配置 {config.Name} 注册热键: {config.GetStartHotkeyText()}");
        }
        catch (Exception ex)
        {
            Logger.Error($"注册热键失败: {ex.Message}", ex);
            throw;
        }
    }

    /// <summary>
    /// 验证配置名称是否重复
    /// </summary>
    public bool IsNameDuplicate(string name, Guid? excludeId = null)
    {
        return Configurations.Any(c =>
            c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
            (!excludeId.HasValue || c.Id != excludeId.Value)
        );
    }

    /// <summary>
    /// 验证热键是否冲突
    /// </summary>
    public bool IsHotkeyConflict(VirtualKeyCode key, ModifierKeys mods, Guid? excludeId = null)
    {
        return Configurations.Any(c =>
            c.StartKey == key &&
            c.StartMods == mods &&
            (!excludeId.HasValue || c.Id != excludeId.Value)
        );
    }

    /// <summary>
    /// 获取配置统计信息
    /// </summary>
    public string GetStatistics()
    {
        var totalConfigs = Configurations.Count;
        var enabledConfigs = Configurations.Count(c => c.IsEnabled);
        var totalKeys = Configurations.Sum(c => c.Keys.Count);

        return $"配置总数: {totalConfigs}, 已启用: {enabledConfigs}, 按键总数: {totalKeys}";
    }

    public void Dispose()
    {
        // 注销所有热键
        foreach (var config in Configurations)
        {
            if (config.StartKey.HasValue)
            {
                try
                {
                    _hotkeyService.UnregisterHotkey(config.StartKey.Value, config.StartMods);
                }
                catch (Exception ex)
                {
                    Logger.Error($"注销热键失败: {ex.Message}", ex);
                }
            }
        }

        Configurations.Clear();
        Logger.Info("KeyConfigurationService 已释放");
    }
}
