using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using WpfApp.Services.Models;
using WpfApp.Services.Utils;

namespace WpfApp.Services.Core
{
    #region 接口定义

    /// <summary>
    /// 统一配置管理接口
    /// </summary>
    public interface IConfigManager
    {
        event EventHandler<ConfigEventArgs> ConfigChanged;
        GlobalConfig GlobalConfig { get; }
        KeyConfigData CurrentKeyConfig { get; }
        MultiKeyConfigData MultiKeyConfigData { get; }
        void Initialize();
        void UpdateGlobalConfig(Action<GlobalConfig> updateAction);
        void UpdateKeyConfig(Action<KeyConfigData> updateAction);
        void UpdateMultiKeyConfig(Action<MultiKeyConfigData> updateAction);
        void Cleanup();
    }

    #endregion

    /// <summary>
    /// 统一配置管理服务
    /// 负责管理全局配置、多按键配置和配置文件的加载、保存等操作
    /// 采用实时保存策略，所有配置更改立即持久化到磁盘
    /// </summary>
    public class ConfigManager : IConfigManager
    {
        #region 私有字段

        private readonly ISerilogManager _logger;
        private readonly IPathService _pathService;

        private string _configDir;
        private string _globalConfigPath;
        private string _multiKeyConfigPath;

        /// <summary>全局配置实例</summary>
        private GlobalConfig _globalConfig;

        /// <summary>多配置数据实例</summary>
        private MultiKeyConfigData _multiKeyConfigData;

        #endregion

        #region 公共属性和事件

        /// <summary>
        /// 配置变更事件
        /// 当配置发生变更时触发，订阅者可根据 ConfigChangeType 执行相应操作
        /// </summary>
        public event EventHandler<ConfigEventArgs> ConfigChanged;

        /// <summary>
        /// 获取全局配置
        /// 包含 UI 设置、调试设置等应用级配置
        /// </summary>
        public GlobalConfig GlobalConfig => _globalConfig;

        /// <summary>
        /// 获取多配置数据
        /// 包含所有按键配置方案
        /// </summary>
        public MultiKeyConfigData MultiKeyConfigData => _multiKeyConfigData;

        /// <summary>
        /// 获取当前激活的配置
        /// </summary>
        public KeyConfiguration ActiveConfiguration => _multiKeyConfigData?.GetActiveConfiguration();

        /// <summary>
        /// 获取当前按键配置（兼容性属性，已废弃）
        /// </summary>
        [Obsolete("请使用 ActiveConfiguration 或 MultiKeyConfigData")]
        public KeyConfigData CurrentKeyConfig => null;

        #endregion

        /// <summary>
        /// 构造函数：初始化配置管理器
        /// </summary>
        public ConfigManager(ISerilogManager logger, IPathService pathService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));

