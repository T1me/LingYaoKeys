using System.Windows.Input;
using WpfApp.Services.Models;
using WpfApp.Services.Utils;
using WpfApp.Services.Core;
using System.Text;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using WpfApp.Views;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows.Threading;
using System.IO;


// 定义KeyItemSettings结构用于传递按键设置
public class KeyItemSettings
{
    public LyKeysCode? KeyCode { get; set; }
    public int Interval { get; set; } = 5;
    public KeyItemType Type { get; set; } = KeyItemType.Keyboard;
    public int? X { get; set; }
    public int? Y { get; set; }
    
    // 创建键盘按键设置
    public static KeyItemSettings CreateKeyboard(LyKeysCode keyCode, int interval = 5)
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
    
    // 创建坐标设置
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

// 按键映射核心业务逻辑层
namespace WpfApp.ViewModels
{
    public class KeyMappingViewModel : ViewModelBase
    {
        // 添加窗口为空的占位符常量，改为公共静态常量以便在XAML中访问
        public const string EMPTY_WINDOW_PLACEHOLDER = "空";
        
        private readonly LyKeysService _lyKeysService;
        public LyKeysService LyKeysService => _lyKeysService;
        private LyKeysCode? _currentKey;
        private string _currentKeyText = string.Empty;
        private ObservableCollection<KeyItem> _keyList;
        private string _hotkeyText = string.Empty; // 简化为单一热键文本
        private LyKeysCode? _hotkey; // 主热键
        private ModifierKeys _hotkeyModifiers = ModifierKeys.None; // 修饰热键
        private int _selectedKeyMode;
        private readonly HotkeyService _hotkeyService;
        private bool _isHotkeyEnabled;
        private string _hotkeyStatus;
        private bool _isSequenceMode = true; // 默认为顺序模式
        private readonly MainViewModel _mainViewModel;
        private MainWindow? _mainWindow;
        private bool _isSoundEnabled = true;
        private readonly AudioService _audioService;
        private double _soundVolume = 0.8; // 默认音量80%
        private bool _isReduceKeyStuck = true; // 默认开启
        private bool _isInitializing = true; // 添加一个标志来标识是否在初始化
        private bool _isExecuting = false; // 添加执行状态标志
        private bool _isFloatingWindowEnabled;
        private bool _autoSwitchToEnglishIME = true; // 默认开启自动切换输入法
        private bool _isHotkeyControlEnabled = true; // 热键总开关，默认开启
        private FloatingStatusWindow _floatingWindow;
        private FloatingStatusViewModel _floatingViewModel;
        private KeyItem? _selectedKeyItem;
        private string _selectedWindowTitle = EMPTY_WINDOW_PLACEHOLDER;
        private IntPtr _selectedWindowHandle = IntPtr.Zero;
        private string _selectedWindowClassName = string.Empty;
        private string _selectedWindowProcessName = string.Empty;
        private System.Timers.Timer? _windowCheckTimer;
        private readonly object _windowCheckLock = new();
        private bool _isTargetWindowActive;
        private readonly System.Timers.Timer _activeWindowCheckTimer;
        private const int ACTIVE_WINDOW_CHECK_INTERVAL = 50; // 50ms检查一次活动窗口
        private int _keyInterval = 10;
        private int _keyPressInterval = 5;
        
        // 添加坐标属性
        private int? _currentX;
        private int? _currentY;

        // 添加浮窗状态更新节流控制变量
        private readonly object _floatingStatusUpdateLock = new object();
        private DateTime _lastFloatingStatusUpdateTime = DateTime.MinValue;
        private const int FLOATING_STATUS_UPDATE_THROTTLE_MS = 200; // 200毫秒节流间隔

        // 添加窗口句柄变化事件
        public event Action<IntPtr>? WindowHandleChanged;

        // 创建坐标索引更新事件
        public event EventHandler? CoordinateIndicesNeedUpdate;

