using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WpfApp.Services.Core;
using WpfApp.Services.Models;
using WpfApp.Services.Utils;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace WpfApp.ViewModels;

/// <summary>
/// 设置页面视图模型
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IConfigManager _configManager;
    private readonly ISerilogManager _logger;
    private readonly IPathService _pathService;
    private readonly UpdateService _updateService;
    private readonly ExceptionHandler _exceptionHandler;

    [ObservableProperty]
    private string _updateStatus = "检查更新";

    [ObservableProperty]
    private string _debugModeStatus = "调试模式关闭";

    [ObservableProperty]
    private Brush _debugModeStatusColor = Brushes.Gray;

    [ObservableProperty]
    private string _driverStatus = "🟢 已加载";

    [ObservableProperty]
    private Brush _driverStatusColor = Brushes.Green;

    [ObservableProperty]
    private string _selectedDriver = "AHK";

    private bool _isCheckingUpdate;

    public SettingsViewModel(
        IConfigManager configManager,
        ISerilogManager logger,
        IPathService pathService)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _updateService = new UpdateService();
        _exceptionHandler = new ExceptionHandler();

        UpdateDebugModeStatus();
        UpdateDriverStatus();
    }

    /// <summary>
    /// 当选中的驱动改变时
    /// </summary>
    partial void OnSelectedDriverChanged(string value)
    {
        _configManager.UpdateGlobalConfig(config => config.SelectedDriver = value);
        ReloadDriver(value);
    }

    /// <summary>
    /// 重新加载驱动
    /// </summary>
    private void ReloadDriver(string driverType)
    {
        try
        {
            SetDriverStatus("🟠 加载中...", Brushes.Orange);

            var driverFile = DriverFactory.PrepareDriverFiles(_logger, driverType, _pathService, App.ExtractEmbeddedResource);
            var driver = DriverFactory.CreateDriver(_logger, driverType, driverFile);

            if (!App.LyKeysDriver.ReloadDriver(driver, driverFile))
            {
                SetDriverStatus("🔴 加载失败", Brushes.Red);
                HandyControl.Controls.MessageBox.Error($"驱动加载失败({driverType})", "错误");
            }
            else
            {
                SetDriverStatus("🟢 已加载", Brushes.Green);
            }
        }
        catch (Exception ex)
        {
            SetDriverStatus("🔴 加载失败", Brushes.Red);
            HandyControl.Controls.MessageBox.Error($"切换驱动失败: {ex.Message}", "错误");
        }
    }

    /// <summary>
    /// 设置驱动状态
    /// </summary>
    private void SetDriverStatus(string status, Brush color)
    {
        DriverStatus = status;
        DriverStatusColor = color;
    }

    /// <summary>
    /// 更新调试模式状态
    /// </summary>
    private void UpdateDebugModeStatus()
    {
        var globalConfig = _configManager.GlobalConfig;
        if (globalConfig.Debug.IsDebugMode)
        {
            DebugModeStatus = "🟢 调试模式：已开启";
            DebugModeStatusColor = Brushes.Green;
        }
        else
        {
            DebugModeStatus = "⭕ 调试模式：已关闭";
            DebugModeStatusColor = Brushes.Gray;
        }
    }

    /// <summary>
    /// 更新驱动状态
    /// </summary>
    private void UpdateDriverStatus()
    {
        var globalConfig = _configManager.GlobalConfig;
        SelectedDriver = globalConfig.SelectedDriver ?? "AHK";

        try
        {
            if (App.LyKeysDriver != null && App.LyKeysDriver.IsInitialized)
            {
                SetDriverStatus("🟢 已加载", Brushes.Green);
            }
            else
            {
                SetDriverStatus("⭕ 未加载", Brushes.Gray);
            }
        }
        catch
        {
            SetDriverStatus("🔴 加载失败", Brushes.Red);
        }
    }

    /// <summary>
    /// 切换调试模式
    /// </summary>
    [RelayCommand]
    private void ToggleDebugMode()
    {
        _exceptionHandler.Execute(
            () =>
            {
                var currentDebugMode = _configManager.GlobalConfig.Debug.IsDebugMode;

                _configManager.UpdateGlobalConfig(config =>
                {
                    config.Debug.IsDebugMode = !currentDebugMode;
                });

                UpdateDebugModeStatus();
                _logger.Initialize(_configManager.GlobalConfig.Debug);
            },
            "切换调试模式");
    }

    /// <summary>
    /// 检查更新
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCheckUpdate))]
    private void CheckUpdate()
    {
        _exceptionHandler.Execute(
            () =>
            {
                _isCheckingUpdate = true;
                CheckUpdateCommand.NotifyCanExecuteChanged();
                UpdateStatus = "正在检查...";

                var updateInfo = _updateService.CheckForUpdate();
                if (updateInfo != null)
                {
                    var result = HandyControl.Controls.MessageBox.Ask(
                        $"发现新版本：{updateInfo.LatestVersion}\n当前版本：{updateInfo.CurrentVersion}\n是否前往下载页面？",
                        "发现新版本");

                    if (result == MessageBoxResult.Yes)
                    {
                        _updateService.OpenDownloadPage(updateInfo.DownloadUrl);
                    }

                    UpdateStatus = "有新版本";
                }
                else
                {
                    HandyControl.Controls.MessageBox.Info("当前已是最新版本", "检查更新");
                    UpdateStatus = "已是最新";
                }
            },
            "检查更新",
            customHandler: ex =>
            {
                // 根据异常类型设置不同的状态
                UpdateStatus = ex switch
                {
                    System.Net.Http.HttpRequestException => "网络错误",
                    TaskCanceledException => "请求超时",
                    InvalidOperationException => "服务异常",
                    _ => "检查失败"
                };
                _isCheckingUpdate = false;
                CheckUpdateCommand.NotifyCanExecuteChanged();
            });

        _isCheckingUpdate = false;
        CheckUpdateCommand.NotifyCanExecuteChanged();
    }

    private bool CanCheckUpdate() => !_isCheckingUpdate;

    /// <summary>
    /// 导出配置
    /// </summary>
    [RelayCommand]
    private void ExportConfig()
    {
        _exceptionHandler.Execute(
            () =>
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON 文件|*.json",
                    FileName = $"LingYaoKeys_Config_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                    DefaultExt = ".json"
                };

                if (dialog.ShowDialog() == true)
                {
                    var config = new
                    {
                        GlobalConfig = _configManager.GlobalConfig,
                        MultiKeyConfig = _configManager.MultiKeyConfigData,
                    };

                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
                    System.IO.File.WriteAllText(dialog.FileName, json);

                    HandyControl.Controls.MessageBox.Success("配置导出成功！", "导出配置");
                }
            },
            "导出配置");
    }

    /// <summary>
    /// 导入配置
    /// </summary>
    [RelayCommand]
    private void ImportConfig()
    {
        _exceptionHandler.Execute(
            () =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "JSON 文件|*.json",
                    DefaultExt = ".json"
                };

                if (dialog.ShowDialog() == true)
                {
                    var json = System.IO.File.ReadAllText(dialog.FileName);
                    var config = Newtonsoft.Json.JsonConvert.DeserializeAnonymousType(json, new
                    {
                        GlobalConfig = (GlobalConfig)null,
                        MultiKeyConfig = (MultiKeyConfigData)null,
                        Version = 2
                    });

                    if (config?.GlobalConfig != null)
                    {
                        _configManager.UpdateGlobalConfig(gc =>
                        {
                            gc.UI = config.GlobalConfig.UI;
                            gc.Debug = config.GlobalConfig.Debug;
                            gc.SelectedDriver = config.GlobalConfig.SelectedDriver;
                        });
                    }

                    if (config?.MultiKeyConfig != null)
                    {
                        _configManager.UpdateMultiKeyConfig(mkc =>
                        {
                            mkc.Configurations = config.MultiKeyConfig.Configurations;
                            mkc.ActiveConfigurationId = config.MultiKeyConfig.ActiveConfigurationId;
                            mkc.Version = config.MultiKeyConfig.Version;
                        });
                    }

                    HandyControl.Controls.MessageBox.Success("配置导入成功！部分设置可能需要重启应用生效。", "导入配置");

                    UpdateDebugModeStatus();
                    UpdateDriverStatus();
                }
            },
            "导入配置");
    }
}
