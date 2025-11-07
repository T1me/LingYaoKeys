using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using WpfApp.Services.Core;

namespace WpfApp.Services.Models;

/// <summary>
/// 按键配置类 - 代表一个完整的按键配置方案
/// 每个配置包含独立的热键、按键列表和执行设置
/// </summary>
public partial class KeyConfiguration : ObservableObject
{
    /// <summary>
    /// 配置唯一标识符
    /// </summary>
    [ObservableProperty]
    private Guid _id;

    /// <summary>
    /// 配置名称
    /// </summary>
    [ObservableProperty]
    private string _name = "新配置";

    /// <summary>
    /// 是否启用此配置
    /// </summary>
    [ObservableProperty]
    private bool _isEnabled = true;

    /// <summary>
    /// 启动热键
    /// </summary>
    [ObservableProperty]
    private VirtualKeyCode? _startKey;

    /// <summary>
    /// 启动热键修饰键
    /// </summary>
    [ObservableProperty]
    private ModifierKeys _startMods = ModifierKeys.None;

    /// <summary>
    /// 停止热键
    /// </summary>
    [ObservableProperty]
    private VirtualKeyCode? _stopKey;

    /// <summary>
    /// 停止热键修饰键
    /// </summary>
    [ObservableProperty]
    private ModifierKeys _stopMods = ModifierKeys.None;

    /// <summary>
    /// 按键执行模式
    /// </summary>
    [ObservableProperty]
    private KeyExecutionMode _executionMode = KeyExecutionMode.Sequence;

    /// <summary>
    /// 循环间隔（毫秒）
    /// </summary>
    [ObservableProperty]
    private int _interval = 10;

    /// <summary>
    /// 按键按下时长（毫秒）
    /// </summary>
    [ObservableProperty]
    private int _keyPressInterval = 5;

    /// <summary>
    /// 是否降低按键卡位
    /// </summary>
    [ObservableProperty]
    private bool _isReduceKeyStuck = true;

    /// <summary>
    /// 是否启用声音提示
    /// </summary>
    [ObservableProperty]
    private bool _soundEnabled = true;

    private double _soundVolume = 0.8;

    /// <summary>
    /// 声音音量 (0.0 - 1.0)
    /// </summary>
    public double SoundVolume
    {
        get => _soundVolume;
        set => SetProperty(ref _soundVolume, Math.Clamp(value, 0.0, 1.0));
    }

    /// <summary>
    /// 是否自动切换到英文输入法
    /// </summary>
    [ObservableProperty]
    private bool _autoSwitchToEnglishIME = true;

    /// <summary>
    /// 按键列表
    /// </summary>
    [ObservableProperty]
    private List<KeyConfig> _keys = new();

    /// <summary>
    /// 目标窗口列表
    /// </summary>
    [ObservableProperty]
    private List<TargetWindow> _targetWindows = new();

    /// <summary>
    /// 构造函数
    /// </summary>
    public KeyConfiguration()
    {
        _id = Guid.NewGuid();
        _keys = new List<KeyConfig>();
        _targetWindows = new List<TargetWindow>();
    }

    /// <summary>
    /// 带名称的构造函数
    /// </summary>
    public KeyConfiguration(string name) : this()
    {
        _name = name;
    }

    /// <summary>
    /// 克隆配置
    /// </summary>
    public KeyConfiguration Clone()
    {
        return new KeyConfiguration
        {
            Id = Guid.NewGuid(), // 新的ID
            Name = $"{Name} - 副本",
            IsEnabled = IsEnabled,
            StartKey = StartKey,
            StartMods = StartMods,
            StopKey = StopKey,
            StopMods = StopMods,
            ExecutionMode = ExecutionMode,
            Interval = Interval,
            KeyPressInterval = KeyPressInterval,
            IsReduceKeyStuck = IsReduceKeyStuck,
            SoundEnabled = SoundEnabled,
            SoundVolume = SoundVolume,
            AutoSwitchToEnglishIME = AutoSwitchToEnglishIME,
            Keys = new List<KeyConfig>(Keys),
            TargetWindows = new List<TargetWindow>(TargetWindows)
        };
    }

    /// <summary>
    /// 验证配置是否有效
    /// </summary>
    public bool IsValid(out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            errorMessage = "配置名称不能为空";
            return false;
        }

        if (!StartKey.HasValue)
        {
            errorMessage = "必须设置激活热键";
            return false;
        }

        if (Keys == null || Keys.Count == 0)
        {
            errorMessage = "按键列表不能为空";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// 获取热键显示文本
    /// </summary>
    public string GetStartHotkeyText()
    {
        if (!StartKey.HasValue) return "未设置";

        var parts = new List<string>();
        if ((StartMods & ModifierKeys.Control) == ModifierKeys.Control) parts.Add("Ctrl");
        if ((StartMods & ModifierKeys.Alt) == ModifierKeys.Alt) parts.Add("Alt");
        if ((StartMods & ModifierKeys.Shift) == ModifierKeys.Shift) parts.Add("Shift");
        if ((StartMods & ModifierKeys.Windows) == ModifierKeys.Windows) parts.Add("Win");
        parts.Add(StartKey.Value.ToString());

        return string.Join(" + ", parts);
    }

    /// <summary>
    /// 获取停止热键显示文本
    /// </summary>
    public string GetStopHotkeyText()
    {
        if (!StopKey.HasValue) return "未设置";

        var parts = new List<string>();
        if ((StopMods & ModifierKeys.Control) == ModifierKeys.Control) parts.Add("Ctrl");
        if ((StopMods & ModifierKeys.Alt) == ModifierKeys.Alt) parts.Add("Alt");
        if ((StopMods & ModifierKeys.Shift) == ModifierKeys.Shift) parts.Add("Shift");
        if ((StopMods & ModifierKeys.Windows) == ModifierKeys.Windows) parts.Add("Win");
        parts.Add(StopKey.Value.ToString());

        return string.Join(" + ", parts);
    }

    /// <summary>
    /// 获取执行模式显示文本
    /// </summary>
    public string GetExecutionModeText()
    {
        return ExecutionMode switch
        {
            KeyExecutionMode.Sequence => "单次",
            KeyExecutionMode.Hold => "按压",
            KeyExecutionMode.Loop => "循环",
            _ => "未知"
        };
    }

}

/// <summary>
/// 按键执行模式枚举
/// </summary>
public enum KeyExecutionMode
{
    /// <summary>
    /// 单次模式 - 按一次热键执行一次完整序列
    /// </summary>
    Sequence = 0,

    /// <summary>
    /// 按压模式 - 按住热键持续执行
    /// </summary>
    Hold = 1,

    /// <summary>
    /// 循环模式 - 按一次热键开始循环，再按一次停止
    /// </summary>
    Loop = 2
}
