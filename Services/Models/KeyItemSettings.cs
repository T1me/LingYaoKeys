using WpfApp.Services.Core;

namespace WpfApp.Services.Models
{
    /// <summary>
    /// 按键项设置 - 用于传递按键或坐标操作的配置
    /// </summary>
    public class KeyItemSettings
    {
        public VirtualKeyCode? KeyCode { get; set; }
        public int Interval { get; set; } = 5;
        public KeyItemType Type { get; set; } = KeyItemType.Keyboard;
        public int? X { get; set; }
        public int? Y { get; set; }

        /// <summary>
        /// 创建键盘按键设置
        /// </summary>
        public static KeyItemSettings CreateKeyboard(VirtualKeyCode keyCode, int interval = 5)
        {
            return new KeyItemSettings
            {
                KeyCode = keyCode,
                Interval = interval,
                Type = KeyItemType.Keyboard,
                X = null,
                Y = null
            };
        }

        /// <summary>
        /// 创建坐标设置
        /// </summary>
        public static KeyItemSettings CreateCoordinates(int? x, int? y, int interval = 5)
        {
            return new KeyItemSettings
            {
                KeyCode = null,
                Interval = interval,
                Type = KeyItemType.Coordinates,
                X = x,
                Y = y
            };
        }
    }
}
