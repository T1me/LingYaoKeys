using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WpfApp.Services.Core;
using WpfApp.Services.Models;
using WpfApp.Services.UI;
using WpfApp.Services.Utils;
using WpfApp.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using Path = System.Windows.Shapes.Path;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Application = System.Windows.Application;
using ListBox = System.Windows.Controls.ListBox;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;

namespace WpfApp.Views;

/// <summary>
/// KeyMappingView.xaml 的交互逻辑
/// </summary>
public partial class KeyMappingView : Page
{
    private readonly SerilogManager _logger = SerilogManager.Instance;
    private readonly CoordinateVisualizationService _coordinateService;
    private readonly InputCaptureService _inputCaptureService;
    private readonly CoordinateDragService _dragService;
    private HotkeyService _hotkeyService;
    private bool _isCoordinateMarkersVisible;
    private bool _isEditMode;

    private const string KEY_ERROR = "无法识别按键，请检查输入法是否关闭";
    private const string HOTKEY_CONFLICT = "无法设置与热键相同的按键";

    public KeyMappingView()
    {
        InitializeComponent();

        _coordinateService = new CoordinateVisualizationService();
        _inputCaptureService = new InputCaptureService();
        _dragService = new CoordinateDragService();

        _coordinateService.CoordinateUpdated += OnCoordinateUpdated;
        _dragService.CoordinateCaptured += OnCoordinateCaptured;

        DataContextChanged += OnDataContextChanged;
        LostFocus += (s, e) => { /* 清除删除确认状态由 Behavior 处理 */ };
        Unloaded += OnUnloaded;

        if (btnMouseIcon != null)
        {
            _dragService.AttachToElement(btnMouseIcon);
            btnMouseIcon.PreviewMouseUp += MouseIcon_PreviewMouseUp;
        }
    }

