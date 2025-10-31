using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using WpfApp.Services.Utils;
using WpfApp.Services.Models;
using WpfApp.Services.Core;
using System.Windows.Media.Animation;
using System.Windows.Media;
using WpfApp.Converters;
using System.ComponentModel;
using System.Windows.Data;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// 使用静态导入
using static System.Windows.Media.RenderOptions;

namespace WpfApp.Views;

public partial class FloatingStatusWindow : Window
{
    private readonly GlobalConfig _config;
    private System.Windows.Point _dragStartPoint;
    private bool _isDragging;
    private const double DRAG_THRESHOLD = 5.0; // 拖拽阈值（像素）
    private MainWindow _mainWindow;
    private DateTime _lastClickTime;
    private const double DOUBLE_CLICK_THRESHOLD = 300; // 双击时间阈值（毫秒）
    private readonly SerilogManager _logger = SerilogManager.Instance;
    private readonly IConfigManager _configManager = ConfigManager.Instance;
    private RotateTransform _borderRotateTransform;
    private string _currentStatus;

    public FloatingStatusWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _config = _configManager.GlobalConfig;
        _mainWindow = mainWindow;
        _logger.Debug("浮窗初始化完成");

        // 设置为工具窗口
        SourceInitialized += (s, e) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var extendedStyle = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, extendedStyle | Win32.WS_EX_TOOLWINDOW);
            _logger.Debug("浮窗工具窗口样式设置完成");
        };

        // 加载上次保存的位置
        var left = _config.UI.FloatingWindow.Left;
        var top = _config.UI.FloatingWindow.Top;

        // 如果是首次运行或位置无效，设置默认位置（右下角）
        if (left == 0 && top == 0)
        {
            left = SystemParameters.WorkArea.Right - Width - 10;
            top = SystemParameters.WorkArea.Bottom - Height - 10;

            // 保存默认位置
            _configManager.UpdateGlobalConfig(config =>
            {
                config.UI.FloatingWindow.Left = Math.Round(left, 2);
                config.UI.FloatingWindow.Top = Math.Round(top, 2);
            });
            _logger.Debug($"浮窗位置初始化为右下角: Left={left}, Top={top}");
        }

        // 确保窗口在屏幕范围内
        if (left >= 0 && top >= 0 &&
            left <= SystemParameters.WorkArea.Right - Width &&
            top <= SystemParameters.WorkArea.Bottom - Height)
        {
            Left = left;
            Top = top;
            _logger.Debug($"浮窗位置设置完成: Left={left}, Top={top}");
        }
        else
        {
            // 如果位置超出屏幕范围，重置到右下角
            Left = SystemParameters.WorkArea.Right - Width - 10;
            Top = SystemParameters.WorkArea.Bottom - Height - 10;

            // 保存新位置
            _configManager.UpdateGlobalConfig(config =>
            {
                config.UI.FloatingWindow.Left = Math.Round(Left, 2);
                config.UI.FloatingWindow.Top = Math.Round(Top, 2);
            });
            _logger.Debug($"浮窗位置重置到右下角: Left={Left}, Top={Top}");
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // 检查配置是否启用硬件加速
            bool enableHardwareAcceleration = ConfigManager.Instance?.GlobalConfig?.EnableHardwareAcceleration ?? true;
            if (enableHardwareAcceleration)
            {
                // 启用硬件加速和优化渲染设置
                SetupHardwareAcceleration();
            }
            else
            {
                _logger.Debug("硬件加速已禁用，跳过浮窗硬件加速设置");
            }

            // 订阅 ViewModel 事件
            if (DataContext is INotifyPropertyChanged viewModel)
            {
                viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }

            DataContextChanged += FloatingStatusWindow_DataContextChanged;
            UpdateBorderStyle();

            _logger.Debug("浮窗动画和边框样式初始化完成");
        }
        catch (Exception ex)
        {
            _logger.Error("初始化浮窗动画时出错", ex);
        }
    }
    
    /// <summary>
    /// DataContext变化事件处理
    /// </summary>
    private void FloatingStatusWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        try
        {
            // 移除旧的事件监听
            if (e.OldValue is INotifyPropertyChanged oldNotify)
            {
                oldNotify.PropertyChanged -= ViewModel_PropertyChanged;
            }
            
            // 添加新的事件监听
            if (e.NewValue is INotifyPropertyChanged newNotify)
            {
                newNotify.PropertyChanged += ViewModel_PropertyChanged;
                _logger.Debug("已注册INotifyPropertyChanged事件监听");
            }
            else if (e.NewValue != null)
            {
                _logger.Warning("ViewModel未实现INotifyPropertyChanged接口，状态更新可能不同步");
            }
            
            // 更新边框样式
            UpdateBorderStyle();
        }
        catch (Exception ex)
        {
            _logger.Error("处理DataContext变化时出错", ex);
        }
    }
    
    /// <summary>
    /// ViewModel属性变化事件处理
    /// </summary>
    private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "StatusText")
        {
            UpdateBorderStyle();
        }
    }

    /// <summary>
    /// 更新浮窗边框样式
    /// </summary>
    private void UpdateBorderStyle()
    {
        if (BorderContainer == null)
            return;

        try
        {
            var statusText = GetStatusText() ?? "已停止";

            if (statusText == _currentStatus && BorderContainer.BorderBrush != null)
                return;

            _currentStatus = statusText;

            var colorIndex = statusText switch
            {
                "运行中" => 0,
                "已禁用" => 1,
                _ => 2
            };

            var colors = StatusToColorConverter.GetBorderColors(colorIndex);

            if (BorderContainer.BorderBrush is LinearGradientBrush brush && brush.GradientStops.Count == 3)
            {
                brush.GradientStops[0].Color = colors[0];
                brush.GradientStops[1].Color = colors[1];
                brush.GradientStops[2].Color = colors[2];
            }
            else
            {
                BorderContainer.BorderBrush = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(1, 1),
                    RelativeTransform = new RotateTransform { CenterX = 0.5, CenterY = 0.5 },
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(colors[0], 0.0),
                        new GradientStop(colors[1], 0.5),
                        new GradientStop(colors[2], 1.0)
                    }
                };
            }
        }
        catch (Exception ex)
        {
            _logger.Error("更新边框样式时出错", ex);
        }
    }

    private string GetStatusText()
    {
        try
        {
            dynamic viewModel = DataContext;
            return viewModel?.StatusText as string;
        }
        catch
        {
            return null;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _isDragging = false;
        CaptureMouse();
        _logger.Debug("浮窗开始拖拽");
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && IsMouseCaptured)
        {
            var currentPosition = e.GetPosition(this);
            var diff = currentPosition - _dragStartPoint;

            // 如果移动距离超过阈值，开始拖拽
            if (!_isDragging && (Math.Abs(diff.X) > DRAG_THRESHOLD || Math.Abs(diff.Y) > DRAG_THRESHOLD))
            {
                _isDragging = true;
                _logger.Debug("浮窗拖拽开始");
            }

            if (_isDragging)
            {
                // 执行拖拽
                Left += diff.X;
                Top += diff.Y;
            }
        }
    }

    private async void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();

        try
        {
            if (_isDragging)
            {
                // 保存拖拽后的新位置
                SaveWindowPosition();
            }
            else
            {
                // 处理双击事件
                HandleDoubleClick();
            }
        }
        catch (Exception ex)
        {
            _logger.Error("处理鼠标抬起事件时发生错误", ex);
        }
        finally
        {
            _isDragging = false;
        }
    }
    
    /// <summary>
    /// 保存窗口位置
    /// </summary>
    private void SaveWindowPosition()
    {
        _configManager.UpdateGlobalConfig(config =>
        {
            config.UI.FloatingWindow.Left = Math.Round(Left, 2);
            config.UI.FloatingWindow.Top = Math.Round(Top, 2);
        });
        _logger.Debug($"保存浮窗位置: Left={Left}, Top={Top}");
    }
    
    /// <summary>
    /// 处理双击事件
    /// </summary>
    private void HandleDoubleClick()
    {
        if (_mainWindow == null)
        {
            _logger.Warning("MainWindow引用为空，无法处理双击事件");
            return;
        }
        
        var currentTime = DateTime.Now;
        var timeSinceLastClick = (currentTime - _lastClickTime).TotalMilliseconds;

        if (timeSinceLastClick <= DOUBLE_CLICK_THRESHOLD)
        {
            _logger.Debug("检测到浮窗双击，显示主窗口");
            _mainWindow.RestoreFromMinimized();
            _lastClickTime = DateTime.MinValue;
        }
        else
        {
            _lastClickTime = currentTime;
        }
    }

    private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (_mainWindow?._trayContextMenu == null)
            {
                _logger.Warning("MainWindow或托盘菜单为空，无法显示菜单");
                return;
            }
            
            Focus();
            
            // 配置并显示托盘菜单
            var menu = _mainWindow._trayContextMenu;
            menu.PlacementTarget = this;
            menu.Placement = PlacementMode.MousePoint;
            menu.StaysOpen = true;
            menu.IsOpen = true;
            
            // 菜单关闭时重置StaysOpen
            menu.Closed += (s, args) =>
            {
                if (_mainWindow?._trayContextMenu != null)
                    _mainWindow._trayContextMenu.StaysOpen = false;
            };

            e.Handled = true;
            _logger.Debug("托盘菜单已显示");
        }
        catch (Exception ex)
        {
            _logger.Error("显示托盘菜单时发生错误", ex);
        }
    }

    private void FloatingStatusWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            SaveWindowPosition();
            _logger.Debug("浮窗关闭前位置已保存");
        }
        catch (Exception ex)
        {
            _logger.Error("保存浮窗位置时出错", ex);
        }
    }

    // 添加 Win32 API 定义
    private static class Win32
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    }

    /// <summary>
    /// 为当前窗口设置硬件加速和渲染优化
    /// </summary>
    private void SetupHardwareAcceleration()
    {
        try
        {
            // 设置窗口缓存模式
            CacheMode = new BitmapCache
            {
                EnableClearType = true,
                SnapsToDevicePixels = true,
                RenderAtScale = 1.0
            };
            
            UseLayoutRounding = true;
            
            // 优化边框渲染
            if (BorderContainer != null)
            {
                BorderContainer.CacheMode = new BitmapCache 
                { 
                    EnableClearType = true, 
                    SnapsToDevicePixels = true
                };
                RenderOptions.SetCachingHint(BorderContainer, CachingHint.Cache);
            }
            
            // 启用硬件渲染模式
            if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
            {
                hwndSource.CompositionTarget.RenderMode = RenderMode.Default;
            }
            
            // 优化渲染线程优先级
            if (Dispatcher?.Thread != null)
            {
                Dispatcher.Thread.Priority = ThreadPriority.AboveNormal;
            }
            
            _logger.Debug("浮窗硬件加速配置完成");
        }
        catch (Exception ex)
        {
            _logger.Error("配置窗口硬件加速时出错", ex);
        }
    }
}