        /// <summary>
        /// 触发坐标索引更新事件，通知视图层更新
        /// </summary>
        public void TriggerCoordinateIndicesUpdate()
        {
             Logger.Debug("触发坐标索引更新事件");
            // 触发事件通知订阅者更新坐标索引
            CoordinateIndicesNeedUpdate?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 获取当前是否处于初始化状态
        /// </summary>
        public bool IsInitializing => _isInitializing;

        // 选中的窗口标题
        public string SelectedWindowTitle
        {
            get => _selectedWindowTitle;
            set => SetProperty(ref _selectedWindowTitle, value);
        }

        // 选中的窗口句柄
        public IntPtr SelectedWindowHandle
        {
            get => _selectedWindowHandle;
            private set
            {
                if (_selectedWindowHandle != value)
                {
                    _selectedWindowHandle = value;
                    OnPropertyChanged();

                    // 触发窗口句柄变化事件
                    WindowHandleChanged?.Invoke(value);

                    // 同步到热键服务
                    _hotkeyService.TargetWindowHandle = value;

                     Logger.Debug($"窗口句柄已更新: {value}, 已同步到热键服务");
                }
            }
        }

        public string SelectedWindowProcessName
        {
            get => _selectedWindowProcessName;
            set => SetProperty(ref _selectedWindowProcessName, value);
        }

        public string SelectedWindowClassName
        {
            get => _selectedWindowClassName;
            set => SetProperty(ref _selectedWindowClassName, value);
        }

        // 更新选中的窗口句柄信息
        public async void UpdateSelectedWindow(IntPtr handle, string title, string className, string processName)
        {
            SelectedWindowHandle = handle;
            SelectedWindowClassName = className;
            SelectedWindowProcessName = processName;
            SelectedWindowTitle = $"{title} (句柄: {handle.ToInt64()})";

            // 同步句柄到 HotkeyService
            _hotkeyService.TargetWindowHandle = handle;

            // 使用统一配置服务保存窗口信息
            if (!_isInitializing)
            {
                ConfigManager.UpdateKeyConfig(keyConfig => {
                    keyConfig.TargetWindowClassName = className;
                    keyConfig.TargetWindowProcessName = processName;
                    keyConfig.TargetWindowTitle = title;
                });
                
                 Logger.Debug($"已更新窗口信息并保存到当前配置文件: {ConfigManager.CurrentConfig?.Name}");
            }

            // 启动定时检查
            StartWindowCheck();

             Logger.Info($"已选择窗口: {title}, 句柄: {handle.ToInt64()}, 类名: {className}, 进程名: {processName}");
        }

        // 清除选中的窗口句柄
        public void ClearSelectedWindow()
        {
            ExceptionHandler.Execute(() =>
            {
                // 停止窗口检查
                StopWindowCheck();

                // 清除窗口信息
                _selectedWindowHandle = IntPtr.Zero;
                _selectedWindowTitle = EMPTY_WINDOW_PLACEHOLDER;
                _selectedWindowClassName = string.Empty;
                _selectedWindowProcessName = string.Empty;

                // 更新热键服务的目标窗口
                if (_hotkeyService != null) _hotkeyService.TargetWindowHandle = IntPtr.Zero;

                // 通知UI更新
                OnPropertyChanged(nameof(SelectedWindowTitle));

                Logger.Debug("已清除窗口信息");

                // 使用统一配置服务保存窗口信息
                if (!_isInitializing)
                {
                    ConfigManager.UpdateKeyConfig(keyConfig => {
                        keyConfig.TargetWindowClassName = null;
                        keyConfig.TargetWindowProcessName = null;
                        keyConfig.TargetWindowTitle = null;
                    });

                    Logger.Debug($"已清除窗口信息并保存到当前配置文件: {ConfigManager.CurrentConfig?.Name}");
                }
            }, "清除窗口信息", showMessageBox: false);
        }

        // 按键列表
        public ObservableCollection<KeyItem> KeyList
        {
            get => _keyList;
            set => SetProperty(ref _keyList, value);
        }

        // 当前按键文本
        public string CurrentKeyText
        {
            get => _currentKeyText;
            set
            {
                _currentKeyText = value;
                OnPropertyChanged(nameof(CurrentKeyText));
            }
        }

        // 热键文本
        public string HotkeyText
        {
            get => _hotkeyText;
            set => SetProperty(ref _hotkeyText, value);
        }

        // 添加按键时间间隔属性
        public int KeyInterval
        {
            get => _keyInterval;
            set
            {
                if (SetProperty(ref _keyInterval, value))
                {
                    // 更新到驱动服务，让它保持与UI一致
                    _lyKeysService.KeyInterval = value;

                    // 配置将在LostFocus事件中进行保存(失去焦点时保存)
                     Logger.Debug($"默认按键间隔已更新为{value}ms");
                }
            }
        }
        
        // 添加按键按下时长属性
        public int KeyPressInterval
        {
            get => _keyPressInterval;
            set => SetProperty(ref _keyPressInterval, value);
        }

        // 添加按键命令
        public ICommand AddKeyCommand { get; }

        /// <summary>
        /// 添加坐标命令
        /// </summary>
        public ICommand AddCoordinateCommand { get; }

        /// <summary>
        /// 开始按键映射命令
        /// </summary>
        public ICommand StartKeyMappingCommand { get; }

        /// <summary>
        /// 停止按键映射命令
        /// </summary>
        public ICommand StopKeyMappingCommand { get; }

        // 按键模式选项
        public List<string> KeyModes { get; } = new List<string>
        {
            "顺序模式",
            "按压模式"
        };

        // 选中的按键模式
        public int SelectedKeyMode
        {
            get => _selectedKeyMode;
            set
            {
                if (SetProperty(ref _selectedKeyMode, value))
                {
                    // 如果正在执行，先停止当前循环
                    if (IsExecuting) StopKeyMapping();

                    IsSequenceMode = value == 0; // 0 表示顺序模式

                    // 恢复输入法
                    if (_lyKeysService != null) _lyKeysService.RestoreIME();
                    
                    // 在非初始化状态下保存到当前活动配置文件
                    if (!_isInitializing)
                    {
                        SaveConfig(); // 使用SaveConfig()方法保存到当前活动配置
                         Logger.Debug($"按键模式已切换为: {(value == 0 ? "顺序模式" : "按压模式")}并保存到当前配置");
                    }
                    else
                    {
                         Logger.Debug($"按键模式已切换为: {(value == 0 ? "顺序模式" : "按压模式")}");
                    }
                }
            }
        }

        // 热键是否启用
        public bool IsHotkeyEnabled
        {
            get => _isHotkeyEnabled;
            set
            {
                SetProperty(ref _isHotkeyEnabled, value);
                HotkeyStatus = value ? "按键已启动" : "按键已停止";
            }
        }

        // 热键总开关
        public bool IsHotkeyControlEnabled
        {
            get => _isHotkeyControlEnabled;
            set
            {
                if (SetProperty(ref _isHotkeyControlEnabled, value))
                {
                     Logger.Debug($"热键总开关已{(value ? "启用" : "禁用")}");

                    // 同步状态到HotkeyService
                    if (_hotkeyService != null) _hotkeyService.IsHotkeyControlEnabled = value;

                    // 更新浮窗状态
                    UpdateFloatingStatus();

                    // 如果禁用总开关，同时停止当前的按键映射
                    if (!value && IsExecuting) StopKeyMapping();

                    // 实时保存到全局配置
                    if (!_isInitializing)
                    {
                        // 热键总开关是全局设置，保存到GlobalConfig
                        SaveGlobalConfig();
                         Logger.Debug($"已将热键总开关状态({value})保存到全局配置");
                    }
                }
            }
        }

        // 按键状态
        public string HotkeyStatus
        {
            get => _hotkeyStatus;
            set => SetProperty(ref _hotkeyStatus, value);
        }

        // 是否为顺序模式
        public bool IsSequenceMode
        {
            get => _isSequenceMode;
            set
            {
                if (SetProperty(ref _isSequenceMode, value))
                {
                    // 当模式改变时更新驱动服务
                    _lyKeysService.IsHoldMode = !value;

                    // 更新HotkeyService的按键列表
                    var selectedKeys = KeyList.Where(k => k.IsSelected).ToList();
                    _hotkeyService.SetKeySequence(
                        selectedKeys.Select(k => new KeyItemSettings
                        {
                            KeyCode = k.KeyCode,
                            Interval = k.KeyInterval
                        }).ToList());

                    // 实时保存模式设置
                    if (!_isInitializing)
                    {
                        // 直接使用SaveConfig保存到当前活动配置
                        _selectedKeyMode = value ? 0 : 1; // 确保内部模式值也一致
                        SaveConfig();
                         Logger.Debug($"模式设置已保存到当前配置: keyMode={_selectedKeyMode}");
                    }

                     Logger.Debug($"模式切换 - 当前模式: {(value ? "顺序模式" : "按压模式")}, " +
                                  $"选中按键数: {selectedKeys.Count}, " +
                                  $"按键间隔: {KeyInterval}ms");
                }
            }
        }

        // 声音是否启用
        public bool IsSoundEnabled
        {
            get => _isSoundEnabled;
            set
            {
                if (SetProperty(ref _isSoundEnabled, value))
                    if (!_isInitializing) // 只在非初始化时保存
                        SaveConfig();
            }
        }

        // 音量属性
        public double SoundVolume
        {
            get => _soundVolume;
            set
            {
                if (SetProperty(ref _soundVolume, value))
                {
                    if (_audioService != null)
                    {
                        _audioService.Volume = value;
                         Logger.Debug($"已设置音量为: {value:P0}");
                        
                        // 非初始化状态下自动保存音量设置
                        if (!_isInitializing)
                        {
                            SaveSoundVolume(value);
                        }
                    }
                }
            }
        }

        // 单独保存音量设置的方法
        private async void SaveSoundVolume(double volume)
        {
            if (_isInitializing) return;

            ExceptionHandler.Execute(() =>
            {
                ConfigManager.UpdateGlobalConfig(config => {
                    config.SoundVolume = volume;
                });
                Logger.Debug($"音量设置已保存到全局配置: {volume:P0}");
            }, "保存音量设置", showMessageBox: false);
        }

        // 是否可以调整音量（基于音频设备是否可用）
        public bool CanAdjustVolume => _audioService?.AudioDeviceAvailable ?? false;

        // 判断是否为为降低卡位模式，为true时按下抬起间隔为5ms，为false时间隔为0ms
        public bool IsReduceKeyStuck
        {
            get => _isReduceKeyStuck;
            set
            {
                if (SetProperty(ref _isReduceKeyStuck, value))
                {
                    // 根据降低卡位模式设置按键间隔
                    var newInterval = value ? LyKeysService.DEFAULT_KEY_PRESS_INTERVAL : 0;
                    _lyKeysService.KeyPressInterval = newInterval;

                    if (!_isInitializing) SaveConfig();
                     Logger.Debug($"降低卡位模式已更改为: {value}, 期望按键间隔: {newInterval}ms, " +
                                  $"实际按键间隔: {_lyKeysService.KeyPressInterval}ms, 默认按键间隔值: {LyKeysService.DEFAULT_KEY_PRESS_INTERVAL}ms");
                }
            }
        }

        // 是否正在执行
        public bool IsExecuting
        {
            get => _isExecuting;
            private set
            {
                if (_isExecuting != value)
                {
                     Logger.Debug($"执行状态改变: {_isExecuting} -> {value}");
                    _isExecuting = value;
                    OnPropertyChanged(nameof(IsExecuting));
                    OnPropertyChanged(nameof(IsNotExecuting));
                    
                    // 确保状态变更能直接同步到浮窗ViewModel
                    try {
                        if (_floatingWindow != null)
                        {
                            // 反射获取DataContext
                            var type = _floatingWindow.GetType();
                            var propInfo = type.GetProperty("DataContext");
                            if (propInfo != null)
                            {
                                var dataContext = propInfo.GetValue(_floatingWindow);
                                if (dataContext is FloatingStatusViewModel viewModel)
                                {
                                    viewModel.IsExecuting = value;
                                     Logger.Debug($"已直接更新浮窗ViewModel状态: IsExecuting={value}");
                                }
                            }
                        }
                    } catch (Exception ex) {
                         Logger.Error("在执行状态变更时更新浮窗状态失败", ex);
                    }
                }
            }
        }

        // 是否未在执行（用于绑定）
        public bool IsNotExecuting => !IsExecuting;

        public bool IsFloatingWindowEnabled
        {
            get => _isFloatingWindowEnabled;
            set
            {
                if (SetProperty(ref _isFloatingWindowEnabled, value))
                {
                    if (!_isInitializing) SaveConfig();

                    if (value)
                        ShowFloatingWindow();
                    else
                        HideFloatingWindow();
                }
            }
        }

        /// <summary>
        /// 获取或设置是否自动切换到英文输入法
        /// </summary>
        public bool AutoSwitchToEnglishIME
        {
            get => _autoSwitchToEnglishIME;
            set
            {
                if (SetProperty(ref _autoSwitchToEnglishIME, value))
                    if (!_isInitializing)
                    {
                        SaveConfig();

                        // 通知LyKeysService更新输入法切换设置
                        _lyKeysService.SetAutoSwitchIME(value);
                    }
            }
        }

        private bool _enableHardwareAcceleration = true;
        
        /// <summary>
        /// 获取或设置是否启用硬件加速
        /// </summary>
        public bool EnableHardwareAcceleration
        {
            get => _enableHardwareAcceleration;
            set
            {
                if (SetProperty(ref _enableHardwareAcceleration, value))
                {
                    if (!_isInitializing)
                    {
                        SaveConfig();
                        Logger.Info($"硬件加速已{(value ? "启用" : "禁用")}，重启应用后生效");
                    }
                }
            }
        }

        // 选中的按键项
        public KeyItem? SelectedKeyItem
        {
            get => _selectedKeyItem;
            set => SetProperty(ref _selectedKeyItem, value);
        }

        public void SetMainWindow(MainWindow mainWindow)
        {
            if (mainWindow == null)
            {
                 Logger.Warning("传入的 MainWindow 为空");
                return;
            }

            _mainWindow = mainWindow;
             Logger.Debug("已设置 MainWindow 引用");

            // 延迟初始化浮窗，避免在主窗口显示时阻塞UI
            if (IsFloatingWindowEnabled)
            {
                // 使用延迟，避免在UI初始化阶段占用主线程
                Task.Delay(500).ContinueWith(_ => {
                    try 
                    {
                        // 在UI线程上执行但使用低优先级
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                            try 
                            {
                                if (_floatingWindow == null)
                                {
                                    InitializeFloatingWindow();
                                }
                            }
                            catch (Exception ex)
                            {
                                 Logger.Error("延迟初始化浮窗时发生错误", ex);
                            }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                    catch (Exception ex)
                    {
                         Logger.Error("延迟初始化浮窗任务失败", ex);
                    }
                });
            }
        }

        // 初始化浮窗的方法
        private void InitializeFloatingWindow()
        {
            ExceptionHandler.Execute(() =>
            {
                if (_mainWindow == null)
                {
                    Logger.Warning("初始化浮窗: MainWindow 引用为空");
                    return;
                }

                // 先创建 ViewModel
                _floatingViewModel = new FloatingStatusViewModel();

                // 初始化ViewModel属性
                _floatingViewModel.IsHotkeyControlEnabled = _isHotkeyControlEnabled;
                _floatingViewModel.IsExecuting = _isExecuting;

                // 创建浮窗
                _floatingWindow = new FloatingStatusWindow(_mainWindow);

                // 设置数据上下文
                var dataContext = _floatingViewModel;
                if (_floatingWindow != null)
                {
                    Logger.Debug("设置浮窗数据上下文");
                    // 使用反射设置DataContext避免类型问题
                    System.Type type = _floatingWindow.GetType();
                    System.Reflection.PropertyInfo propInfo = type.GetProperty("DataContext");
                    if (propInfo != null)
                    {
                        propInfo.SetValue(_floatingWindow, dataContext);
                        Logger.Debug("浮窗数据上下文设置成功");
                    }
                    else
                    {
                        Logger.Warning("未找到浮窗的DataContext属性");
                    }

                    // 使用反射调用Show方法
                    System.Reflection.MethodInfo showMethod = type.GetMethod("Show");
                    if (showMethod != null)
                    {
                        showMethod.Invoke(_floatingWindow, null);
                        UpdateFloatingStatusInternal();
                        Logger.Debug("浮窗已显示并更新状态");
                    }
                    else
                    {
                        Logger.Warning("未找到浮窗的Show方法");
                    }
                }
            }, "初始化浮窗", showMessageBox: false);
        }

        private void ShowFloatingWindow()
        {
            ExceptionHandler.Execute(() =>
            {
                if (_mainWindow == null)
                {
                    return;
                }

                // 通过异步方式初始化和显示浮窗
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                    ExceptionHandler.Execute(() =>
                    {
                        if (_floatingWindow == null)
                        {
                            InitializeFloatingWindow();
                        }
                        else
                        {
                            // 对于已存在的浮窗，使用反射调用Show方法
                            System.Type type = _floatingWindow.GetType();
                            System.Reflection.MethodInfo showMethod = type.GetMethod("Show");
                            if (showMethod != null)
                            {
                                showMethod.Invoke(_floatingWindow, null);
                                UpdateFloatingStatusInternal();
                                Logger.Debug("已有浮窗显示成功");
                            }
                        }
                    }, "显示浮窗", showMessageBox: false);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }, "创建或显示浮窗", showMessageBox: false);
        }

        private void HideFloatingWindow()
        {
            if (_floatingWindow != null)
            {
                ExceptionHandler.Execute(() =>
                {
                    // 使用反射调用Hide方法
                    System.Type type = _floatingWindow.GetType();
                    System.Reflection.MethodInfo hideMethod = type.GetMethod("Hide");
                    if (hideMethod != null)
                    {
                        hideMethod.Invoke(_floatingWindow, null);

                        // 使用反射获取DataContext属性
                        System.Reflection.PropertyInfo propInfo = type.GetProperty("DataContext");
                        if (propInfo != null)
                        {
                            var dataContext = propInfo.GetValue(_floatingWindow);
                            if (dataContext is FloatingStatusViewModel viewModel)
                            {
                                viewModel.StatusText = "已停止";
                            }
                        }

                        Logger.Debug("浮窗已隐藏");
                    }
                }, "隐藏浮窗", showMessageBox: false);
            }
        }

        private void UpdateFloatingStatus(bool forceUpdate = false)
        {
            ExceptionHandler.Execute(() =>
            {
                // 添加节流控制
                lock (_floatingStatusUpdateLock)
                {
                    var now = DateTime.Now;
                    // 如果不是强制更新且距离上次更新不足节流间隔，则跳过本次更新
                    if (!forceUpdate && (now - _lastFloatingStatusUpdateTime).TotalMilliseconds < FLOATING_STATUS_UPDATE_THROTTLE_MS)
                    {
                        Logger.Debug("浮窗状态更新被节流控制跳过");
                        return;
                    }

                    // 更新时间戳
                    _lastFloatingStatusUpdateTime = now;
                }

                Logger.Debug($"执行浮窗状态更新(forceUpdate={forceUpdate})");
                UpdateFloatingStatusInternal();
            }, "更新浮窗状态", showMessageBox: false);
        }

        private void UpdateFloatingStatusInternal()
        {
            if (_floatingWindow == null)
            {
                Logger.Debug("浮窗对象为null，无法更新状态");
                return;
            }

            ExceptionHandler.Execute(() =>
            {
                // 尝试直接获取和保存ViewModel引用，减少后续反射操作
                if (_floatingViewModel == null)
                {
                    // 使用反射获取DataContext属性
                    System.Type type = _floatingWindow.GetType();
                    System.Reflection.PropertyInfo propInfo = type.GetProperty("DataContext");
                    if (propInfo != null)
                    {
                        var dataContext = propInfo.GetValue(_floatingWindow);
                        if (dataContext is FloatingStatusViewModel viewModel)
                        {
                            _floatingViewModel = viewModel;
                            Logger.Debug("成功获取并缓存浮窗ViewModel引用");
                        }
                    }
                }

                // 如果已有缓存的ViewModel引用，直接使用
                if (_floatingViewModel != null)
                {
                    Logger.Debug($"更新浮窗前状态: IsHotkeyControlEnabled={_floatingViewModel.IsHotkeyControlEnabled}, IsExecuting={_floatingViewModel.IsExecuting}");

                    // 更新ViewModel状态
                    _floatingViewModel.IsHotkeyControlEnabled = _isHotkeyControlEnabled; // 同步热键总开关状态
                    _floatingViewModel.IsExecuting = _isExecuting; // 同步执行状态

                    Logger.Debug($"更新浮窗状态完成: 热键总开关={_isHotkeyControlEnabled}, 执行状态={_isExecuting}, 当前状态文本={_floatingViewModel.StatusText}");

                    ExceptionHandler.Execute(() =>
                    {
                        // 直接更新边框颜色，确保边框样式与状态同步
                        _floatingWindow.UpdateBorderStyle(_floatingViewModel.StatusText);
                        Logger.Debug($"已更新浮窗边框样式，状态文本: {_floatingViewModel.StatusText}");
                    }, "更新浮窗边框样式", showMessageBox: false);
                }
                // 如果没有缓存的ViewModel，则使用反射方式
                else
                {
                    // 使用反射获取DataContext属性
                    System.Type type = _floatingWindow.GetType();
                    System.Reflection.PropertyInfo propInfo = type.GetProperty("DataContext");
                    if (propInfo != null)
                    {
                        var dataContext = propInfo.GetValue(_floatingWindow);
                        if (dataContext is FloatingStatusViewModel viewModel)
                        {
                            Logger.Debug($"更新浮窗前状态: IsHotkeyControlEnabled={viewModel.IsHotkeyControlEnabled}, IsExecuting={viewModel.IsExecuting}");

                            // 更新ViewMode状态
                            viewModel.IsHotkeyControlEnabled = _isHotkeyControlEnabled; // 同步热键总开关状态
                            viewModel.IsExecuting = _isExecuting; // 同步执行状态

                            Logger.Debug($"更新浮窗状态完成: 热键总开关={_isHotkeyControlEnabled}, 执行状态={_isExecuting}, 当前状态文本={viewModel.StatusText}");

                            // 缓存ViewModel引用，减少后续反射操作
                            _floatingViewModel = viewModel;

                            ExceptionHandler.Execute(() =>
                            {
                                // 直接更新边框颜色，确保边框样式与状态同步
                                _floatingWindow.UpdateBorderStyle(viewModel.StatusText);
                                Logger.Debug($"已更新浮窗边框样式，状态文本: {viewModel.StatusText}");
                            }, "更新浮窗边框样式", showMessageBox: false);
                        }
                        else
                        {
                            Logger.Warning($"浮窗DataContext类型错误: {dataContext?.GetType().Name ?? "null"}");
                        }
                    }
                    else
                    {
                        Logger.Warning("无法获取浮窗DataContext属性");
                    }
                }
            }, "更新浮窗状态内部处理",
            customHandler: ex =>
            {
                // 尝试重新创建浮窗ViewModel以修复问题
                ExceptionHandler.Execute(() =>
                {
                    if (_floatingViewModel == null)
                    {
                        _floatingViewModel = new FloatingStatusViewModel();
                        _floatingViewModel.IsHotkeyControlEnabled = _isHotkeyControlEnabled;
                        _floatingViewModel.IsExecuting = _isExecuting;
                        Logger.Debug("已创建新的浮窗ViewModel实例");

                        // 尝试更新浮窗DataContext
                        var type = _floatingWindow.GetType();
                        var propInfo = type.GetProperty("DataContext");
                        if (propInfo != null)
                        {
                            propInfo.SetValue(_floatingWindow, _floatingViewModel);
                            Logger.Debug("已成功重置浮窗DataContext");
                        }
                    }
                }, "修复浮窗ViewModel", showMessageBox: false);
            },
            showMessageBox: false);
        }

        public bool IsTargetWindowActive
        {
            get => _isTargetWindowActive;
            private set
            {
                if (_isTargetWindowActive != value)
                {
                    _isTargetWindowActive = value;
                    OnPropertyChanged();

                    // 只在窗口变为非活动状态时停止按键映射
                    if (!value && IsExecuting)
                    {
                        _lyKeysService.EmergencyStop(); // 使用紧急停止
                        StopKeyMapping();
                        UpdateFloatingStatus(); // 更新浮窗状态
                         Logger.Debug("目标窗口切换为非活动状态，停止按键映射，已更新浮窗状态");
                    }
                    else if (value && IsExecuting)
                    {
                        // 如果窗口重新激活，且之前在执行中，更新浮窗状态
                        UpdateFloatingStatus();
                         Logger.Debug("目标窗口重新激活，更新浮窗状态");
                    }
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        public KeyMappingViewModel(LyKeysService lyKeysService,
            HotkeyService hotkeyService, MainViewModel mainViewModel, AudioService audioService)
        {
            _mainViewModel = mainViewModel;
            _lyKeysService = lyKeysService;
            _hotkeyService = hotkeyService;
            _audioService = audioService;

            // 初始化命令
            AddKeyCommand = CreateCommand(AddKey);
            AddCoordinateCommand = CreateCommand(AddCoordinate);
            StartKeyMappingCommand = CreateCommand(StartKeyMapping);
            StopKeyMappingCommand = CreateCommand(StopKeyMapping);

            Logger.Debug("开始初始化KeyMappingViewModel");

            try {
                
                // 初始化热键模式列表 - 属性已包含默认值，不需要重新赋值
                // KeyModes已在属性定义中初始化
                
                // 初始化按键列表
                KeyList = new ObservableCollection<KeyItem>();
                Logger.Debug("已创建KeyList集合");

                // 从AppConfig加载初始配置
                _currentKey = LyKeysCode.VK_ESCAPE; // 使用一个有效的LyKeysCode值

                // 播放声音服务
                if (_audioService != null)
                {
                    // 不直接调用不存在的方法，只设置音量
                    _audioService.Volume = SoundVolume;
                    // 不需要给只读属性赋值，它会通过get访问器自动获取值
                }

                // 注册配置变更事件
                ConfigManager.ConfigChanged += OnConfigChanged;

                // 加载配置
                LoadConfiguration();

                // 标记初始化完成
                _isInitializing = false;
                Logger.Debug($"KeyMappingViewModel初始化完成，按键列表数量: {KeyList.Count}");
            }
            catch (Exception ex)
            {
                Logger.Error("KeyMappingViewModel初始化失败", ex);
                _isInitializing = false;
            }
        }

        private void SyncConfigToServices()
        {
            try
            {
                // 如果正在初始化中，只同步基本参数，而不同步按键列表，避免重复更新
                if (!_isInitializing)
                {
                    // 只在非初始化状态下同步按键列表到服务
                    UpdateHotkeyServiceKeyList();
                }

                // 同步按键模式设置
                _lyKeysService.IsHoldMode = !_isSequenceMode;

                // 同步热键设置
                if (_hotkey.HasValue)
                {
                    try
                    {
                        _hotkeyService.RegisterHotkey(_hotkey.Value, _hotkeyModifiers, saveToConfig: false);
                         Logger.Debug($"初始化时注册热键成功: {_hotkey.Value}");
                    }
                    catch (Exception ex)
                    {
                         Logger.Error($"初始化时注册热键失败: {ex.Message}", ex);
                        // 不阻止初始化流程，只记录错误
                    }
                }

                // 同步其他设置
                _lyKeysService.KeyPressInterval = IsReduceKeyStuck ? LyKeysService.DEFAULT_KEY_PRESS_INTERVAL : 0;
                _lyKeysService.IsEnabled = IsHotkeyEnabled;
                _audioService.Volume = SoundVolume;
                _lyKeysService.KeyInterval = KeyInterval;
                _lyKeysService.IsHoldMode = !IsSequenceMode;

                // 同步热键总开关状态到服务
                if (_hotkeyService != null)
                {
                    _hotkeyService.IsHotkeyControlEnabled = IsHotkeyControlEnabled;
                     Logger.Debug($"已将热键总开关状态({IsHotkeyControlEnabled})同步到HotkeyService");
                }

                // 同步热键总开关状态到浮窗
                if (_floatingViewModel != null)
                {
                    _floatingViewModel.IsHotkeyControlEnabled = IsHotkeyControlEnabled;
                     Logger.Debug($"已将热键总开关状态({IsHotkeyControlEnabled})同步到浮窗");
                }
            }
            catch (Exception ex)
            {
                 Logger.Error("同步配置到服务失败", ex);
            }
        }

        private void LoadConfiguration()
        {
            _isInitializing = true;

            ExceptionHandler.Execute(() =>
            {
                // 加载全局配置
                LoadGlobalConfig(ConfigManager.GlobalConfig);

                // 加载按键配置
                LoadKeyConfig(ConfigManager.CurrentKeyConfig);

                // 查找并设置当前选中的配置文件（优先使用标记为默认的配置）
                var defaultConfig = ConfigManager.ConfigFiles.FirstOrDefault(c => c.IsDefault);
                if (defaultConfig != null)
                {
                    Logger.Debug($"找到默认配置：{defaultConfig.Name}，确保UI选择该配置");
                }
                else if (ConfigManager.ConfigFiles.Count > 0)
                {
                    defaultConfig = ConfigManager.ConfigFiles[0];
                    Logger.Debug($"未找到默认配置，使用第一个配置：{defaultConfig.Name}");
                }

                if (defaultConfig != null)
                {
                    _selectedConfigFile = defaultConfig;
                    Logger.Debug($"初始化阶段设置SelectedConfigFile为：{defaultConfig.Name}");
                }
                else
                {
                    Logger.Warning("未找到任何可用配置");
                }

                // 通知UI更新
                OnPropertyChanged(nameof(SelectedConfigFile));
                OnPropertyChanged(nameof(ConfigFiles));

                Logger.Debug("配置加载完成");
            }, "加载配置",
            customHandler: ex => SetDefaultConfiguration(),
            showMessageBox: false);

            _isInitializing = false;
        }

        private void SetDefaultConfiguration()
        {
            // 此函数用于在配置加载失败或不完整时设置ViewModel的默认值
            // 注意：这些默认值应当与AppConfigService.CreateDefaultConfig中相应配置保持一致
            
            // 基本配置
            IsSequenceMode = true;                // 默认顺序模式
            _selectedKeyMode = 0;                 // 0=顺序模式
            _keyInterval = 10;                     // 默认按键间隔
            
            // 功能开关
            IsSoundEnabled = true;                // 默认开启声音
            IsReduceKeyStuck = true;                    // 默认开启降低卡位模式
            AutoSwitchToEnglishIME = true;        // 默认开启自动切换输入法
            IsFloatingWindowEnabled = true;       // 默认开启浮窗
            IsHotkeyControlEnabled = true;        // 默认启用热键总开关
            
            // 设置音量
            _soundVolume = 0.8;                   // 默认音量80%
            if (_audioService != null)
                _audioService.Volume = _soundVolume;
            
            // 设置按键按下时长
            if (_lyKeysService != null)
                _lyKeysService.KeyPressInterval = IsReduceKeyStuck ? LyKeysService.DEFAULT_KEY_PRESS_INTERVAL : 0;
            
            // 重置当前状态
            IsHotkeyEnabled = false;              // 默认未启动按键
            HotkeyStatus = "初始化完成";           // 重置状态提示
            
             Logger.Debug("已应用SetDefaultConfiguration()函数中的默认配置");
        }

        private void InitializeHotkeyStatus()
        {
            IsHotkeyEnabled = false;
            HotkeyStatus = "初始化完成";
        }

        private void SubscribeToEvents()
        {
            // 订阅热键服务事件
            if (_hotkeyService != null)
            {
                _hotkeyService.SequenceModeStarted += () =>
                {
                    // 直接设置字段而非属性，避免触发额外的更新
                    _isExecuting = true;
                    _mainViewModel.UpdateStatusMessage("已开始按键序列", false);
                    
                    // 触发属性变更通知
                    OnPropertyChanged(nameof(IsExecuting));
                    OnPropertyChanged(nameof(IsNotExecuting));
                    
                    // 一次性更新浮窗状态
                    UpdateFloatingStatus();
                };
                _hotkeyService.SequenceModeStopped += () =>
                {
                    // 只需更新消息，避免重复操作
                    // IsExecuting和更新浮窗已在StopKeyMapping中集中处理
                    if (_isExecuting)
                    {
                         Logger.Debug("收到序列停止事件，但UI仍显示运行中状态，进行状态同步");
                        _mainViewModel.UpdateStatusMessage("已停止按键序列", false);
                        
                        // 确保UI状态一致性，即使StopKeyMapping已被调用，也需要强制更新一次状态
                        _isExecuting = false;
                        OnPropertyChanged(nameof(IsExecuting));
                        OnPropertyChanged(nameof(IsNotExecuting));
                        
                        // 强制更新浮窗状态，不受节流控制
                        UpdateFloatingStatus(true);
                         Logger.Debug("已强制更新浮窗状态，确保显示已停止");
                    }
                    else
                    {
                         Logger.Debug("收到序列停止事件，UI状态已是停止状态");
                        _mainViewModel.UpdateStatusMessage("已停止按键序列", false);
                    }
                };
            }

            // 订阅状态变化事件
            PropertyChanged += async (s, e) =>
            {
                if (e.PropertyName == nameof(IsHotkeyEnabled) && IsSoundEnabled)
                {
                    if (IsHotkeyEnabled)
                        _audioService.PlayStartSound();
                    else
                        _audioService.PlayStopSound();
                }
            };

            // 订阅按键间隔变化事件
            _lyKeysService.KeyIntervalChanged += (s, interval) => OnPropertyChanged(nameof(KeyInterval));

            // 订阅按键项事件
            SubscribeToKeyItemEvents();
        }

        // 设置当前按键
        public void SetCurrentKey(LyKeysCode keyCode)
        {
            _currentKey = keyCode;
            CurrentKeyText = _lyKeysService.GetKeyDescription(keyCode);
            // 通知命令状态更新
            CommandManager.InvalidateRequerySuggested();
             Logger.Debug("SetCurrentKey", $"设置当前按键: {keyCode} | {CurrentKeyText}");
        }

        // 设置热键
        public bool SetHotkey(LyKeysCode keyCode, ModifierKeys modifiers)
        {
            // 检查是否与当前按键序列冲突
            if (IsKeyInList(keyCode))
            {
                 Logger.Warning($"热键({keyCode})与当前按键序列冲突，无法设置");
                _mainViewModel.UpdateStatusMessage("热键与按键序列冲突，请选择其他键", true);
                return false;
            }

            // 更新内部状态
            _hotkey = keyCode;
            _hotkeyModifiers = modifiers;
            UpdateHotkeyText(keyCode, modifiers);

             Logger.Debug($"设置热键: {keyCode}, 修饰键: {modifiers}");

            // 直接让HotkeyService处理热键注册与保存配置
            try
            {
                _hotkeyService.RegisterHotkey(keyCode, modifiers, saveToConfig: true);
                 Logger.Debug("热键注册成功");
                _mainViewModel.UpdateStatusMessage("热键设置成功", false);
                return true;
            }
            catch (Exception ex)
            {
                 Logger.Error($"热键注册失败: {ex.Message}", ex);
                _mainViewModel.UpdateStatusMessage($"热键设置失败: {ex.Message}", true);
                return false;
            }
        }

        // 更新热键文本
        private void UpdateHotkeyText(LyKeysCode keyCode, ModifierKeys modifiers)
        {
            var sb = new StringBuilder();

            // 添加修饰键
            if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control) sb.Append("Ctrl + ");
            if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) sb.Append("Alt + ");
            if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) sb.Append("Shift + ");
            if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows) sb.Append("Win + ");

            // 添加主键
            sb.Append(_lyKeysService.GetKeyDescription(keyCode));

            HotkeyText = sb.ToString();
        }

        // 检查是否可以添加按键
        private bool CanAddKey()
        {
            // 移除了重复键检查(!IsKeyInList)，现在只检查有没有选中按键
            return _currentKey.HasValue;
        }

        // 添加按键
        private void AddKey()
        {
            try
            {
                if (!_currentKey.HasValue)
                {
                     Logger.Warning("没有有效的按键可添加");
                    _mainViewModel.UpdateStatusMessage("没有有效的按键可添加", true);
                    return;
                }

                var keyCode = _currentKey.Value;
                if (!_lyKeysService.IsValidLyKeysCode(keyCode))
                {
                     Logger.Warning($"无效的按键码: {_lyKeysService.GetKeyDescription(keyCode)}");
                    _mainViewModel.UpdateStatusMessage($"无效的按键码: {_lyKeysService.GetKeyDescription(keyCode)}", true);
                    return;
                }

                if (IsHotkeyConflict(keyCode))
                {
                     Logger.Warning($"按键与热键冲突: {_lyKeysService.GetKeyDescription(keyCode)}");
                    _mainViewModel.UpdateStatusMessage($"按键 {_lyKeysService.GetKeyDescription(keyCode)} 与热键冲突，请选择其他按键",
                        true);
                    return;
                }

                var newKey = new KeyItem(keyCode, _lyKeysService);
                newKey.KeyInterval = _keyInterval; // 使用当前默认间隔值
                newKey.SelectionChanged += (s, isSelected) => 
                {
                    SaveConfig();
                    UpdateHotkeyServiceKeyList();
                };

                KeyList.Add(newKey);
                
                // 添加：更新HotkeyService的按键列表，确保添加按键后立即更新循环
                UpdateHotkeyServiceKeyList();
                
                SaveConfig();

                // 重置输入状态
                _mainViewModel.UpdateStatusMessage($"已添加按键: {_lyKeysService.GetKeyDescription(keyCode)}", false);
                 Logger.Debug($"已添加按键: {keyCode}");
                _currentKey = null;
                // 通知UI显示已更新
                OnPropertyChanged(nameof(CurrentKeyText));
            }
            catch (Exception ex)
            {
                 Logger.Error("添加按键时发生异常", ex);
                _mainViewModel.UpdateStatusMessage($"添加按键失败: {ex.Message}", true);
            }
        }

        /// <summary>
        /// 删除指定的按键
        /// </summary>
        /// <param name="keyItem">要删除的按键项</param>
        public void DeleteKey(KeyItem keyItem)
        {
            if (keyItem == null)
                throw new ArgumentNullException(nameof(keyItem));

            try
            {
                // 从列表中移除
                KeyList.Remove(keyItem);
                
                // 如果删除的是坐标类型项，更新所有坐标索引
                if (keyItem.Type == KeyItemType.Coordinates)
                {
                    UpdateCoordinateIndices();
                }
                
                 Logger.Debug($"删除按键: {(keyItem.Type == KeyItemType.Keyboard ? keyItem.KeyCode.ToString() : $"坐标({keyItem.X},{keyItem.Y})")}");

                // 如果是当前选中的项，清除选中状态
                if (SelectedKeyItem == keyItem) SelectedKeyItem = null;

                // 更新HotkeyService的按键列表
                UpdateHotkeyServiceKeyList();

                // 实时保存按键列表
                if (!_isInitializing)
                {
                    SaveConfig();
                     Logger.Debug("配置已保存");
                }
            }
            catch (Exception ex)
            {
                 Logger.Error("删除按键时发生异常", ex);
                throw new InvalidOperationException("删除按键失败", ex);
            }
        }

        // 更新热键服务的按键列表
        private void UpdateHotkeyServiceKeyList()
        {
            try
            {
                if (_isInitializing)
                {
                     Logger.Debug("正在初始化中，跳过更新热键服务按键列表");
                    return;
                }
                
                // 获取选中的按键列表
                var selectedItems = KeyList.Where(k => k.IsSelected).ToList();
                
                // 如果没有选中的按键，则跳过更新
                if (!selectedItems.Any())
                {
                     Logger.Debug("没有选中的按键，跳过更新热键服务按键列表");
                    return;
                }
                
                // 创建操作列表
                var operations = new List<KeyItemSettings>();
                
                foreach (var item in selectedItems)
                {
                    if (item.Type == KeyItemType.Keyboard)
                    {
                        operations.Add(KeyItemSettings.CreateKeyboard(item.KeyCode, item.KeyInterval));
                    }
                    else if (item.Type == KeyItemType.Coordinates)
                    {
                        operations.Add(KeyItemSettings.CreateCoordinates(item.X, item.Y, item.KeyInterval));
                    }
                }
                
                // 设置统一操作列表到服务 - 一站式更新，避免多处重复调用
                _lyKeysService.SetOperationList(operations);

                // 只通知HotkeyService一次
                _hotkeyService.SetKeySequence(operations);

                 Logger.Debug($"已加载操作列表 - 总数: {operations.Count}, 键盘按键: {operations.Count(o => o.Type == KeyItemType.Keyboard)}, 坐标操作: {operations.Count(o => o.Type == KeyItemType.Coordinates)}");
            }
            catch (Exception ex)
            {
                 Logger.Error("更新热键服务按键列表失败", ex);
            }
        }

        // 保存配置
        public void SaveConfig()
        {
            if (_isInitializing) return;
            
            try
            {
                // 日志记录配置保存开始
                 Logger.Debug($"开始保存配置，当前活动配置: {ConfigManager.CurrentConfig?.Name}");
                
                // 保存按键相关配置到当前活动配置文件
                SaveKeyConfig();
                
                // 保存全局设置到GlobalConfig
                SaveGlobalConfig();
                
                 Logger.Debug($"配置保存完成");
            }
            catch (Exception ex)
            {
                 Logger.Error("保存配置失败", ex);
                System.Windows.MessageBox.Show($"保存配置失败: {ex.Message}", "错误", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        
        // 保存按键相关配置到当前活动配置文件
        private async void SaveKeyConfig()
        {
            if (_isInitializing) return;
            
            try
            {
                ConfigManager.UpdateKeyConfig(config => {
                    // 获取所有按键和它们的状态，根据类型创建不同的配置
                    var keyConfigs = new List<KeyConfig>();
                    
                    foreach (var item in KeyList)
                    {
                        KeyConfig itemConfig;
                        
                        if (item.Type == KeyItemType.Keyboard)
                        {
                            // 创建键盘按键配置
                            itemConfig = new KeyConfig(item.KeyCode, item.IsSelected)
                            {
                                KeyInterval = item.KeyInterval,
                                Type = KeyItemType.Keyboard,
                                X = null,  // 确保键盘类型的X和Y为null
                                Y = null
                            };
                        }
                        else // KeyItemType.Coordinates
                        {
                            // 确保坐标不能同时为0
                            int? x = item.X;
                            int? y = item.Y;
                            
                            if ((x ?? 0) == 0 && (y ?? 0) == 0)
                            {
                                 Logger.Warning($"修正无效的坐标配置: ({x}, {y}) => (1, 1)");
                                x = 1;
                                y = 1;
                            }
                            
                            // 创建坐标配置
                            itemConfig = new KeyConfig(x ?? 1, y ?? 1, item.IsSelected)
                            {
                                KeyInterval = item.KeyInterval,
                                Type = KeyItemType.Coordinates,
                                Code = null  // 确保坐标类型的Code为null
                            };
                        }
                        
                        keyConfigs.Add(itemConfig);
                    }
                    
                    // 更新按键配置
                    config.keys = keyConfigs;
                    
                    // 检查并更新热键配置
                    if (_hotkey.HasValue)
                    {
                        config.startKey = _hotkey;
                        config.startMods = _hotkeyModifiers;
                        config.stopKey = _hotkey;
                        config.stopMods = _hotkeyModifiers;
                    }
                    
                    // 检查并更新按键模式
                    config.keyMode = SelectedKeyMode;
                    
                    // 检查并更新按键间隔
                    config.interval = KeyInterval;
                    config.KeyPressInterval = KeyPressInterval;
                    
                    // 更新窗口句柄信息
                    bool hasWindowTarget = !string.IsNullOrEmpty(SelectedWindowClassName);
                    
                    if (hasWindowTarget)
                    {
                        config.TargetWindowClassName = SelectedWindowClassName;
                        config.TargetWindowProcessName = SelectedWindowProcessName;
                        config.TargetWindowTitle = SelectedWindowTitle;
                    }
                    else
                    {
                        config.TargetWindowClassName = null;
                        config.TargetWindowProcessName = null;
                        config.TargetWindowTitle = null;
                    }
                });
                
                 Logger.Debug("按键配置已保存");
            }
            catch (Exception ex)
            {
                 Logger.Error("保存按键配置失败", ex);
                throw;
            }
        }
        
        // 保存全局设置到GlobalConfig
        private async void SaveGlobalConfig()
        {
            if (_isInitializing) return;
            
            try
            {
                ConfigManager.UpdateGlobalConfig(globalConfig => {
                    globalConfig.soundEnabled = IsSoundEnabled;
                    globalConfig.IsReduceKeyStuck = IsReduceKeyStuck;
                    globalConfig.UI.FloatingWindow.IsEnabled = IsFloatingWindowEnabled;
                    globalConfig.AutoSwitchToEnglishIME = AutoSwitchToEnglishIME;
                    globalConfig.isHotkeyControlEnabled = IsHotkeyControlEnabled;
                    globalConfig.SoundVolume = SoundVolume;
                    globalConfig.EnableHardwareAcceleration = EnableHardwareAcceleration;
                });
                
                 Logger.Debug("全局配置已保存");
            }
            catch (Exception ex)
            {
                 Logger.Error("保存全局配置失败", ex);
                throw;
            }
        }

        // 检查两个按键配置列表是否相等
        private bool AreKeyConfigsEqual(List<KeyConfig> list1, List<KeyConfig> list2)
        {
            if (list1 == null || list2 == null)
                return list1 == list2;

            if (list1.Count != list2.Count)
                return false;

            for (var i = 0; i < list1.Count; i++)
                if (list1[i].Code != list2[i].Code ||
                    list1[i].IsSelected != list2[i].IsSelected ||
                    list1[i].KeyInterval != list2[i].KeyInterval ||
                    list1[i].Type != list2[i].Type ||
                    list1[i].X != list2[i].X ||
                    list1[i].Y != list2[i].Y)
                    return false;

            return true;
        }

        // 启动按键映射
        public void StartKeyMapping()
        {
            if (!IsExecuting)
                try
                {
                     Logger.Debug("开始启动按键映射");

                    // 检查是否选择了目标窗口
                    if (SelectedWindowHandle == IntPtr.Zero)
                    {
                        // 区分未选择窗口和进程未运行的情况
                        if (!string.IsNullOrEmpty(_selectedWindowProcessName) && SelectedWindowTitle.Contains("进程未运行"))
                        {
                             Logger.Warning($"目标窗口进程未运行: {_selectedWindowProcessName}");
                            _mainViewModel.UpdateStatusMessage($"目标应用程序未运行: {_selectedWindowProcessName}", true);
                        }
                        
                        IsHotkeyEnabled = false;
                        IsExecuting = false;
                        return;
                    }

                    _hotkeyService.TargetWindowHandle = SelectedWindowHandle;

                    // 选择的按键列表
                    var selectedKeys = KeyList.Where(k => k.IsSelected).ToList();
                    if (!selectedKeys.Any())
                    {
                         Logger.Warning("没有选择任何按键");
                        _mainViewModel.UpdateStatusMessage("请至少选择一个按键", true);
                        IsHotkeyEnabled = false;
                        IsExecuting = false;
                        return;
                    }

                    // 统一设置操作列表（只设置一次）
                    var operations = selectedKeys.Select(k => new KeyItemSettings
                    {
                        KeyCode = k.KeyCode,
                        Interval = k.KeyInterval,
                        Type = KeyItemType.Keyboard
                    }).ToList();

                    _lyKeysService.SetOperationList(operations);

                    // 设置按键模式并启动
                    _lyKeysService.IsHoldMode = !IsSequenceMode;
                    _hotkeyService.StartSequence();

                    // 更新执行状态
                    IsExecuting = true;
                    UpdateFloatingStatus();

                     Logger.Debug($"按键映射已启动 - 模式: {(SelectedKeyMode == 1 ? "按压模式" : "顺序模式")}, " +
                                  $"按键数量: {selectedKeys.Count}, 目标窗口: {SelectedWindowTitle}");
                }
                catch (Exception ex)
                {
                     Logger.Error("启动按键映射失败", ex);
                    StopKeyMapping();
                    _mainViewModel.UpdateStatusMessage("启动按键映射失败", true);
                }
        }

        // 停止按键映射
        public void StopKeyMapping()
        {
            try
            {
                if (_lyKeysService == null) return;

                 Logger.Debug($"开始停止按键映射，当前模式: {(SelectedKeyMode == 1 ? "按压模式" : "顺序模式")}, 当前执行状态: {_isExecuting}");

                // 标记状态变更，但暂不触发更新
                bool wasExecuting = IsExecuting;
                 Logger.Debug($"当前执行状态标记: wasExecuting={wasExecuting}");

                // 先更新UI状态变量，但不触发UI更新和事件
                _isExecuting = false;
                IsHotkeyEnabled = false;
                 Logger.Debug("UI状态变量已更新: _isExecuting=false, IsHotkeyEnabled=false");

                // 再停止热键服务
                 Logger.Debug("即将调用热键服务的StopSequence()方法");
                _hotkeyService?.StopSequence();
                 Logger.Debug("热键服务StopSequence()调用完成");

                // 然后停止驱动服务，但保留模式设置
                _lyKeysService.IsEnabled = false;
                 Logger.Debug("按键服务已禁用: _lyKeysService.IsEnabled=false");

                // 最后在已执行状态发生变化时一次性更新UI
                if (wasExecuting)
                {
                     Logger.Debug("检测到之前的执行状态为true，即将更新UI");
                    // 通知属性变更
                    OnPropertyChanged(nameof(IsExecuting));
                    OnPropertyChanged(nameof(IsNotExecuting));
                    
                    // 一次性更新浮窗状态 - 使用强制更新确保状态立即生效
                    UpdateFloatingStatus(true);
                     Logger.Debug("UI状态更新完成：属性变更通知已发送，浮窗状态已强制更新");
                }
                else
                {
                     Logger.Debug("之前的执行状态为false，跳过UI更新");
                }

                 Logger.Debug("按键映射已停止");
            }
            catch (Exception ex)
            {
                 Logger.Error("停止按键映射失败", ex);
                // 确保状态一致性
                _isExecuting = false;
                IsHotkeyEnabled = false;
                OnPropertyChanged(nameof(IsExecuting));
                OnPropertyChanged(nameof(IsNotExecuting));
                UpdateFloatingStatus(true); // 使用强制更新确保异常情况下也能更新状态
                 Logger.Debug("异常处理：已重置所有状态并强制更新UI");
            }
        }

        // 设置按压模式
        public void SetHoldMode(bool isHold)
        {
            _lyKeysService.IsHoldMode = isHold;
        }

        // 检查按键是否已在列表中
        private bool IsKeyInList(LyKeysCode keyCode)
        {
            // 仅检查键盘类型的按键
            return KeyList.Any(k => k.Type == KeyItemType.Keyboard && k.KeyCode.Equals(keyCode));
        }

        // 开始热键按下事件处理
        private void OnStartHotkeyPressed()
        {
             Logger.Debug("热键已按下");
            
            // 确保UI模式与服务模式保持一致
            bool isCurrentModeSequence = _isSequenceMode;
            bool isServiceModeHold = _lyKeysService.IsHoldMode;
            
            // 如果发现不一致，记录日志
            if (isCurrentModeSequence == isServiceModeHold)
            {
                 Logger.Warning($"模式状态不一致 - UI模式:{(isCurrentModeSequence ? "顺序" : "按压")}, 服务模式:{(isServiceModeHold ? "按压" : "顺序")}");
                // 使用服务模式作为最终依据
                _isSequenceMode = !isServiceModeHold;
            }
            
            if (_isSequenceMode)
            {
                if (_isExecuting)
                {
                    // 如果已在执行中，则停止
                     Logger.Debug("顺序模式 - 热键再次按下，停止序列");
                    StopKeyMapping();
                }
                else
                {
                    // 否则开始执行
                     Logger.Debug("顺序模式 - 热键按下，开始序列");
                    StartKeyMapping();
                }
            }
            else
            {
                // 按压模式下，按下热键开始执行
                 Logger.Debug("按压模式 - 热键按下，开始序列");
                StartKeyMapping();
            }
        }

        // 开始热键释放事件处理
        private void OnStartHotkeyReleased()
        {
             Logger.Debug("热键已释放");
            if (!_isSequenceMode)
            {
                // 在按压模式下，释放热键时停止执行
                 Logger.Debug("按压模式 - 热键释放，停止序列");
                StopKeyMapping();
            }
        }

        // 获取热键服务
        public HotkeyService GetHotkeyService()
        {
            return _hotkeyService;
        }

        // 为拖拽操作提供的公共方法，用于更新HotkeyService中的按键列表
        public void SyncKeyListToHotkeyService()
        {
            UpdateHotkeyServiceKeyList();
        }

        // 添加热键冲突检测方法
        public bool IsHotkeyConflict(LyKeysCode keyCode)
        {
            try
            {
                var isStartConflict = _hotkey.HasValue && keyCode.Equals(_hotkey.Value);

                if (isStartConflict)
                {
                     Logger.Debug(
                        $"检测到热键冲突 - 按键: {keyCode}, 启动键冲突: {isStartConflict}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                 Logger.Error("检查热键冲突时发生异常", ex);
                return false;
            }
        }

        // 为现有的KeyList项添加事件订阅
        private void SubscribeToKeyItemEvents()
        {
            foreach (var keyItem in KeyList)
                keyItem.SelectionChanged += (s, isSelected) =>
                {
                    SaveConfig();
                    UpdateHotkeyServiceKeyList();
                };
        }

        private void LoadWindowConfig()
        {
            try
            {
                 Logger.Debug($"开始加载窗口配置 - 进程名: {_selectedWindowProcessName}, 标题: {_selectedWindowTitle}");

                // 如果没有保存的窗口信息，直接返回
                if (string.IsNullOrEmpty(_selectedWindowProcessName))
                {
                     Logger.Debug("没有保存的窗口进程信息，跳过加载");
                    return;
                }

                var windows = FindWindowsByProcessName(_selectedWindowProcessName, _selectedWindowTitle);
                if (windows != null && windows.Count > 0)
                {
                    // 找到匹配的窗口，使用第一个匹配的窗口
                    var window = windows[0];
                    UpdateSelectedWindow(window.Handle, window.Title, window.ClassName, window.ProcessName);
                     Logger.Debug($"已找到并更新窗口信息 - 句柄: {window.Handle}, 标题: {window.Title}");
                }
                else
                {
                     Logger.Warning($"未找到进程 {_selectedWindowProcessName} 的窗口");

                    // 清除窗口句柄但保留进程信息
                    SelectedWindowHandle = IntPtr.Zero;
                    SelectedWindowTitle = $"{_selectedWindowTitle} (进程未运行)";

                    // 同步窗口句柄为空到热键服务
                    _hotkeyService.TargetWindowHandle = IntPtr.Zero;

                    // 启动定时检查
                    StartWindowCheck();
                     Logger.Debug($"已清除窗口句柄，启动定时检查 - 进程名: {_selectedWindowProcessName}, 标题: {_selectedWindowTitle} (进程未运行)");
                }
            }
            catch (Exception ex)
            {
                 Logger.Error("加载窗口配置时发生异常", ex);
                ClearSelectedWindow();
            }
        }

        private List<WindowInfo> FindWindowsByProcessName(string processName, string targetTitle = null)
        {
            var result = new List<WindowInfo>();
            if (string.IsNullOrEmpty(processName) && string.IsNullOrEmpty(targetTitle)) return result;

             Logger.Debug($"正在查找窗口 - 进程名: {processName}, 目标标题: {targetTitle}");

            try
            {
                var processes = Process.GetProcessesByName(processName);
                if (processes.Length == 0)
                {
                     Logger.Debug($"未找到进程: {processName}");
                    return result;
                }

                foreach (var process in processes)
                    try
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            var title = GetWindowTitle(process.MainWindowHandle);
                            var className = GetWindowClassName(process.MainWindowHandle);

                            // 如果指定了目标标题，则进行匹配
                            if (!string.IsNullOrEmpty(targetTitle))
                            {
                                if (title.Contains(targetTitle, StringComparison.OrdinalIgnoreCase))
                                {
                                    result.Add(new WindowInfo(process.MainWindowHandle, title, className,
                                        process.ProcessName));
                                     Logger.Debug($"找到匹配窗口 - 句柄: {process.MainWindowHandle}, 标题: {title}");
                                }
                            }
                            else
                            {
                                result.Add(new WindowInfo(process.MainWindowHandle, title, className,
                                    process.ProcessName));
                                 Logger.Debug($"找到窗口 - 句柄: {process.MainWindowHandle}, 标题: {title}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                         Logger.Error($"处理进程窗口时发生异常: {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
            }
            catch (Exception ex)
            {
                 Logger.Error($"查找窗口时发生异常: {ex.Message}");
            }

            if (result.Count == 0)  Logger.Debug($"未找到目标窗口 - 进程: {processName}, 目标标题: {targetTitle}");

            return result;
        }

        private string GetWindowTitle(IntPtr hWnd)
        {
            var title = new StringBuilder(256);
            GetWindowText(hWnd, title, title.Capacity);
            return title.ToString().Trim();
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private string GetWindowClassName(IntPtr hWnd)
        {
            var className = new StringBuilder(256);
            GetClassName(hWnd, className, className.Capacity);
            return className.ToString().Trim();
        }

        private void WindowCheckTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (string.IsNullOrEmpty(SelectedWindowProcessName) || string.IsNullOrEmpty(SelectedWindowTitle)) return;

            try
            {
                lock (_windowCheckLock)
                {
                    // 获取原始标题（移除状态信息）
                    var originalTitle = SelectedWindowTitle.Split(new[] { " (句柄:", " (进程未运行)", " (未找到匹配窗口)" },
                        StringSplitOptions.None)[0];

                    var windows = FindWindowsByProcessName(SelectedWindowProcessName, originalTitle);

                    // 添加对Application.Current的空检查
                    if (System.Windows.Application.Current != null)
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (windows.Any())
                            {
                                var targetWindow = windows.First();
                                var needsUpdate = false;

                                // 检查句柄是否变化
                                if (targetWindow.Handle != SelectedWindowHandle)
                                {
                                    SelectedWindowHandle = targetWindow.Handle;
                                    needsUpdate = true;
                                     Logger.Debug($"检测到窗口句柄变化: {targetWindow.Handle}");
                                }

                                // 检查类名是否变化
                                if (targetWindow.ClassName != SelectedWindowClassName)
                                {
                                    SelectedWindowClassName = targetWindow.ClassName;
                                    needsUpdate = true;
                                }

                                // 如果需要更新，则更新标题和配置
                                if (needsUpdate)
                                {
                                    SelectedWindowTitle = $"{targetWindow.Title} (句柄: {targetWindow.Handle.ToInt64()})";

                                    // 更新配置
                                    ConfigManager.UpdateKeyConfig(keyConfig => {
                                        keyConfig.TargetWindowClassName = targetWindow.ClassName;
                                        keyConfig.TargetWindowProcessName = targetWindow.ProcessName;
                                        keyConfig.TargetWindowTitle = targetWindow.Title;
                                    });

                                     Logger.Info(
                                        $"已更新窗口信息 - 句柄: {targetWindow.Handle.ToInt64()}, 类名: {targetWindow.ClassName}, 进程名: {targetWindow.ProcessName}, 标题: {targetWindow.Title}");
                                }
                            }
                            else if (SelectedWindowHandle != IntPtr.Zero)
                            {
                                // 目标进程已关闭，清除窗口句柄但保留进程信息
                                SelectedWindowHandle = IntPtr.Zero;
                                SelectedWindowTitle = $"{originalTitle} (进程未运行)";
                                
                                // 同步窗口句柄到热键服务
                                _hotkeyService.TargetWindowHandle = IntPtr.Zero;
                                
                                // 如果正在执行，停止按键映射
                                if (IsExecuting)
                                {
                                    StopKeyMapping();
                                    _mainViewModel.UpdateStatusMessage($"目标应用程序已关闭，已停止按键映射", false);
                                }
                                
                                 Logger.Warning($"进程 {SelectedWindowProcessName} 已关闭，已清除窗口句柄");
                            }
                        });
                    else
                        // 直接在当前线程处理窗口更新
                         Logger.Debug("Application.Current为空，跳过窗口状态更新");
                }
            }
            catch (Exception ex)
            {
                 Logger.Error("检查窗口状态时发生异常", ex);
            }
        }

        private void StartWindowCheck()
        {
            if (_windowCheckTimer == null)
            {
                _windowCheckTimer = new System.Timers.Timer(5000); // 5秒
                _windowCheckTimer.Elapsed += WindowCheckTimer_Elapsed;
            }

            _windowCheckTimer.Start();
             Logger.Debug("开始定时检查窗口状态");
        }

        private void StopWindowCheck()
        {
            _windowCheckTimer?.Stop();
             Logger.Debug("停止定时检查窗口状态");
        }

        // 在析构函数或Dispose方法中清理定时器
        ~KeyMappingViewModel()
        {
            _windowCheckTimer?.Dispose();
            _activeWindowCheckTimer?.Dispose();
            
            // 解除配置变更事件订阅
            if (ConfigManager != null)
            {
                ConfigManager.ConfigChanged -= OnConfigChanged;
            }
        }

        // 添加活动窗口检查方法
        private void ActiveWindowCheckTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            try
            {
                // 如果没有选择窗口，则不需要检查活动状态
                if (SelectedWindowHandle == IntPtr.Zero)
                {
                    // 添加对Application.Current的空检查
                    if (System.Windows.Application.Current != null)
                    {
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            // 更新为非活动状态
                            if (IsTargetWindowActive)
                            {
                                IsTargetWindowActive = false;
                                _hotkeyService.IsTargetWindowActive = false;
                                
                                // 如果正在执行，则停止
                                if (IsExecuting)
                                {
                                    _lyKeysService.EmergencyStop();
                                    StopKeyMapping();
                                    // 不显示消息，因为窗口状态检查器会处理
                                }
                            }
                        }), DispatcherPriority.Background);
                    }
                    else
                    {
                        // 直接在当前线程更新状态
                        if (IsTargetWindowActive)
                        {
                            IsTargetWindowActive = false;
                            _hotkeyService.IsTargetWindowActive = false;
                            
                            // 如果正在执行，则停止
                            if (IsExecuting)
                            {
                                _lyKeysService.EmergencyStop();
                                StopKeyMapping();
                            }
                        }
                    }

                    return;
                }

                // 如果选择了窗口，则检查是否是当前活动窗口
                var activeWindow = GetForegroundWindow();
                var isActive = activeWindow == SelectedWindowHandle;

                // 添加对Application.Current的空检查
                if (System.Windows.Application.Current != null)
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        IsTargetWindowActive = isActive;
                        _hotkeyService.IsTargetWindowActive = isActive;
                        
                        // 只在窗口变为非活动状态时停止按键映射
                        if (!isActive && IsExecuting)
                        {
                            _lyKeysService.EmergencyStop(); // 使用紧急停止
                            StopKeyMapping();
                            UpdateFloatingStatus(); // 更新浮窗状态
                             Logger.Debug("目标窗口切换为非活动状态，停止按键映射，已更新浮窗状态");
                        }
                        else if (isActive && IsExecuting)
                        {
                            // 如果窗口重新激活，且之前在执行中，更新浮窗状态
                            UpdateFloatingStatus();
                             Logger.Debug("目标窗口重新激活，更新浮窗状态");
                        }
                    }), DispatcherPriority.Background);
                }
                else
                {
                    // 直接在当前线程更新状态
                    IsTargetWindowActive = isActive;
                    _hotkeyService.IsTargetWindowActive = isActive;
                    
                    // 只在窗口变为非活动状态时停止按键映射
                    if (!isActive && IsExecuting)
                    {
                        _lyKeysService.EmergencyStop(); // 使用紧急停止
                        StopKeyMapping();
                         Logger.Debug("目标窗口切换为非活动状态，停止按键映射");
                    }
                }
            }
            catch (Exception ex)
            {
                 Logger.Error("检查活动窗口状态时发生异常", ex);
            }
        }

