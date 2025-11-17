using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WpfApp.Services.Models;
using WpfApp.Services.Utils;

namespace WpfApp.Services.Core.Hooks;

/// <summary>
/// Win32 钩子管理器实现 - 负责键盘和鼠标全局钩子
/// </summary>
public class HookManager : IHookManager
{
    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    #region Win32 API 声明

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    #endregion

    #region 常量定义

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEWHEEL = 0x020A;

    #endregion

    #region 字段

    private IntPtr _keyboardHookHandle;
    private IntPtr _mouseHookHandle;
    private readonly HookProc _keyboardProcDelegate;
    private readonly HookProc _mouseProcDelegate;
    private readonly ISerilogManager _logger;
    private readonly object _hookLock = new();

    #endregion

    #region 事件

    public event Action<int, bool>? KeyboardEvent;
    public event Action<VirtualKeyCode, bool>? MouseButtonEvent;
    public event Action<VirtualKeyCode>? MouseWheelEvent;

    #endregion

    #region 构造函数

    public HookManager(ISerilogManager logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _keyboardProcDelegate = KeyboardHookCallback;
        _mouseProcDelegate = MouseHookCallback;
    }

    #endregion

    #region 公共方法

    public void InstallHooks()
    {
        lock (_hookLock)
        {
            try
            {
                using (var curProcess = Process.GetCurrentProcess())
                using (var curModule = curProcess.MainModule!)
                {
                    var hModule = GetModuleHandle(curModule.ModuleName);

                    _keyboardHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProcDelegate, hModule, 0);
                    _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseProcDelegate, hModule, 0);

                    if (_keyboardHookHandle == IntPtr.Zero || _mouseHookHandle == IntPtr.Zero)
                    {
                        var error = Marshal.GetLastWin32Error();
                        throw new Win32Exception(error, $"安装钩子失败: Win32 错误码 {error}");
                    }

                    _logger.Debug("Win32 钩子安装成功");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("安装钩子失败", ex);
                throw;
            }
        }
    }

    public void UninstallHooks()
    {
        lock (_hookLock)
        {
            if (_keyboardHookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHookHandle);
                _keyboardHookHandle = IntPtr.Zero;
                _logger.Debug("键盘钩子已卸载");
            }

            if (_mouseHookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookHandle);
                _mouseHookHandle = IntPtr.Zero;
                _logger.Debug("鼠标钩子已卸载");
            }
        }
    }

    #endregion

    #region 钩子回调

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var hookStruct = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT))!;
                var wParamInt = (int)wParam;
                var isDown = wParamInt == WM_KEYDOWN || wParamInt == WM_SYSKEYDOWN;

                KeyboardEvent?.Invoke((int)hookStruct.vkCode, isDown);
            }
            catch (Exception ex)
            {
                _logger.Error("键盘钩子回调异常", ex);
            }
        }

        return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT))!;
                var wParamInt = (int)wParam;

                switch (wParamInt)
                {
                    case WM_XBUTTONDOWN:
                    case WM_XBUTTONUP:
                        var xButton = (int)((hookStruct.mouseData >> 16) & 0xFFFF);
                        var xButtonCode = xButton == 1 ? VirtualKeyCode.VK_XBUTTON1 : VirtualKeyCode.VK_XBUTTON2;
                        MouseButtonEvent?.Invoke(xButtonCode, wParamInt == WM_XBUTTONDOWN);
                        break;

                    case WM_MBUTTONDOWN:
                    case WM_MBUTTONUP:
                        MouseButtonEvent?.Invoke(VirtualKeyCode.VK_MBUTTON, wParamInt == WM_MBUTTONDOWN);
                        break;

                    case WM_MOUSEWHEEL:
                        var wheelDelta = (short)((hookStruct.mouseData >> 16) & 0xFFFF);
                        MouseWheelEvent?.Invoke(wheelDelta > 0 ? VirtualKeyCode.VK_WHEELUP : VirtualKeyCode.VK_WHEELDOWN);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error("鼠标钩子回调异常", ex);
            }
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        UninstallHooks();
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Win32 结构体

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    #endregion
}
