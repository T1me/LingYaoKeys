using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Input;
using WpfApp.Services.Core;
using WpfApp.Services.Models;
using WpfApp.Services.Utils;

namespace WpfApp.ViewModels;

/// <summary>
/// 按键配置编辑对话框视图模型
/// </summary>
public class KeyConfigurationDialogViewModel : ViewModelBase
{
    private readonly LyKeysService _lyKeysService;
    private readonly KeyConfiguration _configuration;
    private readonly CoordinateManagementService _coordinateService;
    private readonly KeyListManagementService _keyListService;

    // UI 绑定属性
    private string _configurationName;
    private string _startHotkeyText = "未设置";
    private string _stopHotkeyText = "未设置";
    private VirtualKeyCode? _startKey;
    private ModifierKeys _startMods;
    private VirtualKeyCode? _stopKey;
    private ModifierKeys _stopMods;
    private string _selectedExecutionMode;
    private int _defaultInterval = 10;
    private bool _isReduceKeyStuck = true;
    private bool _soundEnabled = true;
    private double _soundVolume = 0.8;
    private bool _autoSwitchIME = true;
    private VirtualKeyCode? _currentKey;
    private string _currentKeyText = string.Empty;
    private int _currentKeyInterval = 10;
    private ObservableCollection<KeyItem> _keyItems;

    #region 属性

    public string ConfigurationName
    {
        get => _configurationName;
        set => SetProperty(ref _configurationName, value);
    }

    public string StartHotkeyText
    {
        get => _startHotkeyText;
        set => SetProperty(ref _startHotkeyText, value);
    }

    public string StopHotkeyText
    {
        get => _stopHotkeyText;
        set => SetProperty(ref _stopHotkeyText, value);
    }

    public List<string> ExecutionModes { get; } = new() { "循环", "单次", "按压" };

    public string SelectedExecutionMode
    {
        get => _selectedExecutionMode;
        set => SetProperty(ref _selectedExecutionMode, value);
    }

    public int DefaultInterval
    {
        get => _defaultInterval;
        set => SetProperty(ref _defaultInterval, value);
    }

    public bool IsReduceKeyStuck
    {
        get => _isReduceKeyStuck;
        set => SetProperty(ref _isReduceKeyStuck, value);
    }

    public bool SoundEnabled
    {
        get => _soundEnabled;
        set => SetProperty(ref _soundEnabled, value);
    }

    public double SoundVolume
    {
        get => _soundVolume;
        set => SetProperty(ref _soundVolume, value);
    }

    public bool AutoSwitchIME
    {
        get => _autoSwitchIME;
        set => SetProperty(ref _autoSwitchIME, value);
    }

    public string CurrentKeyText
    {
        get => _currentKeyText;
        set => SetProperty(ref _currentKeyText, value);
    }

    public int CurrentKeyInterval
    {
        get => _currentKeyInterval;
        set => SetProperty(ref _currentKeyInterval, value);
    }

    public ObservableCollection<KeyItem> KeyItems
    {
        get => _keyItems;
        set => SetProperty(ref _keyItems, value);
    }

    #endregion

    #region 命令

    public ICommand SaveCommand { get; }
    public ICommand AddKeyCommand { get; }
    public ICommand EditKeyCommand { get; }
    public ICommand DeleteKeyCommand { get; }

    #endregion

    public KeyConfigurationDialogViewModel(KeyConfiguration configuration, LyKeysService lyKeysService)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _lyKeysService = lyKeysService ?? throw new ArgumentNullException(nameof(lyKeysService));

        // 创建服务
        _coordinateService = new CoordinateManagementService();
        _keyListService = new KeyListManagementService(_lyKeysService, null, _coordinateService);

        // 初始化命令
        SaveCommand = CreateCommand(Save, CanSave);
        AddKeyCommand = CreateCommand(AddKey, CanAddKey);
        EditKeyCommand = CreateCommand<KeyItem>(EditKey, CanEditKey);
        DeleteKeyCommand = CreateCommand<KeyItem>(DeleteKey);