        private class WindowInfo
        {
            public IntPtr Handle { get; set; }
            public string Title { get; set; }
            public string ClassName { get; set; }
            public string ProcessName { get; set; }

            public WindowInfo(IntPtr handle, string title, string className, string processName)
            {
                Handle = handle;
                Title = title;
                ClassName = className;
                ProcessName = processName;
            }
        }

        private bool CanAddCoordinate()
        {
            // 检查X和Y坐标是否已设置
            if (!_currentX.HasValue || !_currentY.HasValue)
                return false;
                
            // 检查坐标是否为(0,0)
            // 注意：这里是添加坐标按钮，所以一定是坐标类型
            if (_currentX.Value == 0 && _currentY.Value == 0)
                return false;
                
            return true;
        }
        
        // 添加坐标点
        private void AddCoordinate()
        {
            try
            {
                // 验证输入
                if (!_currentX.HasValue || !_currentY.HasValue)
                {
                     Logger.Warning("X或Y坐标没有填写，无法添加坐标");
                    _mainViewModel.UpdateStatusMessage("请填写X和Y坐标", true);
                    return;
                }

                // 确保坐标不能同时为0
                int x = _currentX.Value;
                int y = _currentY.Value;
                
                // 坐标类型的坐标不能同时为(0,0)
                if (x == 0 && y == 0)
                {
                     Logger.Warning("坐标不能同时为(0,0)，添加操作被拒绝");
                    _mainViewModel.UpdateStatusMessage("坐标不能同时为(0,0)，请修改后再添加", true);
                    return; // 直接返回，不添加坐标
                }

                // 创建坐标类型的KeyItem
                var newCoordinate = new KeyItem(x, y, _lyKeysService);
                newCoordinate.KeyInterval = _keyInterval; // 使用当前默认间隔值
                
                // 添加事件订阅
                newCoordinate.SelectionChanged += (s, isSelected) =>
                {
                    SaveConfig();
                    UpdateHotkeyServiceKeyList();
                };
                newCoordinate.KeyIntervalChanged += (s, newInterval) =>
                {
                    if (!_isInitializing)
                    {
                        SaveConfig();
                        UpdateHotkeyServiceKeyList();
                         Logger.Debug($"坐标[{newCoordinate.X},{newCoordinate.Y}]的间隔已更新为{newInterval}ms");
                    }
                };
                
                // 添加到列表
                KeyList.Add(newCoordinate);
                
                // 更新坐标索引
                UpdateCoordinateIndices();
                
                // 更新HotkeyService的按键列表
                UpdateHotkeyServiceKeyList();
                
                // 保存配置
                SaveConfig();
                
                // 提示和日志
                _mainViewModel.UpdateStatusMessage($"已添加坐标: ({x}, {y})", false);
                 Logger.Debug($"已添加坐标: ({x}, {y})");
                
                // 重置输入状态
                _currentX = null;
                _currentY = null;
                OnPropertyChanged(nameof(CurrentX));
                OnPropertyChanged(nameof(CurrentY));
            }
            catch (Exception ex)
            {
                 Logger.Error("添加坐标时发生异常", ex);
                _mainViewModel.UpdateStatusMessage($"添加坐标失败: {ex.Message}", true);
            }
        }