            _configDir = _pathService.ConfigPath;
            _globalConfigPath = _pathService.GetGlobalConfigPath();
            _multiKeyConfigPath = Path.Combine(_configDir, "multi_key_config.json");
        }

        #region 初始化方法

        /// <summary>
        /// 初始化配置管理器
        /// </summary>
        public void Initialize()
        {
            try
            {
                _logger.Debug("开始初始化配置管理器");

                // 确保配置目录存在
                if (!Directory.Exists(_configDir))
                {
                    Directory.CreateDirectory(_configDir);
                    _logger.Debug($"已创建配置目录: {_configDir}");
                }

                // 加载全局配置
                LoadGlobalConfig();

                // 加载多配置数据
                LoadMultiKeyConfig();

                _logger.Debug("配置管理器初始化完成");
            }
            catch (Exception ex)
            {
                _logger.Error($"初始化配置管理器失败: {ex.Message}", ex);

                try
                {
                    _logger.Warning("尝试使用默认配置初始化配置管理器");

                    // 确保配置目录存在
                    if (!Directory.Exists(_configDir))
                    {
                        Directory.CreateDirectory(_configDir);
                    }

                    // 创建默认配置
                    _globalConfig = CreateDefaultGlobalConfig();
                    _multiKeyConfigData = CreateDefaultMultiKeyConfig();

                    // 保存默认配置
                    SaveGlobalConfig();
                    SaveMultiKeyConfig();

                    _logger.Warning("已使用默认配置初始化配置管理器");
                }
                catch (Exception fallbackEx)
                {
                    _logger.Error($"创建默认配置失败: {fallbackEx.Message}", fallbackEx);
                    throw new InvalidOperationException("无法初始化配置管理器，请检查配置目录权限", fallbackEx);
                }
            }
        }

        #endregion

        #region 配置保存和加载方法

        private void LoadGlobalConfig()
        {
            try
            {
                GlobalConfig loadedConfig;

                if (File.Exists(_globalConfigPath))
                {
                    var json = File.ReadAllText(_globalConfigPath);
                    loadedConfig = JsonConvert.DeserializeObject<GlobalConfig>(json) ?? CreateDefaultGlobalConfig();
                    _logger.Debug("全局配置加载成功");
                }
                else
                {
                    loadedConfig = CreateDefaultGlobalConfig();
                    _logger.Debug("全局配置不存在，已创建默认配置");
                }

                _globalConfig = loadedConfig;

                if (!File.Exists(_globalConfigPath))
                {
                    SaveGlobalConfig();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"加载全局配置失败: {ex.Message}", ex);
                _logger.Warning("将使用默认全局配置");

                _globalConfig = CreateDefaultGlobalConfig();

                SaveGlobalConfig();
            }
        }

        private void LoadMultiKeyConfig()
        {
            try
            {
                MultiKeyConfigData loadedConfig;
                bool needsSave = false;

                if (File.Exists(_multiKeyConfigPath))
                {
                    try
                    {
                        var json = File.ReadAllText(_multiKeyConfigPath);

                        if (string.IsNullOrWhiteSpace(json))
                        {
                            _logger.Warning($"多配置文件为空: {_multiKeyConfigPath}");
                            loadedConfig = CreateDefaultMultiKeyConfig();
                            needsSave = true;
                        }
                        else
                        {
                            loadedConfig = JsonConvert.DeserializeObject<MultiKeyConfigData>(json) ?? CreateDefaultMultiKeyConfig();

                            // 确保配置列表不为 null
                            if (loadedConfig.Configurations == null)
                            {
                                loadedConfig.Configurations = new List<KeyConfiguration>();
                            }

                            // 如果没有配置，创建默认配置
                            if (loadedConfig.Configurations.Count == 0)
                            {
                                var defaultConfig = CreateDefaultKeyConfiguration();
                                loadedConfig.AddConfiguration(defaultConfig);
                                needsSave = true;
                            }

                            _logger.Debug($"已加载多配置数据，配置数量: {loadedConfig.Configurations.Count}");
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.Error($"多配置文件格式错误: {_multiKeyConfigPath}", jsonEx);
                        loadedConfig = CreateDefaultMultiKeyConfig();
                        needsSave = true;
                    }
                }
                else
                {
                    _logger.Warning($"多配置文件不存在: {_multiKeyConfigPath}，创建默认配置");
                    loadedConfig = CreateDefaultMultiKeyConfig();
                    needsSave = true;
                }

                _multiKeyConfigData = loadedConfig;

                if (needsSave)
                {
                    SaveMultiKeyConfig();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"加载多配置数据失败: {ex.Message}", ex);
                _logger.Warning("将使用默认多配置数据");

                _multiKeyConfigData = CreateDefaultMultiKeyConfig();

                try
                {
                    SaveMultiKeyConfig();
                }
                catch (Exception saveEx)
                {
                    _logger.Error($"保存默认多配置失败: {saveEx.Message}", saveEx);
                }
            }
        }

        private void SaveGlobalConfig()
        {
            var json = JsonConvert.SerializeObject(_globalConfig, Formatting.Indented);
            File.WriteAllText(_globalConfigPath, json);
            _logger.Debug("全局配置已保存");
        }

        private void SaveMultiKeyConfig()
        {
            if (_multiKeyConfigData == null)
            {
                _logger.Warning("多配置数据为空，无法保存");
                return;
            }

            try
            {
                // 确保目录存在
                var directory = Path.GetDirectoryName(_multiKeyConfigPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.Debug($"创建配置目录: {directory}");
                }

                var json = JsonConvert.SerializeObject(_multiKeyConfigData, Formatting.Indented);
                File.WriteAllText(_multiKeyConfigPath, json);

                _logger.Debug($"多配置数据已保存，配置数量: {_multiKeyConfigData.Configurations.Count}");
            }
            catch (Exception ex)
            {
                _logger.Error($"保存多配置数据失败: {_multiKeyConfigPath}", ex);
                throw;
            }
        }

        /// <summary>
        /// 创建默认全局配置
        /// 返回包含默认值的全局配置对象
        /// </summary>
        private GlobalConfig CreateDefaultGlobalConfig()
        {
            return new GlobalConfig
            {
                UI = new UIConfig
                {
                    MainWindow = new WindowConfig
                    {
                        Width = 800,
                        Height = 660
                    },
                    FloatingWindow = new FloatingWindowConfig
                    {
                        Left = 0,
                        Top = 0,
                        IsEnabled = true,
                        Opacity = 0.8
                    }
                },
                Debug = new DebugConfig
                {
                    IsDebugMode = false,
                    LogLevel = "Debug",
                    FileSettings = new LogFileSettings
                    {
                        MaxFileSize = 10,
                        MaxFileCount = 10,
                        RollingInterval = "Day",
                        RetainDays = 7
                    }
                },
                isHotkeyControlEnabled = true,
                EnableHardwareAcceleration = true,
                SelectedDriver = "AHK"
            };
        }

        /// <summary>
        /// 创建默认多配置数据
        /// </summary>
        private MultiKeyConfigData CreateDefaultMultiKeyConfig()
        {
            var multiConfig = new MultiKeyConfigData();
            var defaultConfig = CreateDefaultKeyConfiguration();
            multiConfig.AddConfiguration(defaultConfig);
            return multiConfig;
        }

        /// <summary>
        /// 创建默认按键配置
        /// </summary>
        private KeyConfiguration CreateDefaultKeyConfiguration()
        {
            return new KeyConfiguration("默认配置")
            {
                StartKey = VirtualKeyCode.VK_F9,
                StartMods = 0,
                StopKey = VirtualKeyCode.VK_F9,
                StopMods = 0,
                ExecutionMode = KeyExecutionMode.Sequence,
                Interval = 10,
                KeyPressInterval = 5,
                IsReduceKeyStuck = true,
                SoundEnabled = false,
                SoundVolume = 0.8,
                AutoSwitchToEnglishIME = true,
                Keys = new List<KeyConfig>
                {
                    new KeyConfig(VirtualKeyCode.VK_F, true, 10),
                    new KeyConfig(VirtualKeyCode.VK_1, true, 10),
                    new KeyConfig(VirtualKeyCode.VK_2, true, 10),
                    new KeyConfig(VirtualKeyCode.VK_3, true, 10)
                },
                TargetWindows = new List<TargetWindow>()
            };
        }

        #endregion

        #region 事件触发方法

        /// <summary>
        /// 触发配置变更事件
        /// </summary>
        private void RaiseConfigChanged(ConfigChangeType changeType, GlobalConfig globalConfig = null,
            MultiKeyConfigData multiKeyConfig = null)
        {
            try
            {
                var args = new ConfigEventArgs(changeType, globalConfig, multiKeyConfig);
                ConfigChanged?.Invoke(this, args);
                _logger.Debug($"触发配置变更事件: {changeType}");
            }
            catch (Exception ex)
            {
                _logger.Error($"触发配置变更事件失败: {changeType}", ex);
            }
        }

        #endregion

        #region 配置更新方法

        /// <summary>
        /// 更新全局配置
        /// 执行更新操作后立即保存到磁盘，并触发 ConfigChanged 事件
        /// </summary>
        public void UpdateGlobalConfig(Action<GlobalConfig> updateAction)
        {
            if (updateAction == null)
                throw new ArgumentNullException(nameof(updateAction));

            if (_globalConfig == null)
            {
                _logger.Warning("Global配置为空，无法更新");
                return;
            }

            updateAction(_globalConfig);

            SaveGlobalConfig();
            RaiseConfigChanged(ConfigChangeType.Global, _globalConfig, null);
            _logger.Debug("Global配置已更新并保存");
        }

        /// <summary>
        /// 更新多配置数据
        /// 执行更新操作后立即保存到磁盘，并触发 ConfigChanged 事件
        /// </summary>
        public void UpdateMultiKeyConfig(Action<MultiKeyConfigData> updateAction)
        {
            if (updateAction == null)
                throw new ArgumentNullException(nameof(updateAction));

            if (_multiKeyConfigData == null)
            {
                _logger.Warning("多配置数据为空，无法更新");
                return;
            }

            updateAction(_multiKeyConfigData);

            SaveMultiKeyConfig();
            RaiseConfigChanged(ConfigChangeType.MultiKey, null, _multiKeyConfigData);
            _logger.Debug("多配置数据已更新并保存");
        }

        /// <summary>
        /// 更新当前按键配置（已废弃，保留用于兼容性）
        /// </summary>
        [Obsolete("请使用 UpdateMultiKeyConfig")]
        public void UpdateKeyConfig(Action<KeyConfigData> updateAction)
        {
            _logger.Warning("UpdateKeyConfig 已废弃，请使用 UpdateMultiKeyConfig");
        }

        #endregion

        #region 资源清理和辅助方法

        /// <summary>
        /// 清理配置管理器资源
        /// 取消所有事件订阅，释放资源
        /// 注意：实时保存策略下，所有配置已自动保存，无需手动保存
        /// </summary>
        public void Cleanup()
        {
            try
            {
                // 清理事件订阅
                ConfigChanged = null;

                _logger.Debug("配置管理器资源已清理");
            }
            catch (Exception ex)
            {
                _logger.Error("清理配置管理器资源失败", ex);
            }
        }

        #endregion
    }

    /// <summary>
    /// 配置变更类型枚举
    /// </summary>
    public enum ConfigChangeType
    {
        /// <summary>全局配置变更</summary>
        Global,

        /// <summary>多配置数据变更</summary>
        MultiKey,

        /// <summary>所有配置变更</summary>
        All
    }

    /// <summary>
    /// 配置变更事件参数
    /// </summary>
    public class ConfigEventArgs : EventArgs
    {
        public ConfigChangeType ChangeType { get; }
        public GlobalConfig GlobalConfigData { get; }
        public MultiKeyConfigData MultiKeyConfigData { get; }

        public ConfigEventArgs(ConfigChangeType changeType, GlobalConfig globalConfig, MultiKeyConfigData multiKeyConfig)
        {
            ChangeType = changeType;
            GlobalConfigData = globalConfig;
            MultiKeyConfigData = multiKeyConfig;
        }
    }
}