        // 加载配置数据
        LoadConfiguration();
    }

    #region 初始化

    private void LoadConfiguration()
    {
        // 加载基本信息
        ConfigurationName = _configuration.Name;
        _startKey = _configuration.StartKey;
        _startMods = _configuration.StartMods;
        _stopKey = _configuration.StopKey;
        _stopMods = _configuration.StopMods;
        UpdateHotkeyText();

        // 加载执行模式
        SelectedExecutionMode = _configuration.ExecutionMode switch
        {
            KeyExecutionMode.Loop => "循环",
            KeyExecutionMode.Sequence => "单次",
            KeyExecutionMode.Hold => "按压",
            _ => "单次"
        };

        // 加载设置
        DefaultInterval = _configuration.Interval;
        IsReduceKeyStuck = _configuration.IsReduceKeyStuck;
        SoundEnabled = _configuration.SoundEnabled;
        SoundVolume = _configuration.SoundVolume;
        AutoSwitchIME = _configuration.AutoSwitchToEnglishIME;

        // 加载按键列表
        KeyItems = new ObservableCollection<KeyItem>();
        _keyListService.LoadFromConfig(_configuration.Keys, KeyItems);

        Logger.Debug($"已加载配置: {ConfigurationName}, 按键数: {KeyItems.Count}");
    }

    #endregion

    #region 热键管理

    public void SetStartHotkey(VirtualKeyCode keyCode, ModifierKeys modifiers)
    {
        _startKey = keyCode;
        _startMods = modifiers;
        UpdateHotkeyText();
        Logger.Debug($"设置激活热键: {StartHotkeyText}");
    }

    public void SetStopHotkey(VirtualKeyCode keyCode, ModifierKeys modifiers)
    {
        _stopKey = keyCode;
        _stopMods = modifiers;
        UpdateHotkeyText();
        Logger.Debug($"设置停止热键: {StopHotkeyText}");
    }

    public void ClearStartHotkey()
    {
        _startKey = null;
        _startMods = ModifierKeys.None;
        UpdateHotkeyText();
    }

    public void ClearStopHotkey()
    {
        _stopKey = null;
        _stopMods = ModifierKeys.None;
        UpdateHotkeyText();
    }

    private void UpdateHotkeyText()
    {
        StartHotkeyText = BuildHotkeyText(_startKey, _startMods);
        StopHotkeyText = BuildHotkeyText(_stopKey, _stopMods);
    }

    private string BuildHotkeyText(VirtualKeyCode? keyCode, ModifierKeys modifiers)
    {
        if (!keyCode.HasValue) return "未设置";

        var sb = new StringBuilder();
        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control) sb.Append("Ctrl + ");
        if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) sb.Append("Alt + ");
        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) sb.Append("Shift + ");
        if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows) sb.Append("Win + ");
        sb.Append(_lyKeysService.GetKeyDescription(keyCode.Value));

        return sb.ToString();
    }

    #endregion

    #region 按键管理

    public void SetCurrentKey(VirtualKeyCode keyCode)
    {
        _currentKey = keyCode;
        CurrentKeyText = _lyKeysService.GetKeyDescription(keyCode);
        Logger.Debug($"设置当前按键: {keyCode}");
    }

    private bool CanAddKey()
    {
        return _currentKey.HasValue;
    }

    private void AddKey()
    {
        if (!_currentKey.HasValue) return;

        try
        {
            _keyListService.AddKeyboardKey(_currentKey.Value, _currentKeyInterval, KeyItems, _startKey);
            Logger.Info($"已添加按键: {CurrentKeyText}");

            // 清空输入
            _currentKey = null;
            CurrentKeyText = string.Empty;
        }
        catch (Exception ex)
        {
            Logger.Error("添加按键失败", ex);
        }
    }

    /// <summary>
    /// 添加坐标
    /// </summary>
    public void AddCoordinate(int x, int y)
    {
        try
        {
            _keyListService.AddCoordinate(x, y, _currentKeyInterval, KeyItems);
            Logger.Info($"已添加坐标: ({x}, {y})");
        }
        catch (Exception ex)
        {
            Logger.Error("添加坐标失败", ex);
            HandyControl.Controls.MessageBox.Error($"添加坐标失败: {ex.Message}", "错误");
        }
    }

    private bool CanEditKey(KeyItem? keyItem)
    {
        return keyItem != null;
    }

    private void EditKey(KeyItem? keyItem)
    {
        if (keyItem == null) return;

        try
        {
            // 只支持编辑键盘按键类型
            if (keyItem.Type == KeyItemType.Keyboard)
            {
                // 将选中的按键信息加载到输入框
                _currentKey = keyItem.KeyCode;
                CurrentKeyText = keyItem.DisplayName;
                CurrentKeyInterval = keyItem.KeyInterval;

                Logger.Info($"正在编辑按键: {keyItem.DisplayName}");
            }
            else
            {
                Logger.Warning("坐标类型的按键项不支持编辑");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("编辑按键失败", ex);
        }
    }

    private void DeleteKey(KeyItem keyItem)
    {
        try
        {
            _keyListService.DeleteKey(keyItem, KeyItems);
            Logger.Info($"已删除按键: {keyItem.DisplayName}");
        }
        catch (Exception ex)
        {
            Logger.Error("删除按键失败", ex);
        }
    }

    #endregion

    #region 保存

    private bool CanSave()
    {
        return !string.IsNullOrWhiteSpace(ConfigurationName) &&
               _startKey.HasValue &&
               KeyItems.Count > 0;
    }

    private void Save()
    {
        try
        {
            // 更新配置
            _configuration.Name = ConfigurationName;
            _configuration.StartKey = _startKey;
            _configuration.StartMods = _startMods;
            _configuration.StopKey = _stopKey;
            _configuration.StopMods = _stopMods;

            // 更新执行模式
            _configuration.ExecutionMode = SelectedExecutionMode switch
            {
                "循环" => KeyExecutionMode.Loop,
                "单次" => KeyExecutionMode.Sequence,
                "按压" => KeyExecutionMode.Hold,
                _ => KeyExecutionMode.Sequence
            };

            // 更新设置
            _configuration.Interval = DefaultInterval;
            _configuration.IsReduceKeyStuck = IsReduceKeyStuck;
            _configuration.SoundEnabled = SoundEnabled;
            _configuration.SoundVolume = SoundVolume;
            _configuration.AutoSwitchToEnglishIME = AutoSwitchIME;

            // 更新按键列表
            _configuration.Keys = _keyListService.ToConfigFormat(KeyItems);

            Logger.Info($"配置已保存: {ConfigurationName}");

            // 触发保存成功事件（会在 KeyMappingViewModel 中处理保存和关闭对话框）
            SaveCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error("保存配置失败", ex);
            HandyControl.Controls.MessageBox.Error($"保存配置失败: {ex.Message}", "错误");
        }
    }

    #endregion

    #region 事件和窗口控制

    public event EventHandler? SaveCompleted;
    public event EventHandler? CloseRequested;

    /// <summary>
    /// 取消编辑并关闭窗口
    /// </summary>
    public void Cancel()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion
}
