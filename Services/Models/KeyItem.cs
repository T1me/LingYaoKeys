using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using WpfApp.Services.Core;

namespace WpfApp.Services.Models;

/// <summary>
/// 按键项目类型枚举
/// </summary>
public enum KeyItemType
{
    /// <summary>
    /// 键盘按键
    /// </summary>
    Keyboard,

    /// <summary>
    /// 鼠标坐标点击
    /// </summary>
    Coordinates
}

public partial class KeyItem : ObservableObject
{
    private readonly LyKeysService _lyKeysService;

    public event EventHandler<bool>? SelectionChanged;
    public event EventHandler<int>? KeyIntervalChanged;

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private VirtualKeyCode _keyCode;

    [ObservableProperty]
    private int _keyInterval;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private int? _x;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private int? _y;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private KeyItemType _type = KeyItemType.Keyboard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private int _coordinateIndex = 0;

    /// <summary>
    /// 创建键盘按键类型的项目
    /// </summary>
    public KeyItem(VirtualKeyCode keyCode, LyKeysService lyKeysService)
    {
        _keyCode = keyCode;
        _lyKeysService = lyKeysService ?? throw new ArgumentNullException(nameof(lyKeysService));
        _type = KeyItemType.Keyboard;
        _x = null;
        _y = null;
    }

    /// <summary>
    /// 创建鼠标坐标类型的项目
    /// </summary>
    public KeyItem(int x, int y, LyKeysService lyKeysService)
    {
        _x = x;
        _y = y;
        _lyKeysService = lyKeysService ?? throw new ArgumentNullException(nameof(lyKeysService));
        _type = KeyItemType.Coordinates;
        _keyCode = default;
    }

    // 属性变更通知 - 触发自定义事件
    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke(this, value);
    }

    partial void OnKeyIntervalChanged(int value)
    {
        KeyIntervalChanged?.Invoke(this, value);
    }

    /// <summary>
    /// 在列表中显示的名称，根据类型显示不同格式
    /// </summary>
    public string DisplayName
    {
        get
        {
            return _type switch
            {
                KeyItemType.Keyboard => _lyKeysService.GetKeyDescription(_keyCode),
                KeyItemType.Coordinates => $"坐标 {_coordinateIndex + 1}-({_x ?? 0}, {_y ?? 0})",
                _ => "未知类型"
            };
        }
    }

}