    private KeyMappingViewModel ViewModel => DataContext as KeyMappingViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is KeyMappingViewModel oldVm && oldVm.KeyList != null)
            oldVm.KeyList.CollectionChanged -= OnKeyListChanged;

        if (e.NewValue is KeyMappingViewModel newVm)
        {
            _hotkeyService = newVm.GetHotkeyService();
            if (newVm.KeyList != null)
                newVm.KeyList.CollectionChanged += OnKeyListChanged;
        }
    }

    private void OnKeyListChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_isCoordinateMarkersVisible && !_isEditMode) return;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems != null)
                {
                    var coordinates = ViewModel.KeyList.Where(item => item.Type == KeyItemType.Coordinates).ToList();
                    foreach (KeyItem newItem in e.NewItems)
                    {
                        if (newItem.Type != KeyItemType.Coordinates) continue;
                        int index = coordinates.IndexOf(newItem);
                        if (index >= 0)
                            _coordinateService.AddMarker(newItem, index);
                    }
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems != null)
                {
                    foreach (KeyItem oldItem in e.OldItems)
                        _coordinateService.RemoveMarker(oldItem);
                    _coordinateService.UpdateAllIndices(ViewModel.KeyList);
                }
                break;

            case NotifyCollectionChangedAction.Reset:
                _coordinateService.ShowAll(ViewModel.KeyList);
                break;
        }
    }

    private void OnCoordinateUpdated(object sender, CoordinateUpdatedEventArgs e)
    {
        // 仅同步到热键服务，不保存配置，避免触发配置变更事件导致 KeyList 重新加载
        ViewModel?.SyncKeyListToHotkeyService();
    }

    private void OnCoordinateCaptured(object sender, CoordinateCapturedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.CurrentX = e.X;
            ViewModel.CurrentY = e.Y;
            Dispatcher.BeginInvoke(() =>
            {
                XCoordinateInputBox?.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                YCoordinateInputBox?.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            });
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _coordinateService?.Dispose();
        _dragService?.Dispose();
        if (ViewModel?.KeyList != null)
            ViewModel.KeyList.CollectionChanged -= OnKeyListChanged;
    }

    // 按键输入处理
    private void KeyInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        e.Handled = true;

        var keyCode = _inputCaptureService.CaptureKeyboardInput(e);
        if (!keyCode.HasValue)
        {
            ShowMessage(KEY_ERROR, true);
            return;
        }

        if (ViewModel.IsHotkeyConflict(keyCode.Value))
        {
            ShowMessage(HOTKEY_CONFLICT, true);
            return;
        }

        ViewModel?.SetCurrentKey(keyCode.Value);
        ShowMessage($"已选择按键: {ViewModel?.CurrentKeyText}");
        ClearFocus(textBox);
    }

    private void KeyInputBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox) return;

        if (!textBox.IsFocused)
        {
            textBox.Focus();
            e.Handled = true;
            return;
        }

        var keyCode = _inputCaptureService.CaptureMouseInput(e);
        if (keyCode.HasValue)
        {
            e.Handled = true;
            if (ViewModel.IsHotkeyConflict(keyCode.Value))
            {
                ShowMessage(HOTKEY_CONFLICT, true);
                return;
            }
            ViewModel?.SetCurrentKey(keyCode.Value);
            ShowMessage($"已选择按键: {ViewModel?.CurrentKeyText}");
            ClearFocus(textBox);
        }
    }

    // 热键输入处理
    private void StartHotkeyInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        e.Handled = true;

        var keyCode = _inputCaptureService.CaptureKeyboardInput(e);
        if (!keyCode.HasValue)
        {
            ShowMessage(KEY_ERROR, true);
            return;
        }

        if (_inputCaptureService.IsModifierKey(keyCode.Value)) return;

        if (ViewModel?.IsHotkeyConflict(keyCode.Value) == true)
        {
            ShowMessage("热键与按键序列冲突，请选择其他键", true);
            return;
        }

        ViewModel?.SetHotkey(keyCode.Value, Keyboard.Modifiers);
        ClearFocus(textBox);
    }

    private void StartHotkeyInput_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox) return;

        var keyCode = _inputCaptureService.CaptureMouseInput(e);
        if (keyCode.HasValue && keyCode != VirtualKeyCode.VK_LBUTTON && keyCode != VirtualKeyCode.VK_RBUTTON)
        {
            ViewModel?.SetHotkey(keyCode.Value, Keyboard.Modifiers);
            e.Handled = true;
        }
    }

    private void StartHotkeyInput_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            e.Handled = true;
            var keyCode = _inputCaptureService.CaptureMouseWheel(e);
            if (keyCode.HasValue)
                ViewModel?.SetHotkey(keyCode.Value, Keyboard.Modifiers);
        }
    }

    // 焦点管理
    private void KeyInputBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_hotkeyService != null) _hotkeyService.IsInputFocused = true;
    }

    private void KeyInputBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_hotkeyService != null) _hotkeyService.IsInputFocused = false;
    }

    private void ClearFocus(TextBox textBox)
    {
        var focusScope = FocusManager.GetFocusScope(textBox);
        FocusManager.SetFocusedElement(focusScope, null);
        Keyboard.ClearFocus();
        if (textBox.IsFocused && textBox.Parent is UIElement parent)
            parent.Focus();
    }

    // 坐标编辑模式
    private void MouseIcon_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Released && !_dragService.IsDragStarted)
        {
            ToggleEditMode();
            e.Handled = true;
        }
    }

    private void ToggleEditMode()
    {
        if (!_isEditMode)
        {
            // 进入编辑模式前检查是否有坐标
            if (!ViewModel.KeyList.Any(k => k.Type == KeyItemType.Coordinates))
            {
                ShowMessage("按键列表中没有坐标，无法进入编辑模式，请先拖动箭头添加坐标位置", isError: true);
                return;
            }
        }

        _isEditMode = !_isEditMode;
        if (_isEditMode)
        {
            ChangeMouseIconToExit();
            _coordinateService.ShowAll(ViewModel.KeyList);
            ViewModel.IsCoordinateEditMode = true;
            ShowMessage("已进入坐标编辑模式");
        }
        else
        {
            RestoreOriginalMouseIcon();
            if (!_isCoordinateMarkersVisible)
                _coordinateService.HideAll();

            ViewModel.IsCoordinateEditMode = false;
            // 退出编辑模式时保存坐标配置
            ViewModel?.SaveKeyConfig();
            ShowMessage("已退出坐标编辑模式并保存坐标");
        }
    }

    private void ChangeMouseIconToExit()
    {
        if (btnMouseIcon.Content is Path path)
        {
            path.Tag = path.Data;
            path.Data = Geometry.Parse("M512 456.310154L325.15799 269.469166c-16.662774-16.662774-43.677083-16.662774-60.339857 0s-16.662774 43.677083 0 60.339857L451.656143 516.650011 264.818154 703.490999c-16.662774 16.662774-16.662774 43.677083 0 60.339857s43.677083 16.662774 60.339857 0l186.840988-186.840988 186.840988 186.840988c16.662774 16.662774 43.677083 16.662774 60.339857 0s16.662774-43.677083 0-60.339857L572.340834 516.650011l186.840988-186.840988c16.662774-16.662774 16.662774-43.677083 0-60.339857s-43.677083-16.662774-60.339857 0L512 456.310154z");
            path.Fill = new SolidColorBrush(Color.FromRgb(232, 75, 108));
            btnMouseIcon.ToolTip = "退出编辑模式并保存坐标值";
        }
    }

    private void RestoreOriginalMouseIcon()
    {
        if (btnMouseIcon.Content is Path path && path.Tag is Geometry originalData)
        {
            path.Data = originalData;
            path.Tag = null;
            path.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#707070"));
            btnMouseIcon.ToolTip = "鼠标位置";
        }
    }

    // 辅助方法：统一消息显示接口
    private void ShowMessage(string message, bool isError = false)
    {
        ViewModel?.ShowMessage(message, isError);
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }


    // ListView 选择管理
    private void KeysList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListView listView)
        {
            var scrollBar = FindParent<ScrollBar>(e.OriginalSource as DependencyObject);
            if (scrollBar != null) return;

            var hitTest = VisualTreeHelper.HitTest(listView, e.GetPosition(listView));
            if (hitTest == null || FindParent<System.Windows.Controls.ListViewItem>(hitTest.VisualHit as DependencyObject) == null)
            {
                listView.SelectedItem = null;
                e.Handled = true;
            }
        }
    }

    private static T FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        if (child == null) return null;
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null && !(parent is T))
            parent = VisualTreeHelper.GetParent(parent);
        return parent as T;
    }

    // Popup 帮助按钮
    private void IntervalHelp_Click(object sender, RoutedEventArgs e)
    {
        if (FindName("helpPopup") is Popup popup)
            popup.IsOpen = !popup.IsOpen;
    }

    private void ModeHelp_Click(object sender, RoutedEventArgs e)
    {
        if (FindName("modeHelpPopup") is Popup popup)
            popup.IsOpen = !popup.IsOpen;
    }

    private void VolumeHelp_Click(object sender, RoutedEventArgs e)
    {
        if (FindName("volumeHelpPopup") is Popup popup)
            popup.IsOpen = !popup.IsOpen;
    }

    private void SoundSettings_Click(object sender, RoutedEventArgs e)
    {
        if (soundSettingsPopup != null)
        {
            soundSettingsPopup.IsOpen = !soundSettingsPopup.IsOpen;
            if (soundSettingsPopup.IsOpen && volumeSlider != null)
                volumeSlider.Focus();
        }
    }

    private void FloatingOpacitySettings_Click(object sender, RoutedEventArgs e)
    {
        if (floatingOpacityPopup != null)
        {
            floatingOpacityPopup.IsOpen = !floatingOpacityPopup.IsOpen;
            if (floatingOpacityPopup.IsOpen && opacitySlider != null)
                opacitySlider.Focus();
        }
    }

    private void ToggleCoordinateMode_Click(object sender, RoutedEventArgs e)
    {
        bool isCoordinateMode = CoordinateInputArea.Visibility == Visibility.Visible;
        KeyInputArea.Visibility = isCoordinateMode ? Visibility.Visible : Visibility.Collapsed;
        CoordinateInputArea.Visibility = isCoordinateMode ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AddKey_Click(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() => KeyInputBox?.Clear());
    }

    private void AddCoordinate_Click(object sender, RoutedEventArgs e)
    {
        bool wasInEditMode = _isEditMode;
        Dispatcher.BeginInvoke(() =>
        {
            XCoordinateInputBox?.Clear();
            YCoordinateInputBox?.Clear();
            if (ViewModel != null)
            {
                ViewModel.CurrentX = null;
                ViewModel.CurrentY = null;
            }
            if (wasInEditMode && _isEditMode)
                _coordinateService.ShowAll(ViewModel.KeyList);
        });
    }

    // 数字输入验证（由 NumericInputBehavior 处理，但 XAML 仍需要这些方法）
    private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !int.TryParse(e.Text, out _);
    }

    private void NumberInput_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_hotkeyService != null)
            _hotkeyService.IsInputFocused = true;
    }

    private void NumberInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_hotkeyService != null)
            _hotkeyService.IsInputFocused = false;
    }

    private void Page_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 空实现，保持兼容性
    }

    private void HotkeyInputBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_hotkeyService != null)
            _hotkeyService.IsInputFocused = false;
    }

    private void DeleteKeyButton_Click(object sender, RoutedEventArgs e)
    {
        // 由 DeleteConfirmationBehavior 处理
    }


    private void StartHotkeyInput_KeyDown(object sender, KeyEventArgs e)
    {
        StartHotkeyInput_PreviewKeyDown(sender, e);
    }

    private void StartHotkeyInput_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        // 空实现
    }

    private void StartHotkeyInput_MouseDown(object sender, MouseButtonEventArgs e)
    {
        StartHotkeyInput_PreviewMouseDown(sender, e);
    }

    private void HotkeyInputBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_hotkeyService != null)
            _hotkeyService.IsInputFocused = true;
    }

    private void KeysList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {

    }

    private void KeysList_SelectionChanged_1(object sender, SelectionChangedEventArgs e)
    {

    }
}
