using System.IO;
using Newtonsoft.Json;
using WpfApp.Services.Utils;
using WpfApp.Services.Models;

namespace WpfApp.Services.Core;

public class ConfigChangedEventArgs : EventArgs
{
    public string Section { get; }
    public AppConfig Config { get; }

    public ConfigChangedEventArgs(string section, AppConfig config)
    {
        Section = section;
        Config = config;
    }
}

public class AppConfigService
{
    private static readonly SerilogManager _logger = SerilogManager.Instance;
    private static readonly PathService _pathService = PathService.Instance;

    private static string _globalConfigPath;
    private static string _keyConfigPath;
    private static AppConfig? _config;
    private static GlobalConfig? _globalConfig;
    private static KeyConfigData? _keyConfig;
    private static readonly object _lockObject = new();
    public static event EventHandler<ConfigChangedEventArgs>? ConfigChanged;

    public static AppConfig Config
    {
        get
        {
            if (_config == null)
                lock (_lockObject)
                {
                    if (_config == null) LoadConfig();
                }

            return _config ?? CreateDefaultConfig();
        }
    }

    public static GlobalConfig GlobalConfig
    {
        get
        {
            if (_globalConfig == null)
                lock (_lockObject)
                {
                    if (_globalConfig == null) LoadConfig();
                }

            return _globalConfig ?? CreateDefaultGlobalConfig();
        }
    }

    public static KeyConfigData KeyConfig
    {
        get
        {
            if (_keyConfig == null)
                lock (_lockObject)
                {
                    if (_keyConfig == null) LoadConfig();
                }

            return _keyConfig ?? CreateDefaultKeyConfig();
        }
    }

    public static void Initialize(string? userDataPath = null)
    {
        System.Diagnostics.Debug.WriteLine("开始初始化配置服务...");

        lock (_lockObject)
        {
            // 使用PathService获取配置文件路径
            _globalConfigPath = _pathService.GetGlobalConfigPath();
            _keyConfigPath = _pathService.GetKeyConfigPath();
            System.Diagnostics.Debug.WriteLine($"使用配置路径: {_globalConfigPath}, {_keyConfigPath}");

            // 确保配置目录存在
            Directory.CreateDirectory(Path.GetDirectoryName(_globalConfigPath)!);

            // 检查配置文件是否存在
            bool hasConfigs = File.Exists(_globalConfigPath) && File.Exists(_keyConfigPath);

            if (!hasConfigs)
            {
                // 不存在配置文件，创建默认配置
                _globalConfig = CreateDefaultGlobalConfig();
                _keyConfig = CreateDefaultKeyConfig();
                _config = AppConfig.FromConfigs(_globalConfig, _keyConfig);
                SaveConfigs();
                System.Diagnostics.Debug.WriteLine("已创建新的默认配置文件");
            }
            else
            {
                // 已存在配置文件，直接加载
                LoadConfig();
            }

            System.Diagnostics.Debug.WriteLine("配置服务初始化完成");
        }
    }

    private static void LoadConfig()
    {
        try
        {
            // 检查全局配置文件和按键配置文件是否存在
            bool hasGlobalConfig = File.Exists(_globalConfigPath);
            bool hasKeyConfig = File.Exists(_keyConfigPath);

            // 决定加载策略
            if (hasGlobalConfig && hasKeyConfig)
            {
                // 加载分离配置
                LoadSplitConfigs();
            }
            else
            {
                // 不存在任何配置，创建默认配置
                _globalConfig = CreateDefaultGlobalConfig();
                _keyConfig = CreateDefaultKeyConfig();
                _config = AppConfig.FromConfigs(_globalConfig, _keyConfig);
                SaveConfigs();
            }

            System.Diagnostics.Debug.WriteLine("配置加载成功");
            ValidateConfig();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载配置失败: {ex.Message}");
            _globalConfig = CreateDefaultGlobalConfig();
            _keyConfig = CreateDefaultKeyConfig();
            _config = AppConfig.FromConfigs(_globalConfig, _keyConfig);
            SaveConfigs();
        }
    }

