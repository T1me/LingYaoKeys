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
        private LyKeys? _lyKeys;
        private readonly SerilogManager _logger;
        private readonly IConfigManager _configManager;
        private bool _isInitialized;
        private bool _isEnabled;
        private bool _isHoldMode;
        internal readonly InputMethodService _inputMethodService;
        private readonly object _stateLock = new object();
        private readonly Stopwatch _sequenceStopwatch = new Stopwatch();
        // 新的统一操作列表
        private List<KeyItemSettings> _operationList = new List<KeyItemSettings>();
        
        private const int MIN_KEY_INTERVAL = 1;  // 最小按键间隔
        public const int DEFAULT_KEY_PRESS_INTERVAL = 5; // 默认按键按下时长
        private int _keyInterval = 10; // 按键间隔
        private int _keyPressInterval = DEFAULT_KEY_PRESS_INTERVAL; // 按键按下时长
        private bool _isDisposed;
        private CancellationTokenSource? _holdModeCts;
        private readonly Dictionary<int, LyKeysCode> _virtualKeyMap;
        private volatile bool _emergencyStop;
        private const int EMERGENCY_STOP_THRESHOLD = 100; // 100ms内未能停止则强制停止
        private readonly object _emergencyStopLock = new object();
        private bool _autoSwitchIME = true; // 是否自动切换输入法
        // 是否正在执行SetOperationList
        private bool _isSettingKeyList = false;
        // 是否启用降低卡位功能
        private bool _isReduceKeyStuck = false;
        // 按键模式实例
        private SequenceKeyMode? _sequenceMode;
        private HoldKeyMode? _holdMode;
        private KeyModeBase? _currentMode;
        #endregion

        #region 事件定义
        /// <summary>
        /// 初始化状态变更事件
        /// </summary>
        public event EventHandler<bool>? InitializationStatusChanged;

        /// <summary>
        /// 启用状态变更事件
        /// </summary>
        public event EventHandler<bool>? EnableStatusChanged;

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

        /// <summary>
        /// 模式切换事件
        /// </summary>
        public event EventHandler<bool>? ModeSwitched;
        #endregion

        #region 属性
        /// <summary>
        /// 获取服务是否已初始化
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// 获取或设置服务是否启用
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    EnableStatusChanged?.Invoke(this, value);
                    
                    if (_isEnabled)
                    {
                        if (_autoSwitchIME)
                        {
                            _inputMethodService.StoreCurrentLayout();
                            _inputMethodService.SwitchToEnglish();
                            _logger.Debug("服务启用：已切换到英文输入法");
                        }
                        else
                        {
                            _logger.Debug("服务启用：保持当前输入法不变");
                        }

                        _currentMode = _isHoldMode ? _holdMode : _sequenceMode;
                        _currentMode?.SetOperationList(_operationList);
                        _currentMode?.Start();
                    }
                    else
                    {
                        _currentMode?.Stop();
                        _currentMode = null;
                        }

                        // 只在完全停止时恢复输入法
                        RestoreIME();
                        _logger.Debug("服务停用：已恢复输入法");
                }
            }
        }
        

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
                    _logger.SequenceEvent($"按键间隔已更新为: {validValue}ms");
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
                    _logger.SequenceEvent($"按键按下时长已更新为: {value}ms");
                }
            }
        }

        /// <summary>
        /// 获取或设置是否为按压模式
        /// </summary>
        public bool IsHoldMode
        {
            get => _isHoldMode;
            set
            {
                if (_isHoldMode != value)
                {
                    bool wasEnabled = _isEnabled;
                    if (wasEnabled)
                    {
                        IsEnabled = false;
                    }

                    _isHoldMode = value;
                    ModeSwitched?.Invoke(this, value);

                    if (wasEnabled)
                    {
                        IsEnabled = true;
                    }
                }
            }
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
                    // 根据降低卡位状态更新按键按下时长
                    KeyPressInterval = _isReduceKeyStuck ? DEFAULT_KEY_PRESS_INTERVAL : 0;
                    _logger.Debug($"降低卡位功能状态：{(_isReduceKeyStuck ? "开启" : "关闭")}，按键按下时长已调整为：{KeyPressInterval}ms");
                }
            }
        }
        #endregion

        #region 构造函数
        /// <summary>
        /// 初始化LyKeys服务
        /// </summary>
        public LyKeysService()
        {
            _logger = SerilogManager.Instance;
            _configManager = ConfigManager.Instance;
            _isInitialized = false;
            _isEnabled = false;
            _isHoldMode = false;
            _virtualKeyMap = InitializeVirtualKeyMap();
            _inputMethodService = new InputMethodService();  // 初始化InputMethodService

            // 从配置中读取是否自动切换输入法
            try
            {
                var globalConfig = _configManager.GlobalConfig;
                _autoSwitchIME = globalConfig.AutoSwitchToEnglishIME ?? true;
                _logger.Debug($"LyKeysService构造函数：输入法自动切换设置为 {(_autoSwitchIME ? "开启" : "关闭")}");
                
                // 读取降低卡位配置
                _isReduceKeyStuck = globalConfig.IsReduceKeyStuck ?? false;
                // 根据降低卡位状态设置按键按下时长
                _keyPressInterval = _isReduceKeyStuck ? DEFAULT_KEY_PRESS_INTERVAL : 0;
                _logger.Debug($"LyKeysService构造函数：降低卡位功能设置为 {(_isReduceKeyStuck ? "开启" : "关闭")}，按键按下时长：{_keyPressInterval}ms");
            }
            catch (Exception ex)
            {
                _logger.Error("读取配置失败，使用默认值", ex);
                _autoSwitchIME = true;
                _isReduceKeyStuck = false;
                _keyPressInterval = 0;
            }

            // 初始化按键模式实例
            _sequenceMode = new SequenceKeyMode(this);
            _holdMode = new HoldKeyMode(this);

            _logger.Debug("LyKeysService构造函数：已初始化InputMethodService和按键模式");
        }
        #endregion

        #region 按键映射
        // 初始化虚拟键码映射
        private Dictionary<int, LyKeysCode> InitializeVirtualKeyMap()
        {
            var map = new Dictionary<int, LyKeysCode>();
            
            // 添加基本按键映射
            foreach (LyKeysCode code in Enum.GetValues(typeof(LyKeysCode)))
            {
                map[(int)code] = code;
            }

            // 添加特殊映射
            map[0x10] = LyKeysCode.VK_SHIFT;    // Shift
            map[0x11] = LyKeysCode.VK_CONTROL;  // Control
            map[0x12] = LyKeysCode.VK_MENU;     // Alt
            map[0x14] = LyKeysCode.VK_CAPITAL;  // Caps Lock
            map[0x1B] = LyKeysCode.VK_ESCAPE;   // Escape
            map[0x20] = LyKeysCode.VK_SPACE;    // Space
            map[0x2E] = LyKeysCode.VK_DELETE;   // Delete

            // 添加鼠标按键映射
            map[0x01] = LyKeysCode.VK_LBUTTON;  // 左键
            map[0x02] = LyKeysCode.VK_RBUTTON;  // 右键
            map[0x04] = LyKeysCode.VK_MBUTTON;  // 中键
            map[0x05] = LyKeysCode.VK_XBUTTON1; // 侧键1
            map[0x06] = LyKeysCode.VK_XBUTTON2; // 侧键2

            return map;
        }

        /// <summary>
        /// 将虚拟键码转换为LyKeys键码
        /// </summary>
        /// <param name="virtualKeyCode">虚拟键码</param>
        /// <returns>对应的LyKeys键码，如果没有对应的键码则返回null</returns>
        public LyKeysCode? GetLyKeysCode(int virtualKeyCode)
        {
            if (_virtualKeyMap.TryGetValue(virtualKeyCode, out LyKeysCode code))
            {
                return code;
            }
            return null;
        }

        /// <summary>
        /// 检查是否为有效的LyKeys键码
        /// </summary>
        /// <param name="code">要检查的键码</param>
        /// <returns>是否有效</returns>
        public bool IsValidLyKeysCode(LyKeysCode code)
        {
            return _virtualKeyMap.ContainsValue(code);
        }

        /// <summary>
        /// 获取所有支持的LyKeys键码
        /// </summary>
        /// <returns>支持的键码列表</returns>
        public IEnumerable<LyKeysCode> GetSupportedKeyCodes()
        {
            return _virtualKeyMap.Values.Distinct();
        }

        /// <summary>
        /// 获取键码的描述信息
        /// </summary>
        /// <param name="code">键码</param>
        /// <returns>描述信息</returns>
        public string GetKeyDescription(LyKeysCode code)
        {
            // 首先处理鼠标按键的特殊描述
            switch (code)
            {
                case LyKeysCode.VK_LBUTTON:
                    return "鼠标左键";
                case LyKeysCode.VK_RBUTTON:
                    return "鼠标右键";
                case LyKeysCode.VK_MBUTTON:
                    return "鼠标中键";
                case LyKeysCode.VK_XBUTTON1:
                    return "鼠标侧键1";
                case LyKeysCode.VK_XBUTTON2:
                    return "鼠标侧键2";
            }

            var field = typeof(LyKeysCode).GetField(code.ToString());
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
        /// <param name="driverPath">驱动文件路径</param>
        /// <returns>是否初始化成功</returns>
        public bool Initialize(string driverPath)
        {
            try
            {
                if (_isInitialized)
                {
                    _logger.Warning("服务已经初始化");
                    return true;
                }

                _logger.Debug($"开始初始化LyKeys服务，驱动路径: {driverPath}");

                // 验证驱动文件
                if (!File.Exists(driverPath))
                {
                    _logger.Error($"驱动文件不存在: {driverPath}");
                    SendStatusMessage($"初始化失败：驱动文件不存在({driverPath})", true);
                    return false;
                }

                // 初始化驱动
                _lyKeys = new LyKeys(driverPath);
                try
                {
                    if (!_lyKeys.Initialize())
                    {   
                        // 获取并处理详细的错误信息
                        var lastStatus = _lyKeys.GetLastStatus();
                        string errorMessage;
                        
                        switch (lastStatus)
                        {
                            case LyKeys.DeviceStatus.NoKeyboard:
                                errorMessage = "找不到键盘设备，请检查您的键盘连接是否正常，且系统能识别到键盘设备。";
                                break;
                            case LyKeys.DeviceStatus.NoMouse:
                                errorMessage = "找不到鼠标设备，请检查您的鼠标连接是否正常，且系统能识别到鼠标设备。";
                                break;
                            case LyKeys.DeviceStatus.InitFailed:
                                errorMessage = "驱动初始化失败，请尝试重新启动计算机或重新安装驱动。";
                                break;
                            case LyKeys.DeviceStatus.Error:
                                errorMessage = "设备发生错误，无法完成初始化。请尝试重新安装驱动或重启电脑。";
                                break;
                            default:
                                errorMessage = $"驱动初始化失败 ({lastStatus})，请尝试重新安装驱动或重启电脑。";
                                break;
                        }
                        
                        _logger.Error($"驱动初始化失败: {errorMessage}");
                        SendStatusMessage($"初始化失败：{errorMessage}", true);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"驱动初始化过程发生异常: {ex.Message}", ex);
                    SendStatusMessage($"初始化过程发生错误：{ex.Message}", true);
                    return false;
                }
                
                _isInitialized = true;
                InitializationStatusChanged?.Invoke(this, true);
                SendStatusMessage("服务初始化成功");
                _logger.Debug("LyKeys服务初始化完成");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("服务初始化异常", ex);
                SendStatusMessage($"初始化过程发生异常：{ex.Message}", true);
                return false;
            }
        }



        /// <summary>
        /// 设置按键列表 - 统一操作列表版本
        /// </summary>
        /// <param name="operations">操作列表，包括按键和坐标</param>
        public void SetOperationList(List<KeyItemSettings> operations)
        {
            try
            {
                // 防循环调用保护
                if (_isSettingKeyList)
                {
                    _logger.Warning("检测到SetOperationList正在执行中，跳过重复调用");
                    return;
                }

                _isSettingKeyList = true;

                // 验证输入
                if (operations == null || operations.Count == 0)
                {
                    _logger.Warning("收到空的操作列表");
                    if (_isEnabled) IsEnabled = false;
                    _operationList.Clear();
                    return;
                }

                // 验证键盘按键有效性
                var keyboardOps = operations
                    .Where(op => op.Type == KeyItemType.Keyboard && op.KeyCode.HasValue)
                    .Select(op => op.KeyCode.Value)
                    .ToList();

                if (keyboardOps.Any(k => !IsValidLyKeysCode(k)))
                {
                    _logger.Warning("操作列表包含无效的键码");
                    return;
                }

                // 直接更新操作列表
                _operationList = operations.ToList();

                _logger.Debug($"操作列表已更新 - 总操作数: {_operationList.Count}, 键盘按键数: {keyboardOps.Count}, 坐标点数: {operations.Count(op => op.Type == KeyItemType.Coordinates)}");
            }
            catch (Exception ex)
            {
                _logger.Error("设置操作列表异常", ex);
                _operationList.Clear();
                IsEnabled = false;
            }
            finally
            {
                _isSettingKeyList = false;
            }
        }


        /// <summary>
        /// 模拟按键按下
        /// </summary>
        /// <param name="keyCode">按键代码</param>
        /// <returns>是否成功</returns>
        public bool SendKeyDown(LyKeysCode keyCode)
        {
            if (!CheckInitialization()) return false;
            if (!IsValidLyKeysCode(keyCode))
            {
                _logger.Error($"无效的键码: {keyCode}");
                return false;
            }

            try
            {
                // 检查是否为鼠标按键
                if (IsMouseButton(keyCode))
                {
                    return _lyKeys.SendMouseButton(ConvertToMouseButtonType(keyCode), true);
                }
                
                _lyKeys.SendKeyDown((ushort)keyCode);
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
        public bool SendKeyUp(LyKeysCode keyCode)
        {
            if (!CheckInitialization()) return false;
            if (!IsValidLyKeysCode(keyCode))
            {
                _logger.Error($"无效的键码: {keyCode}");
                return false;
            }

            try
            {
                // 检查是否为鼠标按键
                if (IsMouseButton(keyCode))
                {
                    return _lyKeys.SendMouseButton(ConvertToMouseButtonType(keyCode), false);
                }
                
                _lyKeys.SendKeyUp((ushort)keyCode);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"按键释放异常: {keyCode}", ex);
                return false;
            }
        }

        private bool IsMouseButton(LyKeysCode keyCode)
        {
            return keyCode == LyKeysCode.VK_LBUTTON ||
                   keyCode == LyKeysCode.VK_RBUTTON ||
                   keyCode == LyKeysCode.VK_MBUTTON ||
                   keyCode == LyKeysCode.VK_XBUTTON1 ||
                   keyCode == LyKeysCode.VK_XBUTTON2 ||
                   keyCode == LyKeysCode.VK_WHEELUP ||
                   keyCode == LyKeysCode.VK_WHEELDOWN;
        }

        private LyKeys.MouseButtonType ConvertToMouseButtonType(LyKeysCode keyCode)
        {
            return keyCode switch
            {
                LyKeysCode.VK_LBUTTON => LyKeys.MouseButtonType.Left,
                LyKeysCode.VK_RBUTTON => LyKeys.MouseButtonType.Right,
                LyKeysCode.VK_MBUTTON => LyKeys.MouseButtonType.Middle,
                LyKeysCode.VK_XBUTTON1 => LyKeys.MouseButtonType.XButton1,
                LyKeysCode.VK_XBUTTON2 => LyKeys.MouseButtonType.XButton2,
                LyKeysCode.VK_WHEELUP => LyKeys.MouseButtonType.WheelUp,
                LyKeysCode.VK_WHEELDOWN => LyKeys.MouseButtonType.WheelDown,
                _ => throw new ArgumentException($"非法的鼠标按键类型: {keyCode}")
            };
        }

        /// <summary>
        /// 模拟按键点击
        /// </summary>
        /// <param name="keyCode">按键代码</param>
        /// <param name="duration">按下持续时间(毫秒)</param>
        /// <returns>是否成功</returns>
        public bool SendKeyPress(LyKeysCode keyCode, int duration = 100)
        {
            if (!CheckInitialization()) return false;
            if (!IsValidLyKeysCode(keyCode))
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
        public void SimulateKeyCombo(params LyKeysCode[] keyCodes)
        {
            if (!CheckInitialization()) return;
            if (keyCodes.Any(k => !IsValidLyKeysCode(k)))
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
            if (!CheckInitialization())
            {
                _logger.Error($"鼠标移动失败：驱动未初始化，坐标: ({x}, {y})");
                return false;
            }

            // 处理null值
            if (x == null || y == null)
            {
                _logger.Warning("坐标移动失败：坐标包含null值");
                return false;
            }

            try
            {
                if (_lyKeys != null)
                {
                    _logger.Debug($"移动鼠标到坐标: ({x}, {y})");
                    return _lyKeys.MoveMouseAbsolute(x.Value, y.Value);
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"移动鼠标到坐标({x}, {y})失败: {ex.Message}", ex);
                return false;
            }
        }
        #endregion

        #region 私有方法
        private bool CheckInitialization()
        {
            if (!_isInitialized)
            {
                _logger.Error("服务未初始化");
                return false;
            }
            return true;
        }

        public void EmergencyStop()
        {
            try
            {
                _logger.Debug("开始执行紧急停止");
                _currentMode?.Stop();

                foreach (var operation in _operationList)
                    if (operation.Type == KeyItemType.Keyboard && operation.KeyCode.HasValue)
                        SendKeyUp(operation.KeyCode.Value);
            }
            catch (Exception ex)
            {
                _logger.Error("紧急停止异常", ex);
                ForceStop();
            }
        }


        private void SendStatusMessage(string message, bool isError = false)
        {
            var args = new StatusMessageEventArgs(message, isError);
            StatusMessageChanged?.Invoke(this, args);
            if (isError)
                _logger.Error(message);
            else
                _logger.Debug(message);
        }

        private void ForceStop()
        {
            try
            {
                _isEnabled = false;
                _isHoldMode = false;
                
                // 确保所有按键都被释放
                foreach (var operation in _operationList)
                {
                    if (operation.Type == KeyItemType.Keyboard && operation.KeyCode.HasValue)
                    {
                        try
                        {
                            SendKeyUp(operation.KeyCode.Value);
                            Thread.Sleep(1); // 给予系统短暂时间处理按键释放
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"强制释放按键时发生异常: {operation.KeyCode}", ex);
                        }
                    }
                }

                // 重置所有状态
                _emergencyStop = false;
                EnableStatusChanged?.Invoke(this, false);
                
                // 恢复输入法
                RestoreIME();
                _logger.Debug("已强制停止所有按键操作，输入法已恢复");
            }
            catch (Exception ex)
            {
                _logger.Error("强制停止时发生异常", ex);
            }
        }
        #endregion

        #region IDisposable实现
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                _logger.Debug("开始释放LyKeysService资源");

                // 1. 停止所有操作
                IsEnabled = false;
                
                // 2. 停止序列
                try
                {
                    _currentMode?.Stop();
                    RestoreIME();
                }
                catch (Exception ex)
                {
                    _logger.Error("停止序列失败", ex);
                }

                // 3. 释放驱动
                try
                {
                    _lyKeys?.Dispose();
                    _lyKeys = null;
                }
                catch (Exception ex)
                {
                    _logger.Error("释放驱动失败", ex);
                }

                _isInitialized = false;
                _logger.Debug("LyKeysService资源释放完成");
            }
            catch (Exception ex)
            {
                _logger.Error("释放LyKeysService资源异常", ex);
            }
        }
        #endregion


        #region 输入法管理
        /// <summary>
        /// 设置是否自动切换输入法
        /// </summary>
        /// <param name="autoSwitch">是否自动切换</param>
        public void SetAutoSwitchIME(bool autoSwitch)
        {
            _autoSwitchIME = autoSwitch;
            _logger.Debug($"输入法自动切换设置已更新: {(autoSwitch ? "开启" : "关闭")}");
        }

        /// <summary>
        /// 根据降低卡位状态更新按键按下时长，确保与降低卡位功能状态保持一致
        /// </summary>
        public void UpdateKeyPressIntervalByReduceKeyStuck()
        {
            KeyPressInterval = _isReduceKeyStuck ? DEFAULT_KEY_PRESS_INTERVAL : 0;
            _logger.Debug($"根据降低卡位功能状态({(_isReduceKeyStuck ? "开启" : "关闭")})更新按键按下时长：{_keyPressInterval}ms");
        }

        /// <summary>
        /// 恢复输入法到之前的状态
        /// </summary>
        public void RestoreIME()
        {
            try
            {
                // 只有在自动切换输入法开启时才恢复
                if (_autoSwitchIME)
                {
                    _inputMethodService.RestorePreviousLayout();
                    _logger.Debug("已恢复原始输入法");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("恢复输入法失败", ex);
            }
        }
        #endregion


    }
} 