        // X坐标
        public int? CurrentX
        {
            get => _currentX;
            set => SetProperty(ref _currentX, value);
        }
        
        // Y坐标
        public int? CurrentY
        {
            get => _currentY;
            set => SetProperty(ref _currentY, value);
        }

        // 添加这个新方法，用于更新所有坐标类型项目的索引
        private void UpdateCoordinateIndices()
        {
            try
            {
                // 筛选出所有坐标类型的项目
                var coordinateItems = _keyList.Where(item => item.Type == KeyItemType.Coordinates).ToList();
                
                // 为所有坐标设置索引
                for (int i = 0; i < coordinateItems.Count; i++)
                {
                    coordinateItems[i].CoordinateIndex = i;
                     Logger.Debug($"设置坐标索引: 项目={i}, 坐标=({coordinateItems[i].X},{coordinateItems[i].Y})");
                }
                
                // 通知视图更新坐标索引
                CoordinateIndicesNeedUpdate?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                 Logger.Error("更新坐标索引时发生异常", ex);
            }
        }

        // 配置文件管理相关属性
        public ObservableCollection<ConfigFileInfo> ConfigFiles => ConfigManager.ConfigFiles;
        
        private ConfigFileInfo _selectedConfigFile;
        public ConfigFileInfo SelectedConfigFile
        {
            get => _selectedConfigFile;
            set
            {
                if (value == null)
                {
                    return;
                }
                
                if (SetProperty(ref _selectedConfigFile, value))
                {
                     Logger.Debug($"SelectedConfigFile已设置：{value.Name}");
                    
                    // 在初始化阶段不执行切换，避免覆盖已加载的配置
                    if (!_isInitializing)
                    {
                        SwitchToConfig(value);
                    }
                    else
                    {
                         Logger.Debug($"初始化阶段，跳过配置切换操作");
                    }
                }
            }
        }
        
