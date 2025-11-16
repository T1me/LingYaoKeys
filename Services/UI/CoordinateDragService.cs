using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfApp.Services.Models;
using WpfApp.Services.Utils;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace WpfApp.Services.UI;

/// <summary>
/// 坐标拖拽服务，负责鼠标拖拽获取屏幕坐标
/// </summary>
public class CoordinateDragService : IDisposable
{
    private readonly ISerilogManager _logger;
    private const double DRAG_THRESHOLD = 5.0;

    private bool _isDragging;
    private bool _isDragStarted;
    private Point _startPoint;
    private Window _dragWindow;
    private UIElement _source;

    public bool IsDragStarted => _isDragStarted;
    public event EventHandler<CoordinateCapturedEventArgs> CoordinateCaptured;

    public void AttachToElement(UIElement element)
    {
        _source = element;
        element.PreviewMouseDown += OnMouseDown;
        element.PreviewMouseMove += OnMouseMove;
        element.PreviewMouseUp += OnMouseUp;
    }

    public void DetachFromElement(UIElement element)
    {
        element.PreviewMouseDown -= OnMouseDown;
        element.PreviewMouseMove -= OnMouseMove;
        element.PreviewMouseUp -= OnMouseUp;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _startPoint = e.GetPosition(null);
            _isDragging = true;
            _isDragStarted = false;
            _source.CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _isDragging)
        {
            var currentPos = e.GetPosition(null);
            var moveDistance = currentPos - _startPoint;
            double distance = Math.Sqrt(moveDistance.X * moveDistance.X + moveDistance.Y * moveDistance.Y);

            if (!_isDragStarted && distance > DRAG_THRESHOLD)
            {
                _isDragStarted = true;
                CreateDragWindow();
            }

            if (_isDragStarted && _dragWindow != null)
            {
                var (x, y) = GetDpiAwareScreenPosition();
                _dragWindow.Left = x - 10;
                _dragWindow.Top = y - 10;
                if (!_dragWindow.IsVisible)
                    _dragWindow.Show();
            }
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Released && _isDragging)
        {
            _source.ReleaseMouseCapture();

            if (_isDragStarted)
            {
                var (rawX, rawY) = GetRawScreenPosition();
                CoordinateCaptured?.Invoke(this, new CoordinateCapturedEventArgs(rawX + 1, rawY + 1));
                e.Handled = true;
            }

            Cleanup();
        }
    }

    private void CreateDragWindow()
    {
        if (_dragWindow != null) return;

        var mainGrid = new Grid { Width = 20, Height = 20, Background = null };
        var hLine = new Rectangle
        {
            Width = 20, Height = 1,
            Fill = new SolidColorBrush(Color.FromRgb(255, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var vLine = new Rectangle
        {
            Width = 1, Height = 20,
            Fill = new SolidColorBrush(Color.FromRgb(255, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var center = new Ellipse
        {
            Width = 5, Height = 5,
            Fill = new SolidColorBrush(Color.FromRgb(255, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        mainGrid.Children.Add(hLine);
        mainGrid.Children.Add(vLine);
        mainGrid.Children.Add(center);

        _dragWindow = new Window
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
            Width = 20, Height = 20,
            Left = -100, Top = -100
        };

        Win32WindowHelper.HideFromAltTab(_dragWindow);
        _dragWindow.Show();
    }

    private (int X, int Y) GetRawScreenPosition()
    {
        var cursorPos = System.Windows.Forms.Cursor.Position;
        return (cursorPos.X, cursorPos.Y);
    }

    private (int X, int Y) GetDpiAwareScreenPosition()
    {
        var cursorPos = System.Windows.Forms.Cursor.Position;
        if (_dragWindow != null)
        {
            var source = PresentationSource.FromVisual(_dragWindow);
            if (source?.CompositionTarget != null)
            {
                var matrix = source.CompositionTarget.TransformFromDevice;
                var logicalPoint = matrix.Transform(new Point(cursorPos.X, cursorPos.Y));
                return ((int)logicalPoint.X, (int)logicalPoint.Y);
            }
        }
        return (cursorPos.X, cursorPos.Y);
    }

    private void Cleanup()
    {
        if (_source != null && _source.IsMouseCaptured)
            _source.ReleaseMouseCapture();

        if (_dragWindow != null)
        {
            _dragWindow.Close();
            _dragWindow = null;
        }

        _isDragging = false;
        _isDragStarted = false;
    }

    public void Dispose()
    {
        Cleanup();
    }
}

public class CoordinateCapturedEventArgs : EventArgs
{
    public int X { get; }
    public int Y { get; }
    public CoordinateCapturedEventArgs(int x, int y)
    {
        X = x;
        Y = y;
    }
}
