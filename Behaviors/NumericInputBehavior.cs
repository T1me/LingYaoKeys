using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;
using TextBox = System.Windows.Controls.TextBox;

namespace WpfApp.Behaviors;

/// <summary>
/// 数字输入验证行为
/// </summary>
public class NumericInputBehavior : Behavior<TextBox>
{
    public static readonly DependencyProperty MinValueProperty =
        DependencyProperty.Register(nameof(MinValue), typeof(int?), typeof(NumericInputBehavior), new PropertyMetadata(null));

    public static readonly DependencyProperty MaxValueProperty =
        DependencyProperty.Register(nameof(MaxValue), typeof(int?), typeof(NumericInputBehavior), new PropertyMetadata(null));

    public static readonly DependencyProperty AllowEmptyProperty =
        DependencyProperty.Register(nameof(AllowEmpty), typeof(bool), typeof(NumericInputBehavior), new PropertyMetadata(false));

    public static readonly DependencyProperty DefaultValueProperty =
        DependencyProperty.Register(nameof(DefaultValue), typeof(int), typeof(NumericInputBehavior), new PropertyMetadata(5));

    public int? MinValue
    {
        get => (int?)GetValue(MinValueProperty);
        set => SetValue(MinValueProperty, value);
    }

    public int? MaxValue
    {
        get => (int?)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public bool AllowEmpty
    {
        get => (bool)GetValue(AllowEmptyProperty);
        set => SetValue(AllowEmptyProperty, value);
    }

    public int DefaultValue
    {
        get => (int)GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.PreviewTextInput += OnPreviewTextInput;
        AssociatedObject.LostFocus += OnLostFocus;
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        AssociatedObject.PreviewTextInput -= OnPreviewTextInput;
        AssociatedObject.LostFocus -= OnLostFocus;
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !int.TryParse(e.Text, out _);
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        var textBox = (TextBox)sender;
        var text = textBox.Text;

        if (string.IsNullOrWhiteSpace(text))
        {
            if (!AllowEmpty)
                textBox.Text = DefaultValue.ToString();
            return;
        }

        if (int.TryParse(text, out var value))
        {
            if (MinValue.HasValue && value < MinValue.Value)
            {
                textBox.Text = MinValue.Value.ToString();
                return;
            }

            if (MaxValue.HasValue && value > MaxValue.Value)
            {
                textBox.Text = MaxValue.Value.ToString();
                return;
            }
        }
        else
        {
            textBox.Text = AllowEmpty ? string.Empty : DefaultValue.ToString();
        }
    }
}
