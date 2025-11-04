using System.IO;
using System.Diagnostics;
using WpfApp.ViewModels;
using WpfApp.Services.Models;
using WpfApp.Services.Events;
using WpfApp.Services.Utils;
using System.Text;
using System.Runtime.InteropServices; // 添加此命名空间

namespace WpfApp.Services.Core
{
    /// <summary>
    /// LyKeys服务类 - 提供键盘模拟和按键序列管理功能
    /// </summary>
    public class LyKeysService : IDisposable
    {
        #region 私有字段
        private IDriver _driver;
        private readonly SerilogManager _logger;
        private readonly IConfigManager _configManager;
        private bool _isInitialized;
        private bool _isHoldMode;
        internal readonly InputMethodService _inputMethodService;
        private readonly object _stateLock = new object();

        private const int MIN_KEY_INTERVAL = 1;  // 最小按键间隔
        public const int DEFAULT_KEY_PRESS_INTERVAL = 5; // 默认按键按下时长
        private int _keyInterval = 10; // 按键间隔
        private int _keyPressInterval = DEFAULT_KEY_PRESS_INTERVAL; // 按键按下时长
        private bool _isDisposed;
        private readonly Dictionary<int, VirtualKeyCode> _virtualKeyMap;
        private bool _isReduceKeyStuck = false;
        #endregion

        #region 事件定义
        /// <summary>
        /// 初始化状态变更事件
        /// </summary>
        public event EventHandler<bool>? InitializationStatusChanged;

        /// <summary>
        /// 按键间隔变更事件
        /// </summary>
        public event EventHandler<int>? KeyIntervalChanged;

        /// <summary>
        /// 状态消息变更事件
        /// </summary>
        public event EventHandler<StatusMessageEventArgs>? StatusMessageChanged;

        /// <summary>
        /// 按键按下间隔变更事件
        /// </summary>
        public event EventHandler<int>? KeyPressIntervalChanged;
        #endregion

        #region 属性
        /// <summary>
        /// 获取服务是否已初始化
        /// </summary>
        public bool IsInitialized => _isInitialized;


        /// <summary>
        /// 获取或设置按键间隔
        /// </summary>
        public int KeyInterval
        {
            get => _keyInterval;
            set
            {
                int validValue = Math.Max(MIN_KEY_INTERVAL, value);
                if (_keyInterval != validValue)
                {
                    _keyInterval = validValue;
                    KeyIntervalChanged?.Invoke(this, validValue);
                }
            }
        }

        /// <summary>
        /// 获取或设置按键按下时长
        /// </summary>
        public int KeyPressInterval
        {
            get => _keyPressInterval;
            set
            {
                if (value >= 0 && _keyPressInterval != value)
                {
                    _keyPressInterval = value;
                    KeyPressIntervalChanged?.Invoke(this, value);
                }
            }
        }

        /// <summary>
        /// 获取或设置是否为按压模式
        /// </summary>
        public bool IsHoldMode
        {
            get => _isHoldMode;
            set => _isHoldMode = value;
        }

        /// <summary>
        /// 获取或设置是否启用降低卡位功能
        /// </summary>
        public bool IsReduceKeyStuck
        {
            get => _isReduceKeyStuck;
            set
            {
                if (_isReduceKeyStuck != value)
                {
                    _isReduceKeyStuck = value;
                    KeyPressInterval = _isReduceKeyStuck ? DEFAULT_KEY_PRESS_INTERVAL : 0;
                }
            }
        }
        #endregion

        #region 构造函数
        /// <summary>
        /// 初始化LyKeys服务
        /// </summary>
        public LyKeysService(IDriver driver)
        {
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _logger = SerilogManager.Instance;
            _configManager = ConfigManager.Instance;
            _isInitialized = false;
            _isHoldMode = false;
            _virtualKeyMap = InitializeVirtualKeyMap();
            _inputMethodService = new InputMethodService();

            // 初始化降低卡位配置（默认关闭，由配置动态设置）
            _isReduceKeyStuck = false;
            _keyPressInterval = 0;

        }
        #endregion

