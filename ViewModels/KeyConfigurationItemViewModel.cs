using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WpfApp.Services.Models;

namespace WpfApp.ViewModels;

/// <summary>
/// 按键配置项视图模型
/// 用于在配置列表中显示单个配置
/// </summary>
public partial class KeyConfigurationItemViewModel : ObservableObject
{
    private readonly KeyConfiguration _configuration;

    [ObservableProperty]
    private bool _isActive;

    /// <summary>
    /// 配置ID
    /// </summary>
    public Guid Id => _configuration.Id;

    /// <summary>
    /// 配置名称
    /// </summary>
    public string Name
    {
        get => _configuration.Name;
        set
        {
            if (_configuration.Name != value)
            {
                _configuration.Name = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled
    {
        get => _configuration.IsEnabled;
        set
        {
            if (_configuration.IsEnabled != value)
            {
                _configuration.IsEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }


    /// <summary>
    /// 执行模式文本
    /// </summary>
    public string ExecutionModeText => _configuration.GetExecutionModeText();

    /// <summary>
    /// 激活热键文本
    /// </summary>
    public string StartHotkeyText => _configuration.GetStartHotkeyText();

    /// <summary>
    /// 停止热键文本
    /// </summary>
    public string StopHotkeyText => _configuration.GetStopHotkeyText();

    /// <summary>
    /// 按键数量
    /// </summary>
    public int KeyCount => _configuration.Keys?.Count ?? 0;

    /// <summary>
    /// 状态文本
    /// </summary>
    public string StatusText => IsEnabled ? "已启用" : "已禁用";

    /// <summary>
    /// 底层配置对象
    /// </summary>
    public KeyConfiguration Configuration => _configuration;

    public KeyConfigurationItemViewModel(KeyConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        // 订阅配置变更
        _configuration.PropertyChanged += OnConfigurationPropertyChanged;
    }

    private void OnConfigurationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 转发配置属性变更通知
        switch (e.PropertyName)
        {
            case nameof(KeyConfiguration.Name):
                OnPropertyChanged(nameof(Name));
                break;
            case nameof(KeyConfiguration.IsEnabled):
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(StatusText));
                break;
            case nameof(KeyConfiguration.ExecutionMode):
                OnPropertyChanged(nameof(ExecutionModeText));
                break;
            case nameof(KeyConfiguration.StartKey):
            case nameof(KeyConfiguration.StartMods):
                OnPropertyChanged(nameof(StartHotkeyText));
                break;
            case nameof(KeyConfiguration.StopKey):
            case nameof(KeyConfiguration.StopMods):
                OnPropertyChanged(nameof(StopHotkeyText));
                break;
            case nameof(KeyConfiguration.Keys):
                OnPropertyChanged(nameof(KeyCount));
                break;
        }
    }

}