        // 切换到指定配置
        private void SwitchToConfig(ConfigFileInfo configInfo)
        {
            SwitchToConfig(configInfo, true);
        }

        // 切换到指定配置的重载方法，允许控制是否保存当前配置
        private async void SwitchToConfig(ConfigFileInfo configInfo, bool saveCurrentConfig)
        {
            try
            {
                if (configInfo == null) return;
                
                 Logger.Debug($"切换到配置：{configInfo.Name}, 是否保存当前配置: {saveCurrentConfig}");
                
                // 根据参数决定是否保存当前配置
                if (saveCurrentConfig)
                {
                SaveConfig();
                }
                else
                {
                     Logger.Debug("跳过保存当前配置");
                }
                
                // 使用统一配置服务切换配置
                ConfigManager.SwitchConfig(configInfo);
                
                // 更新UI
                _mainViewModel.UpdateStatusMessage($"已切换到配置：{configInfo.Name}", false);
            }
            catch (Exception ex)
            {
                 Logger.Error($"切换配置失败: {configInfo?.Name}", ex);
                _mainViewModel.UpdateStatusMessage($"切换配置失败: {ex.Message}", true);
            }
        }
        
        // 创建新配置
        public void CreateNewConfig(string configName, bool copyFromCurrent)
        {
            ExceptionHandler.Execute(() =>
            {
                var newConfig = ConfigManager.CreateNewConfig(configName, copyFromCurrent);
                SelectedConfigFile = newConfig;
                _mainViewModel.UpdateStatusMessage($"已创建新配置：{newConfig.Name}", false);
            }, $"创建新配置: {configName}",
            customHandler: ex => _mainViewModel.UpdateStatusMessage($"创建新配置失败: {ex.Message}", true));
        }
        
