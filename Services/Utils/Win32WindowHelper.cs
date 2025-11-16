using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WpfApp.Services.Utils;

namespace WpfApp.Services.Models;

/// <summary>
/// Win32 API 辅助类
/// </summary>
public static class Win32WindowHelper
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    /// <summary>
    /// 设置窗口为工具窗口，不在Alt+Tab列表中显示
    /// </summary>
    public static void HideFromAltTab(Window window)
    {
        try
        {
            window.SourceInitialized += (s, e) =>
            {
                var handle = new WindowInteropHelper(window).Handle;
                int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
                exStyle |= WS_EX_TOOLWINDOW;
                exStyle &= ~WS_EX_APPWINDOW;
                SetWindowLong(handle, GWL_EXSTYLE, exStyle);
            };
        }
        catch (Exception)
        {
            // 静态方法中无法使用注入的logger，异常会被SourceInitialized事件处理
        }
    }
}
