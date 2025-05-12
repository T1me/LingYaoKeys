using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using WpfApp.Services.Models;
using WpfApp.Services.Utils;

namespace WpfApp.Services.Core
{
    /// <summary>
    /// 统一配置管理服务
    /// </summary>
    public class ConfigManager : IConfigManager
    {
        private static ConfigManager _instance;
        private static readonly object _instanceLock = new object();
        
        // 使用单一的简单对象锁替代ReaderWriterLockSlim
        private readonly object _configLock = new object();
        private readonly SerilogManager _logger = SerilogManager.Instance;
        private readonly PathService _pathService = PathService.Instance;
        
        private string _configDir;
        private string _globalConfigPath;
        private const string CONFIG_INDEX_FILE = "config_index.json";
        private const int MAX_BACKUP_FILES = 5;
        
        private GlobalConfig _globalConfig;
        private KeyConfigData _currentKeyConfig;
        private ObservableCollection<ConfigFileInfo> _configFiles;
        private ConfigFileInfo _currentConfig;
        
        // 脏标记，用于标识配置是否已修改
        private bool _isGlobalConfigDirty = false;
        private bool _isKeyConfigDirty = false;
        private bool _isConfigIndexDirty = false;
        
        /// <summary>
        /// 配置变更事件
        /// </summary>
        public event EventHandler<ConfigEventArgs> ConfigChanged;
        
        /// <summary>
        /// 获取全局配置
        /// </summary>
        public GlobalConfig GlobalConfig => _globalConfig;
        
        /// <summary>
        /// 获取当前按键配置
        /// </summary>
        public KeyConfigData CurrentKeyConfig => _currentKeyConfig;
        
        /// <summary>
        /// 获取所有配置文件
        /// </summary>
        public ObservableCollection<ConfigFileInfo> ConfigFiles => _configFiles;
        
        /// <summary>
        /// 获取当前配置文件
        /// </summary>
        public ConfigFileInfo CurrentConfig => _currentConfig;
        
        /// <summary>
        /// 获取单例实例
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
        /// </summary>
        private ConfigManager()
        {
            _configFiles = new ObservableCollection<ConfigFileInfo>();
            _configDir = _pathService.ConfigPath;
            _globalConfigPath = _pathService.GetGlobalConfigPath();
        }
        
        /// <summary>
        /// 初始化配置管理器
        /// </summary>
        public void Initialize()
            {
                try
            {
                lock (_configLock)
                {
                    _logger.Debug("开始初始化配置管理器");
                    
                    // 确保配置目录存在
                    Directory.CreateDirectory(_configDir);
                    
                    // 初始化配置文件列表
                    InitializeConfigFiles();
                    
                    // 加载全局配置
                    LoadGlobalConfig();
                    
                    // 加载当前配置文件的按键配置
                    LoadCurrentKeyConfig();
                    
                    _logger.Debug("配置管理器初始化完成");
                }
                }
                catch (Exception ex)
                {
                    _logger.Error("初始化配置管理器失败", ex);
                    
                    // 创建默认配置
                    _globalConfig = CreateDefaultGlobalConfig();
                    _currentKeyConfig = CreateDefaultKeyConfig();
                    
                    // 保存默认配置
                _isGlobalConfigDirty = true;
                _isKeyConfigDirty = true;
                
                lock (_configLock)
                {
                    SaveGlobalConfig();
                    SaveCurrentKeyConfig();
                }
            }
        }
        