        // 重命名配置
        public async void RenameConfig(string newName)
        {
            try
            {
                if (SelectedConfigFile == null)
                {
                     Logger.Warning("重命名配置失败：未选择配置文件");
                    _mainViewModel.UpdateStatusMessage("重命名配置失败：未选择配置文件", true);
                    return;
                }
                
                string oldName = SelectedConfigFile.Name;
                 Logger.Debug($"开始重命名配置：{oldName} -> {newName}");
                
                // 使用统一配置服务重命名配置
                ConfigManager.RenameConfig(SelectedConfigFile, newName);
                
                 Logger.Debug($"重命名配置成功：{oldName} -> {SelectedConfigFile.Name}");
                _mainViewModel.UpdateStatusMessage($"已重命名配置：{oldName} -> {SelectedConfigFile.Name}", false);
            }
            catch (Exception ex)
            {
                 Logger.Error($"重命名配置失败: {newName}", ex);
                _mainViewModel.UpdateStatusMessage($"重命名配置失败: {ex.Message}", true);
                throw; // 向上抛出异常，以便UI层处理
            }
        }
        
        // 删除配置
        public async void DeleteConfig()
        {
            try
            {
                if (SelectedConfigFile == null) return;
                
                // 检查是否为默认配置
                if (SelectedConfigFile.IsDefault)
                {
                    _mainViewModel.UpdateStatusMessage("无法删除默认配置", true);
                    return;
                }
                
                // 记录当前配置名称
                var name = SelectedConfigFile.Name;
                
                 Logger.Debug($"开始删除配置：{name}");
                
                // 保存当前需要删除的配置引用
                var configToDelete = SelectedConfigFile;
                
                // 预先获取将要切换到的新配置（默认配置或第一个配置）
                var newConfig = ConfigManager.ConfigFiles.FirstOrDefault(c => c != configToDelete && c.IsDefault) ?? 
                                ConfigManager.ConfigFiles.FirstOrDefault(c => c != configToDelete);
                        
                if (newConfig == null)
                {
                     Logger.Warning("没有可用的备选配置，无法删除当前配置");
                    _mainViewModel.UpdateStatusMessage("没有备选配置，无法删除", true);
                    return;
                }
                
                // 先切换到新配置，避免在删除操作中触发切换
                // 注意：这里使用特殊的重载以避免保存当前配置
                SwitchToConfig(newConfig, false);
                
                // 执行删除操作
                ConfigManager.DeleteConfig(configToDelete);
                
                // 刷新UI选中项，确保显示当前活动配置
                _selectedConfigFile = ConfigManager.CurrentConfig;
                OnPropertyChanged(nameof(SelectedConfigFile));
                OnPropertyChanged(nameof(ConfigFiles));
                
                // 提示用户
                _mainViewModel.UpdateStatusMessage($"已删除配置：{name}", false);
            }
            catch (Exception ex)
            {
                 Logger.Error("删除配置失败", ex);
                _mainViewModel.UpdateStatusMessage($"删除配置失败: {ex.Message}", true);
                
                // 刷新UI状态，确保与后端状态一致
                _selectedConfigFile = ConfigManager.CurrentConfig;
                OnPropertyChanged(nameof(SelectedConfigFile));
                OnPropertyChanged(nameof(ConfigFiles));
            }
        }
        
