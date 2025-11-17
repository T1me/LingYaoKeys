using System.Reflection;
using Newtonsoft.Json;
using System.Windows.Input;
using WpfApp.Services.Core;
using WpfApp.Services.Models;

namespace WpfApp.Services.Models;

public class LogFileSettings
{
    public int MaxFileSize { get; set; } = 10;
    public int MaxFileCount { get; set; } = 10;
    public string RollingInterval { get; set; } = "Day";
    public int RetainDays { get; set; } = 7;
}

public class DebugConfig
{
    public bool IsDebugMode { get; set; } = false;
    public string LogLevel { get; set; } = "Information";
    public LogFileSettings FileSettings { get; set; } = new();

}

public class UpdateInfo
{
    public string CurrentVersion { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
}

public class VersionInfo
{
    public string Version { get; set; } = ""; // 版本号
    public string DownloadUrl { get; set; } = ""; // oss下载链接
    public string? GithubUrl { get; set; } // github下载链接
    public string? ReleaseDate { get; set; } // 发布日期
    public string? MinVersion { get; set; } // 最小版本号
    public bool ForceUpdate { get; set; } // 是否强制更新
}

public class KeyConfig
{
    public VirtualKeyCode? Code { get; set; }  // 使用可空类型，坐标类型不需要此属性
    public bool IsSelected { get; set; }
    public int KeyInterval { get; set; }
    public int HoldDuration { get; set; } = 0;  // 按压时长（毫秒）
    public KeyItemType Type { get; set; } = KeyItemType.Keyboard;
    public int? X { get; set; }
    public int? Y { get; set; }

    // 无参构造函数，用于JSON反序列化
    public KeyConfig()
    {
        IsSelected = true;
        KeyInterval = 5;
        Type = KeyItemType.Keyboard;
    }

    // 键盘按键构造函数
    public KeyConfig(VirtualKeyCode code, bool isSelected = true, int keyInterval = 5)
    {
        Code = code;
        IsSelected = isSelected;
        KeyInterval = keyInterval;
        Type = KeyItemType.Keyboard;
        X = null;
        Y = null;
    }
    
    // 坐标构造函数
    public KeyConfig(int x, int y, bool isSelected = true, int keyInterval = 5)
    {
        // 验证坐标不能同时为0（只针对坐标类型）
        if (x == 0 && y == 0)
        {
            throw new ArgumentException("坐标不能同时为(0,0)");
        }
        
        X = x;
        Y = y;
        IsSelected = isSelected;
        KeyInterval = keyInterval;
        Type = KeyItemType.Coordinates;
        Code = null; // 坐标类型不使用Code属性
    }
    
    /// <summary>
    /// 将KeyConfig转换为VirtualKeyCode，用于与LyKeysService兼容
    /// 注意：坐标类型会被忽略，返回null
    /// </summary>
    public VirtualKeyCode? ToVirtualKeyCode()
    {
        // 只有键盘类型且被选中的按键才会返回有效值
        if (Type == KeyItemType.Keyboard && IsSelected && Code.HasValue)
        {
            return Code.Value;
        }
        return null;
    }
    
    /// <summary>
    /// 从KeyConfig列表中提取有效的VirtualKeyCode列表
    /// </summary>
    public static List<VirtualKeyCode> ExtractValidKeyCodes(List<KeyConfig> keyConfigs)
    {
        if (keyConfigs == null) return new List<VirtualKeyCode>();
        
        return keyConfigs
            .Where(k => k.Type == KeyItemType.Keyboard && k.IsSelected && k.Code.HasValue)
            .Select(k => k.Code.Value)
            .ToList();
    }
}

/// <summary>
/// 全局配置类，包含UI和调试等全局配置
/// </summary>
public class GlobalConfig
{
    [JsonIgnore] public AppInfo AppInfo { get; set; } = new();
    public UIConfig UI { get; set; } = new();
    public DebugConfig Debug { get; set; } = new();

    // 全局配置（所有配置共享）
    public bool? isHotkeyControlEnabled { get; set; } = true;
    public bool? EnableHardwareAcceleration { get; set; } = true;
    public string? SelectedDriver { get; set; } = "INPUTSIMULATOR";

    [JsonIgnore] public string Author { get; set; } = "慕长秋";

    public GlobalConfig()
    {
        Debug = new DebugConfig();
        UI = new UIConfig();
    }
}

/// <summary>
/// 目标窗口信息类
/// </summary>
public class TargetWindow
{
    public string ProcessName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public Guid Id { get; set; } = Guid.NewGuid();
}

/// <summary>
/// 多配置数据类 - 支持多个按键配置方案
/// </summary>
public class MultiKeyConfigData
{
    /// <summary>
    /// 所有配置列表
    /// </summary>
    public List<KeyConfiguration> Configurations { get; set; } = new();

    /// <summary>
    /// 当前激活的配置ID
    /// </summary>
    public Guid? ActiveConfigurationId { get; set; }

    /// <summary>
    /// 配置文件版本
    /// </summary>
    public int Version { get; set; } = 2;

    public MultiKeyConfigData()
    {
        Configurations = new List<KeyConfiguration>();
    }

    /// <summary>
    /// 获取当前激活的配置
    /// </summary>
    public KeyConfiguration? GetActiveConfiguration()
    {
        if (!ActiveConfigurationId.HasValue)
            return Configurations.FirstOrDefault();

        return Configurations.FirstOrDefault(c => c.Id == ActiveConfigurationId.Value);
    }

    /// <summary>
    /// 设置激活的配置
    /// </summary>
    public void SetActiveConfiguration(Guid configId)
    {
        if (Configurations.Any(c => c.Id == configId))
        {
            ActiveConfigurationId = configId;
        }
    }

    /// <summary>
    /// 添加配置
    /// </summary>
    public void AddConfiguration(KeyConfiguration config)
    {
        if (config == null) return;

        Configurations.Add(config);

        // 如果是第一个配置，自动设为激活
        if (Configurations.Count == 1)
        {
            ActiveConfigurationId = config.Id;
        }
    }

    /// <summary>
    /// 删除配置
    /// </summary>
    public bool RemoveConfiguration(Guid configId)
    {
        var config = Configurations.FirstOrDefault(c => c.Id == configId);
        if (config == null) return false;

        Configurations.Remove(config);

        // 如果删除的是激活配置，切换到第一个
        if (ActiveConfigurationId == configId)
        {
            ActiveConfigurationId = Configurations.FirstOrDefault()?.Id;
        }

        return true;
    }
}

public class AppInfo
{
    private static readonly Assembly Assembly = Assembly.GetExecutingAssembly();

    [JsonIgnore] public string Title { get; set; } = "灵曜按键 (LingYao Keys)";

    [JsonIgnore] public string Version => Assembly.GetName().Version?.ToString() ?? "1.0.0";
    
    [JsonIgnore] public string GitHubUrl { get; } = "https://github.com/ZyphrZero/LingYaoKeys";
}

public class UIConfig
{
    public WindowConfig MainWindow { get; set; } = new();
    public FloatingWindowConfig FloatingWindow { get; set; } = new();
}

public class WindowConfig
{
    [JsonIgnore] public double MinWidth { get; set; } = 800;
    [JsonIgnore] public double MinHeight { get; set; } = 660;

    // 当前窗口尺寸
    public double Width { get; set; } = 800;
    public double Height { get; set; } = 660;
}

public class FloatingWindowConfig
{
    public double Left { get; set; }
    public double Top { get; set; }
    public bool IsEnabled { get; set; } = true;
    public double Opacity { get; set; } = 0.8;
}