using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfApp.Services.Core;
using WpfApp.Services.Models;
using WpfApp.Services.UI;
using WpfApp.ViewModels;
using Button = System.Windows.Controls.Button;

namespace WpfApp.Views;

/// <summary>
/// KeyConfigurationWindow.xaml 的交互逻辑
/// </summary>
public partial class KeyConfigurationWindow : Window
{
    private readonly InputCaptureService _inputCaptureService;
    private readonly CoordinateDragService _dragService;
    private readonly CoordinateVisualizationService _coordinateService;
    private bool _isEditMode = false;

    public KeyConfigurationWindow()
    {
        InitializeComponent();
        _inputCaptureService = new InputCaptureService();
        _dragService = new CoordinateDragService();
        _coordinateService = new CoordinateVisualizationService();

        // 订阅坐标捕获事件
        _dragService.CoordinateCaptured += OnCoordinateCaptured;

        // 订阅坐标更新事件
        _coordinateService.CoordinateUpdated += OnCoordinateUpdated;

        // 附加拖拽行为到鼠标图标按钮
        Loaded += (s, e) =>
        {
            if (btnMouseIcon != null)
            {
                _dragService.AttachToElement(btnMouseIcon);
                // 添加 PreviewMouseUp 事件处理点击进入编辑模式
                btnMouseIcon.PreviewMouseUp += MouseIcon_PreviewMouseUp;
            }

            // 订阅 ViewModel 事件
            if (ViewModel != null)
            {
                ViewModel.SaveCompleted += ViewModel_SaveCompleted;
                ViewModel.CloseRequested += ViewModel_CloseRequested;
            }
        };

        Unloaded += (s, e) =>
        {
            if (btnMouseIcon != null)
            {
                btnMouseIcon.PreviewMouseUp -= MouseIcon_PreviewMouseUp;
            }

            // 退出编辑模式（如果处于编辑模式）
            if (_isEditMode)
            {
                _isEditMode = false;
                _coordinateService?.HideAll();
            }

            // 清理资源
            _dragService?.Dispose();
            _coordinateService?.Dispose();

            // 取消订阅 ViewModel 事件
            if (ViewModel != null)
            {
                ViewModel.SaveCompleted -= ViewModel_SaveCompleted;
                ViewModel.CloseRequested -= ViewModel_CloseRequested;
            }
        };
    }

    private KeyConfigurationDialogViewModel ViewModel => DataContext as KeyConfigurationDialogViewModel;