    private static void LoadSplitConfigs()
    {
        var jsonSettings = new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            NullValueHandling = NullValueHandling.Include,
            TypeNameHandling = TypeNameHandling.Auto,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Populate
        };

        try
        {
            // 读取全局配置
            string globalJson = File.ReadAllText(_globalConfigPath);
            _globalConfig = JsonConvert.DeserializeObject<GlobalConfig>(globalJson, jsonSettings);
            
            // 读取按键配置
            string keyJson = File.ReadAllText(_keyConfigPath);
            _keyConfig = JsonConvert.DeserializeObject<KeyConfigData>(keyJson, jsonSettings);
            
            // 验证按键配置中的keys列表不为空
            if (_keyConfig?.keys == null || _keyConfig.keys.Count == 0)
            {
                _logger.Debug("配置文件中按键列表为空，使用默认按键列表");
                var defaultConfig = CreateDefaultKeyConfig();
                _keyConfig.keys = defaultConfig.keys;
            }
            
            // 合并生成完整配置供应用程序使用
            _config = AppConfig.FromConfigs(_globalConfig!, _keyConfig!);
            
            System.Diagnostics.Debug.WriteLine($"已加载分离配置文件: 全局配置和按键配置");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载分离配置文件失败: {ex.Message}");
            throw;
        }
    }

    private static AppConfig CreateDefaultConfig()
    {
        System.Diagnostics.Debug.WriteLine("创建新的默认配置");
        _globalConfig = CreateDefaultGlobalConfig();
        _keyConfig = CreateDefaultKeyConfig();
        _config = AppConfig.FromConfigs(_globalConfig, _keyConfig);
        SaveConfigs();
        return _config;
    }

    private static GlobalConfig CreateDefaultGlobalConfig()
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
                IsDebugMode = false, // 调试模式总开关
                EnableLogging = false, // 日志记录开关
                LogLevel = "Debug", // 日志级别
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
            soundEnabled = true,
            IsReduceKeyStuck = true,
            SoundVolume = 0.8,
            AutoSwitchToEnglishIME = true,
            isHotkeyControlEnabled = true // 热键总开关默认启用
        };
    }

    private static KeyConfigData CreateDefaultKeyConfig()
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

    private static void ValidateConfig()
    {
        if (_config == null || _globalConfig == null || _keyConfig == null) return;

        var configChanged = false;

        // 验证并修正窗口尺寸
        if (_globalConfig.UI.MainWindow.Width < _globalConfig.UI.MainWindow.MinWidth)
        {
            _logger.Debug($"窗口宽度 {_globalConfig.UI.MainWindow.Width} 小于最小值，已修正为 {_globalConfig.UI.MainWindow.MinWidth}");
            _globalConfig.UI.MainWindow.Width = _globalConfig.UI.MainWindow.MinWidth;
            configChanged = true;
        }

        if (_globalConfig.UI.MainWindow.Height < _globalConfig.UI.MainWindow.MinHeight)
        {
            _logger.Debug($"窗口高度 {_globalConfig.UI.MainWindow.Height} 小于最小值，已修正为 {_globalConfig.UI.MainWindow.MinHeight}");
            _globalConfig.UI.MainWindow.Height = _globalConfig.UI.MainWindow.MinHeight;
            configChanged = true;
        }

        // 验证热键模式配置
        if (_keyConfig.keyMode != 0 && _keyConfig.keyMode != 1)
        {
            _logger.Debug($"无效的按键模式 {_keyConfig.keyMode}，已修正为顺序模式(0)");
            _keyConfig.keyMode = 0;
            configChanged = true;
        }

        // 验证热键配置
        if (_keyConfig.startKey == null)
        {
            _logger.Debug("启动热键未设置，已设置为默认值");
            _keyConfig.startKey = LyKeysCode.VK_F9;
            configChanged = true;
        }

        if (_keyConfig.stopKey == null)
        {
            _logger.Debug("停止热键未设置，已设置为默认值");
            _keyConfig.stopKey = LyKeysCode.VK_F9;
            configChanged = true;
        }

        // 验证热键总开关配置
        if (_globalConfig.isHotkeyControlEnabled == null)
        {
            _logger.Debug("热键总开关状态未设置，已设置为默认值(启用)");
            _globalConfig.isHotkeyControlEnabled = true;
            configChanged = true;
        }

        if (configChanged)
        {
            _logger.Debug("配置已更新并验证");
            // 更新合并的配置
            _config = AppConfig.FromConfigs(_globalConfig, _keyConfig);
            SaveConfigs();
        }
    }

    // 优化保存配置方法
    public static void SaveConfig()
    {
        try
        {
            lock (_lockObject)
            {
                if (_config == null) return;

                // 将当前AppConfig拆分为GlobalConfig和KeyConfig
                _globalConfig = _config.ToGlobalConfig();
                _keyConfig = _config.ToKeyConfigData();
                
                // 保存拆分的配置文件
                SaveConfigs();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存配置文件失败: {ex.Message}");
            throw; // 重新抛出异常，让调用者知道保存失败
        }
    }
    
    // 保存分离的配置文件
    private static void SaveConfigs()
    {
        try
        {
            lock (_lockObject)
            {
                if (_globalConfig == null || _keyConfig == null) return;
                
                var jsonSettings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Include,
                    TypeNameHandling = TypeNameHandling.Auto,
                    DefaultValueHandling = DefaultValueHandling.Populate
                };
                
                // 保存全局配置
                var globalJson = JsonConvert.SerializeObject(_globalConfig, jsonSettings);
                File.WriteAllText(_globalConfigPath, globalJson);
                
                // 保存按键配置 - 使用当前激活的配置文件路径
                var keyJson = JsonConvert.SerializeObject(_keyConfig, jsonSettings);
                File.WriteAllText(_keyConfigPath, keyJson);
                
                // 更新合并的配置对象
                _config = AppConfig.FromConfigs(_globalConfig, _keyConfig);
                
                // 触发配置更改事件
                ConfigChanged?.Invoke(null, new ConfigChangedEventArgs("AppConfig", _config));
                
                System.Diagnostics.Debug.WriteLine($"配置已更新并保存: 全局配置和按键配置 (路径: {_keyConfigPath})");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存配置文件失败: {ex.Message}");
            throw;
        }
    }

    // 添加更新配置方法
    public static void UpdateConfig(Action<AppConfig> updateAction)
    {
        try
        {
            lock (_lockObject)
            {
                if (_config == null) return;

                updateAction(_config);
                
                // 将更新后的AppConfig拆分并保存
                _globalConfig = _config.ToGlobalConfig();
                _keyConfig = _config.ToKeyConfigData();
                
                ValidateConfig(); // 验证更新后的配置
                SaveConfigs(); // 保存并触发事件
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"更新配置失败: {ex.Message}");
            throw;
        }
    }
    
    // 更新全局配置
    public static void UpdateGlobalConfig(Action<GlobalConfig> updateAction)
    {
        try
        {
            lock (_lockObject)
            {
                if (_globalConfig == null) return;

                updateAction(_globalConfig);
                
                // 更新合并的配置
                _config = AppConfig.FromConfigs(_globalConfig, _keyConfig!);
                
                ValidateConfig(); // 验证更新后的配置
                SaveConfigs(); // 保存并触发事件
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"更新全局配置失败: {ex.Message}");
            throw;
        }
    }
    
    // 更新按键配置
    public static void UpdateKeyConfig(Action<KeyConfigData> updateAction)
    {
        try
        {
            lock (_lockObject)
            {
                if (_keyConfig == null) return;

                updateAction(_keyConfig);
                
                // 更新合并的配置
                _config = AppConfig.FromConfigs(_globalConfig!, _keyConfig);
                
                ValidateConfig(); // 验证更新后的配置
                SaveConfigs(); // 保存并触发事件
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"更新按键配置失败: {ex.Message}");
            throw;
        }
    }

    // 添加资源清理方法
    public static void Cleanup()
    {
        lock (_lockObject)
        {
            ConfigChanged = null;
            _config = null;
            _globalConfig = null;
            _keyConfig = null;
        }
    }
}