        /// <summary>
        /// 初始化配置文件列表
        /// </summary>
        private void InitializeConfigFiles()
        {
            try
            {
                var indexPath = Path.Combine(_configDir, CONFIG_INDEX_FILE);
                
                if (!File.Exists(indexPath))
                {
                    // 创建默认配置文件索引
                    CreateDefaultConfigIndex();
                }
                else
                {
                    // 加载现有配置文件索引
                    var json = File.ReadAllText(indexPath);
                    var files = JsonConvert.DeserializeObject<List<ConfigFileInfo>>(json);
                    
                    if (files == null || files.Count == 0)
                    {
                        // 索引文件为空或无效，创建默认配置
                        CreateDefaultConfigIndex();
                    }
                    else
                    {
                        _configFiles.Clear();
                        foreach (var file in files)
                        {
                            // 检查配置文件是否存在
                            if (File.Exists(file.FilePath))
                            {
                                _configFiles.Add(file);
                            }
                        }
                        
                        // 如果没有默认配置，设置第一个为默认
                        if (!_configFiles.Any(c => c.IsDefault))
                        {
                            if (_configFiles.Count > 0)
                            {
                                _configFiles[0].IsDefault = true;
                                _isConfigIndexDirty = true;
                            }
                            else
                            {
                                // 如果没有任何有效配置，创建默认配置
                                CreateDefaultConfigIndex();
                            }
                        }
                    }
                }
                
                // 设置当前配置为默认配置
                _currentConfig = _configFiles.FirstOrDefault(c => c.IsDefault) ?? _configFiles.FirstOrDefault();
                
                // 保存配置索引
                if (_isConfigIndexDirty)
                {
                SaveConfigIndex();
                }
                
                _logger.Debug($"配置文件列表初始化完成，当前配置：{_currentConfig?.Name ?? "无"}");
            }
            catch (Exception ex)
            {
                _logger.Error("初始化配置文件列表失败", ex);
                CreateDefaultConfigIndex();
            }
        }
        
        /// <summary>
        /// 创建默认配置文件索引
        /// </summary>
        private void CreateDefaultConfigIndex()
        {
            _configFiles.Clear();
            
            // 添加默认配置
            var defaultConfig = new ConfigFileInfo
            {
                Name = "默认配置",
                FilePath = Path.Combine(_configDir, "default_keyconfig.json"),
                IsDefault = true,
                LastEditTime = DateTime.Now
            };
            
            _configFiles.Add(defaultConfig);
            _currentConfig = defaultConfig;
            
            // 标记索引为脏
            _isConfigIndexDirty = true;
            
            // 保存配置索引
            SaveConfigIndex();
            
            _logger.Debug("已创建默认配置文件索引");
        }
        
        /// <summary>
        /// 保存配置文件索引
        /// </summary>
        private void SaveConfigIndex()
        {
            // 如果索引没有变更，不需要保存
            if (!_isConfigIndexDirty)
            {
                return;
            }
            
            try
            {
                var indexPath = Path.Combine(_configDir, CONFIG_INDEX_FILE);
                var json = JsonConvert.SerializeObject(_configFiles, Formatting.Indented);
                File.WriteAllText(indexPath, json);
                _isConfigIndexDirty = false; // 重置脏标记
                _logger.Debug("配置文件索引已保存");
            }
            catch (Exception ex)
            {
                _logger.Error("保存配置文件索引失败", ex);
            }
        }
        
