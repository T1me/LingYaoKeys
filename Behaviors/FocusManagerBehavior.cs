using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Application = System.Windows.Application;
using RichTextBox = System.Windows.Controls.RichTextBox;
using PasswordBox = System.Windows.Controls.PasswordBox;
using WpfApp.Services.Utils;

namespace WpfApp.Behaviors;

/// <summary>
/// 焦点管理行为 - 提供简单的方式来管理控件的焦点
/// </summary>
public static class FocusManagerBehavior
{
    #region EnableFocusManagement 附加属性

    public static readonly DependencyProperty EnableFocusManagementProperty =
        DependencyProperty.RegisterAttached(
            "EnableFocusManagement",
            typeof(bool),
            typeof(FocusManagerBehavior),
            new PropertyMetadata(false, OnEnableFocusManagementChanged));

    public static bool GetEnableFocusManagement(DependencyObject obj)
    {
        return (bool)obj.GetValue(EnableFocusManagementProperty);
    }

    public static void SetEnableFocusManagement(DependencyObject obj, bool value)
    {
        obj.SetValue(EnableFocusManagementProperty, value);
    }

    private static void OnEnableFocusManagementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && (bool)e.NewValue)
        {
            if (element.IsLoaded)
                InitializeFocusManagement(element);
            else
                element.Loaded += (s, args) => InitializeFocusManagement(element);
        }
    }

    #endregion

    #region AutoClearFocusOnClick 附加属性

    public static readonly DependencyProperty AutoClearFocusOnClickProperty =
        DependencyProperty.RegisterAttached(
            "AutoClearFocusOnClick",
            typeof(bool),
            typeof(FocusManagerBehavior),
            new PropertyMetadata(false, OnAutoClearFocusOnClickChanged));

    public static bool GetAutoClearFocusOnClick(DependencyObject obj)
    {
        return (bool)obj.GetValue(AutoClearFocusOnClickProperty);
    }

    public static void SetAutoClearFocusOnClick(DependencyObject obj, bool value)
    {
        obj.SetValue(AutoClearFocusOnClickProperty, value);
    }

    private static void OnAutoClearFocusOnClickChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && (bool)e.NewValue)
        {
            element.PreviewMouseDown += (s, args) =>
            {
                // 检查点击位置是否在弹出窗口上
                var hitPoint = args.GetPosition(element);
                
                // 如果点击位置超出当前元素的范围，可能点击在弹窗上，不应清除焦点
                if (hitPoint.X < 0 || hitPoint.Y < 0 || 
                    hitPoint.X > element.ActualWidth || hitPoint.Y > element.ActualHeight)
                {
                    return;
                }
                
                var hitTestResult = VisualTreeHelper.HitTest(element, hitPoint);
                
                // 检查点击的是否为弹窗或其子元素
                if (IsPopupOrChildOfPopup(args.OriginalSource as DependencyObject))
                {
                    return;
                }
                
                if (hitTestResult == null || !IsInputControl(hitTestResult.VisualHit as DependencyObject))
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        FocusManagementService.Instance.ClearFocus();
                        Keyboard.ClearFocus();

                        // 如果是窗口，设置焦点到窗口本身
                        if (element is Window window) window.Focus();
                    }), System.Windows.Threading.DispatcherPriority.Input);

                    args.Handled = true;
                }
            };

            // 添加窗口失去焦点的处理
            if (element is Window window)
                window.Deactivated += (s, args) =>
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        FocusManagementService.Instance.ClearFocus();
                        Keyboard.ClearFocus();
                    }), System.Windows.Threading.DispatcherPriority.Input);
                };
        }
    }

    #endregion

    #region 私有辅助方法

    private static void InitializeFocusManagement(FrameworkElement element)
    {
        if (element is TextBox || element is ComboBox)
        {
            FocusManagementService.Instance.RegisterFocusableElement(element);

            // 为TextBox添加特殊处理
            if (element is TextBox textBox)
                SetupTextBoxBehavior(textBox);
            // 为ComboBox添加特殊处理
            else if (element is ComboBox comboBox) SetupComboBoxBehavior(comboBox);
        }
    }

    private static void SetupTextBoxBehavior(TextBox textBox)
    {
        // 处理Enter和Escape键
        textBox.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter || e.Key == Key.Escape)
            {
                e.Handled = true;
                FocusManagementService.Instance.ClearFocus();
            }
        };
    }

    private static void SetupComboBoxBehavior(ComboBox comboBox)
    {
        // 处理选择变化
        comboBox.SelectionChanged += (s, e) =>
        {
            if (comboBox.IsLoaded && !comboBox.IsDropDownOpen && !comboBox.IsFocused)
                Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => { FocusManagementService.Instance.ClearFocus(); }),
                    System.Windows.Threading.DispatcherPriority.Input);
        };

        // 处理下拉框关闭
        comboBox.DropDownClosed += (s, e) =>
        {
            if (comboBox.IsLoaded && !comboBox.IsFocused)
                Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => { FocusManagementService.Instance.ClearFocus(); }),
                    System.Windows.Threading.DispatcherPriority.Input);
        };

        // 处理失去焦点
        comboBox.LostFocus += (s, e) =>
        {
            if (comboBox.IsLoaded && !comboBox.IsDropDownOpen)
                Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => { FocusManagementService.Instance.ClearFocus(); }),
                    System.Windows.Threading.DispatcherPriority.Input);
        };
    }

    private static bool IsInputControl(DependencyObject element)
    {
        if (element == null) return false;

        while (element != null)
        {
            // 检查是否是输入控件
            if (element is TextBox || element is ComboBox ||
                element is PasswordBox || element is RichTextBox)
                return true;

            // 检查是否有特定标记
            if (element is FrameworkElement fe &&
                GetEnableFocusManagement(fe))
                return true;

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }
    
    private static bool IsPopupOrChildOfPopup(DependencyObject element)
    {
        if (element == null) return false;
        
        // 遍历可视树向上查找
        while (element != null)
        {
            // 检查是否是弹出窗口相关类型
            if (element is System.Windows.Controls.Primitives.Popup || 
                element is Window window && window != Application.Current.MainWindow)
                return true;
            
            // 检查名称是否包含常见的弹窗关键词
            if (element is FrameworkElement fe && 
                (fe.Name?.Contains("Popup") == true || 
                 fe.Name?.Contains("Dialog") == true ||
                 fe.Name?.Contains("Config") == true))
                return true;
                
            try
            {
                element = VisualTreeHelper.GetParent(element);
            }
            catch
            {
                // 如果获取父元素失败(例如跨窗口边界)，尝试获取逻辑父元素
                if (element is FrameworkElement frameworkElement)
                    element = frameworkElement.Parent;
                else
                    break;
            }
        }
        
        return false;
    }

    #endregion
}