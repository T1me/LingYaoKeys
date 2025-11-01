using System.Windows.Input;
using WpfApp.Services.Models;
using WpfApp.Services.Utils;
using WpfApp.Services.Core;
using System.Text;
using System.Collections.ObjectModel;
using System.Windows;
using WpfApp.Views;
using Application = System.Windows.Application;

namespace WpfApp.ViewModels
{
    /// <summary>
    /// 按键映射视图模型 - 重构后的精简版本
    /// 职责: 协调各个服务,处理UI绑定和用户命令
    /// </summary>
    public class KeyMappingViewModel : ViewModelBase
    {
        // 服务依赖
        private readonly LyKeysService _lyKeysService;
        private readonly HotkeyService _hotkeyService;
        private readonly AudioService _audioService;
        private readonly MainViewModel _mainViewModel;
        private readonly WindowManagementService _windowService;
        private readonly FloatingWindowService _floatingService;
        private readonly CoordinateManagementService _coordinateService;
        private readonly KeyListManagementService _keyListService;

        // UI 绑定属性
        private VirtualKeyCode? _currentKey;
        private string _currentKeyText = string.Empty;
        private ObservableCollection<KeyItem> _keyList;
        private string _hotkeyText = string.Empty;
        private VirtualKeyCode? _hotkey;
        private ModifierKeys _hotkeyModifiers = ModifierKeys.None;
        private int _selectedKeyMode;
        private bool _isSequenceMode = true;
        private bool _isSoundEnabled = true;
        private double _soundVolume = 0.8;
        private bool _isReduceKeyStuck = true;
        private bool _isExecuting = false;
        private bool _autoSwitchToEnglishIME = true;
        private bool _isHotkeyControlEnabled = true;
        private int _keyInterval = 10;
        private int _keyPressInterval = 5;
        private int? _currentX;
        private int? _currentY;
        private KeyItem? _selectedKeyItem;
        private bool _enableHardwareAcceleration = true;

        // 常量
        public const string EMPTY_WINDOW_PLACEHOLDER = "空";

        #region 属性

        public LyKeysService LyKeysService => _lyKeysService;

        public ObservableCollection<KeyItem> KeyList
        {
            get => _keyList;
            set => SetProperty(ref _keyList, value);
        }

        public string CurrentKeyText
        {
            get => _currentKeyText;
            set => SetProperty(ref _currentKeyText, value);
        }

        public string HotkeyText
        {
            get => _hotkeyText;
            set => SetProperty(ref _hotkeyText, value);
        }

        public int KeyInterval
        {
            get => _keyInterval;
            set
            {
                if (SetProperty(ref _keyInterval, value))
                {
                    _lyKeysService.KeyInterval = value;
                    Logger.Debug($"默认按键间隔已更新为{value}ms");
                }
            }
        }

        public int KeyPressInterval
        {
            get => _keyPressInterval;
            set => SetProperty(ref _keyPressInterval, value);
        }

        public List<string> KeyModes { get; } = new List<string> { "单次模式", "按压模式" };

        public int SelectedKeyMode
        {
            get => _selectedKeyMode;
            set
            {
                if (SetProperty(ref _selectedKeyMode, value))
                {
                    if (IsExecuting) StopKeyMapping();
                    IsSequenceMode = value == 0;
                    SaveConfig();
                    Logger.Debug($"按键模式已切换为: {(value == 0 ? "单次模式" : "按压模式")}");
                }
            }
        }

        public bool IsSequenceMode
        {
            get => _isSequenceMode;
            set
            {
                if (SetProperty(ref _isSequenceMode, value))
                {
                    _lyKeysService.IsHoldMode = !value;
                    _keyListService.SyncToHotkeyService(KeyList);
                }
            }
        }

        public bool IsSoundEnabled
        {
            get => _isSoundEnabled;
            set
            {
                if (SetProperty(ref _isSoundEnabled, value))
                {
                    OnPropertyChanged(nameof(CanAdjustVolume));
                    SaveConfig();
                }
            }
        }

