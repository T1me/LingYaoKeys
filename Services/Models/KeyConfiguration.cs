using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using WpfApp.Services.Core;

namespace WpfApp.Services.Models;

/// <summary>
/// 按键配置类 - 代表一个完整的按键配置方案
/// 每个配置包含独立的热键、按键列表和执行设置
/// </summary>
public class KeyConfiguration : INotifyPropertyChanged
{
    private Guid _id;
    private string _name = "新配置";
    private bool _isEnabled = true;
    private VirtualKeyCode? _startKey;
    private ModifierKeys _startMods = ModifierKeys.None;
    private VirtualKeyCode? _stopKey;
    private ModifierKeys _stopMods = ModifierKeys.None;
    private KeyExecutionMode _executionMode = KeyExecutionMode.Sequence;
    private int _interval = 10;
    private int _keyPressInterval = 5;
    private bool _isReduceKeyStuck = true;
    private bool _soundEnabled = true;
    private double _soundVolume = 0.8;
    private bool _autoSwitchToEnglishIME = true;
    private List<KeyConfig> _keys = new();
    private List<TargetWindow> _targetWindows = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 配置唯一标识符
    /// </summary>
    public Guid Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    /// <summary>
    /// 配置名称
    /// </summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>
    /// 是否启用此配置
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    /// <summary>
    /// 启动热键
    /// </summary>
    public VirtualKeyCode? StartKey
    {
        get => _startKey;
        set => SetProperty(ref _startKey, value);
    }

    /// <summary>
    /// 启动热键修饰键
    /// </summary>
    public ModifierKeys StartMods
    {
        get => _startMods;
        set => SetProperty(ref _startMods, value);
    }

    /// <summary>
    /// 停止热键
    /// </summary>
    public VirtualKeyCode? StopKey
    {
        get => _stopKey;
        set => SetProperty(ref _stopKey, value);
    }

    /// <summary>
    /// 停止热键修饰键
    /// </summary>
    public ModifierKeys StopMods
    {
        get => _stopMods;
        set => SetProperty(ref _stopMods, value);
    }

    /// <summary>
    /// 按键执行模式
    /// </summary>
    public KeyExecutionMode ExecutionMode
    {
        get => _executionMode;
        set => SetProperty(ref _executionMode, value);
    }

    /// <summary>
    /// 循环间隔（毫秒）
    /// </summary>
    public int Interval
    {
        get => _interval;
        set => SetProperty(ref _interval, value);
    }

    /// <summary>
    /// 按键按下时长（毫秒）
    /// </summary>
    public int KeyPressInterval
    {
        get => _keyPressInterval;
        set => SetProperty(ref _keyPressInterval, value);
    }

    /// <summary>
    /// 是否降低按键卡位
    /// </summary>
    public bool IsReduceKeyStuck
    {
        get => _isReduceKeyStuck;
        set => SetProperty(ref _isReduceKeyStuck, value);
    }

    /// <summary>
    /// 是否启用声音提示
    /// </summary>
    public bool SoundEnabled
    {
        get => _soundEnabled;
        set => SetProperty(ref _soundEnabled, value);
    }

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
    public bool AutoSwitchToEnglishIME
    {
        get => _autoSwitchToEnglishIME;
        set => SetProperty(ref _autoSwitchToEnglishIME, value);
    }

    /// <summary>
    /// 按键列表
    /// </summary>
    public List<KeyConfig> Keys
    {
        get => _keys;
        set => SetProperty(ref _keys, value);
    }

    /// <summary>
    /// 目标窗口列表
    /// </summary>
    public List<TargetWindow> TargetWindows
    {
        get => _targetWindows;
        set => SetProperty(ref _targetWindows, value);
    }

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

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