        // 设置配置快捷键
        public async void SetConfigHotkey(string hotkeyText)
        {
            try
            {
                if (SelectedConfigFile == null) return;
                
                ConfigManager.SetConfigHotkey(SelectedConfigFile, hotkeyText);
                _mainViewModel.UpdateStatusMessage($"已设置配置快捷键", false);
            }
            catch (Exception ex)
            {
                 Logger.Error("设置配置快捷键失败", ex);
                _mainViewModel.UpdateStatusMessage($"设置配置快捷键失败: {ex.Message}", true);
            }
        }
        
        // 清除配置快捷键
        public async void ClearConfigHotkey()
        {
            try
            {
                if (SelectedConfigFile == null) return;
                
                ConfigManager.SetConfigHotkey(SelectedConfigFile, string.Empty);
                _mainViewModel.UpdateStatusMessage("已清除配置快捷键", false);
            }
            catch (Exception ex)
            {
                 Logger.Error("清除配置快捷键失败", ex);
                _mainViewModel.UpdateStatusMessage($"清除配置快捷键失败: {ex.Message}", true);
            }
        }
        
        // 导入配置文件
        public async void ImportKeyConfig(string sourceFile)
        {
            try
            {
                var configName = Path.GetFileNameWithoutExtension(sourceFile);
                var newConfig = ConfigManager.ImportKeyConfig(sourceFile, configName);
                SelectedConfigFile = newConfig;
                _mainViewModel.UpdateStatusMessage($"已导入配置：{newConfig.Name}", false);
            }
            catch (Exception ex)
            {
                 Logger.Error($"导入配置失败: {sourceFile}", ex);
                _mainViewModel.UpdateStatusMessage($"导入配置失败: {ex.Message}", true);
            }
        }
        