        public bool CanAdjustVolume => IsSoundEnabled;

        public double SoundVolume
        {
            get => _soundVolume;
            set
            {
                if (SetProperty(ref _soundVolume, value))
                {
                    _audioService.Volume = value;
                    SaveGlobalConfig();
                }
            }
        }

        public bool IsReduceKeyStuck
        {
            get => _isReduceKeyStuck;
            set
            {
                if (SetProperty(ref _isReduceKeyStuck, value))
                {
                    _lyKeysService.KeyPressInterval = value ? LyKeysService.DEFAULT_KEY_PRESS_INTERVAL : 0;
                    SaveConfig();
                }
            }
        }

        public bool IsExecuting
        {
            get => _isExecuting;
            private set
            {
                if (SetProperty(ref _isExecuting, value))
                {
                    OnPropertyChanged(nameof(IsNotExecuting));
                    _floatingService.IsExecuting = value;
                }
            }
        }

        public bool IsNotExecuting => !IsExecuting;

        public bool IsFloatingWindowEnabled
        {
            get => _floatingService.IsEnabled;
            set
            {
                if (_floatingService.IsEnabled != value)
                {
                    _floatingService.IsEnabled = value;
                    OnPropertyChanged();
                    SaveConfig();
                }
            }
        }

        public bool AutoSwitchToEnglishIME
        {
            get => _autoSwitchToEnglishIME;
            set
            {
                if (SetProperty(ref _autoSwitchToEnglishIME, value))
                    SaveConfig();
            }
        }

        public bool IsHotkeyControlEnabled
        {
            get => _isHotkeyControlEnabled;
            set
            {
                if (SetProperty(ref _isHotkeyControlEnabled, value))
                {
                    _hotkeyService.IsHotkeyControlEnabled = value;
                    _floatingService.IsHotkeyControlEnabled = value;
                    if (!value && IsExecuting) StopKeyMapping();
                    SaveGlobalConfig();
                }
            }
        }

        public bool EnableHardwareAcceleration
        {
            get => _enableHardwareAcceleration;
            set
            {
                if (SetProperty(ref _enableHardwareAcceleration, value))
                {
                    SaveConfig();
                    Logger.Info($"硬件加速已{(value ? "启用" : "禁用")}，重启应用后生效");
                }
            }
        }

        public double FloatingWindowOpacity
        {
            get => _floatingService.Opacity;
            set
            {
                if (Math.Abs(_floatingService.Opacity - value) > 0.01)
                {
                    _floatingService.Opacity = value;
                    OnPropertyChanged();
                    SaveConfig();
                }
            }
        }

        public KeyItem? SelectedKeyItem
        {
            get => _selectedKeyItem;
            set => SetProperty(ref _selectedKeyItem, value);
        }

        public int? CurrentX
        {
            get => _currentX;
            set => SetProperty(ref _currentX, value);
        }

        public int? CurrentY
        {
            get => _currentY;
            set => SetProperty(ref _currentY, value);
        }

        // 窗口相关属性(委托给 WindowManagementService)
        public string SelectedWindowTitle => _windowService.SelectedWindowTitle;
        public IntPtr SelectedWindowHandle => _windowService.SelectedWindowHandle;
        public string SelectedWindowClassName => _windowService.SelectedWindowClassName;
        public string SelectedWindowProcessName => _windowService.SelectedWindowProcessName;
        public bool IsTargetWindowActive => _windowService.IsTargetWindowActive;

        #endregion

        #region 命令

        public ICommand AddKeyCommand { get; }
        public ICommand AddCoordinateCommand { get; }
        public ICommand StartKeyMappingCommand { get; }
        public ICommand StopKeyMappingCommand { get; }
        public ICommand DeleteKeyCommand { get; }
        public ICommand ClearWindowHandleCommand { get; }

        #endregion

        #region 事件

        public event Action<IntPtr>? WindowHandleChanged;
        public event EventHandler? CoordinateIndicesNeedUpdate;