        #region 按键映射
        // 初始化虚拟键码映射
        private Dictionary<int, VirtualKeyCode> InitializeVirtualKeyMap()
        {
            var map = new Dictionary<int, VirtualKeyCode>();
            
            // 添加基本按键映射
            foreach (VirtualKeyCode code in Enum.GetValues(typeof(VirtualKeyCode)))
            {
                map[(int)code] = code;
            }

            // 添加特殊映射
            map[0x10] = VirtualKeyCode.VK_SHIFT;    // Shift
            map[0x11] = VirtualKeyCode.VK_CONTROL;  // Control
            map[0x12] = VirtualKeyCode.VK_MENU;     // Alt
            map[0x14] = VirtualKeyCode.VK_CAPITAL;  // Caps Lock
            map[0x1B] = VirtualKeyCode.VK_ESCAPE;   // Escape
            map[0x20] = VirtualKeyCode.VK_SPACE;    // Space
            map[0x2E] = VirtualKeyCode.VK_DELETE;   // Delete

            // 添加鼠标按键映射
            map[0x01] = VirtualKeyCode.VK_LBUTTON;  // 左键
            map[0x02] = VirtualKeyCode.VK_RBUTTON;  // 右键
            map[0x04] = VirtualKeyCode.VK_MBUTTON;  // 中键
            map[0x05] = VirtualKeyCode.VK_XBUTTON1; // 侧键1
            map[0x06] = VirtualKeyCode.VK_XBUTTON2; // 侧键2

            return map;
        }

        /// <summary>
        /// 检查是否为有效的虚拟键码
        /// </summary>
        public bool IsValidVirtualKeyCode(VirtualKeyCode code)
        {
            return _virtualKeyMap.ContainsValue(code);
        }

        /// <summary>
        /// 获取键码的描述信息
        /// </summary>
        /// <param name="code">键码</param>
        /// <returns>描述信息</returns>
        public string GetKeyDescription(VirtualKeyCode code)
        {
            // 首先处理鼠标按键的特殊描述
            switch (code)
            {
                case VirtualKeyCode.VK_LBUTTON:
                    return "鼠标左键";
                case VirtualKeyCode.VK_RBUTTON:
                    return "鼠标右键";
                case VirtualKeyCode.VK_MBUTTON:
                    return "鼠标中键";
                case VirtualKeyCode.VK_XBUTTON1:
                    return "鼠标侧键1";
                case VirtualKeyCode.VK_XBUTTON2:
                    return "鼠标侧键2";
            }

            var field = typeof(VirtualKeyCode).GetField(code.ToString());
            if (field != null)
            {
                var attributes = (System.ComponentModel.DescriptionAttribute[])field.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false);
                if (attributes.Length > 0)
                {
                    return attributes[0].Description;
                }
            }
            return code.ToString();
        }
        #endregion

        #region 公共方法
        /// <summary>
        /// 初始化驱动
        /// </summary>
        /// <param name="driverPath">驱动文件路径（保留兼容性，实际由工厂创建）</param>
        /// <returns>是否初始化成功</returns>
        public bool Initialize(string driverPath)
        {
            try
            {
                if (_isInitialized)
                {
                    return true;
                }

                try
                {
                    if (!_driver.Initialize())
                    {
                        var lastStatus = _driver.GetLastStatus();
                        string errorMessage = lastStatus switch
                        {
                            DeviceStatus.NoKeyboard => "找不到键盘设备，请检查您的键盘连接是否正常，且系统能识别到键盘设备。",
                            DeviceStatus.NoMouse => "找不到鼠标设备，请检查您的鼠标连接是否正常，且系统能识别到鼠标设备。",
                            DeviceStatus.InitFailed => "驱动初始化失败，请尝试重新启动计算机或重新安装驱动。",
                            DeviceStatus.Error => "设备发生错误，无法完成初始化。请尝试重新安装驱动或重启电脑。",
                            _ => $"驱动初始化失败 ({lastStatus})，请尝试重新安装驱动或重启电脑。"
                        };

                        _logger.Error($"驱动初始化失败: {errorMessage}");
                        SendStatusMessage($"驱动初始化失败：{errorMessage}", true);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"驱动初始化过程发生异常: {ex.Message}", ex);
                    SendStatusMessage($"驱动服务初始化过程发生错误：{ex.Message}", true);
                    return false;
                }

                _isInitialized = true;
                InitializationStatusChanged?.Invoke(this, true);
                SendStatusMessage("驱动服务初始化完成");
                _logger.Debug("驱动服务初始化完成");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("驱动服务初始化异常", ex);
                SendStatusMessage($"驱动服务过程发生异常：{ex.Message}", true);
                return false;
            }
        }

