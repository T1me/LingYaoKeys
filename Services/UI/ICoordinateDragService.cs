using System;
using System.Windows;

namespace WpfApp.Services.UI;

/// <summary>
/// 坐标拖拽服务接口 - 负责鼠标拖拽获取屏幕坐标
/// </summary>
public interface ICoordinateDragService : IDisposable
{
    /// <summary>
    /// 是否已开始拖拽
    /// </summary>
    bool IsDragStarted { get; }

    /// <summary>
    /// 坐标捕获事件
    /// </summary>
    event EventHandler<CoordinateCapturedEventArgs> CoordinateCaptured;

    /// <summary>
    /// 附加到 UI 元素
    /// </summary>
    /// <param name="element">要附加的 UI 元素</param>
    void AttachToElement(UIElement element);

    /// <summary>
    /// 从 UI 元素分离
    /// </summary>
    /// <param name="element">要分离的 UI 元素</param>
    void DetachFromElement(UIElement element);
}
