using System.Runtime.InteropServices;
using WpfApp.Services.Utils;

// 输入法服务
namespace WpfApp.Services.Core;

public class InputMethodService : IInputMethodService
{
    private const int WM_INPUTLANGCHANGEREQUEST = 0x0050;
    private const int INPUTLANGCHANGE_FORWARD = 0x0002;

    [DllImport("user32.dll")]
    private static extern IntPtr PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    private readonly ISerilogManager _logger;
    private IntPtr _previousLayout;
    private bool _hasStoredLayout;

    // 保存当前输入法状态
    public void StoreCurrentLayout()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                var threadId = GetWindowThreadProcessId(hwnd, IntPtr.Zero);
                _previousLayout = GetKeyboardLayout(threadId);
                _hasStoredLayout = true;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("保存输入法状态异常", ex);
        }
    }

    // 切换到英文输入法
    public void SwitchToEnglish()
    {
        try
        {
            // 如果还没有保存当前状态，先保存
            if (!_hasStoredLayout) StoreCurrentLayout();

            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                // 加载英文键盘布局
                var layout = LoadKeyboardLayout("00000409", 1);
                if (layout != IntPtr.Zero)
                {
                    PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, (IntPtr)INPUTLANGCHANGE_FORWARD, layout);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("切换输入法异常", ex);
        }
    }

    // 恢复之前的输入法状态
    public void RestorePreviousLayout()
    {
        try
        {
            if (!_hasStoredLayout) return;

            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero && _previousLayout != IntPtr.Zero)
            {
                PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, (IntPtr)INPUTLANGCHANGE_FORWARD, _previousLayout);
                _hasStoredLayout = false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("恢复输入法状态异常", ex);
        }
    }
}