        #endregion

        public KeyMappingViewModel(
            LyKeysService lyKeysService,
            HotkeyService hotkeyService,
            MainViewModel mainViewModel,
            AudioService audioService)
        {
            _lyKeysService = lyKeysService ?? throw new ArgumentNullException(nameof(lyKeysService));
            _hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
            _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
            _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));

            // 创建服务实例
            _windowService = new WindowManagementService(_hotkeyService);
            _floatingService = new FloatingWindowService();
            _coordinateService = new CoordinateManagementService();
            _keyListService = new KeyListManagementService(_lyKeysService, _hotkeyService, _coordinateService);

            // 初始化
            KeyList = new ObservableCollection<KeyItem>();
            AddKeyCommand = CreateCommand(AddKey, CanAddKey);
            AddCoordinateCommand = CreateCommand(AddCoordinate, CanAddCoordinate);
            StartKeyMappingCommand = CreateCommand(StartKeyMapping);
            StopKeyMappingCommand = CreateCommand(StopKeyMapping);
            DeleteKeyCommand = CreateCommand<KeyItem>(DeleteKey);
            ClearWindowHandleCommand = CreateCommand(ClearSelectedWindow);

            // 订阅事件
            SubscribeToEvents();

            // 加载配置
            LoadConfiguration();

        }

        #region 初始化和配置

        private void SubscribeToEvents()
        {
            // 热键服务事件
            _hotkeyService.SequenceModeStarted += () =>
            {
                IsExecuting = true;
                _mainViewModel.UpdateStatusMessage("已开始按键序列", false);
            };

            _hotkeyService.SequenceModeStopped += () =>
            {
                IsExecuting = false;
                _mainViewModel.UpdateStatusMessage("已停止按键序列", false);
            };

            // 窗口服务事件
            _windowService.WindowHandleChanged += handle =>
            {
                OnPropertyChanged(nameof(SelectedWindowHandle));
                WindowHandleChanged?.Invoke(handle);
            };

            _windowService.WindowInfoChanged += (handle, title, className, processName) =>
            {
                OnPropertyChanged(nameof(SelectedWindowTitle));
                OnPropertyChanged(nameof(SelectedWindowClassName));
                OnPropertyChanged(nameof(SelectedWindowProcessName));
            };

            _windowService.TargetWindowActiveChanged += isActive =>
            {
                OnPropertyChanged(nameof(IsTargetWindowActive));
                if (!isActive && IsExecuting)
                {
                    StopKeyMapping();
                }
            };

            // 坐标服务事件
            _coordinateService.CoordinateIndicesUpdated += (s, e) =>
            {
                CoordinateIndicesNeedUpdate?.Invoke(this, EventArgs.Empty);
            };

            // 按键列表服务事件
            _keyListService.KeyListChanged += (s, e) =>
            {
                SaveConfig();
                _keyListService.SyncToHotkeyService(KeyList);
            };

            // 配置变更事件
            ConfigManager.ConfigChanged += OnConfigChanged;

            // 音频服务
            _audioService.Volume = SoundVolume;
        }

        private void LoadConfiguration()
        {
            ExceptionHandler.Execute(() =>
            {
                LoadGlobalConfig(ConfigManager.GlobalConfig);
                LoadKeyConfig(ConfigManager.CurrentKeyConfig);
                Logger.Debug("全局配置和按键配置加载完成");
            }, "加载配置", showMessageBox: false);
        }

        private void LoadGlobalConfig(GlobalConfig globalConfig)
        {
            _isSoundEnabled = globalConfig.soundEnabled ?? true;
            _isReduceKeyStuck = globalConfig.IsReduceKeyStuck ?? true;
            _floatingService.IsEnabled = globalConfig.UI.FloatingWindow.IsEnabled;
            _floatingService.Opacity = globalConfig.UI.FloatingWindow.Opacity;
            _autoSwitchToEnglishIME = globalConfig.AutoSwitchToEnglishIME ?? true;
            _isHotkeyControlEnabled = globalConfig.isHotkeyControlEnabled ?? true;
            _soundVolume = globalConfig.SoundVolume ?? 0.8;
            _enableHardwareAcceleration = globalConfig.EnableHardwareAcceleration ?? true;

            // 通知UI更新
            OnPropertyChanged(nameof(IsSoundEnabled));
            OnPropertyChanged(nameof(IsReduceKeyStuck));
            OnPropertyChanged(nameof(IsFloatingWindowEnabled));
            OnPropertyChanged(nameof(FloatingWindowOpacity));
            OnPropertyChanged(nameof(AutoSwitchToEnglishIME));
            OnPropertyChanged(nameof(IsHotkeyControlEnabled));
            OnPropertyChanged(nameof(SoundVolume));
            OnPropertyChanged(nameof(EnableHardwareAcceleration));

            // 同步到服务
            _audioService.Volume = _soundVolume;
            _hotkeyService.IsHotkeyControlEnabled = _isHotkeyControlEnabled;
            _floatingService.IsHotkeyControlEnabled = _isHotkeyControlEnabled;

            Logger.Debug("已加载全局配置");
        }

        private void LoadKeyConfig(KeyConfigData keyConfig)
        {
            if (keyConfig == null) return;

            // 加载热键
            if (keyConfig.startKey.HasValue)
            {
                _hotkey = keyConfig.startKey;
                _hotkeyModifiers = keyConfig.startMods;
                UpdateHotkeyText(_hotkey.Value, keyConfig.startMods);

                try
                {
                    _hotkeyService.RegisterHotkey(_hotkey.Value, _hotkeyModifiers, saveToConfig: false);
                }
                catch (Exception ex)
                {
                    Logger.Error($"加载热键失败: {ex.Message}", ex);
                }
            }

            // 加载按键模式和间隔
            SelectedKeyMode = keyConfig.keyMode;
            KeyInterval = keyConfig.interval;
            KeyPressInterval = keyConfig.KeyPressInterval ?? 5;

            // 加载窗口信息
            if (!string.IsNullOrEmpty(keyConfig.TargetWindowClassName))
            {
                _windowService.LoadWindowFromConfig(
                    keyConfig.TargetWindowProcessName ?? "",
                    keyConfig.TargetWindowTitle ?? "");
            }

            // 加载按键列表
            _keyListService.LoadFromConfig(keyConfig.keys, KeyList);

        }

        private void OnConfigChanged(object sender, ConfigEventArgs e)
        {
            try
            {
                if (e.ChangeType == ConfigChangeType.Global && e.GlobalConfigData != null)
                {
                    LoadGlobalConfig(e.GlobalConfigData);
                }
                else if (e.ChangeType == ConfigChangeType.Key && e.KeyConfigData != null)
                {
                    LoadKeyConfig(e.KeyConfigData);
                }
                else if (e.ChangeType == ConfigChangeType.All)
                {
                    if (e.GlobalConfigData != null) LoadGlobalConfig(e.GlobalConfigData);
                    if (e.KeyConfigData != null) LoadKeyConfig(e.KeyConfigData);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("处理配置变更事件失败", ex);
            }
        }

        #endregion

        #region 按键管理

        public void SetCurrentKey(VirtualKeyCode keyCode)
        {
            _currentKey = keyCode;
            CurrentKeyText = _lyKeysService.GetKeyDescription(keyCode);
            CommandManager.InvalidateRequerySuggested();
            Logger.Debug($"设置当前按键: {keyCode}");
        }

        private bool CanAddKey() => _currentKey.HasValue;

        private void AddKey()
        {
            try
            {
                if (!_currentKey.HasValue)
                {
                    _mainViewModel.UpdateStatusMessage("没有有效的按键可添加", true);
                    return;
                }

                _keyListService.AddKeyboardKey(_currentKey.Value, _keyInterval, KeyList, _hotkey);

                _mainViewModel.UpdateStatusMessage($"已添加按键: {_lyKeysService.GetKeyDescription(_currentKey.Value)}", false);
                _currentKey = null;
                OnPropertyChanged(nameof(CurrentKeyText));
            }
            catch (Exception ex)
            {
                Logger.Error("添加按键失败", ex);
                _mainViewModel.UpdateStatusMessage($"添加按键失败: {ex.Message}", true);
            }
        }

        private bool CanAddCoordinate()
        {
            return _coordinateService.ValidateCoordinate(_currentX, _currentY, out _);
        }

        private void AddCoordinate()
        {
            try
            {
                if (!_coordinateService.ValidateCoordinate(_currentX, _currentY, out var errorMessage))
                {
                    _mainViewModel.UpdateStatusMessage(errorMessage, true);
                    return;
                }

                _keyListService.AddCoordinate(_currentX!.Value, _currentY!.Value, _keyInterval, KeyList);

                _mainViewModel.UpdateStatusMessage($"已添加坐标: ({_currentX}, {_currentY})", false);
                _currentX = null;
                _currentY = null;
                OnPropertyChanged(nameof(CurrentX));
                OnPropertyChanged(nameof(CurrentY));
            }
            catch (Exception ex)
            {
                Logger.Error("添加坐标失败", ex);
                _mainViewModel.UpdateStatusMessage($"添加坐标失败: {ex.Message}", true);
            }
        }

        public void DeleteKey(KeyItem keyItem)
        {
            try
            {
                _keyListService.DeleteKey(keyItem, KeyList);
                if (SelectedKeyItem == keyItem) SelectedKeyItem = null;
            }
            catch (Exception ex)
            {
                Logger.Error("删除按键失败", ex);
                throw;
            }
        }

        #endregion

        #region 热键管理

        public bool SetHotkey(VirtualKeyCode keyCode, ModifierKeys modifiers)
        {
            // 检查冲突
            if (KeyList.Any(k => k.Type == KeyItemType.Keyboard && k.KeyCode.Equals(keyCode)))
            {
                _mainViewModel.UpdateStatusMessage("热键与按键序列冲突", true);
                return false;
            }

            _hotkey = keyCode;
            _hotkeyModifiers = modifiers;
            UpdateHotkeyText(keyCode, modifiers);

            try
            {
                _hotkeyService.RegisterHotkey(keyCode, modifiers, saveToConfig: true);
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

        private void UpdateHotkeyText(VirtualKeyCode keyCode, ModifierKeys modifiers)
        {
            var sb = new StringBuilder();
            if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control) sb.Append("Ctrl + ");
            if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) sb.Append("Alt + ");
            if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) sb.Append("Shift + ");
            if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows) sb.Append("Win + ");
            sb.Append(_lyKeysService.GetKeyDescription(keyCode));
            HotkeyText = sb.ToString();
        }

        public bool IsHotkeyConflict(VirtualKeyCode keyCode)
        {
            return _hotkey.HasValue && keyCode.Equals(_hotkey.Value);
        }

        #endregion

        #region 执行控制

        public void StartKeyMapping()
        {
            if (IsExecuting) return;

            try
            {
                if (SelectedWindowHandle == IntPtr.Zero)
                {
                    _mainViewModel.UpdateStatusMessage("请先选择目标窗口", true);
                    return;
                }

                var selectedKeys = KeyList.Where(k => k.IsSelected).ToList();
                if (!selectedKeys.Any())
                {
                    _mainViewModel.UpdateStatusMessage("请至少选择一个按键", true);
                    return;
                }

                _lyKeysService.IsHoldMode = !IsSequenceMode;
                _hotkeyService.StartSequence();
                IsExecuting = true;

                Logger.Debug($"按键映射已启动 - 模式: {(IsSequenceMode ? "单次" : "按压")}, 按键数: {selectedKeys.Count}");
            }
            catch (Exception ex)
            {
                Logger.Error("启动按键映射失败", ex);
                StopKeyMapping();
                _mainViewModel.UpdateStatusMessage("启动失败", true);
            }
        }

        public void StopKeyMapping()
        {
            try
            {
                _hotkeyService?.StopSequence();
                IsExecuting = false;
                Logger.Debug("按键映射已停止");
            }
            catch (Exception ex)
            {
                Logger.Error("停止按键映射失败", ex);
            }
        }

        #endregion

        #region 窗口管理

        public void UpdateSelectedWindow(IntPtr handle, string title, string className, string processName)
        {
            _windowService.UpdateSelectedWindow(handle, title, className, processName);
            OnPropertyChanged(nameof(SelectedWindowTitle));
            OnPropertyChanged(nameof(SelectedWindowHandle));
            OnPropertyChanged(nameof(SelectedWindowClassName));
            OnPropertyChanged(nameof(SelectedWindowProcessName));
            SaveConfig();
        }

        public void ClearSelectedWindow()
        {
            _windowService.ClearSelectedWindow();
            OnPropertyChanged(nameof(SelectedWindowTitle));
            OnPropertyChanged(nameof(SelectedWindowHandle));
            SaveConfig();
        }

        #endregion

        #region 浮窗管理

        public void SetMainWindow(MainWindow mainWindow)
        {
            if (mainWindow == null) return;

            Task.Delay(500).ContinueWith(_ =>
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        _floatingService.Initialize(mainWindow);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("初始化浮窗失败", ex);
                    }
                }));
            });
        }

        public void TriggerCoordinateIndicesUpdate()
        {
            CoordinateIndicesNeedUpdate?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region 配置保存

        public void SaveConfig()
        {
            try
            {
                SaveKeyConfig();
                SaveGlobalConfig();
                Logger.Debug("配置保存完成");
            }
            catch (Exception ex)
            {
                Logger.Error("保存配置失败", ex);
            }
        }

        private void SaveKeyConfig()
        {
            ConfigManager.UpdateKeyConfig(config =>
            {
                config.keys = _keyListService.ToConfigFormat(KeyList);

                if (_hotkey.HasValue)
                {
                    config.startKey = _hotkey;
                    config.startMods = _hotkeyModifiers;
                    config.stopKey = _hotkey;
                    config.stopMods = _hotkeyModifiers;
                }

                config.keyMode = SelectedKeyMode;
                config.interval = KeyInterval;
                config.KeyPressInterval = KeyPressInterval;

                if (!string.IsNullOrEmpty(SelectedWindowClassName))
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
        }

        private void SaveGlobalConfig()
        {
            ConfigManager.UpdateGlobalConfig(config =>
            {
                config.soundEnabled = IsSoundEnabled;
                config.IsReduceKeyStuck = IsReduceKeyStuck;
                config.UI.FloatingWindow.IsEnabled = IsFloatingWindowEnabled;
                config.UI.FloatingWindow.Opacity = FloatingWindowOpacity;
                config.AutoSwitchToEnglishIME = AutoSwitchToEnglishIME;
                config.isHotkeyControlEnabled = IsHotkeyControlEnabled;
                config.SoundVolume = SoundVolume;
                config.EnableHardwareAcceleration = EnableHardwareAcceleration;
            });
        }

        #endregion

        #region 辅助方法

        public HotkeyService GetHotkeyService() => _hotkeyService;

        public void SyncKeyListToHotkeyService()
        {
            _keyListService.SyncToHotkeyService(KeyList);
        }

        public void SetHoldMode(bool isHold)
        {
            _lyKeysService.IsHoldMode = isHold;
        }

        // 兼容性属性
        public string HotkeyStatus => IsExecuting ? "运行中" : "已停止";

        #endregion

        #region 清理

        ~KeyMappingViewModel()
        {
            _windowService?.Dispose();
            _floatingService?.Dispose();
            ConfigManager.ConfigChanged -= OnConfigChanged;
        }

        #endregion
    }
}
