using System.Windows;
using System.Windows.Controls;

namespace WpfApp.Views
{
    /// <summary>
    /// KeyMappingView.xaml 的交互逻辑
    /// 多配置架构 - 简化版本
    /// </summary>
    public partial class KeyMappingView : Page
    {
        public KeyMappingView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 浮窗透明度设置按钮点击事件
        /// </summary>
        private void FloatingOpacitySettings_Click(object sender, RoutedEventArgs e)
        {
            if (floatingOpacityPopup != null)
            {
                floatingOpacityPopup.IsOpen = !floatingOpacityPopup.IsOpen;
                if (floatingOpacityPopup.IsOpen && opacitySlider != null)
                    opacitySlider.Focus();
            }
        }
    }
}
