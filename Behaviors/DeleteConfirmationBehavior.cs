using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Xaml.Behaviors;
using Button = System.Windows.Controls.Button;
using ToolTip = System.Windows.Controls.ToolTip;
using TextBlock = System.Windows.Controls.TextBlock;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace WpfApp.Behaviors;

/// <summary>
/// 删除确认行为，提供二次确认机制
/// </summary>
public class DeleteConfirmationBehavior : Behavior<Button>
{
    private static readonly Dictionary<Button, DispatcherTimer> PendingButtons = new();
    private static readonly DependencyProperty ConfirmStateProperty =
        DependencyProperty.RegisterAttached("ConfirmState", typeof(bool), typeof(DeleteConfirmationBehavior), new PropertyMetadata(false));

    public static readonly DependencyProperty ConfirmTimeoutProperty =
        DependencyProperty.Register(nameof(ConfirmTimeout), typeof(int), typeof(DeleteConfirmationBehavior), new PropertyMetadata(3));

    public static readonly DependencyProperty ConfirmedCommandProperty =
        DependencyProperty.Register(nameof(ConfirmedCommand), typeof(ICommand), typeof(DeleteConfirmationBehavior));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(DeleteConfirmationBehavior));

    public int ConfirmTimeout
    {
        get => (int)GetValue(ConfirmTimeoutProperty);
        set => SetValue(ConfirmTimeoutProperty, value);
    }

    public ICommand ConfirmedCommand
    {
        get => (ICommand)GetValue(ConfirmedCommandProperty);
        set => SetValue(ConfirmedCommandProperty, value);
    }

    public object CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Click += OnButtonClick;

        if (ConfirmedCommand == null)
        {
            AssociatedObject.Loaded += (s, e) =>
            {
                var listBox = FindParent<System.Windows.Controls.ListBox>(AssociatedObject);
                if (listBox != null)
                    AssociatedObject.Tag = listBox.DataContext;
            };
        }
    }

    private static T FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null && parent is not T)
            parent = VisualTreeHelper.GetParent(parent);
        return parent as T;
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        AssociatedObject.Click -= OnButtonClick;
        ResetButton(AssociatedObject);
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var isConfirmState = (bool)button.GetValue(ConfirmStateProperty);

        if (isConfirmState)
        {
            if (PendingButtons.TryGetValue(button, out var timer))
            {
                timer.Stop();
                PendingButtons.Remove(button);
            }

            button.SetValue(ConfirmStateProperty, false);

            var command = ConfirmedCommand ?? (button.Tag as dynamic)?.DeleteKeyCommand as ICommand;
            if (command?.CanExecute(CommandParameter) == true)
                command.Execute(CommandParameter);

            ResetButton(button);
        }
        else
        {
            ClearAllConfirmStates();
            button.SetValue(ConfirmStateProperty, true);
            ConvertToConfirmButton(button);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ConfirmTimeout) };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                if (PendingButtons.ContainsKey(button))
                    PendingButtons.Remove(button);
                button.SetValue(ConfirmStateProperty, false);
                ResetButton(button);
            };

            if (PendingButtons.ContainsKey(button))
                PendingButtons[button].Stop();
            PendingButtons[button] = timer;
            timer.Start();
        }
    }

    private void ConvertToConfirmButton(Button button)
    {
        if (button.Content is Path path)
        {
            button.SetValue(ConfirmStateProperty, true);
            path.Tag = path.Data;
            path.Data = Geometry.Parse("M12,2C17.53,2 22,6.47 22,12C22,17.53 17.53,22 12,22C6.47,22 2,17.53 2,12C2,6.47 6.47,2 12,2M15.59,7L12,10.59L8.41,7L7,8.41L10.59,12L7,15.59L8.41,17L12,13.41L15.59,17L17,15.59L13.41,12L17,8.41L15.59,7Z");
            path.Fill = new SolidColorBrush(Colors.Red);
            button.Background = new SolidColorBrush(Color.FromArgb(20, 255, 0, 0));
            button.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 0, 0));

            var animation = new ColorAnimation
            {
                To = Color.FromArgb(40, 255, 0, 0),
                Duration = TimeSpan.FromMilliseconds(200),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3)
            };
            if (button.Background is SolidColorBrush brush)
                brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);

            if (button.ToolTip is ToolTip toolTip && toolTip.Content is TextBlock textBlock)
                textBlock.Text = "点击确认删除";
        }
    }

    private void ResetButton(Button button)
    {
        button.SetValue(ConfirmStateProperty, false);

        if (button.Content is Path path)
        {
            if (path.Tag is Geometry originalGeometry)
            {
                path.Data = originalGeometry;
                path.Tag = null;
            }

            path.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666666"));
            button.Background = new SolidColorBrush(Colors.Transparent);
            button.BorderBrush = new SolidColorBrush(Colors.Transparent);

            if (button.ToolTip is ToolTip toolTip && toolTip.Content is TextBlock textBlock)
                textBlock.Text = "删除此按键";
        }

        if (PendingButtons.TryGetValue(button, out var timer))
        {
            timer.Stop();
            PendingButtons.Remove(button);
        }
    }

    private static void ClearAllConfirmStates()
    {
        var buttonsToReset = new List<Button>(PendingButtons.Keys);
        foreach (var button in buttonsToReset)
        {
            if (PendingButtons.TryGetValue(button, out var timer))
            {
                timer.Stop();
                PendingButtons.Remove(button);
            }
            button.SetValue(ConfirmStateProperty, false);
        }
    }
}
