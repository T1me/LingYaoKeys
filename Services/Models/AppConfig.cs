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
    
    // 通用配置
    public bool? soundEnabled { get; set; }
    public bool? IsReduceKeyStuck { get; set; }
    public double? SoundVolume { get; set; } = 0.8;
    public bool? AutoSwitchToEnglishIME { get; set; } = true;
    public bool? isHotkeyControlEnabled { get; set; } = true;
    public bool? EnableHardwareAcceleration { get; set; } = true;
    public string? SelectedDriver { get; set; } = "AHK";

    [JsonIgnore] public string Author { get; set; } = "慕长秋";

    public GlobalConfig()
    {
        Debug = new DebugConfig();
        UI = new UIConfig();
    }
}

/// <summary>
/// 按键配置类，包含按键相关的所有配置
/// </summary>
public class KeyConfigData
{
    // 按键配置相关属性
    public VirtualKeyCode? startKey { get; set; }
    public ModifierKeys startMods { get; set; }
    public VirtualKeyCode? stopKey { get; set; }
    public ModifierKeys stopMods { get; set; }
    public List<KeyConfig> keys { get; set; } = new();
    public int keyMode { get; set; }
    public int interval { get; set; } = 10;
    public int? KeyPressInterval { get; set; }
    
    // 窗口句柄相关信息
    public string? TargetWindowClassName { get; set; }
    public string? TargetWindowProcessName { get; set; }
    public string? TargetWindowTitle { get; set; }
    
    public KeyConfigData()
    {
        keys = new List<KeyConfig>();
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
}