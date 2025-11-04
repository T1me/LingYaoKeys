using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WpfApp.Converters;

/// <summary>
/// ListBox 项索引转换器
/// 将 ListBoxItem 转换为其在 ListBox 中的索引（从 1 开始）
/// </summary>
public class IndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ListBoxItem item)
        {
            var listBox = ItemsControl.ItemsControlFromItemContainer(item) as System.Windows.Controls.ListBox;
            if (listBox != null)
            {
                int index = listBox.ItemContainerGenerator.IndexFromContainer(item);
                return (index + 1).ToString(); // 从 1 开始计数
            }
        }
        return "0";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("IndexConverter 不支持反向转换");
    }
}
