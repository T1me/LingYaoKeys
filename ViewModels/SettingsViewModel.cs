using System;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WpfApp.Services.Core;
using WpfApp.Services.Models;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace WpfApp.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly UpdateService _updateService;
    private bool _isCheckingUpdate;
    private string _updateStatus = "检查更新";
    private string _debugModeStatus = "调试模式关闭";
    private string _selectedDriver = "AHK";

    public string UpdateStatus
    {
        get => _updateStatus;
        set => SetProperty(ref _updateStatus, value);
    }

    public string DebugModeStatus
    {
        get => _debugModeStatus;
        set => SetProperty(ref _debugModeStatus, value);
    }

    public string SelectedDriver
    {
        get => _selectedDriver;
        set
        {
            if (SetProperty(ref _selectedDriver, value))
            {
                ConfigManager.UpdateGlobalConfig(config => config.SelectedDriver = value);
            }
        }
    }

    public ICommand CheckUpdateCommand { get; }
    public ICommand ToggleDebugModeCommand { get; }
    public ICommand ExportConfigCommand { get; }
    public ICommand ImportConfigCommand { get; }

    public SettingsViewModel()
    {
        _updateService = new UpdateService();

        // 使用统一的命令初始化模式
        CheckUpdateCommand = CreateCommand(async () => await CheckForUpdateAsync(), () => !_isCheckingUpdate);
        ToggleDebugModeCommand = CreateCommand(ToggleDebugMode);
        ExportConfigCommand = CreateCommand(ExportConfig);
        ImportConfigCommand = CreateCommand(ImportConfig);

        UpdateDebugModeStatus();
        UpdateDriverStatus();
    }

    private void UpdateDebugModeStatus()
    {
        var globalConfig = ConfigManager.GlobalConfig;
        _debugModeStatus = globalConfig.Debug.IsDebugMode ? "🟢 调试模式：已开启" : "⭕ 调试模式：已关闭";
    }

    private void UpdateDriverStatus()
    {
        var globalConfig = ConfigManager.GlobalConfig;
        _selectedDriver = globalConfig.SelectedDriver ?? "AHK";
    }

    private async void ToggleDebugMode()
    {
        ExceptionHandler.Execute(
            () =>
            {
                var currentDebugMode = ConfigManager.GlobalConfig.Debug.IsDebugMode;

                ConfigManager.UpdateGlobalConfig(config =>
                {
                    config.Debug.IsDebugMode = !currentDebugMode;
                });

                UpdateDebugModeStatus();

                var result = MessageBox.Show(
                    "调试模式设置已更改，需要重启程序才能生效。是否立即重启？",
                    "重启提示",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes) RestartApplication();
            },
            "切换调试模式");
    }

    private void RestartApplication()
    {
        ExceptionHandler.Execute(
            () =>
            {
                var appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                              ?? throw new InvalidOperationException("无法获取应用程序路径");

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = appPath,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                System.Diagnostics.Process.Start(startInfo);
                Application.Current.Shutdown();
            },
            "重启应用程序");
    }

    private async Task CheckForUpdateAsync()
    {
        await ExceptionHandler.ExecuteAsync(
            async () =>
            {
                _isCheckingUpdate = true;
                UpdateStatus = "正在检查...";

                var updateInfo = await _updateService.CheckForUpdateAsync();
                if (updateInfo != null)
                {
                    var result = MessageBox.Show(
                        $"发现新版本：{updateInfo.LatestVersion}\n当前版本：{updateInfo.CurrentVersion}\n是否前往下载页面？",
                        "发现新版本",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        _updateService.OpenDownloadPage(updateInfo.DownloadUrl);
                    }

                    UpdateStatus = "有新版本";
                }
                else
                {
                    MessageBox.Show(
                        "当前已是最新版本",
                        "检查更新",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
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
            });

        _isCheckingUpdate = false;
    }

    private void ExportConfig()
    {
        ExceptionHandler.Execute(
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
                        GlobalConfig = ConfigManager.GlobalConfig,
                        KeyConfig = ConfigManager.CurrentKeyConfig
                    };

                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
                    System.IO.File.WriteAllText(dialog.FileName, json);

                    MessageBox.Show("配置导出成功！", "导出配置", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            },
            "导出配置");
    }

    private void ImportConfig()
    {
        ExceptionHandler.Execute(
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
                        KeyConfig = (KeyConfigData)null
                    });

                    if (config?.GlobalConfig != null)
                    {
                        ConfigManager.UpdateGlobalConfig(gc =>
                        {
                            gc.UI = config.GlobalConfig.UI;
                            gc.Debug = config.GlobalConfig.Debug;
                            gc.soundEnabled = config.GlobalConfig.soundEnabled;
                            gc.SoundVolume = config.GlobalConfig.SoundVolume;
                            gc.AutoSwitchToEnglishIME = config.GlobalConfig.AutoSwitchToEnglishIME;
                            gc.SelectedDriver = config.GlobalConfig.SelectedDriver;
                        });
                    }

                    if (config?.KeyConfig != null)
                    {
                        ConfigManager.UpdateKeyConfig(kc =>
                        {
                            kc.startKey = config.KeyConfig.startKey;
                            kc.startMods = config.KeyConfig.startMods;
                            kc.stopKey = config.KeyConfig.stopKey;
                            kc.stopMods = config.KeyConfig.stopMods;
                            kc.keys = config.KeyConfig.keys;
                            kc.keyMode = config.KeyConfig.keyMode;
                            kc.interval = config.KeyConfig.interval;
                            kc.KeyPressInterval = config.KeyConfig.KeyPressInterval;
                            kc.TargetWindowTitle = config.KeyConfig.TargetWindowTitle;
                            kc.TargetWindowClassName = config.KeyConfig.TargetWindowClassName;
                            kc.TargetWindowProcessName = config.KeyConfig.TargetWindowProcessName;
                        });
                    }

                    MessageBox.Show("配置导入成功！部分设置可能需要重启应用生效。", "导入配置", MessageBoxButton.OK, MessageBoxImage.Information);

                    UpdateDebugModeStatus();
                    UpdateDriverStatus();
                }
            },
            "导入配置");
    }

}