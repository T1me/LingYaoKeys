using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace WpfApp.Behaviors;

/// <summary>
/// 焦点管理行为 - 通过附加属性提供声明式焦点控制
/// </summary>
public static class FocusManagerBehavior
{
    #region AutoClearOnEnterEscape 附加属性

    public static readonly DependencyProperty AutoClearOnEnterEscapeProperty =
        DependencyProperty.RegisterAttached(
            "AutoClearOnEnterEscape",
            typeof(bool),
            typeof(FocusManagerBehavior),
            new PropertyMetadata(false, OnAutoClearOnEnterEscapeChanged));

    public static void SetAutoClearOnEnterEscape(DependencyObject obj, bool value)
        => obj.SetValue(AutoClearOnEnterEscapeProperty, value);

    public static bool GetAutoClearOnEnterEscape(DependencyObject obj)
        => (bool)obj.GetValue(AutoClearOnEnterEscapeProperty);

    private static void OnAutoClearOnEnterEscapeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;

        element.PreviewKeyDown -= OnPreviewKeyDown;
        if ((bool)e.NewValue)
        {
            element.PreviewKeyDown += OnPreviewKeyDown;
        }
    }

    private static void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Escape)
        {
            e.Handled = true;
            ClearFocus(sender as FrameworkElement);
        }
    }

    #endregion

    #region ClearFocusOnClickOutside 附加属性

    public static readonly DependencyProperty ClearFocusOnClickOutsideProperty =
        DependencyProperty.RegisterAttached(
            "ClearFocusOnClickOutside",
            typeof(bool),
            typeof(FocusManagerBehavior),
            new PropertyMetadata(false, OnClearFocusOnClickOutsideChanged));

    public static void SetClearFocusOnClickOutside(DependencyObject obj, bool value)
        => obj.SetValue(ClearFocusOnClickOutsideProperty, value);

    public static bool GetClearFocusOnClickOutside(DependencyObject obj)
        => (bool)obj.GetValue(ClearFocusOnClickOutsideProperty);

    private static void OnClearFocusOnClickOutsideChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;

        element.PreviewMouseDown -= OnPreviewMouseDown;

        if (element is Window window)
        {
            window.Deactivated -= OnWindowDeactivated;
        }

        if ((bool)e.NewValue)
        {
            element.PreviewMouseDown += OnPreviewMouseDown;

            if (element is Window win)
            {
                win.Deactivated += OnWindowDeactivated;
            }
        }
    }

    private static void OnWindowDeactivated(object? sender, EventArgs e) => ClearFocus(null);

    private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        var hitPoint = e.GetPosition(element);

        if (hitPoint.X < 0 || hitPoint.Y < 0 ||
            hitPoint.X > element.ActualWidth || hitPoint.Y > element.ActualHeight)
        {
            return;
        }

        if (IsPopupOrChildOfPopup(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var hitTestResult = VisualTreeHelper.HitTest(element, hitPoint);
        var hitElement = hitTestResult?.VisualHit as DependencyObject;

        // 只在点击空白区域时清除焦点，不阻止可交互控件的事件
        if (hitTestResult != null && !IsInputControl(hitElement) && !IsInteractiveControl(hitElement))
        {
            ClearFocus(null);
        }
    }

    #endregion

    #region 辅助方法

    private static void ClearFocus(FrameworkElement element)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            // 更新数据绑定
            if (element != null)
            {
                var property = element switch
                {
                    System.Windows.Controls.TextBox => System.Windows.Controls.TextBox.TextProperty,
                    System.Windows.Controls.ComboBox => System.Windows.Controls.ComboBox.TextProperty,
                    _ => null
                };

                if (property != null)
                {
                    BindingOperations.GetBindingExpression(element, property)?.UpdateSource();
                }

                if (element is System.Windows.Controls.ComboBox comboBox)
                {
                    comboBox.IsDropDownOpen = false;
                }
            }

            // 清除焦点
            var focusScope = FocusManager.GetFocusScope(element ?? System.Windows.Application.Current.MainWindow);
            if (focusScope != null)
            {
                FocusManager.SetFocusedElement(focusScope, null);
            }

            Keyboard.ClearFocus();

            // 如果是窗口，设置焦点到窗口本身
            if (element is Window window)
            {
                window.Focus();
            }
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private static bool IsInputControl(DependencyObject element)
    {
        if (element == null) return false;

        while (element != null)
        {
            if (element is System.Windows.Controls.TextBox || element is System.Windows.Controls.ComboBox ||
                element is PasswordBox || element is System.Windows.Controls.RichTextBox)
            {
                return true;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private static bool IsInteractiveControl(DependencyObject element)
    {
        if (element == null) return false;

        while (element != null)
        {
            if (element is System.Windows.Controls.Button ||
                element is System.Windows.Controls.Primitives.ToggleButton ||
                element is System.Windows.Controls.CheckBox ||
                element is System.Windows.Controls.RadioButton ||
                element is System.Windows.Controls.Slider ||
                element is System.Windows.Controls.ListBox ||
                element is System.Windows.Controls.ListBoxItem)
            {
                return true;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private static bool IsPopupOrChildOfPopup(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is System.Windows.Controls.Primitives.Popup ||
                element is System.Windows.Controls.ComboBoxItem ||
                element is Window window && window != System.Windows.Application.Current.MainWindow)
            {
                return true;
            }

            if (element is FrameworkElement { Name: var name } && IsPopupRelatedName(name))
            {
                return true;
            }

            element = GetParentElement(element);
        }

        return false;
    }

    private static bool IsPopupRelatedName(string? name) =>
        name?.Contains("Popup") == true ||
        name?.Contains("Dialog") == true ||
        name?.Contains("Config") == true;

    private static DependencyObject? GetParentElement(DependencyObject element)
    {
        try
        {
            return VisualTreeHelper.GetParent(element);
        }
        catch (InvalidOperationException)
        {
            return element is FrameworkElement fe ? fe.Parent : null;
        }
    }

    #endregion
}
