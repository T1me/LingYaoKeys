using CommunityToolkit.Mvvm.ComponentModel;

namespace WpfApp.ViewModels;

/// <summary>
/// 浮窗状态视图模型
/// 简单的状态展示，不需要 Logger 或 ConfigManager
/// </summary>
public partial class FloatingStatusViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isExecuting = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isHotkeyControlEnabled = true;

    [ObservableProperty]
    private double _opacity = 1.0;

    // 当前状态文本（只读计算属性）
    public string StatusText
    {
        get
        {
            if (!IsHotkeyControlEnabled) return "已禁用";
            return IsExecuting ? "运行中" : "已停止";
        }
    }
}
