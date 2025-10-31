namespace WpfApp.ViewModels;

public class FloatingStatusViewModel : ViewModelBase
{
    private bool _isExecuting = false;
    private bool _isHotkeyControlEnabled = true;

    // 当前状态文本（只读计算属性）
    public string StatusText
    {
        get
        {
            if (!_isHotkeyControlEnabled) return "已禁用";
            return _isExecuting ? "运行中" : "已停止";
        }
    }

    // 是否正在执行
    public bool IsExecuting
    {
        get => _isExecuting;
        set
        {
            if (_isExecuting != value)
            {
                _isExecuting = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    // 热键总开关状态
    public bool IsHotkeyControlEnabled
    {
        get => _isHotkeyControlEnabled;
        set
        {
            if (_isHotkeyControlEnabled != value)
            {
                _isHotkeyControlEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }
}