    /// <summary>
    /// 鼠标抬起事件 - 处理点击进入/退出坐标编辑模式
    /// </summary>
    private void MouseIcon_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        // 只有在点击（未拖拽）时才切换编辑模式
        if (e.LeftButton == MouseButtonState.Released && !_dragService.IsDragStarted)
        {
            ToggleEditMode();
            e.Handled = true;
        }
    }

    /// <summary>
    /// 切换坐标编辑模式
    /// </summary>
    private void ToggleEditMode()
    {
        if (!_isEditMode)
        {
            // 进入编辑模式前检查是否有坐标
            if (ViewModel?.KeyItems == null || !ViewModel.KeyItems.Any(k => k.Type == KeyItemType.Coordinates))
            {
                HandyControl.Controls.MessageBox.Warning("按键列表中没有坐标，无法进入编辑模式\n请先添加坐标位置", "提示");
                return;
            }
        }

        _isEditMode = !_isEditMode;
        if (_isEditMode)
        {
            ChangeMouseIconToExit();
            _coordinateService.ShowAll(ViewModel.KeyItems);
            HandyControl.Controls.Growl.Success("已进入坐标编辑模式\n可拖动红点调整坐标位置");
        }
        else
        {
            RestoreOriginalMouseIcon();
            _coordinateService.HideAll();
            HandyControl.Controls.Growl.Info("已退出坐标编辑模式");
        }
    }

    /// <summary>
    /// 更改鼠标图标为退出标志
    /// </summary>
    private void ChangeMouseIconToExit()
    {
        if (btnMouseIcon.Content is Path path)
        {
            // 保存原始图标数据
            path.Tag = path.Data;

            // 更改为退出图标（X 符号）
            path.Data = Geometry.Parse("M512 456.310154L325.15799 269.469166c-16.662774-16.662774-43.677083-16.662774-60.339857 0s-16.662774 43.677083 0 60.339857L451.656143 516.650011 264.818154 703.490999c-16.662774 16.662774-16.662774 43.677083 0 60.339857s43.677083 16.662774 60.339857 0l186.840988-186.840988 186.840988 186.840988c16.662774 16.662774 43.677083 16.662774 60.339857 0s16.662774-43.677083 0-60.339857L572.340834 516.650011l186.840988-186.840988c16.662774-16.662774 16.662774-43.677083 0-60.339857s-43.677083-16.662774-60.339857 0L512 456.310154z");
            path.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 75, 108));
            btnMouseIcon.ToolTip = "退出编辑模式";
        }
    }

    /// <summary>
    /// 恢复原始鼠标图标
    /// </summary>
    private void RestoreOriginalMouseIcon()
    {
        if (btnMouseIcon.Content is Path path && path.Tag is Geometry originalData)
        {
            path.Data = originalData;
            path.Tag = null;
            path.Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#707070"));
            btnMouseIcon.ToolTip = "点击进入坐标编辑模式，或拖拽到目标位置捕获坐标";
        }
    }

    /// <summary>
    /// 坐标更新事件处理（拖动标记时触发）
    /// </summary>
    private void OnCoordinateUpdated(object sender, CoordinateUpdatedEventArgs e)
    {
        // 坐标已通过拖拽更新，无需额外操作
        // KeyItem 已自动更新，退出编辑模式时会保存
    }

    /// <summary>
    /// 坐标捕获事件处理（拖拽触发）
    /// </summary>
    private void OnCoordinateCaptured(object sender, CoordinateCapturedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (XCoordinateInputBox != null)
                XCoordinateInputBox.Text = e.X.ToString();
            if (YCoordinateInputBox != null)
                YCoordinateInputBox.Text = e.Y.ToString();

            // 更新输入框绑定
            XCoordinateInputBox?.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
            YCoordinateInputBox?.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
        });
    }

    /// <summary>
    /// 热键输入处理
    /// </summary>
    private void HotkeyInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox) return;
        e.Handled = true;

        var keyCode = _inputCaptureService.CaptureKeyboardInput(e);
        if (!keyCode.HasValue)
        {
            return;
        }

        // 忽略单独的修饰键
        if (_inputCaptureService.IsModifierKey(keyCode.Value))
            return;

        var modifiers = Keyboard.Modifiers;
        var tag = textBox.Tag?.ToString();

        if (tag == "Start")
        {
            ViewModel?.SetStartHotkey(keyCode.Value, modifiers);
        }
        else if (tag == "Stop")
        {
            ViewModel?.SetStopHotkey(keyCode.Value, modifiers);
        }

        // 清除焦点
        ClearFocus(textBox);
    }

    /// <summary>
    /// 按键输入处理
    /// </summary>
    private void KeyInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox) return;
        e.Handled = true;

        var keyCode = _inputCaptureService.CaptureKeyboardInput(e);
        if (!keyCode.HasValue)
        {
            return;
        }

        ViewModel?.SetCurrentKey(keyCode.Value);
        ClearFocus(textBox);
    }

    /// <summary>
    /// 清除热键
    /// </summary>
    private void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        var tag = button.Tag?.ToString();
        if (tag == "Start")
        {
            ViewModel?.ClearStartHotkey();
        }
        else if (tag == "Stop")
        {
            ViewModel?.ClearStopHotkey();
        }
    }

    /// <summary>
    /// 清除焦点
    /// </summary>
    private void ClearFocus(System.Windows.Controls.TextBox textBox)
    {
        var focusScope = FocusManager.GetFocusScope(textBox);
        FocusManager.SetFocusedElement(focusScope, null);
        Keyboard.ClearFocus();
        if (textBox.IsFocused && textBox.Parent is UIElement parent)
            parent.Focus();
    }

    /// <summary>
    /// 音量设置按钮点击事件
    /// </summary>
    private void SoundVolumeSettings_Click(object sender, RoutedEventArgs e)
    {
        if (soundVolumePopup != null)
        {
            soundVolumePopup.IsOpen = !soundVolumePopup.IsOpen;
            if (soundVolumePopup.IsOpen && volumeSlider != null)
                volumeSlider.Focus();
        }
    }

    /// <summary>
    /// 添加坐标按钮点击事件
    /// </summary>
    private void AddCoordinate_Click(object sender, RoutedEventArgs e)
    {
        if (XCoordinateInputBox == null || YCoordinateInputBox == null) return;

        if (!int.TryParse(XCoordinateInputBox.Text, out var x))
            x = 0;
        if (!int.TryParse(YCoordinateInputBox.Text, out var y))
            y = 0;

        if (x <= 0 && y <= 0)
        {
            HandyControl.Controls.MessageBox.Warning("请输入有效的坐标值", "提示");
            return;
        }

        ViewModel?.AddCoordinate(x, y);

        // 清空输入
        XCoordinateInputBox.Text = string.Empty;
        YCoordinateInputBox.Text = string.Empty;
    }

    /// <summary>
    /// 切换按键/坐标模式
    /// </summary>
    private void ToggleCoordinateMode_Click(object sender, RoutedEventArgs e)
    {
        if (KeyInputArea.Visibility == Visibility.Visible)
        {
            // 切换到坐标模式
            KeyInputArea.Visibility = Visibility.Collapsed;
            CoordinateInputArea.Visibility = Visibility.Visible;
        }
        else
        {
            // 切换到按键模式
            KeyInputArea.Visibility = Visibility.Visible;
            CoordinateInputArea.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 按键输入框鼠标按下事件
    /// </summary>
    private void KeyInputBox_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox) return;

        if (!textBox.IsFocused)
        {
            textBox.Focus();
            e.Handled = true;
        }
    }

    /// <summary>
    /// 数字输入框获得焦点
    /// </summary>
    private void NumberInput_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    /// <summary>
    /// 数字输入框失去焦点
    /// </summary>
    private void NumberInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox) return;

        // 验证数字输入
        if (string.IsNullOrWhiteSpace(textBox.Text))
        {
            textBox.Text = "0";
        }
        else if (!int.TryParse(textBox.Text, out _))
        {
            textBox.Text = "0";
        }
    }

    /// <summary>
    /// 数字输入验证
    /// </summary>
    private void NumberValidationTextBox(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        // 只允许数字输入
        e.Handled = !int.TryParse(e.Text, out _);
    }

    /// <summary>
    /// 间隔帮助按钮点击
    /// </summary>
    private void IntervalHelp_Click(object sender, RoutedEventArgs e)
    {
        HandyControl.Controls.MessageBox.Info(
            "按键间隔说明：\n\n" +
            "• 间隔值单位为毫秒(ms)\n" +
            "• 表示两个按键操作之间的等待时间\n" +
            "• 建议值：10-100ms\n" +
            "• 过小可能导致按键失效\n" +
            "• 过大会降低执行速度",
            "按键间隔说明");
    }

    /// <summary>
    /// 保存完成事件处理
    /// </summary>
    private void ViewModel_SaveCompleted(object? sender, EventArgs e)
    {
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// 关闭请求事件处理
    /// </summary>
    private void ViewModel_CloseRequested(object? sender, EventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// 取消按钮点击事件
    /// </summary>
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
