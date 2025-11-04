using System.Windows.Input;
using WpfApp.Services.Models;
using WpfApp.Services.Utils;
using WpfApp.Services.Core;
using System.Collections.ObjectModel;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace WpfApp.ViewModels
{
    /// <summary>
    /// 按键映射视图模型 - 多配置架构
    /// 职责: 管理多个按键配置，协调配置服务和UI交互
    /// </summary>
    public class KeyMappingViewModel : ViewModelBase
    {
        // 服务依赖
        private readonly LyKeysService _lyKeysService;
        private readonly HotkeyService _hotkeyService;
        private readonly AudioService _audioService;
        private readonly MainViewModel _mainViewModel;
        private readonly KeyConfigurationService _configService;
        private readonly FloatingWindowService _floatingService;

        // UI 绑定属性
        private ObservableCollection<KeyConfigurationItemViewModel> _configurations;
        private KeyConfigurationItemViewModel? _selectedConfiguration;
        private bool _isExecuting = false;
        private bool _isHotkeyControlEnabled = true;
        private bool _enableHardwareAcceleration = true;
        private bool _isLoadingConfig = false;

        #region 属性

        /// <summary>
        /// 配置列表
        /// </summary>
        public ObservableCollection<KeyConfigurationItemViewModel> Configurations
        {
            get => _configurations;
            set => SetProperty(ref _configurations, value);
        }

        /// <summary>
        /// 当前选中的配置
        /// </summary>
        public KeyConfigurationItemViewModel? SelectedConfiguration
        {
            get => _selectedConfiguration;
            set
            {
                if (SetProperty(ref _selectedConfiguration, value))
                {
                    OnPropertyChanged(nameof(HasSelectedConfiguration));
                }
            }
        }

        /// <summary>
        /// 是否有选中的配置
        /// </summary>
        public bool HasSelectedConfiguration => SelectedConfiguration != null;

        /// <summary>
        /// 是否正在执行
        /// </summary>
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

        /// <summary>
        /// 是否未执行
        /// </summary>
        public bool IsNotExecuting => !IsExecuting;

        /// <summary>
        /// 是否启用浮窗
        /// </summary>
        public bool IsFloatingWindowEnabled
        {
            get => _floatingService.IsEnabled;
            set
            {
                if (_floatingService.IsEnabled != value)
                {
                    _floatingService.IsEnabled = value;
                    OnPropertyChanged();
                    SaveGlobalConfig();
                }
            }
        }

        /// <summary>
        /// 浮窗透明度
        /// </summary>
        public double FloatingWindowOpacity
        {
            get => _floatingService.Opacity;
            set
            {
                if (Math.Abs(_floatingService.Opacity - value) > 0.01)
                {
                    _floatingService.Opacity = value;
                    OnPropertyChanged();
                    SaveGlobalConfig();
                }
            }
        }

        /// <summary>
        /// 是否启用热键控制
        /// </summary>
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

        /// <summary>
        /// 是否启用硬件加速
        /// </summary>
        public bool EnableHardwareAcceleration
        {
            get => _enableHardwareAcceleration;
            set
            {
                if (SetProperty(ref _enableHardwareAcceleration, value))
                {
                    SaveGlobalConfig();
                    Logger.Info($"硬件加速已{(value ? "启用" : "禁用")}，重启应用后生效");
                }
            }
        }

        /// <summary>
        /// 是否处于坐标编辑模式（兼容性属性）
        /// 在多配置架构中，坐标编辑在对话框中进行，此属性始终返回 false
        /// </summary>
        public bool IsCoordinateEditMode => false;

        #endregion

        #region 命令

        public ICommand AddConfigurationCommand { get; }
        public ICommand DeleteConfigurationCommand { get; }
        public ICommand CloneConfigurationCommand { get; }
        public ICommand EditConfigurationCommand { get; }
        public ICommand SetActiveConfigurationCommand { get; }

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
            _configService = new KeyConfigurationService(_hotkeyService);
            _floatingService = new FloatingWindowService();

            // 初始化
            _configurations = new ObservableCollection<KeyConfigurationItemViewModel>();

            // 初始化命令
            AddConfigurationCommand = CreateCommand(AddConfiguration);
            DeleteConfigurationCommand = CreateCommand<Guid>(DeleteConfiguration, CanDeleteConfiguration);
            CloneConfigurationCommand = CreateCommand<Guid>(CloneConfiguration, CanCloneConfiguration);
            EditConfigurationCommand = CreateCommand<Guid>(EditConfiguration, CanEditConfiguration);
            SetActiveConfigurationCommand = CreateCommand<Guid>(SetActiveConfiguration);

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
                ShowMessage("已开始按键序列");
            };

            _hotkeyService.SequenceModeStopped += () =>
            {
                IsExecuting = false;
                ShowMessage("已停止按键序列");
            };

            // 配置服务事件
            _configService.ConfigurationsChanged += OnConfigurationsChanged;
            _configService.ActiveConfigurationChanged += OnActiveConfigurationChanged;

            // 配置变更事件
            WpfApp.Services.Core.ConfigManager.Instance.ConfigChanged += OnConfigChanged;
        }

        private void LoadConfiguration()
        {
            ExceptionHandler.Execute(() =>
            {
                _isLoadingConfig = true;
                try
                {
                    // 加载全局配置
                    LoadGlobalConfig(WpfApp.Services.Core.ConfigManager.Instance.GlobalConfig);

                    // 加载多配置数据
                    var multiConfig = WpfApp.Services.Core.ConfigManager.Instance.MultiKeyConfigData;
                    if (multiConfig != null)
                    {
                        _configService.LoadConfigurations(multiConfig);
                        LoadConfigurationsToUI();
                    }

                    Logger.Debug("配置加载完成");
                }
                finally
                {
                    _isLoadingConfig = false;
                }
            }, "加载配置", showMessageBox: false);
        }

        private void LoadGlobalConfig(GlobalConfig globalConfig)
        {
            _floatingService.IsEnabled = globalConfig.UI.FloatingWindow.IsEnabled;
            _floatingService.Opacity = globalConfig.UI.FloatingWindow.Opacity;
            _isHotkeyControlEnabled = globalConfig.isHotkeyControlEnabled ?? true;
            _enableHardwareAcceleration = globalConfig.EnableHardwareAcceleration ?? true;

            // 通知UI更新
            OnPropertyChanged(nameof(IsFloatingWindowEnabled));
            OnPropertyChanged(nameof(FloatingWindowOpacity));
            OnPropertyChanged(nameof(IsHotkeyControlEnabled));
            OnPropertyChanged(nameof(EnableHardwareAcceleration));

            // 同步到服务
            _hotkeyService.IsHotkeyControlEnabled = _isHotkeyControlEnabled;
            _floatingService.IsHotkeyControlEnabled = _isHotkeyControlEnabled;

            Logger.Debug("已加载全局配置");
        }

        private void LoadConfigurationsToUI()
        {
            Configurations.Clear();

            foreach (var config in _configService.Configurations)
            {
                var viewModel = new KeyConfigurationItemViewModel(config);

                // 设置激活状态
                if (_configService.ActiveConfiguration?.Id == config.Id)
                {
                    viewModel.IsActive = true;
                    SelectedConfiguration = viewModel;
                }

                Configurations.Add(viewModel);
            }

            Logger.Debug($"已加载 {Configurations.Count} 个配置到UI");
        }

        private void OnConfigChanged(object sender, ConfigEventArgs e)
        {
            try
            {
                if (e.ChangeType == ConfigChangeType.Global && e.GlobalConfigData != null)
                {
                    LoadGlobalConfig(e.GlobalConfigData);
                }
                else if (e.ChangeType == ConfigChangeType.MultiKey)
                {
                    // 多配置数据变更，重新加载
                    LoadConfigurationsToUI();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("处理配置变更事件失败", ex);
            }
        }

        private void OnConfigurationsChanged(object sender, EventArgs e)
        {
            // 配置列表变更，保存到配置文件
            SaveMultiKeyConfig();
        }

        private void OnActiveConfigurationChanged(object sender, KeyConfiguration? config)
        {
            // 更新UI中的激活状态
            foreach (var vm in Configurations)
            {
                vm.IsActive = (config != null && vm.Id == config.Id);
            }

            // 保存激活配置ID
            SaveMultiKeyConfig();

            Logger.Info($"激活配置已切换: {config?.Name ?? "无"}");
        }

        #endregion

        #region 配置管理

        private void AddConfiguration()
        {
            try
            {
                // 创建新的临时配置
                var newConfig = new KeyConfiguration
                {
                    Id = Guid.NewGuid(),
                    Name = $"配置 {Configurations.Count + 1}",
                    ExecutionMode = KeyExecutionMode.Hold,
                    Keys = new List<KeyConfig>(),
                    Interval = 10,
                    KeyPressInterval = 5,
                    IsReduceKeyStuck = false,
                    SoundEnabled = true,
                    SoundVolume = 0.5,
                    AutoSwitchToEnglishIME = true,
                    IsEnabled = true
                };

                // 创建对话框 ViewModel
                var dialogViewModel = new KeyConfigurationDialogViewModel(newConfig, _lyKeysService);

                // 创建配置窗口
                var dialogView = new Views.KeyConfigurationWindow
                {
                    DataContext = dialogViewModel,
                    Owner = Application.Current.MainWindow
                };

                // 订阅保存完成事件
                dialogViewModel.SaveCompleted += (s, e) =>
                {
                    try
                    {
                        // 用户点击保存后，才真正添加到配置列表
                        var addedConfig = _configService.AddConfiguration(newConfig.Name);

                        // 复制所有设置到新添加的配置
                        addedConfig.ExecutionMode = newConfig.ExecutionMode;
                        addedConfig.StartKey = newConfig.StartKey;
                        addedConfig.StartMods = newConfig.StartMods;
                        addedConfig.StopKey = newConfig.StopKey;
                        addedConfig.StopMods = newConfig.StopMods;
                        addedConfig.Interval = newConfig.Interval;
                        addedConfig.KeyPressInterval = newConfig.KeyPressInterval;
                        addedConfig.IsReduceKeyStuck = newConfig.IsReduceKeyStuck;
                        addedConfig.SoundEnabled = newConfig.SoundEnabled;
                        addedConfig.SoundVolume = newConfig.SoundVolume;
                        addedConfig.AutoSwitchToEnglishIME = newConfig.AutoSwitchToEnglishIME;
                        addedConfig.IsEnabled = newConfig.IsEnabled;

                        // 复制按键列表
                        addedConfig.Keys.Clear();
                        foreach (var key in newConfig.Keys)
                        {
                            addedConfig.Keys.Add(new KeyConfig
                            {
                                Code = key.Code,
                                IsSelected = key.IsSelected,
                                KeyInterval = key.KeyInterval,
                                Type = key.Type,
                                X = key.X,
                                Y = key.Y
                            });
                        }

                        // 更新配置
                        _configService.UpdateConfiguration(addedConfig);

                        // 通过 ConfigManager 保存配置
                        WpfApp.Services.Core.ConfigManager.Instance.UpdateMultiKeyConfig(multiConfig =>
                        {
                            // 配置已经在 UpdateConfiguration 中更新，这里只是触发保存
                        });

                        // 刷新配置列表
                        LoadConfiguration();

                        ShowMessage($"已添加配置: {addedConfig.Name}");
                        Logger.Info($"已添加配置: {addedConfig.Name}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("保存新配置失败", ex);
                        ShowMessage($"保存新配置失败: {ex.Message}", true);
                    }
                };

                // 显示模态对话框
                var result = dialogView.ShowDialog();

                Logger.Debug($"添加配置对话框关闭, 结果: {result}");
            }
            catch (Exception ex)
            {
                Logger.Error("添加配置失败", ex);
                ShowMessage($"添加配置失败: {ex.Message}", true);
            }
        }

        private bool CanDeleteConfiguration(Guid configId)
        {
            return Configurations.Count > 1; // 至少保留一个配置
        }

        private void DeleteConfiguration(Guid configId)
        {
            try
            {
                var config = Configurations.FirstOrDefault(c => c.Id == configId);
                if (config == null)
                {
                    ShowMessage("未找到要删除的配置", true);
                    return;
                }

                // 删除配置（DeleteConfirmationBehavior 已经提供了二次确认）
                if (_configService.RemoveConfiguration(configId))
                {
                    Configurations.Remove(config);
                    ShowMessage($"已删除配置: {config.Name}");
                    Logger.Info($"已删除配置: {config.Name}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("删除配置失败", ex);
                ShowMessage($"删除配置失败: {ex.Message}", true);
            }
        }

        private bool CanCloneConfiguration(Guid configId)
        {
            return Configurations.Any(c => c.Id == configId);
        }

        private void CloneConfiguration(Guid configId)
        {
            try
            {
                var clonedConfig = _configService.CloneConfiguration(configId);
                var viewModel = new KeyConfigurationItemViewModel(clonedConfig);
                Configurations.Add(viewModel);

                ShowMessage($"已克隆配置: {clonedConfig.Name}");
                Logger.Info($"已克隆配置: {clonedConfig.Name}");
            }
            catch (Exception ex)
            {
                Logger.Error("克隆配置失败", ex);
                ShowMessage($"克隆配置失败: {ex.Message}", true);
            }
        }

        private bool CanEditConfiguration(Guid configId)
        {
            return Configurations.Any(c => c.Id == configId);
        }

        private void EditConfiguration(Guid configId)
        {
            try
            {
                var config = _configService.Configurations.FirstOrDefault(c => c.Id == configId);
                if (config == null)
                {
                    ShowMessage("未找到要编辑的配置", true);
                    return;
                }

                // 打开配置编辑对话框
                OpenConfigurationDialog(config);
            }
            catch (Exception ex)
            {
                Logger.Error("编辑配置失败", ex);
                ShowMessage($"编辑配置失败: {ex.Message}", true);
            }
        }

        /// <summary>
        /// 打开配置编辑对话框
        /// </summary>
        private void OpenConfigurationDialog(KeyConfiguration config)
        {
            try
            {
                // 创建对话框 ViewModel
                var dialogViewModel = new KeyConfigurationDialogViewModel(config, _lyKeysService);

                // 创建配置窗口
                var dialogView = new Views.KeyConfigurationWindow
                {
                    DataContext = dialogViewModel,
                    Owner = Application.Current.MainWindow
                };

                // 订阅保存完成事件
                dialogViewModel.SaveCompleted += (s, e) =>
                {
                    try
                    {
                        // 更新配置
                        _configService.UpdateConfiguration(config);

                        // 通过 ConfigManager 保存配置
                        WpfApp.Services.Core.ConfigManager.Instance.UpdateMultiKeyConfig(multiConfig =>
                        {
                            // 配置已经在 UpdateConfiguration 中更新，这里只是触发保存
                        });

                        // 刷新配置列表
                        LoadConfiguration();

                        ShowMessage("配置已保存");
                        Logger.Info($"配置已保存: {config.Name}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("保存配置后处理失败", ex);
                        ShowMessage($"保存配置后处理失败: {ex.Message}", true);
                    }
                };

                // 显示模态对话框
                var result = dialogView.ShowDialog();

                Logger.Debug($"配置对话框关闭, 结果: {result}");
            }
            catch (Exception ex)
            {
                Logger.Error("打开配置对话框失败", ex);
                ShowMessage($"打开配置对话框失败: {ex.Message}", true);
            }
        }

        private void SetActiveConfiguration(Guid configId)
        {
            try
            {
                _configService.SetActiveConfiguration(configId);
                ShowMessage("已切换激活配置");
            }
            catch (Exception ex)
            {
                Logger.Error("切换激活配置失败", ex);
                ShowMessage($"切换激活配置失败: {ex.Message}", true);
            }
        }

        #endregion

        #region 执行控制

        public void StartKeyMapping()
        {
            if (IsExecuting) return;

            try
            {
                _hotkeyService.StartSequence();
                IsExecuting = true;
                Logger.Debug("按键映射已启动");
            }
            catch (Exception ex)
            {
                Logger.Error("启动按键映射失败", ex);
                StopKeyMapping();
                ShowMessage("启动失败", true);
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

        #region 浮窗管理

        public void SetMainWindow(Views.MainWindow mainWindow)
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

        #endregion

        #region 配置保存

        private void SaveMultiKeyConfig()
        {
            if (_isLoadingConfig) return;

            try
            {
                var configData = _configService.GetConfigData();
                WpfApp.Services.Core.ConfigManager.Instance.UpdateMultiKeyConfig(data =>
                {
                    data.Configurations = configData.Configurations;
                    data.ActiveConfigurationId = configData.ActiveConfigurationId;
                });

                Logger.Debug("多配置数据已保存");
            }
            catch (Exception ex)
            {
                Logger.Error("保存多配置数据失败", ex);
            }
        }

        private void SaveGlobalConfig()
        {
            if (_isLoadingConfig) return;

            WpfApp.Services.Core.ConfigManager.Instance.UpdateGlobalConfig(config =>
            {
                config.UI.FloatingWindow.IsEnabled = IsFloatingWindowEnabled;
                config.UI.FloatingWindow.Opacity = FloatingWindowOpacity;
                config.isHotkeyControlEnabled = IsHotkeyControlEnabled;
                config.EnableHardwareAcceleration = EnableHardwareAcceleration;
            });
        }

        /// <summary>
        /// 保存所有配置（兼容性方法）
        /// </summary>
        public void SaveConfig()
        {
            SaveGlobalConfig();
            SaveMultiKeyConfig();
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 统一的消息显示接口
        /// </summary>
        public void ShowMessage(string message, bool isError = false)
        {
            _mainViewModel.UpdateStatusMessage(message, isError);
        }

        public HotkeyService GetHotkeyService() => _hotkeyService;

        // 兼容性属性
        public string HotkeyStatus => IsExecuting ? "运行中" : "已停止";

        #endregion

        #region 清理

        ~KeyMappingViewModel()
        {
            _configService?.Dispose();
            _floatingService?.Dispose();
            WpfApp.Services.Core.ConfigManager.Instance.ConfigChanged -= OnConfigChanged;
        }

        #endregion
    }
}
