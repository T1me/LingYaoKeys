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
    /// 负责管理全局配置、按键配置和配置文件的加载、保存、切换等操作
    /// 采用实时保存策略，所有配置更改立即持久化到磁盘
    /// </summary>
    /// <remarks>
    /// 设计要点：
    /// 1. 单例模式：确保全局唯一的配置管理器实例
    /// 2. 实时保存：配置修改后立即异步保存，无需手动保存
    /// 3. 线程安全：使用锁保护共享状态，I/O操作在锁外执行
    /// 4. 事件驱动：通过 ConfigChanged 事件通知订阅者配置变更
    /// 5. 异步优先：所有 I/O 操作使用异步方法，避免阻塞 UI 线程
    /// </remarks>
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
        private const string CONFIG_INDEX_FILE = "config_index.json";
        private const int MAX_BACKUP_FILES = 5;
        
        /// <summary>全局配置实例</summary>
        private GlobalConfig _globalConfig;
        
        /// <summary>当前按键配置实例</summary>
        private KeyConfigData _currentKeyConfig;
        
        /// <summary>所有配置文件列表</summary>
        private ObservableCollection<ConfigFileInfo> _configFiles;
        
        /// <summary>当前选中的配置文件</summary>
        private ConfigFileInfo _currentConfig;
        
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
        
        /// <summary>
        /// 获取所有配置文件列表
        /// 用于 UI 显示和配置切换
        /// </summary>
        public ObservableCollection<ConfigFileInfo> ConfigFiles => _configFiles;
        
        /// <summary>
        /// 获取当前选中的配置文件
        /// </summary>
        public ConfigFileInfo CurrentConfig => _currentConfig;
        
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
        /// 初始化配置文件列表和路径信息
        /// </summary>
        private ConfigManager()
        {
            _configFiles = new ObservableCollection<ConfigFileInfo>();
            _configDir = _pathService.ConfigPath;
            _globalConfigPath = _pathService.GetGlobalConfigPath();
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
                
                Directory.CreateDirectory(_configDir);
                InitializeConfigFiles();
                LoadGlobalConfigAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                LoadCurrentKeyConfigAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                
                _logger.Debug("配置管理器初始化完成");
            }
            catch (Exception ex)
            {
                _logger.Error($"初始化配置管理器失败: {ex.Message}", ex);
                
                try
                {
                    _globalConfig = CreateDefaultGlobalConfig();
                    _currentKeyConfig = CreateDefaultKeyConfig();
                    
                    SaveGlobalConfigAsync().GetAwaiter().GetResult();
                    SaveCurrentKeyConfigAsync().GetAwaiter().GetResult();
                    
                    _logger.Warning("已使用默认配置初始化配置管理器");
                }
                catch (Exception fallbackEx)
                {
                    _logger.Error($"创建默认配置失败: {fallbackEx.Message}", fallbackEx);
                    throw new InvalidOperationException("无法初始化配置管理器", fallbackEx);
                }
            }
        }
        
        private void InitializeConfigFiles()
        {
            try
            {
                var indexPath = Path.Combine(_configDir, CONFIG_INDEX_FILE);
                
                if (!File.Exists(indexPath))
                {
                    CreateDefaultConfigIndex();
                    return;
                }
                
                var json = File.ReadAllText(indexPath);
                var files = JsonConvert.DeserializeObject<List<ConfigFileInfo>>(json);
                
                if (files == null || files.Count == 0)
                {
                    _logger.Warning("配置索引文件为空或无效，将创建默认配置");
                    CreateDefaultConfigIndex();
                    return;
                }
                
                var validFiles = files.Where(f => File.Exists(f.FilePath)).ToList();
                
                if (validFiles.Count < files.Count)
                {
                    _logger.Warning($"发现 {files.Count - validFiles.Count} 个配置文件不存在，已从列表中移除");
                }
                
                if (validFiles.Count == 0)
                {
                    _logger.Warning("没有找到有效的配置文件，将创建默认配置");
                    CreateDefaultConfigIndex();
                    return;
                }
                
                lock (_configLock)
                {
                    _configFiles.Clear();
                    foreach (var file in validFiles)
                    {
                        _configFiles.Add(file);
                    }
                    
                    if (!_configFiles.Any(c => c.IsDefault) && _configFiles.Count > 0)
                    {
                        _configFiles[0].IsDefault = true;
                        _logger.Debug($"设置第一个配置为默认: {_configFiles[0].Name}");
                    }
                    
                    _currentConfig = _configFiles.FirstOrDefault(c => c.IsDefault) ?? _configFiles.FirstOrDefault();
                }
                
                if (!validFiles.Any(c => c.IsDefault))
                {
                    SaveConfigIndexAsync().GetAwaiter().GetResult();
                }
                
                _logger.Debug($"配置文件列表初始化完成，当前配置：{_currentConfig?.Name ?? "无"}");
            }
            catch (Exception ex)
            {
                _logger.Error($"初始化配置文件列表失败: {ex.Message}", ex);
                _logger.Warning("将创建新的默认配置索引");
                CreateDefaultConfigIndex();
            }
        }
        
        /// <summary>
        /// 创建默认配置文件索引
        /// 当配置索引文件不存在或无效时调用
        /// 创建一个名为"默认配置"的配置文件并设置为当前配置
        /// </summary>
        private void CreateDefaultConfigIndex()
        {
            // 创建默认配置对象
            var defaultConfig = new ConfigFileInfo
            {
                Name = "默认配置",
                FilePath = Path.Combine(_configDir, "default_keyconfig.json"),
                IsDefault = true,
                LastEditTime = DateTime.Now
            };
            
            // 只在更新共享状态时使用锁
            lock (_configLock)
            {
                _configFiles.Clear();
                _configFiles.Add(defaultConfig);
                _currentConfig = defaultConfig;
            }
            
            // I/O操作在锁外执行
            SaveConfigIndexAsync().GetAwaiter().GetResult();
            
            _logger.Debug("已创建默认配置文件索引");
        }
        
        #endregion
        
        #region 配置保存和加载方法
        
        private async System.Threading.Tasks.Task SaveConfigIndexAsync()
        {
            List<ConfigFileInfo> configFilesSnapshot;
            
            lock (_configLock)
            {
                configFilesSnapshot = _configFiles.ToList();
            }
            
            var indexPath = Path.Combine(_configDir, CONFIG_INDEX_FILE);
            var json = JsonConvert.SerializeObject(configFilesSnapshot, Formatting.Indented);
            await File.WriteAllTextAsync(indexPath, json);
            _logger.Debug("配置文件索引已保存");
        }
        
        private async System.Threading.Tasks.Task LoadGlobalConfigAsync()
        {
            try
            {
                GlobalConfig loadedConfig;
                
                if (File.Exists(_globalConfigPath))
                {
                    // 使用同步方法避免死锁
                    var json = File.ReadAllText(_globalConfigPath);
                    loadedConfig = JsonConvert.DeserializeObject<GlobalConfig>(json) ?? CreateDefaultGlobalConfig();
                    _logger.Debug("全局配置加载成功");
                }
                else
                {
                    loadedConfig = CreateDefaultGlobalConfig();
                    _logger.Debug("全局配置不存在，已创建默认配置");
                }
                
                // 添加await以保持异步方法签名
                await System.Threading.Tasks.Task.CompletedTask;
                
                lock (_configLock)
                {
                    _globalConfig = loadedConfig;
                }
                
                if (!File.Exists(_globalConfigPath))
                {
                    await SaveGlobalConfigAsync();
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
                
                await SaveGlobalConfigAsync();
            }
        }
        
        private async System.Threading.Tasks.Task LoadCurrentKeyConfigAsync()
        {
            try
            {
                ConfigFileInfo currentConfigSnapshot;
                
                lock (_configLock)
                {
                    currentConfigSnapshot = _currentConfig;
                }
                
                KeyConfigData loadedConfig;
                bool needsSave = false;
                
                if (currentConfigSnapshot != null && File.Exists(currentConfigSnapshot.FilePath))
                {
                    // 使用同步方法避免死锁
                    var json = File.ReadAllText(currentConfigSnapshot.FilePath);
                    loadedConfig = JsonConvert.DeserializeObject<KeyConfigData>(json) ?? CreateDefaultKeyConfig();
                    
                    if (loadedConfig.keys == null || loadedConfig.keys.Count == 0)
                    {
                        var defaultConfig = CreateDefaultKeyConfig();
                        loadedConfig.keys = defaultConfig.keys;
                        needsSave = true;
                    }
                    
                    _logger.Debug($"已加载按键配置: {currentConfigSnapshot.Name}, 按键数量: {loadedConfig.keys.Count}");
                }
                else
                {
                    loadedConfig = CreateDefaultKeyConfig();
                    needsSave = true;
                    _logger.Debug("按键配置不存在，已创建默认配置");
                }
                
                lock (_configLock)
                {
                    _currentKeyConfig = loadedConfig;
                }
                
                if (needsSave)
                {
                    await SaveCurrentKeyConfigAsync();
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
                
                await SaveCurrentKeyConfigAsync();
            }
        }
        
        private async System.Threading.Tasks.Task SaveGlobalConfigAsync()
        {
            GlobalConfig configSnapshot;
            
            lock (_configLock)
            {
                configSnapshot = _globalConfig;
            }
            
            var json = JsonConvert.SerializeObject(configSnapshot, Formatting.Indented);
            await File.WriteAllTextAsync(_globalConfigPath, json);
            _logger.Debug("全局配置已保存");
        }
        
        private async System.Threading.Tasks.Task SaveCurrentKeyConfigAsync()
        {
            KeyConfigData configSnapshot;
            ConfigFileInfo currentConfigSnapshot;
            
            lock (_configLock)
            {
                if (_currentConfig == null) return;
                
                configSnapshot = _currentKeyConfig;
                currentConfigSnapshot = _currentConfig;
            }
            
            var json = JsonConvert.SerializeObject(configSnapshot, Formatting.Indented);
            await File.WriteAllTextAsync(currentConfigSnapshot.FilePath, json);
            
            lock (_configLock)
            {
                currentConfigSnapshot.LastEditTime = DateTime.Now;
            }
            
            await SaveConfigIndexAsync();
            _logger.Debug($"按键配置已保存: {currentConfigSnapshot.Name}");
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
        /// 返回包含默认热键和按键序列的配置对象
        /// </summary>
        /// <returns>默认按键配置实例</returns>
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
        
        #endregion
        
        #region 事件触发方法
        
        /// <summary>
        /// 触发配置变更事件
        /// 通知所有订阅者配置已发生变更
        /// </summary>
        /// <param name="changeType">变更类型</param>
        /// <param name="globalConfig">全局配置（仅在 Global 类型时传递）</param>
        /// <param name="keyConfig">按键配置（仅在 Key 类型时传递）</param>
        /// <param name="configFile">配置文件信息（在 ConfigFile 和 ConfigList 类型时传递）</param>
        /// <remarks>
        /// 事件触发规则：
        /// - Global: 仅传递 globalConfig，用于通知全局设置变更
        /// - Key: 传递 keyConfig 和可选的 configFile，用于通知按键配置变更
        /// - ConfigFile: 传递 configFile，用于通知配置切换（如停止序列等操作）
        /// - ConfigList: 传递 configFile，用于通知配置列表变更（UI 更新）
        /// </remarks>
        private void RaiseConfigChanged(ConfigChangeType changeType, GlobalConfig globalConfig = null, 
            KeyConfigData keyConfig = null, ConfigFileInfo configFile = null)
        {
            try
            {
                var args = new ConfigEventArgs(changeType, globalConfig, keyConfig, configFile);
                ConfigChanged?.Invoke(this, args);
                _logger.Debug($"触发配置变更事件: {changeType}, ConfigFile: {configFile?.Name ?? "null"}");
            }
            catch (Exception ex)
            {
                _logger.Error($"触发配置变更事件失败: {changeType}", ex);
            }
        }
        
        #endregion
        
        #region 配置更新方法
        
        /// <summary>
        /// 异步更新全局配置
        /// 执行更新操作后立即保存到磁盘，并触发 ConfigChanged 事件
        /// </summary>
        public async System.Threading.Tasks.Task UpdateGlobalConfigAsync(Action<GlobalConfig> updateAction)
        {
            if (updateAction == null)
                throw new ArgumentNullException(nameof(updateAction));
            
            GlobalConfig configSnapshot;
            
            lock (_configLock)
            {
                if (_globalConfig == null)
                {
                    _logger.Warning("全局配置为空，无法更新");
                    return;
                }
                updateAction(_globalConfig);
                configSnapshot = _globalConfig;
            }
            
            await SaveGlobalConfigAsync();
            RaiseConfigChanged(ConfigChangeType.Global, configSnapshot, null, null);
            _logger.Debug("全局配置已更新并保存");
        }
        
        /// <summary>
        /// 异步更新当前按键配置
        /// 执行更新操作后立即保存到磁盘，并触发 ConfigChanged 事件
        /// </summary>
        public async System.Threading.Tasks.Task UpdateKeyConfigAsync(Action<KeyConfigData> updateAction)
        {
            if (updateAction == null)
                throw new ArgumentNullException(nameof(updateAction));
            
            KeyConfigData configSnapshot;
            ConfigFileInfo currentConfigSnapshot;
            
            lock (_configLock)
            {
                if (_currentKeyConfig == null)
                {
                    _logger.Warning("按键配置为空，无法更新");
                    return;
                }
                updateAction(_currentKeyConfig);
                configSnapshot = _currentKeyConfig;
                currentConfigSnapshot = _currentConfig;
            }
            
            await SaveCurrentKeyConfigAsync();
            RaiseConfigChanged(ConfigChangeType.Key, null, configSnapshot, currentConfigSnapshot);
            _logger.Debug("按键配置已更新并保存");
        }
        
        #endregion
        
        #region 配置切换方法
        
        /// <summary>
        /// 异步切换当前配置文件
        /// </summary>
        public async System.Threading.Tasks.Task SwitchConfigAsync(ConfigFileInfo configInfo)
        {
            if (configInfo == null)
                throw new ArgumentNullException(nameof(configInfo));
            
            if (_currentConfig == configInfo)
            {
                _logger.Debug($"配置已是当前配置，无需切换: {configInfo.Name}");
                return;
            }
            
            _logger.Debug($"切换配置: {_currentConfig?.Name} -> {configInfo.Name}");
            
            RaiseConfigChanged(ConfigChangeType.ConfigFile, null, null, configInfo);
            
            lock (_configLock)
            {
                foreach (var config in _configFiles)
                {
                    config.IsDefault = (config == configInfo);
                }
                _currentConfig = configInfo;
            }
            
            await SaveConfigIndexAsync();
            await LoadCurrentKeyConfigAsync();
            
            KeyConfigData keyConfigSnapshot;
            lock (_configLock)
            {
                keyConfigSnapshot = _currentKeyConfig;
            }
            RaiseConfigChanged(ConfigChangeType.Key, null, keyConfigSnapshot, configInfo);
            
            _logger.Debug($"配置切换完成: {configInfo.Name}");
        }
        
        #endregion
        
        #region 配置文件操作方法
        
        /// <summary>
        /// 异步创建新配置文件
        /// </summary>
        public async System.Threading.Tasks.Task<ConfigFileInfo> CreateNewConfigAsync(string configName, bool copyFromCurrent = true)
        {
            if (string.IsNullOrWhiteSpace(configName))
                throw new ArgumentException("配置名称不能为空", nameof(configName));
            
            string validName;
            string newConfigPath;
            ConfigFileInfo newConfig;
            string sourceFilePath = null;
            
            lock (_configLock)
            {
                validName = ValidateConfigName(configName);
                newConfigPath = Path.Combine(_configDir, $"{validName}.json");
                newConfig = new ConfigFileInfo(validName, newConfigPath);
                
                if (copyFromCurrent && _currentConfig != null)
                {
                    sourceFilePath = _currentConfig.FilePath;
                }
                
                newConfig.LastEditTime = DateTime.Now;
                _configFiles.Add(newConfig);
            }
            
            try
            {
                if (sourceFilePath != null && File.Exists(sourceFilePath))
                {
                    File.Copy(sourceFilePath, newConfigPath, true);
                }
                else
                {
                    var emptyConfig = CreateDefaultKeyConfig();
                    var json = JsonConvert.SerializeObject(emptyConfig, Formatting.Indented);
                    await File.WriteAllTextAsync(newConfigPath, json);
                }
            }
            catch (Exception ex)
            {
                lock (_configLock)
                {
                    _configFiles.Remove(newConfig);
                }
                _logger.Error($"创建配置文件失败: {ex.Message}", ex);
                throw;
            }
            
            await SaveConfigIndexAsync();
            RaiseConfigChanged(ConfigChangeType.ConfigList, null, null, newConfig);
            _logger.Debug($"创建新配置: {newConfig.Name}");
            
            return newConfig;
        }
        
        /// <summary>
        /// 异步重命名配置文件
        /// </summary>
        public async System.Threading.Tasks.Task RenameConfigAsync(ConfigFileInfo configInfo, string newName)
        {
            if (configInfo == null)
                throw new ArgumentNullException(nameof(configInfo));
            
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("新配置名称不能为空", nameof(newName));
            
            string validatedName;
            string oldFilePath;
            string newFilePath;
            
            lock (_configLock)
            {
                validatedName = ValidateConfigName(newName, configInfo);
                oldFilePath = configInfo.FilePath;
                newFilePath = Path.Combine(_configDir, $"{validatedName}.json");
            }
            
            if (File.Exists(oldFilePath) && !string.Equals(oldFilePath, newFilePath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(newFilePath))
                    throw new IOException($"目标文件已存在: {newFilePath}");
                
                File.Move(oldFilePath, newFilePath);
                _logger.Debug($"重命名文件: {oldFilePath} -> {newFilePath}");
            }
            
            lock (_configLock)
            {
                configInfo.Name = validatedName;
                configInfo.FilePath = newFilePath;
                configInfo.UpdateEditTime();
            }
            
            await SaveConfigIndexAsync();
            RaiseConfigChanged(ConfigChangeType.ConfigList, null, null, configInfo);
            _logger.Debug($"重命名配置: {configInfo.Name}");
        }
        
        /// <summary>
        /// 异步删除配置文件
        /// </summary>
        public async System.Threading.Tasks.Task DeleteConfigAsync(ConfigFileInfo configInfo)
        {
            if (configInfo == null)
                throw new ArgumentNullException(nameof(configInfo));
            
            if (configInfo.IsDefault)
                throw new InvalidOperationException("无法删除默认配置");
            
            string fileToDelete;
            bool isCurrentConfig;
            ConfigFileInfo newCurrentConfig = null;
            
            lock (_configLock)
            {
                fileToDelete = configInfo.FilePath;
                isCurrentConfig = (_currentConfig == configInfo);
                _configFiles.Remove(configInfo);
                
                if (isCurrentConfig)
                {
                    newCurrentConfig = _configFiles.FirstOrDefault(c => c.IsDefault) ?? _configFiles.FirstOrDefault();
                    if (newCurrentConfig == null)
                        throw new InvalidOperationException("删除配置后没有可用的配置文件");
                    
                    _currentConfig = newCurrentConfig;
                }
            }
            
            await SaveConfigIndexAsync();
            
            if (File.Exists(fileToDelete))
            {
                File.Delete(fileToDelete);
                _logger.Debug($"物理文件已删除: {fileToDelete}");
            }
            
            if (isCurrentConfig && newCurrentConfig != null)
            {
                await LoadCurrentKeyConfigAsync();
                _logger.Debug($"删除配置完成: {configInfo.Name}，当前配置已切换到: {newCurrentConfig.Name}");
                
                RaiseConfigChanged(ConfigChangeType.ConfigFile, null, null, newCurrentConfig);
                
                KeyConfigData keyConfigSnapshot;
                lock (_configLock)
                {
                    keyConfigSnapshot = _currentKeyConfig;
                }
                RaiseConfigChanged(ConfigChangeType.Key, null, keyConfigSnapshot, newCurrentConfig);
            }
            
            RaiseConfigChanged(ConfigChangeType.ConfigList, null, null, configInfo);
            _logger.Debug($"删除配置完成: {configInfo.Name}");
        }
        
        /// <summary>
        /// 异步设置配置文件快捷键
        /// </summary>
        public async System.Threading.Tasks.Task SetConfigHotkeyAsync(ConfigFileInfo configInfo, string hotkeyText)
        {
            if (configInfo == null)
                throw new ArgumentNullException(nameof(configInfo));
            
            lock (_configLock)
            {
                configInfo.ConfigHotkey = hotkeyText;
            }
            
            await SaveConfigIndexAsync();
            RaiseConfigChanged(ConfigChangeType.ConfigList, null, null, configInfo);
            _logger.Debug($"设置配置快捷键: {configInfo.Name} -> {hotkeyText}");
        }
        
        #endregion
        
        #region 配置导入导出方法
        
        /// <summary>
        /// 异步导入配置文件
        /// </summary>
        public async System.Threading.Tasks.Task<ConfigFileInfo> ImportKeyConfigAsync(string sourceFile, string configName = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceFile))
                    throw new ArgumentException("源文件路径不能为空", nameof(sourceFile));
                
                if (!File.Exists(sourceFile))
                    throw new FileNotFoundException($"找不到源文件: {sourceFile}");
                
                _logger.Debug($"开始导入配置文件: {sourceFile}");
                
                var json = await File.ReadAllTextAsync(sourceFile);
                
                if (string.IsNullOrWhiteSpace(json))
                    throw new InvalidDataException("配置文件内容为空");
                
                var importedConfig = JsonConvert.DeserializeObject<KeyConfigData>(json);
                
                if (importedConfig == null)
                    throw new InvalidDataException("配置文件格式无效");
                
                if (importedConfig.keys == null)
                {
                    _logger.Warning("导入的配置文件缺少按键列表，将使用空列表");
                    importedConfig.keys = new List<KeyConfig>();
                }
                
                _logger.Debug($"配置文件格式验证通过，包含 {importedConfig.keys.Count} 个按键");
                
                if (string.IsNullOrEmpty(configName))
                {
                    configName = Path.GetFileNameWithoutExtension(sourceFile);
                }
                
                string validName;
                string newConfigPath;
                ConfigFileInfo newConfig;
                
                lock (_configLock)
                {
                    validName = ValidateConfigName(configName);
                    newConfigPath = Path.Combine(_configDir, $"{validName}.json");
                    newConfig = new ConfigFileInfo(validName, newConfigPath);
                    newConfig.LastEditTime = DateTime.Now;
                    _configFiles.Add(newConfig);
                }
                
                try
                {
                    var configJson = JsonConvert.SerializeObject(importedConfig, Formatting.Indented);
                    await File.WriteAllTextAsync(newConfigPath, configJson);
                }
                catch (Exception ex)
                {
                    lock (_configLock)
                    {
                        _configFiles.Remove(newConfig);
                    }
                    _logger.Error($"写入配置文件失败: {newConfigPath}", ex);
                    throw;
                }
                
                await SaveConfigIndexAsync();
                RaiseConfigChanged(ConfigChangeType.ConfigList, null, null, newConfig);
                _logger.Debug($"导入配置成功: {newConfig.Name}");
                
                return newConfig;
            }
            catch (Exception ex)
            {
                _logger.Error($"导入配置失败: {sourceFile}", ex);
                throw;
            }
        }
        
        /// <summary>
        /// 异步导出配置文件
        /// </summary>
        public async System.Threading.Tasks.Task ExportKeyConfigAsync(string targetFile, ConfigFileInfo configInfo = null)
        {
            if (string.IsNullOrWhiteSpace(targetFile))
                throw new ArgumentException("目标文件路径不能为空", nameof(targetFile));
            
            var targetDir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
                _logger.Debug($"创建目标目录: {targetDir}");
            }
            
            ConfigFileInfo sourceConfig;
            string sourceFilePath;
            
            lock (_configLock)
            {
                sourceConfig = configInfo ?? _currentConfig;
                if (sourceConfig == null)
                    throw new InvalidOperationException("没有可导出的配置");
                
                sourceFilePath = sourceConfig.FilePath;
            }
            
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException($"配置文件不存在: {sourceConfig.Name}");
            
            _logger.Debug($"开始导出配置: {sourceConfig.Name} -> {targetFile}");
            
            var json = await File.ReadAllTextAsync(sourceFilePath);
            var config = JsonConvert.DeserializeObject<KeyConfigData>(json);
            if (config == null)
                throw new InvalidDataException("配置文件内容无效");
            
            await File.WriteAllTextAsync(targetFile, json);
            _logger.Debug($"导出配置成功: {sourceConfig.Name}");
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
        
        /// <summary>
        /// 验证配置名称，确保唯一性
        /// 自动移除非法字符，处理重复名称
        /// </summary>
        /// <param name="name">原始配置名称</param>
        /// <param name="excludeConfig">排除的配置（用于重命名时排除自身）</param>
        /// <returns>验证后的唯一配置名称</returns>
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
        
        #endregion
    }
} 