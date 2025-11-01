using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using WpfApp.Services.Models;
using WpfApp.Services.Utils;

namespace WpfApp.Services.Core
{
    /// <summary>
    /// 统一配置管理服务
    /// 负责管理全局配置、按键配置和配置文件的加载、保存、切换等操作
    /// 采用实时保存策略，所有配置更改立即持久化到磁盘
    /// </summary>

    public class ConfigManager : IConfigManager
    {
        #region 私有字段
        
        private static ConfigManager _instance;
        private static readonly object _instanceLock = new object();
        
        /// <summary>
        /// 配置锁：保护共享状态的访问，确保线程安全
        /// 注意：I/O 操作应在锁外执行，避免长时间持有锁
        /// </summary>
        private readonly object _configLock = new object();
        
        private readonly SerilogManager _logger = SerilogManager.Instance;
        private readonly PathService _pathService = PathService.Instance;

        private string _configDir;
        private string _globalConfigPath;
        private string _keyConfigPath;

        /// <summary>全局配置实例</summary>
        private GlobalConfig _globalConfig;

        /// <summary>当前按键配置实例</summary>
        private KeyConfigData _currentKeyConfig;
        
        #endregion
        
        #region 公共属性和事件
        
        /// <summary>
        /// 配置变更事件
        /// 当配置发生变更时触发，订阅者可根据 ConfigChangeType 执行相应操作
        /// </summary>
        public event EventHandler<ConfigEventArgs> ConfigChanged;
        
        /// <summary>
        /// 获取全局配置
        /// 包含 UI 设置、调试设置、音频设置等应用级配置
        /// </summary>
        public GlobalConfig GlobalConfig => _globalConfig;

        /// <summary>
        /// 获取当前按键配置
        /// 包含热键、按键序列、目标窗口等按键相关配置
        /// </summary>
        public KeyConfigData CurrentKeyConfig => _currentKeyConfig;
        
        #endregion
        
        #region 单例模式
        