        /// <summary>
        /// 加载全局配置
        /// </summary>
        private void LoadGlobalConfig()
        {
            try
            {
                if (File.Exists(_globalConfigPath))
                {
                    var json = File.ReadAllText(_globalConfigPath);
                    _globalConfig = JsonConvert.DeserializeObject<GlobalConfig>(json) ?? CreateDefaultGlobalConfig();
                    _logger.Debug("全局配置加载成功");
                }
                else
                {
                    _globalConfig = CreateDefaultGlobalConfig();
                    _isGlobalConfigDirty = true;
                    SaveGlobalConfig();
                    _logger.Debug("全局配置不存在，已创建默认配置");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("加载全局配置失败", ex);
                _globalConfig = CreateDefaultGlobalConfig();
                _isGlobalConfigDirty = true;
                SaveGlobalConfig();
            }
        }
        
        /// <summary>
        /// 加载当前配置文件的按键配置
        /// </summary>
        private void LoadCurrentKeyConfig()
        {
            try
            {
                if (_currentConfig != null && File.Exists(_currentConfig.FilePath))
                {
                    var json = File.ReadAllText(_currentConfig.FilePath);
                    _currentKeyConfig = JsonConvert.DeserializeObject<KeyConfigData>(json) ?? CreateDefaultKeyConfig();
                    
                    // 验证按键配置中的keys列表不为空
                    if (_currentKeyConfig.keys == null || _currentKeyConfig.keys.Count == 0)
                    {
                        var defaultConfig = CreateDefaultKeyConfig();
                        _currentKeyConfig.keys = defaultConfig.keys;
                        _isKeyConfigDirty = true;
                    }
                    
                    _logger.Debug($"已加载按键配置: {_currentConfig.Name}, 按键数量: {_currentKeyConfig.keys.Count}");
                }
                else
                {
                    _currentKeyConfig = CreateDefaultKeyConfig();
                    _isKeyConfigDirty = true;
                    SaveCurrentKeyConfig();
                    _logger.Debug("按键配置不存在，已创建默认配置");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("加载按键配置失败", ex);
                _currentKeyConfig = CreateDefaultKeyConfig();
                _isKeyConfigDirty = true;
                SaveCurrentKeyConfig();
            }
        }
        
        /// <summary>
        /// 保存全局配置
        /// </summary>
        private void SaveGlobalConfig()
        {
            // 如果全局配置没有变更，不需要保存
            if (!_isGlobalConfigDirty)
            {
                return;
            }
            
            try
            {
                var json = JsonConvert.SerializeObject(_globalConfig, Formatting.Indented);
                File.WriteAllText(_globalConfigPath, json);
                _isGlobalConfigDirty = false; // 重置脏标记
                _logger.Debug("全局配置已保存");
            }
            catch (Exception ex)
            {
                _logger.Error("保存全局配置失败", ex);
            }
        }
        
        /// <summary>
        /// 保存当前配置文件的按键配置
        /// </summary>
        private void SaveCurrentKeyConfig()
        {
            // 如果按键配置没有变更，不需要保存
            if (!_isKeyConfigDirty)
            {
                return;
            }
            
            try
            {
                if (_currentConfig != null)
                {
                    var json = JsonConvert.SerializeObject(_currentKeyConfig, Formatting.Indented);
                    File.WriteAllText(_currentConfig.FilePath, json);
                    
                    // 更新最后编辑时间
                    _currentConfig.LastEditTime = DateTime.Now;
                    _isConfigIndexDirty = true;
                    _isKeyConfigDirty = false; // 重置脏标记
                    
                    // 保存配置索引
                    SaveConfigIndex();
                    
                    _logger.Debug($"按键配置已保存: {_currentConfig.Name}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("保存按键配置失败", ex);
            }
        }
        
        /// <summary>
        /// 创建默认全局配置
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
                        IsEnabled = true
                    }
                },
                Debug = new DebugConfig
                {
                    IsDebugMode = false,
                    EnableLogging = false,
                    LogLevel = "Debug",
                    FileSettings = new LogFileSettings
                    {
                        MaxFileSize = 10,
                        MaxFileCount = 10,
                        RollingInterval = "Day",
                        RetainDays = 7
                    },
                    ExcludedTags = new List<string>(),
                    ExcludedSources = new List<string>
                    {
                        "*.xaml*",
                        "ControlStyles.xaml"
                    },
                    ExcludedMethods = new List<string> { },
                    ExcludedPatterns = new List<string>
                    {
                        "窗口初始化完成*"
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
        /// </summary>
        private KeyConfigData CreateDefaultKeyConfig()
        {
            return new KeyConfigData
            {
                startKey = LyKeysCode.VK_F9,
                startMods = 0,
                stopKey = LyKeysCode.VK_F9,
                stopMods = 0,
                keys = new List<KeyConfig>
                {
                    new KeyConfig(LyKeysCode.VK_F, true, 10),
                    new KeyConfig(LyKeysCode.VK_1, true, 10),
                    new KeyConfig(LyKeysCode.VK_2, true, 10),
                    new KeyConfig(LyKeysCode.VK_3, true, 10)
                },
                keyMode = 0,
                interval = 10,
                KeyPressInterval = 5,
                TargetWindowClassName = null,
                TargetWindowProcessName = null,
                TargetWindowTitle = null
            };
        }
        
        /// <summary>
        /// 触发配置变更事件
        /// </summary>
        private void RaiseConfigChanged(ConfigChangeType changeType, GlobalConfig globalConfig = null, 
            KeyConfigData keyConfig = null, ConfigFileInfo configFile = null)
        {
            try
            {
                var args = new ConfigEventArgs(changeType, globalConfig, keyConfig, configFile);
                ConfigChanged?.Invoke(this, args);
                _logger.Debug($"触发配置变更事件: {changeType}");
            }
            catch (Exception ex)
            {
                _logger.Error($"触发配置变更事件失败: {changeType}", ex);
            }
        }
        
        /// <summary>
        /// 更新全局配置
        /// </summary>
        public void UpdateGlobalConfig(Action<GlobalConfig> updateAction)
            {
                try
            {
                lock (_configLock)
                {
                    if (_globalConfig == null) return;
                    
                    updateAction(_globalConfig);
                    _isGlobalConfigDirty = true;
                    SaveGlobalConfig();
                    
                    RaiseConfigChanged(ConfigChangeType.Global, _globalConfig);
                }
                
                    _logger.Debug("全局配置已更新");
                }
                catch (Exception ex)
                {
                    _logger.Error("更新全局配置失败", ex);
                    throw;
            }
        }
        
        /// <summary>
        /// 更新当前按键配置
        /// </summary>
        public void UpdateKeyConfig(Action<KeyConfigData> updateAction)
            {
                try
            {
                lock (_configLock)
                {
                    if (_currentKeyConfig == null) return;
                    
                    updateAction(_currentKeyConfig);
                    _isKeyConfigDirty = true;
                    SaveCurrentKeyConfig();
                    
                    RaiseConfigChanged(ConfigChangeType.Key, null, _currentKeyConfig);
                }
                
                    _logger.Debug("按键配置已更新");
                }
                catch (Exception ex)
                {
                    _logger.Error("更新按键配置失败", ex);
                    throw;
            }
        }
        
        /// <summary>
        /// 切换当前配置文件
        /// </summary>
        public void SwitchConfig(ConfigFileInfo configInfo)
        {
            if (configInfo == null || _currentConfig == configInfo) return;
            
                try
            {
                lock (_configLock)
                {
                    _logger.Debug($"切换配置: {_currentConfig?.Name} -> {configInfo.Name}");
                    
                    // 保存当前配置
                    if (_isKeyConfigDirty)
                    {
                        SaveCurrentKeyConfig();
                    }
                    
                    // 设置当前配置
                    _currentConfig = configInfo;
                    
                    // 加载新配置的按键配置
                    LoadCurrentKeyConfig();
                    
                    // 触发配置变更事件
                    RaiseConfigChanged(ConfigChangeType.ConfigFile, null, _currentKeyConfig, configInfo);
                }
                }
                catch (Exception ex)
                {
                    _logger.Error($"切换配置失败: {configInfo.Name}", ex);
                    throw;
            }
        }
        
        /// <summary>
        /// 创建新配置文件
        /// </summary>
        public ConfigFileInfo CreateNewConfig(string configName, bool copyFromCurrent = true)
            {
                try
            {
                lock (_configLock)
                {
                    // 验证配置名称
                    var validName = ValidateConfigName(configName);
                    
                    // 创建新配置文件路径
                    var newConfigPath = Path.Combine(_configDir, $"{validName}.json");
                    
                    // 创建新配置信息
                    var newConfig = new ConfigFileInfo(validName, newConfigPath);
                    
                    // 如果当前有配置且选择复制
                    if (copyFromCurrent && _currentConfig != null)
                    {
                        // 复制当前配置文件内容
                        File.Copy(_currentConfig.FilePath, newConfigPath, true);
                    }
                    else
                    {
                        // 创建空配置
                        var emptyConfig = CreateDefaultKeyConfig();
                        var json = JsonConvert.SerializeObject(emptyConfig, Formatting.Indented);
                        File.WriteAllText(newConfigPath, json);
                    }
                    
                    // 设置最后编辑时间
                    newConfig.LastEditTime = DateTime.Now;
                    
                    // 添加到配置列表
                    _configFiles.Add(newConfig);
                    
                    // 标记索引为脏
                    _isConfigIndexDirty = true;
                    
                    // 保存配置索引
                    SaveConfigIndex();
                    
                    // 触发配置变更事件
                    RaiseConfigChanged(ConfigChangeType.ConfigList, null, null, newConfig);
                    
                    _logger.Debug($"创建新配置: {newConfig.Name}, 路径: {newConfig.FilePath}");
                    
                    return newConfig;
                }
                }
                catch (Exception ex)
                {
                    _logger.Error($"创建新配置失败: {configName}", ex);
                    throw;
            }
        }
        
        /// <summary>
        /// 重命名配置文件
        /// </summary>
        public void RenameConfig(ConfigFileInfo configInfo, string newName)
        {
            if (configInfo == null) return;
            
                try
            {
                lock (_configLock)
                {
                    // 验证配置名称
                    newName = ValidateConfigName(newName, configInfo);
                    
                    // 保存旧文件路径
                    string oldFilePath = configInfo.FilePath;
                    
                    // 生成新文件路径
                    string newFilePath = Path.Combine(_configDir, $"{newName}.json");
                    
                    // 检查文件是否存在
                    if (File.Exists(oldFilePath))
                    {
                        // 检查目标文件是否已存在（防止路径冲突）
                        if (File.Exists(newFilePath) && !string.Equals(oldFilePath, newFilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new IOException($"目标文件已存在: {newFilePath}");
                        }
                        
                        // 重命名文件
                        if (!string.Equals(oldFilePath, newFilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Move(oldFilePath, newFilePath);
                            _logger.Debug($"重命名文件: {oldFilePath} -> {newFilePath}");
                        }
                    }
                    else
                    {
                        _logger.Warning($"源文件不存在，无法重命名物理文件: {oldFilePath}");
                    }
                    
                    // 更新名称和文件路径
                    configInfo.Name = newName;
                    configInfo.FilePath = newFilePath;
                    configInfo.UpdateEditTime(); // 更新编辑时间
                    
                    // 标记索引为脏
                    _isConfigIndexDirty = true;
                    
                    // 保存索引
                    SaveConfigIndex();
                    
                    // 触发配置变更事件
                    RaiseConfigChanged(ConfigChangeType.ConfigList, null, null, configInfo);
                    
                    _logger.Debug($"重命名配置: {configInfo.Name}, 路径: {configInfo.FilePath}");
                }
                }
                catch (Exception ex)
                {
                    _logger.Error($"重命名配置失败: {configInfo.Name} -> {newName}", ex);
                    throw;
            }
        }
        
        /// <summary>
        /// 删除配置文件
        /// </summary>
        public void DeleteConfig(ConfigFileInfo configInfo)
        {
            if (configInfo == null) return;
            
                try
            {
                lock (_configLock)
                {
                    // 不能删除默认配置
                    if (configInfo.IsDefault)
                    {
                        throw new InvalidOperationException("无法删除默认配置");
                    }
                    
                    // 首先保存目标配置文件的路径用于后续删除
                    string fileToDelete = configInfo.FilePath;
                    
                    // 判断删除的是否是当前配置
                    bool isCurrentConfig = (_currentConfig == configInfo);
                    ConfigFileInfo newCurrentConfig = null;
                    
                    // 从配置列表中移除配置信息
                    _configFiles.Remove(configInfo);
                    
                    // 标记索引为脏
                    _isConfigIndexDirty = true;
                    
                    // 如果删除的是当前配置，需要先确定新的当前配置
                    if (isCurrentConfig)
                    {
                        newCurrentConfig = _configFiles.FirstOrDefault(c => c.IsDefault) ?? _configFiles.FirstOrDefault();
                        
                        // 直接设置字段，避免触发不必要的事件
                        _currentConfig = newCurrentConfig;
                    }
                    
                    // 先保存配置索引文件，确保配置列表已更新
                    SaveConfigIndex();
                    
                    // 最后才删除物理文件，避免在事件处理过程中触发自动创建
                    if (File.Exists(fileToDelete))
                    {
                        File.Delete(fileToDelete);
                        _logger.Debug($"物理文件已删除: {fileToDelete}");
                    }
                    
                    // 在所有操作完成后，才触发事件通知
                    if (isCurrentConfig && newCurrentConfig != null)
                    {
                        // 加载新配置的按键配置
                        LoadCurrentKeyConfig();
                        
                        // 触发配置变更事件
                        _logger.Debug($"删除配置完成: {configInfo.Name}，当前配置已切换到: {newCurrentConfig.Name}");
                        
                        // 首先触发配置文件变更事件
                        RaiseConfigChanged(ConfigChangeType.ConfigFile, null, _currentKeyConfig, _currentConfig);
                    }
                    
                    // 然后触发配置列表变更事件
                    RaiseConfigChanged(ConfigChangeType.ConfigList, null, null, configInfo);
                    
                    _logger.Debug($"删除配置完成: {configInfo.Name}");
                }
                }
                catch (Exception ex)
                {
                    _logger.Error($"删除配置失败: {configInfo.Name}", ex);
                    throw;
            }
        }
        
        /// <summary>
        /// 设置配置文件快捷键
        /// </summary>
        public void SetConfigHotkey(ConfigFileInfo configInfo, string hotkeyText)
        {
            if (configInfo == null) return;
            
                try
            {
                lock (_configLock)
                {
                    // 更新快捷键
                    configInfo.ConfigHotkey = hotkeyText;
                    
                    // 标记索引为脏
                    _isConfigIndexDirty = true;
                    
                    // 保存索引
                    SaveConfigIndex();
                    
                    // 触发配置变更事件
                    RaiseConfigChanged(ConfigChangeType.ConfigList, null, null, configInfo);
                    
                    _logger.Debug($"设置配置快捷键: {configInfo.Name} -> {hotkeyText}");
                }
                }
                catch (Exception ex)
                {
                    _logger.Error($"设置配置快捷键失败: {configInfo.Name} -> {hotkeyText}", ex);
                    throw;
            }
        }
        
        /// <summary>
        /// 导入配置文件
        /// </summary>
        public ConfigFileInfo ImportKeyConfig(string sourceFile, string configName = null)
            {
                try
            {
                lock (_configLock)
                {
                    // 检查源文件是否存在
                    if (!File.Exists(sourceFile))
                    {
                        throw new FileNotFoundException($"找不到源文件: {sourceFile}");
                    }
                    
                    // 获取配置名称
                    if (string.IsNullOrEmpty(configName))
                    {
                        configName = Path.GetFileNameWithoutExtension(sourceFile);
                    }
                    
                    // 验证配置名称
                    var validName = ValidateConfigName(configName);
                    
                    // 创建新配置文件路径
                    var newConfigPath = Path.Combine(_configDir, $"{validName}.json");
                    
                    // 创建新配置信息
                    var newConfig = new ConfigFileInfo(validName, newConfigPath);
                    
                    // 复制配置文件
                    File.Copy(sourceFile, newConfigPath, true);
                    
                    // 设置最后编辑时间
                    newConfig.LastEditTime = DateTime.Now;
                    
                    // 添加到配置列表
                    _configFiles.Add(newConfig);
                    
                    // 标记索引为脏
                    _isConfigIndexDirty = true;
                    
                    // 保存配置索引
                    SaveConfigIndex();
                    
                    // 触发配置变更事件
                    RaiseConfigChanged(ConfigChangeType.ConfigList, null, null, newConfig);
                    
                    _logger.Debug($"导入配置: {newConfig.Name}, 路径: {newConfig.FilePath}");
                    
                    return newConfig;
                }
                }
                catch (Exception ex)
                {
                    _logger.Error($"导入配置失败: {sourceFile}", ex);
                    throw;
            }
        }
        
        /// <summary>
        /// 导出配置文件
        /// </summary>
        public void ExportKeyConfig(string targetFile, ConfigFileInfo configInfo = null)
        {
            try
        {
            lock (_configLock)
            {
                configInfo ??= _currentConfig;
                
                    if (configInfo != null && File.Exists(configInfo.FilePath))
                    {
                        File.Copy(configInfo.FilePath, targetFile, true);
                        _logger.Debug($"导出配置: {configInfo.Name} -> {targetFile}");
                    }
                    else
                    {
                        throw new FileNotFoundException("配置文件不存在");
                    }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"导出配置失败: {configInfo?.Name}", ex);
                    throw;
            }
        }
        
        /// <summary>
        /// 清理资源
        /// </summary>
        public void Cleanup()
            {
                try
            {
                lock (_configLock)
                {
                    // 保存配置
                    if (_isGlobalConfigDirty)
                    {
                    SaveGlobalConfig();
                    }
                    
                    if (_isKeyConfigDirty)
                    {
                    SaveCurrentKeyConfig();
                    }
                    
                    if (_isConfigIndexDirty)
                    {
                    SaveConfigIndex();
                    }
                    
                    // 清理事件
                    ConfigChanged = null;
                    
                    _logger.Debug("配置管理器资源已清理");
                }
                }
                catch (Exception ex)
                {
                    _logger.Error("清理配置管理器资源失败", ex);
            }
        }
        
        /// <summary>
        /// 验证配置名称，确保唯一性
        /// </summary>
        private string ValidateConfigName(string name, ConfigFileInfo excludeConfig = null)
        {
            // 移除不允许的字符
            var validName = Regex.Replace(name, @"[\\/:*?""<>|]", "_");
            validName = validName.Trim();
            
            // 如果为空，使用默认名称
            if (string.IsNullOrWhiteSpace(validName))
            {
                validName = "新配置";
            }
            
            // 检查名称是否存在（排除自身）
            string baseName = validName;
            int suffix = 1;
            
            while (_configFiles.Any(c => c != excludeConfig && c.Name.Equals(validName, StringComparison.OrdinalIgnoreCase)))
            {
                validName = $"{baseName} ({suffix})";
                suffix++;
            }
            
            return validName;
        }
    }
} 