        public bool ReloadDriver(IDriver newDriver, string driverPath)
        {
            try
            {
                // 释放旧驱动
                if (_isInitialized)
                {
                    _driver?.Dispose();
                    _isInitialized = false;
                }

                // 更新驱动实例
                _driver = newDriver ?? throw new ArgumentNullException(nameof(newDriver));

                // 初始化新驱动
                return Initialize(driverPath);
            }
            catch (Exception ex)
            {
                _logger.Error("重新加载驱动失败", ex);
                return false;
            }
        }



        /// <summary>
        /// 模拟按键按下
        /// </summary>
        /// <param name="keyCode">按键代码</param>
        /// <returns>是否成功</returns>
        public bool SendKeyDown(VirtualKeyCode keyCode)
        {
            if (!_isInitialized) return false;
            if (!IsValidVirtualKeyCode(keyCode))
            {
                _logger.Error($"无效的键码: {keyCode}");
                return false;
            }

            try
            {
                // 检查是否为鼠标按键
                if (IsMouseButton(keyCode))
                {
                    return _driver.SendMouseButton(ConvertToMouseButtonType(keyCode), true);
                }

                _driver.SendKeyDown((ushort)keyCode);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"按键按下异常: {keyCode}", ex);
                return false;
            }
        }

        /// <summary>
        /// 模拟按键释放
        /// </summary>
        /// <param name="keyCode">按键代码</param>
        /// <returns>是否成功</returns>
        public bool SendKeyUp(VirtualKeyCode keyCode)
        {
            if (!_isInitialized) return false;
            if (!IsValidVirtualKeyCode(keyCode))
            {
                _logger.Error($"无效的键码: {keyCode}");
                return false;
            }

            try
            {
                // 检查是否为鼠标按键
                if (IsMouseButton(keyCode))
                {
                    return _driver.SendMouseButton(ConvertToMouseButtonType(keyCode), false);
                }

                _driver.SendKeyUp((ushort)keyCode);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"按键释放异常: {keyCode}", ex);
                return false;
            }
        }

        private bool IsMouseButton(VirtualKeyCode keyCode)
        {
            return keyCode == VirtualKeyCode.VK_LBUTTON ||
                   keyCode == VirtualKeyCode.VK_RBUTTON ||
                   keyCode == VirtualKeyCode.VK_MBUTTON ||
                   keyCode == VirtualKeyCode.VK_XBUTTON1 ||
                   keyCode == VirtualKeyCode.VK_XBUTTON2 ||
                   keyCode == VirtualKeyCode.VK_WHEELUP ||
                   keyCode == VirtualKeyCode.VK_WHEELDOWN;
        }

        private MouseButtonType ConvertToMouseButtonType(VirtualKeyCode keyCode)
        {
            return keyCode switch
            {
                VirtualKeyCode.VK_LBUTTON => MouseButtonType.Left,
                VirtualKeyCode.VK_RBUTTON => MouseButtonType.Right,
                VirtualKeyCode.VK_MBUTTON => MouseButtonType.Middle,
                VirtualKeyCode.VK_XBUTTON1 => MouseButtonType.XButton1,
                VirtualKeyCode.VK_XBUTTON2 => MouseButtonType.XButton2,
                VirtualKeyCode.VK_WHEELUP => MouseButtonType.WheelUp,
                VirtualKeyCode.VK_WHEELDOWN => MouseButtonType.WheelDown,
                _ => throw new ArgumentException($"非法的鼠标按键类型: {keyCode}")
            };
        }

        /// <summary>
        /// 模拟按键点击
        /// </summary>
        /// <param name="keyCode">按键代码</param>
        /// <param name="duration">按下持续时间(毫秒)</param>
        /// <returns>是否成功</returns>
        public bool SendKeyPress(VirtualKeyCode keyCode, int duration = 100)
        {
            if (!_isInitialized) return false;
            if (!IsValidVirtualKeyCode(keyCode))
            {
                _logger.Error($"无效的键码: {keyCode}");
                return false;
            }

            try
            {
                bool isMouseButton = IsMouseButton(keyCode);
                if (isMouseButton)
                {
                    _logger.Debug($"正在执行鼠标按键: {keyCode}, 持续时间: {duration}ms");
                }

                if (!SendKeyDown(keyCode))
                {
                    _logger.Error($"按键按下失败: {keyCode}");
                    return false;
                }
                
                Thread.Sleep(duration);
                
                bool result = SendKeyUp(keyCode);
                if (!result)
                {
                    _logger.Error($"按键释放失败: {keyCode}");
                }
                
                if (isMouseButton)
                {
                    _logger.Debug($"鼠标按键执行完成: {keyCode}, 结果: {result}");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"按键点击异常: {keyCode}", ex);
                return false;
            }
        }

        /// <summary>
        /// 模拟组合键
        /// </summary>
        /// <param name="keyCodes">按键代码数组</param>
        public void SimulateKeyCombo(params VirtualKeyCode[] keyCodes)
        {
            if (!_isInitialized) return;
            if (keyCodes.Any(k => !IsValidVirtualKeyCode(k)))
            {
                _logger.Error("组合键包含无效的键码");
                return;
            }

            try
            {
                // 按下所有键
                foreach (var key in keyCodes)
                {
                    SendKeyDown(key);
                    Thread.Sleep(5);
                }

                Thread.Sleep(10);

                // 释放所有键（反序）
                for (int i = keyCodes.Length - 1; i >= 0; i--)
                {
                    SendKeyUp(keyCodes[i]);
                    Thread.Sleep(5);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("模拟组合键异常", ex);
                // 确保释放所有按键
                foreach (var key in keyCodes)
                {
                    SendKeyUp(key);
                }
            }
        }


        /// <summary>
        /// 移动鼠标到指定的绝对坐标位置
        /// </summary>
        /// <param name="x">屏幕X坐标</param>
        /// <param name="y">屏幕Y坐标</param>
        /// <returns>操作是否成功</returns>
        public bool MoveMouseToPosition(int? x, int? y)
        {
            if (!_isInitialized || x == null || y == null)
                return false;

            try
            {
                _logger.Debug($"移动鼠标到坐标: ({x}, {y})");
                return _driver.MoveMouseAbsolute(x.Value, y.Value);
            }
            catch (Exception ex)
            {
                _logger.Error($"移动鼠标到坐标({x}, {y})失败: {ex.Message}", ex);
                return false;
            }
        }
        #endregion

        #region 私有方法
        private void SendStatusMessage(string message, bool isError = false)
        {
            var args = new StatusMessageEventArgs(message, isError);
            StatusMessageChanged?.Invoke(this, args);
            if (isError)
                _logger.Error(message);
            else
                _logger.Debug(message);
        }
        #endregion

        #region IDisposable实现
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {

                try
                {
                    _driver?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error("释放驱动失败", ex);
                }

                _isInitialized = false;
            }
            catch (Exception ex)
            {
                _logger.Error("释放LyKeysService资源异常", ex);
            }
        }
        #endregion


        #region 按键配置
        /// <summary>
        /// 根据降低卡位状态更新按键按下时长，确保与降低卡位功能状态保持一致
        /// </summary>
        public void UpdateKeyPressIntervalByReduceKeyStuck()
        {
            KeyPressInterval = _isReduceKeyStuck ? DEFAULT_KEY_PRESS_INTERVAL : 0;
            _logger.Debug($"根据降低卡位功能状态({(_isReduceKeyStuck ? "开启" : "关闭")})更新按键按下时长：{_keyPressInterval}ms");
        }
        #endregion
    }
} 