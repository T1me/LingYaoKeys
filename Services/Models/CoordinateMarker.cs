using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using WpfApp.Services.Utils;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace WpfApp.Services.Models;

/// <summary>
/// 坐标标记，用于在屏幕上显示坐标点
/// </summary>
public class CoordinateMarker
{
    private readonly ISerilogManager _logger;

    public Window MarkerWindow { get; private set; }
    public Window LabelWindow { get; private set; }
    public KeyItem KeyItem { get; private set; }
    public int Index { get; set; }
    public bool IsDragging { get; set; }

    public CoordinateMarker(KeyItem keyItem, int index)
    {
        KeyItem = keyItem;
        Index = index;
        CreateMarkerWindow();
        CreateLabelWindow();
    }

    private void CreateMarkerWindow()
    {
        var mainGrid = new Grid { Width = 16, Height = 16, Background = null };
        var point = new Ellipse
        {
            Width = 10, Height = 10,
            Fill = new SolidColorBrush(Color.FromRgb(255, 0, 0)),
            Stroke = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        mainGrid.Children.Add(point);

        MarkerWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = null,
            Topmost = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            SizeToContent = SizeToContent.Manual,
            Content = mainGrid,
            Width = 16, Height = 16
        };

        Win32WindowHelper.HideFromAltTab(MarkerWindow);
    }

    private void CreateLabelWindow()
    {
        var scaleTransform = new ScaleTransform(0.8, 0.8);
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 30, 30, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, Direction = 315, ShadowDepth = 3,
                Opacity = 0.7, BlurRadius = 5
            },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scaleTransform,
            Opacity = 0.9
        };

        var label = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            FontSize = 12, FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center
        };
        UpdateLabelText(label);
        border.Child = label;

        LabelWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = null,
            Topmost = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = border,
            Visibility = Visibility.Collapsed
        };

        Win32WindowHelper.HideFromAltTab(LabelWindow);

    }

    public void UpdateLabel()
    {
        if (LabelWindow.Content is Border border && border.Child is TextBlock label)
        {
            UpdateLabelText(label);
            LabelWindow.UpdateLayout();
            UpdatePosition();
        }
    }

    private void UpdateLabelText(TextBlock label)
    {
        label.Text = $"{Index + 1}-({KeyItem.X ?? 0},{KeyItem.Y ?? 0})";
    }

    public void UpdatePosition()
    {
        try
        {
            if (KeyItem.Type != KeyItemType.Coordinates) return;

            int x = KeyItem.X ?? 0;
            int y = KeyItem.Y ?? 0;

            // 获取 DPI 缩放比例
            var dpiScale = VisualTreeHelper.GetDpi(MarkerWindow);
            double dpiScaleX = dpiScale.DpiScaleX;
            double dpiScaleY = dpiScale.DpiScaleY;

            // 将物理坐标转换为逻辑坐标
            double dpiAwareX = x / dpiScaleX;
            double dpiAwareY = y / dpiScaleY;

            MarkerWindow.Left = dpiAwareX - MarkerWindow.Width / 2;
            MarkerWindow.Top = dpiAwareY - MarkerWindow.Height / 2;

            LabelWindow.UpdateLayout();
            var currentScreen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x, y));
            var workingArea = currentScreen.WorkingArea;
            double labelWidth = LabelWindow.ActualWidth;
            double labelHeight = LabelWindow.ActualHeight;

            // 将工作区坐标也转换为逻辑坐标
            var dpiAwareWorkingArea = new Rect(
                workingArea.Left / dpiScaleX,
                workingArea.Top / dpiScaleY,
                workingArea.Width / dpiScaleX,
                workingArea.Height / dpiScaleY
            );

            double labelLeft = dpiAwareX + 10;
            double labelTop = dpiAwareY - labelHeight / 2;

            if (labelLeft + labelWidth > dpiAwareWorkingArea.Right - 10)
                labelLeft = dpiAwareX - 10 - labelWidth;
            if (labelLeft < dpiAwareWorkingArea.Left + 10)
            {
                labelLeft = dpiAwareX - labelWidth / 2;
                labelTop = dpiAwareY - 10 - labelHeight;
                if (labelTop < dpiAwareWorkingArea.Top + 10)
                    labelTop = dpiAwareY + 10;
            }
            if (labelTop < dpiAwareWorkingArea.Top + 10)
                labelTop = dpiAwareY + 10;
            if (labelTop + labelHeight > dpiAwareWorkingArea.Bottom - 10)
            {
                labelTop = dpiAwareY - 10 - labelHeight;
                if (labelTop < dpiAwareWorkingArea.Top + 10)
                    labelTop = Math.Max(dpiAwareWorkingArea.Top + 5, dpiAwareY - labelHeight / 2);
            }

            LabelWindow.Left = labelLeft;
            LabelWindow.Top = labelTop;
        }
        catch (Exception ex)
        {
            _logger.Error("更新坐标标签位置时发生异常", ex);
        }
    }

    public void Show()
    {
        UpdatePosition();
        MarkerWindow.Show();
        LabelWindow.Show();

        if (LabelWindow.RenderTransform is ScaleTransform scaleTransform)
        {
            scaleTransform.ScaleX = 0.7;
            scaleTransform.ScaleY = 0.7;
            var scaleAnim = new DoubleAnimation
            {
                From = 0.7, To = 1.0, Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        }

        LabelWindow.Visibility = Visibility.Visible;
    }

    public void ShowWithoutAnimation()
    {
        UpdatePosition();
        MarkerWindow.Show();
        LabelWindow.Visibility = Visibility.Visible;
        LabelWindow.Show();
    }

    public void Hide()
    {
        MarkerWindow.Hide();
        LabelWindow.Hide();
    }

    public void Close()
    {
        MarkerWindow.Close();
        LabelWindow.Close();
    }
}