        /// <summary>
        /// 获取 ConfigManager 的单例实例
        /// 使用双重检查锁定模式确保线程安全
        /// </summary>
        public static ConfigManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        _instance ??= new ConfigManager();
                    }
                }
                return _instance;
            }
        }
        
        /// <summary>
        /// 私有构造函数，确保单例模式
        /// 初始化配置路径信息
        /// </summary>
        private ConfigManager()
        {
            _configDir = _pathService.ConfigPath;
            _globalConfigPath = _pathService.GetGlobalConfigPath();
            _keyConfigPath = _pathService.GetKeyConfigPath();
        }
        
        #endregion
        
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

                // 加载当前按键配置
                LoadCurrentKeyConfig();

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
                    _currentKeyConfig = CreateDefaultKeyConfig();

                    // 保存默认配置
                    SaveGlobalConfig();
                    SaveCurrentKeyConfig();

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
                
                lock (_configLock)
                {
                    _globalConfig = loadedConfig;
                }
                
                if (!File.Exists(_globalConfigPath))
                {
                    SaveGlobalConfig();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"加载全局配置失败: {ex.Message}", ex);
                _logger.Warning("将使用默认全局配置");
                
                lock (_configLock)
                {
                    _globalConfig = CreateDefaultGlobalConfig();
                }
                
                SaveGlobalConfig();
            }
        }
        
        private void LoadCurrentKeyConfig()
        {
            try
            {
                KeyConfigData loadedConfig;
                bool needsSave = false;

                if (File.Exists(_keyConfigPath))
                {
                    try
                    {
                        var json = File.ReadAllText(_keyConfigPath);

                        if (string.IsNullOrWhiteSpace(json))
                        {
                            _logger.Warning($"配置文件为空: {_keyConfigPath}");
                            loadedConfig = CreateDefaultKeyConfig();
                            needsSave = true;
                        }
                        else
                        {
                            loadedConfig = JsonConvert.DeserializeObject<KeyConfigData>(json) ?? CreateDefaultKeyConfig();

                            if (loadedConfig.keys == null || loadedConfig.keys.Count == 0)
                            {
                                var defaultConfig = CreateDefaultKeyConfig();
                                loadedConfig.keys = defaultConfig.keys;
                                needsSave = true;
                            }

                            _logger.Debug($"已加载按键配置，按键数量: {loadedConfig.keys.Count}");
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.Error($"配置文件格式错误: {_keyConfigPath}", jsonEx);
                        loadedConfig = CreateDefaultKeyConfig();
                        needsSave = true;
                    }
                }
                else
                {
                    _logger.Warning($"配置文件不存在: {_keyConfigPath}，创建默认配置");
                    loadedConfig = CreateDefaultKeyConfig();
                    needsSave = true;
                }

                lock (_configLock)
                {
                    _currentKeyConfig = loadedConfig;
                }

                if (needsSave)
                {
                    SaveCurrentKeyConfig();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"加载按键配置失败: {ex.Message}", ex);
                _logger.Warning("将使用默认按键配置");

                lock (_configLock)
                {
                    _currentKeyConfig = CreateDefaultKeyConfig();
                }

                try
                {
                    SaveCurrentKeyConfig();
                }
                catch (Exception saveEx)
                {
                    _logger.Error($"保存默认配置失败: {saveEx.Message}", saveEx);
                }
            }
        }
        
        private void SaveGlobalConfig()
        {
            GlobalConfig configSnapshot;
            
            lock (_configLock)
            {
                configSnapshot = _globalConfig;
            }
            
            var json = JsonConvert.SerializeObject(configSnapshot, Formatting.Indented);
            File.WriteAllText(_globalConfigPath, json);
            _logger.Debug("全局配置已保存");
        }
        
        private void SaveCurrentKeyConfig()
        {
            KeyConfigData configSnapshot;

            lock (_configLock)
            {
                if (_currentKeyConfig == null)
                {
                    _logger.Warning("按键配置数据为空，无法保存");
                    return;
                }

                configSnapshot = _currentKeyConfig;
            }

            try
            {
                // 确保目录存在
                var directory = Path.GetDirectoryName(_keyConfigPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.Debug($"创建配置目录: {directory}");
                }

                var json = JsonConvert.SerializeObject(configSnapshot, Formatting.Indented);
                File.WriteAllText(_keyConfigPath, json);

                _logger.Debug("按键配置已保存");
            }
            catch (Exception ex)
            {
                _logger.Error($"保存按键配置失败: {_keyConfigPath}", ex);
                throw;
            }
        }
        
        /// <summary>
        /// 创建默认全局配置
        /// 返回包含默认值的全局配置对象
        /// </summary>
        /// <returns>默认全局配置实例</returns>
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
                        IsEnabled = true
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
                soundEnabled = false,
                IsReduceKeyStuck = true,
                SoundVolume = 0.8,
                AutoSwitchToEnglishIME = true,
                isHotkeyControlEnabled = true
            };
        }
        
        /// <summary>
        /// 创建默认按键配置
        /// 返回包含默认热键和按键序列的配置对象
        /// </summary>
        /// <returns>默认按键配置实例</returns>
        private KeyConfigData CreateDefaultKeyConfig()
        {
            return new KeyConfigData
            {
                startKey = VirtualKeyCode.VK_F9,
                startMods = 0,
                stopKey = VirtualKeyCode.VK_F9,
                stopMods = 0,
                keys = new List<KeyConfig>
                {
                    new KeyConfig(VirtualKeyCode.VK_F, true, 10),
                    new KeyConfig(VirtualKeyCode.VK_1, true, 10),
                    new KeyConfig(VirtualKeyCode.VK_2, true, 10),
                    new KeyConfig(VirtualKeyCode.VK_3, true, 10)
                },
                keyMode = 0,
                interval = 10,
                KeyPressInterval = 5,
                TargetWindowClassName = null,
                TargetWindowProcessName = null,
                TargetWindowTitle = null
            };
        }
        
        #endregion
        
        #region 事件触发方法
        
        /// <summary>
        /// 触发配置变更事件
        /// 通知所有订阅者配置已发生变更
        /// </summary>
        /// <param name="changeType">变更类型</param>
        /// <param name="globalConfig">全局配置（仅在 Global 类型时传递）</param>
        /// <param name="keyConfig">按键配置（仅在 Key 类型时传递）</param>
        /// <remarks>
        /// 事件触发规则：
        /// - Global: 仅传递 globalConfig，用于通知全局设置变更
        /// - Key: 传递 keyConfig，用于通知按键配置变更
        /// </remarks>
        private void RaiseConfigChanged(ConfigChangeType changeType, GlobalConfig globalConfig = null,
            KeyConfigData keyConfig = null)
        {
            try
            {
                var args = new ConfigEventArgs(changeType, globalConfig, keyConfig);
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
            
            GlobalConfig configSnapshot;
            
            lock (_configLock)
            {
                if (_globalConfig == null)
                {
                    _logger.Warning("Global配置为空，无法更新");
                    return;
                }
                updateAction(_globalConfig);
                configSnapshot = _globalConfig;
            }
            
            SaveGlobalConfig();
            RaiseConfigChanged(ConfigChangeType.Global, configSnapshot, null);
            _logger.Debug("Global配置已更新并保存");
        }
        
        /// <summary>
        /// 更新当前按键配置
        /// 执行更新操作后立即保存到磁盘，并触发 ConfigChanged 事件
        /// </summary>
        public void UpdateKeyConfig(Action<KeyConfigData> updateAction)
        {
            if (updateAction == null)
                throw new ArgumentNullException(nameof(updateAction));

            KeyConfigData configSnapshot;

            lock (_configLock)
            {
                if (_currentKeyConfig == null)
                {
                    _logger.Warning("Key配置为空，无法更新");
                    return;
                }
                updateAction(_currentKeyConfig);
                configSnapshot = _currentKeyConfig;
            }

            SaveCurrentKeyConfig();
            RaiseConfigChanged(ConfigChangeType.Key, null, configSnapshot);
            _logger.Debug("Key配置已更新并保存");
        }
        
        #endregion
        
        #region 配置切换方法
        
        #endregion
        
        #region 配置文件操作方法
        
        #endregion
        
        #region 配置导入导出方法
        
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
                lock (_configLock)
                {
                    // 清理事件订阅
                    ConfigChanged = null;
                    
                    _logger.Debug("配置管理器资源已清理");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("清理配置管理器资源失败", ex);
            }
        }
        
        #endregion
    }
} 