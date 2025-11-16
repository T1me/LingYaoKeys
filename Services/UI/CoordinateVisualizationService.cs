using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WpfApp.Services.Models;
using WpfApp.Services.Utils;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace WpfApp.Services.UI;

/// <summary>
/// 坐标可视化服务，负责坐标标记的显示、隐藏和拖拽管理
/// </summary>
public class CoordinateVisualizationService : IDisposable
{
    private readonly ISerilogManager _logger;
    private readonly Dictionary<KeyItem, CoordinateMarker> _markers = new();
    private CoordinateMarker _draggingMarker;

    public event EventHandler<CoordinateUpdatedEventArgs> CoordinateUpdated;

    public void ShowAll(IEnumerable<KeyItem> keyList)
    {
        try
        {
            var coordinates = keyList.Where(item => item.Type == KeyItemType.Coordinates).ToList();

            // 移除不再存在的标记
            var toRemove = _markers.Keys.Where(k => !coordinates.Contains(k)).ToList();
            foreach (var key in toRemove)
            {
                if (_markers.TryGetValue(key, out var marker))
                {
                    DetachDragEvents(marker);
                    marker.Close();
                    _markers.Remove(key);
                }
            }

            // 更新或创建标记
            for (int i = 0; i < coordinates.Count; i++)
            {
                var keyItem = coordinates[i];
                keyItem.CoordinateIndex = i;

                if (_markers.TryGetValue(keyItem, out var existingMarker))
                {
                    // 复用现有标记
                    existingMarker.Index = i;
                    existingMarker.UpdateLabel();
                    existingMarker.UpdatePosition();
                    existingMarker.ShowWithoutAnimation();
                }
                else
                {
                    // 创建新标记
                    var marker = new CoordinateMarker(keyItem, i);
                    _markers[keyItem] = marker;
                    AttachDragEvents(marker);
                    marker.ShowWithoutAnimation();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("显示所有坐标点时发生异常", ex);
            ClearAll();
            throw;
        }
    }

    public void HideAll()
    {
        foreach (var marker in _markers.Values)
            marker.Hide();
    }

    public void UpdateMarker(KeyItem item)
    {
        if (_markers.TryGetValue(item, out var marker))
        {
            marker.UpdateLabel();
            marker.UpdatePosition();
        }
    }

    public void UpdateAllIndices(IEnumerable<KeyItem> keyList)
    {
        var coordinates = keyList.Where(item => item.Type == KeyItemType.Coordinates).ToList();
        for (int i = 0; i < coordinates.Count; i++)
        {
            var keyItem = coordinates[i];
            keyItem.CoordinateIndex = i;
            if (_markers.TryGetValue(keyItem, out var marker))
            {
                marker.Index = i;
                marker.UpdateLabel();
            }
        }
    }

    public void AddMarker(KeyItem item, int index)
    {
        item.CoordinateIndex = index;
        var marker = new CoordinateMarker(item, index);
        _markers[item] = marker;
        AttachDragEvents(marker);
        marker.Show();
    }

    public void RemoveMarker(KeyItem item)
    {
        if (_markers.TryGetValue(item, out var marker))
        {
            DetachDragEvents(marker);
            marker.Close();
            _markers.Remove(item);
        }
    }

    private void AttachDragEvents(CoordinateMarker marker)
    {
        if (marker?.MarkerWindow?.Content is not FrameworkElement grid) return;

        grid.MouseLeftButtonDown += (s, e) => OnMarkerMouseDown(marker, e);
        grid.MouseMove += (s, e) => OnMarkerMouseMove(marker, e);
        grid.MouseLeftButtonUp += (s, e) => OnMarkerMouseUp(marker, e);
    }

    private void DetachDragEvents(CoordinateMarker marker)
    {
        if (marker?.MarkerWindow?.Content is not FrameworkElement grid) return;

        grid.MouseLeftButtonDown -= (s, e) => OnMarkerMouseDown(marker, e);
        grid.MouseMove -= (s, e) => OnMarkerMouseMove(marker, e);
        grid.MouseLeftButtonUp -= (s, e) => OnMarkerMouseUp(marker, e);
    }

    private void OnMarkerMouseDown(CoordinateMarker marker, MouseButtonEventArgs e)
    {
        marker.IsDragging = true;
        _draggingMarker = marker;
        if (marker.MarkerWindow.Content is FrameworkElement grid)
            grid.CaptureMouse();
        e.Handled = true;
    }

    private void OnMarkerMouseMove(CoordinateMarker marker, MouseEventArgs e)
    {
        if (!marker.IsDragging || e.LeftButton != MouseButtonState.Pressed) return;

        var (rawX, rawY) = GetRawScreenPosition();

        marker.KeyItem.X = rawX;
        marker.KeyItem.Y = rawY;
        marker.UpdatePosition();
        marker.UpdateLabel();
    }

    private void OnMarkerMouseUp(CoordinateMarker marker, MouseButtonEventArgs e)
    {
        if (!marker.IsDragging) return;

        if (marker.MarkerWindow.Content is FrameworkElement grid && grid.IsMouseCaptured)
            grid.ReleaseMouseCapture();

        marker.IsDragging = false;
        _draggingMarker = null;

        var (rawX, rawY) = GetRawScreenPosition();

        marker.KeyItem.X = rawX;
        marker.KeyItem.Y = rawY;
        marker.UpdatePosition();
        marker.UpdateLabel();

        CoordinateUpdated?.Invoke(this, new CoordinateUpdatedEventArgs(marker.KeyItem));
        e.Handled = true;
    }

    private (int X, int Y) GetRawScreenPosition()
    {
        var cursorPos = System.Windows.Forms.Cursor.Position;
        return (cursorPos.X, cursorPos.Y);
    }

    private void ClearAll()
    {
        foreach (var marker in _markers.Values)
        {
            DetachDragEvents(marker);
            marker.Close();
        }
        _markers.Clear();
    }

    public void Dispose()
    {
        ClearAll();
    }
}

public class CoordinateUpdatedEventArgs : EventArgs
{
    public KeyItem KeyItem { get; }
    public CoordinateUpdatedEventArgs(KeyItem keyItem) => KeyItem = keyItem;
}
