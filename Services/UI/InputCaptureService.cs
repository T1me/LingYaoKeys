using System.Windows.Input;
using WpfApp.Services.Core;
using WpfApp.Services.Models;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;

namespace WpfApp.Services.UI;

/// <summary>
/// 输入捕获服务，负责键盘和鼠标输入的捕获和转换
/// </summary>
public class InputCaptureService
{
    public VirtualKeyCode? CaptureKeyboardInput(KeyEventArgs e)
    {
        if (e.Key == Key.ImeProcessed && e.SystemKey == Key.None)
            return null;

        var key = e.SystemKey != Key.None ? e.SystemKey :
            e.Key == Key.ImeProcessed ? e.SystemKey : e.Key;

        if (key == Key.None) return null;

        return TryConvertToVirtualKeyCode(key, out var code) ? code : null;
    }

    public VirtualKeyCode? CaptureMouseInput(MouseButtonEventArgs e)
    {
        return e.ChangedButton switch
        {
            MouseButton.Left => VirtualKeyCode.VK_LBUTTON,
            MouseButton.Right => VirtualKeyCode.VK_RBUTTON,
            MouseButton.Middle => VirtualKeyCode.VK_MBUTTON,
            MouseButton.XButton1 => VirtualKeyCode.VK_XBUTTON1,
            MouseButton.XButton2 => VirtualKeyCode.VK_XBUTTON2,
            _ => null
        };
    }

    public VirtualKeyCode? CaptureMouseWheel(MouseWheelEventArgs e)
    {
        return e.Delta > 0 ? VirtualKeyCode.VK_WHEELUP : VirtualKeyCode.VK_WHEELDOWN;
    }

    public bool IsModifierKey(VirtualKeyCode keyCode)
    {
        return keyCode == VirtualKeyCode.VK_LCONTROL || keyCode == VirtualKeyCode.VK_RCONTROL ||
               keyCode == VirtualKeyCode.VK_LMENU || keyCode == VirtualKeyCode.VK_RMENU ||
               keyCode == VirtualKeyCode.VK_LSHIFT || keyCode == VirtualKeyCode.VK_RSHIFT;
    }