        // 导出配置文件
        public async void ExportKeyConfig(string targetFile)
        {
            try
            {
                if (SelectedConfigFile == null) return;
                
                ConfigManager.ExportKeyConfig(targetFile, SelectedConfigFile);
                _mainViewModel.UpdateStatusMessage($"已导出配置到：{targetFile}", false);
            }
            catch (Exception ex)
            {
                 Logger.Error($"导出配置失败: {targetFile}", ex);
                _mainViewModel.UpdateStatusMessage($"导出配置失败: {ex.Message}", true);
            }
        }
        
        // 配置变更事件处理
        private void OnConfigChanged(object sender, ConfigEventArgs e)
        {
            try
            {
                 Logger.Debug($"接收到配置变更事件: {e.ChangeType}");

                if (e.ChangeType == ConfigChangeType.Global)
                {
                    // 加载全局配置
                    var config = e.GlobalConfigData;
                    if (config != null)
                        LoadGlobalConfig(config);
                }
                else if (e.ChangeType == ConfigChangeType.Key)
                {
                    // 加载按键配置
                    var config = e.KeyConfigData;
                    if (config != null)
                        LoadKeyConfig(config);
                }
                else if (e.ChangeType == ConfigChangeType.ConfigFile)
                {
                    // 配置文件切换
                    OnConfigFileChanged(e.ConfigFile);
                }
                else if (e.ChangeType == ConfigChangeType.ConfigList)
                {
                    // 配置文件列表变更
                    OnConfigListChanged();
                }
                else if (e.ChangeType == ConfigChangeType.All)
                {
                    // 所有配置变更
                    var globalConfig = e.GlobalConfigData;
                    var keyConfig = e.KeyConfigData;
                    
                    if (globalConfig != null) 
                        LoadGlobalConfig(globalConfig);
                        
                    if (keyConfig != null) 
                        LoadKeyConfig(keyConfig);
                }
            }
            catch (Exception ex)
            {
                 Logger.Error("处理配置变更事件失败", ex);
            }
        }
        
        // 加载全局配置
        private void LoadGlobalConfig(GlobalConfig globalConfig)
        {
            _isInitializing = true;
            try
            {
                _isSoundEnabled = globalConfig.soundEnabled ?? true;
                _isReduceKeyStuck = globalConfig.IsReduceKeyStuck ?? true;
                _isFloatingWindowEnabled = globalConfig.UI.FloatingWindow.IsEnabled;
                _autoSwitchToEnglishIME = globalConfig.AutoSwitchToEnglishIME ?? true;
                _isHotkeyControlEnabled = globalConfig.isHotkeyControlEnabled ?? true;
                _soundVolume = globalConfig.SoundVolume ?? 0.8;
                _enableHardwareAcceleration = globalConfig.EnableHardwareAcceleration ?? true;
                
                // 通知UI更新这些属性
                OnPropertyChanged(nameof(IsSoundEnabled));
                OnPropertyChanged(nameof(IsReduceKeyStuck));
                OnPropertyChanged(nameof(IsFloatingWindowEnabled));
                OnPropertyChanged(nameof(AutoSwitchToEnglishIME));
                OnPropertyChanged(nameof(IsHotkeyControlEnabled));
                OnPropertyChanged(nameof(SoundVolume));
                OnPropertyChanged(nameof(EnableHardwareAcceleration));
                
                // 同步设置到服务
                SyncConfigToServices();
                
                 Logger.Debug("已从GlobalConfig加载全局配置");
            }
            catch (Exception ex)
            {
                 Logger.Error("加载全局配置失败", ex);
            }
            finally
            {
                _isInitializing = false;
            }
        }
        
        // 加载按键配置
        private void LoadKeyConfig(KeyConfigData keyConfig)
        {
            _isInitializing = true;
            try
            {
                if (keyConfig == null)
                {
                     Logger.Warning("传入的KeyConfigData为null");
                    return;
                }
                
                // 加载热键设置
                if (keyConfig.startKey.HasValue)
                {
                    _hotkey = keyConfig.startKey;
                    _hotkeyModifiers = keyConfig.startMods;
                    UpdateHotkeyText(_hotkey.Value, keyConfig.startMods);
                    
                    // 注册热键（不保存到配置，因为是从配置加载）
                    try
                    {
                        _hotkeyService.RegisterHotkey(_hotkey.Value, _hotkeyModifiers, saveToConfig: false);
                         Logger.Debug($"从配置加载热键成功: {_hotkey.Value}");
                    }
                    catch (Exception ex)
                    {
                         Logger.Error($"从配置加载热键失败: {ex.Message}", ex);
                        // 不阻止配置加载流程，只记录错误
                    }
                }
                
                // 加载按键模式
                SelectedKeyMode = keyConfig.keyMode;
                IsSequenceMode = keyConfig.keyMode == 0;
                
                // 加载按键间隔
                KeyInterval = keyConfig.interval;
                KeyPressInterval = keyConfig.KeyPressInterval ?? 5;
                
                // 加载窗口信息
                if (!string.IsNullOrEmpty(keyConfig.TargetWindowClassName))
                {
                    _selectedWindowClassName = keyConfig.TargetWindowClassName;
                    _selectedWindowTitle = keyConfig.TargetWindowTitle ?? "";
                    _selectedWindowProcessName = keyConfig.TargetWindowProcessName ?? "";
                    
                    // 尝试加载窗口
                    LoadWindowConfig();
                }
                else
                {
                    ClearSelectedWindow();
                }
                
                // 加载按键列表
                LoadKeyList(keyConfig);
                
                 Logger.Debug("按键配置加载完成");
            }
            catch (Exception ex)
            {
                 Logger.Error("加载按键配置失败", ex);
            }
            finally
            {
                _isInitializing = false;
            }
        }
        
        // 加载按键列表
        private void LoadKeyList(KeyConfigData keyConfig)
        {
            try
            {
                KeyList.Clear();
                if (keyConfig.keys != null && keyConfig.keys.Count > 0)
                {
                    foreach (var key in keyConfig.keys)
                    {
                        KeyItem item = null;
                        
                        if (key.Type == KeyItemType.Keyboard && key.Code.HasValue)
                        {
                            item = new KeyItem(key.Code.Value, _lyKeysService)
                            {
                                IsSelected = key.IsSelected,
                                KeyInterval = key.KeyInterval
                            };
                        }
                        else if (key.Type == KeyItemType.Coordinates && key.X.HasValue && key.Y.HasValue)
                        {
                            item = new KeyItem(key.X.Value, key.Y.Value, _lyKeysService)
                            {
                                IsSelected = key.IsSelected,
                                KeyInterval = key.KeyInterval
                            };
                        }
                        
                        if (item != null)
                        {
                            // 添加事件处理
                            item.SelectionChanged += (s, isSelected) => 
                            {
                                SaveConfig();
                                UpdateHotkeyServiceKeyList();
                            };
                            
                            item.KeyIntervalChanged += (s, newInterval) => 
                            {
                                if (!_isInitializing)
                                {
                                    SaveConfig();
                                }
                            };
                            
                            KeyList.Add(item);
                        }
                    }
                    
                    // 更新坐标索引
                    UpdateCoordinateIndices();
                    
                     Logger.Debug($"已加载按键列表，总数: {KeyList.Count}");
                }
            }
            catch (Exception ex)
            {
                 Logger.Error("加载按键列表失败", ex);
            }
        }
        
        // 配置文件变更处理
        private void OnConfigFileChanged(ConfigFileInfo configInfo)
        {
            if (configInfo != null)
            {
                 Logger.Debug($"收到配置文件变更事件：{configInfo.Name}");
                
                // 更新引用
                _selectedConfigFile = configInfo;
                OnPropertyChanged(nameof(SelectedConfigFile));
                
                // 立即使用统一配置服务的当前按键配置
                LoadKeyConfig(ConfigManager.CurrentKeyConfig);
            }
        }
        
        // 配置列表变更处理
        private void OnConfigListChanged()
        {
            OnPropertyChanged(nameof(ConfigFiles));
        }

        // 添加TempConfigHotkey属性，用于临时存储配置热键
        private string _tempConfigHotkey;
        public string TempConfigHotkey
        {
            get => _tempConfigHotkey;
            set => SetProperty(ref _tempConfigHotkey, value);
        }
    }
}