    private bool TryConvertToVirtualKeyCode(Key key, out VirtualKeyCode code)
    {
        code = key switch
        {
            Key.A => VirtualKeyCode.VK_A, Key.B => VirtualKeyCode.VK_B, Key.C => VirtualKeyCode.VK_C,
            Key.D => VirtualKeyCode.VK_D, Key.E => VirtualKeyCode.VK_E, Key.F => VirtualKeyCode.VK_F,
            Key.G => VirtualKeyCode.VK_G, Key.H => VirtualKeyCode.VK_H, Key.I => VirtualKeyCode.VK_I,
            Key.J => VirtualKeyCode.VK_J, Key.K => VirtualKeyCode.VK_K, Key.L => VirtualKeyCode.VK_L,
            Key.M => VirtualKeyCode.VK_M, Key.N => VirtualKeyCode.VK_N, Key.O => VirtualKeyCode.VK_O,
            Key.P => VirtualKeyCode.VK_P, Key.Q => VirtualKeyCode.VK_Q, Key.R => VirtualKeyCode.VK_R,
            Key.S => VirtualKeyCode.VK_S, Key.T => VirtualKeyCode.VK_T, Key.U => VirtualKeyCode.VK_U,
            Key.V => VirtualKeyCode.VK_V, Key.W => VirtualKeyCode.VK_W, Key.X => VirtualKeyCode.VK_X,
            Key.Y => VirtualKeyCode.VK_Y, Key.Z => VirtualKeyCode.VK_Z,

            Key.D0 => VirtualKeyCode.VK_0, Key.D1 => VirtualKeyCode.VK_1, Key.D2 => VirtualKeyCode.VK_2,
            Key.D3 => VirtualKeyCode.VK_3, Key.D4 => VirtualKeyCode.VK_4, Key.D5 => VirtualKeyCode.VK_5,
            Key.D6 => VirtualKeyCode.VK_6, Key.D7 => VirtualKeyCode.VK_7, Key.D8 => VirtualKeyCode.VK_8,
            Key.D9 => VirtualKeyCode.VK_9,

            Key.NumPad0 => VirtualKeyCode.VK_NUMPAD0, Key.NumPad1 => VirtualKeyCode.VK_NUMPAD1,
            Key.NumPad2 => VirtualKeyCode.VK_NUMPAD2, Key.NumPad3 => VirtualKeyCode.VK_NUMPAD3,
            Key.NumPad4 => VirtualKeyCode.VK_NUMPAD4, Key.NumPad5 => VirtualKeyCode.VK_NUMPAD5,
            Key.NumPad6 => VirtualKeyCode.VK_NUMPAD6, Key.NumPad7 => VirtualKeyCode.VK_NUMPAD7,
            Key.NumPad8 => VirtualKeyCode.VK_NUMPAD8, Key.NumPad9 => VirtualKeyCode.VK_NUMPAD9,

            Key.Multiply => VirtualKeyCode.VK_MULTIPLY, Key.Add => VirtualKeyCode.VK_ADD,
            Key.Separator => VirtualKeyCode.VK_SEPARATOR, Key.Subtract => VirtualKeyCode.VK_SUBTRACT,
            Key.Decimal => VirtualKeyCode.VK_DECIMAL, Key.Divide => VirtualKeyCode.VK_DIVIDE,

            Key.F1 => VirtualKeyCode.VK_F1, Key.F2 => VirtualKeyCode.VK_F2, Key.F3 => VirtualKeyCode.VK_F3,
            Key.F4 => VirtualKeyCode.VK_F4, Key.F5 => VirtualKeyCode.VK_F5, Key.F6 => VirtualKeyCode.VK_F6,
            Key.F7 => VirtualKeyCode.VK_F7, Key.F8 => VirtualKeyCode.VK_F8, Key.F9 => VirtualKeyCode.VK_F9,
            Key.F10 => VirtualKeyCode.VK_F10, Key.F11 => VirtualKeyCode.VK_F11, Key.F12 => VirtualKeyCode.VK_F12,

            Key.Escape => VirtualKeyCode.VK_ESCAPE, Key.Tab => VirtualKeyCode.VK_TAB,
            Key.CapsLock => VirtualKeyCode.VK_CAPITAL, Key.LeftShift => VirtualKeyCode.VK_LSHIFT,
            Key.RightShift => VirtualKeyCode.VK_RSHIFT, Key.LeftCtrl => VirtualKeyCode.VK_LCONTROL,
            Key.RightCtrl => VirtualKeyCode.VK_RCONTROL, Key.LeftAlt => VirtualKeyCode.VK_LMENU,
            Key.RightAlt => VirtualKeyCode.VK_RMENU, Key.Space => VirtualKeyCode.VK_SPACE,
            Key.Enter => VirtualKeyCode.VK_RETURN, Key.Back => VirtualKeyCode.VK_BACK,

            Key.Insert => VirtualKeyCode.VK_INSERT, Key.Delete => VirtualKeyCode.VK_DELETE,
            Key.Home => VirtualKeyCode.VK_HOME, Key.End => VirtualKeyCode.VK_END,
            Key.PageUp => VirtualKeyCode.VK_PRIOR, Key.PageDown => VirtualKeyCode.VK_NEXT,
            Key.Up => VirtualKeyCode.VK_UP, Key.Down => VirtualKeyCode.VK_DOWN,
            Key.Left => VirtualKeyCode.VK_LEFT, Key.Right => VirtualKeyCode.VK_RIGHT,

            Key.OemTilde => VirtualKeyCode.VK_OEM_3, Key.OemMinus => VirtualKeyCode.VK_OEM_MINUS,
            Key.OemPlus => VirtualKeyCode.VK_OEM_PLUS, Key.OemOpenBrackets => VirtualKeyCode.VK_OEM_4,
            Key.OemCloseBrackets => VirtualKeyCode.VK_OEM_6, Key.OemSemicolon => VirtualKeyCode.VK_OEM_1,
            Key.OemQuotes => VirtualKeyCode.VK_OEM_7, Key.OemComma => VirtualKeyCode.VK_OEM_COMMA,
            Key.OemPeriod => VirtualKeyCode.VK_OEM_PERIOD, Key.OemQuestion => VirtualKeyCode.VK_OEM_2,
            Key.OemBackslash => VirtualKeyCode.VK_OEM_5, Key.OemPipe => VirtualKeyCode.VK_OEM_5,

            Key.LWin => VirtualKeyCode.VK_LWIN, Key.RWin => VirtualKeyCode.VK_RWIN,
            Key.Apps => VirtualKeyCode.VK_APPS, Key.NumLock => VirtualKeyCode.VK_NUMLOCK,
            Key.Scroll => VirtualKeyCode.VK_SCROLL, Key.Pause => VirtualKeyCode.VK_PAUSE,
            Key.PrintScreen => VirtualKeyCode.VK_SNAPSHOT, Key.Sleep => VirtualKeyCode.VK_SLEEP,

            Key.BrowserBack => VirtualKeyCode.VK_BROWSER_BACK,
            Key.BrowserForward => VirtualKeyCode.VK_BROWSER_FORWARD,
            Key.BrowserRefresh => VirtualKeyCode.VK_BROWSER_REFRESH,
            Key.BrowserStop => VirtualKeyCode.VK_BROWSER_STOP,
            Key.BrowserSearch => VirtualKeyCode.VK_BROWSER_SEARCH,
            Key.BrowserFavorites => VirtualKeyCode.VK_BROWSER_FAVORITES,
            Key.BrowserHome => VirtualKeyCode.VK_BROWSER_HOME,

            Key.VolumeMute => VirtualKeyCode.VK_VOLUME_MUTE,
            Key.VolumeDown => VirtualKeyCode.VK_VOLUME_DOWN,
            Key.VolumeUp => VirtualKeyCode.VK_VOLUME_UP,

            Key.MediaNextTrack => VirtualKeyCode.VK_MEDIA_NEXT_TRACK,
            Key.MediaPreviousTrack => VirtualKeyCode.VK_MEDIA_PREV_TRACK,
            Key.MediaStop => VirtualKeyCode.VK_MEDIA_STOP,
            Key.MediaPlayPause => VirtualKeyCode.VK_MEDIA_PLAY_PAUSE,

            _ => VirtualKeyCode.VK_ESCAPE
        };

        return code != VirtualKeyCode.VK_ESCAPE || key == Key.Escape;
    }
}
