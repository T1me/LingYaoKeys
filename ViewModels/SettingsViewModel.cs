using System;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Configuration;
using WpfApp.Services;
using WpfApp.Services.Utils;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;
using System.Net.Http;
using WpfApp.Services.Core;

namespace WpfApp.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly SerilogManager _logger = SerilogManager.Instance;
    private readonly UpdateService _updateService;
    private readonly ConfigService _configService;
    private bool _isCheckingUpdate;
    private string _updateStatus = "检查更新";
    private string _debugModeStatus = "调试模式关闭";

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

    public ICommand CheckUpdateCommand { get; }
    public ICommand ImportConfigCommand { get; }
    public ICommand ExportConfigCommand { get; }
    public ICommand ToggleDebugModeCommand { get; }

    public SettingsViewModel()
    {
        _configService = new ConfigService();
        _updateService = new UpdateService();

        CheckUpdateCommand = new RelayCommand(async () => await CheckForUpdateAsync(), () => !_isCheckingUpdate);
        ImportConfigCommand = new RelayCommand(ImportConfig);
        ExportConfigCommand = new RelayCommand(ExportConfig);
        ToggleDebugModeCommand = new RelayCommand(ToggleDebugMode);

        UpdateDebugModeStatus();
    }

    private void UpdateDebugModeStatus()
    {
        var config = AppConfigService.Config;
        _debugModeStatus = config.Debug.IsDebugMode ? "🟢 调试模式：已开启" : "⭕ 调试模式：已关闭";
    }

    private void ToggleDebugMode()
    {
        try
        {
            var currentDebugMode = AppConfigService.Config.Debug.IsDebugMode;

            AppConfigService.UpdateConfig(config =>
            {
                config.Debug.IsDebugMode = !currentDebugMode;
                config.Debug.UpdateDebugState();
            });

            UpdateDebugModeStatus();

            var result = MessageBox.Show(
                "调试模式设置已更改，需要重启程序才能生效。是否立即重启？",
                "重启提示",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes) RestartApplication();
        }
        catch (Exception ex)
        {
            _logger.Error("切换调试模式失败", ex);
            MessageBox.Show($"切换调试模式失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RestartApplication()
    {
        try
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
        }
        catch (Exception ex)
        {
            _logger.Error("重启应用程序失败", ex);
            MessageBox.Show($"重启应用程序失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            _isCheckingUpdate = true;
            UpdateStatus = "正在检查...";

            var updateInfo = await _updateService.CheckForUpdateAsync();
            if (updateInfo != null)
            {
                var dialog = new Views.UpdateDialog(
                    updateInfo.LatestVersion,
                    updateInfo.CurrentVersion,
                    updateInfo.DownloadUrl);

                if (dialog.ShowDialog() == true) _updateService.OpenDownloadPage(updateInfo.DownloadUrl);
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
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("检查更新失败：网络连接问题", ex);
            MessageBox.Show(
                "无法连接到更新服务器，请检查网络连接",
                "网络错误",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            UpdateStatus = "网络错误";
        }
        catch (TaskCanceledException)
        {
            _logger.Error("检查更新失败：请求超时");
            MessageBox.Show(
                "更新服务器响应超时，请稍后重试",
                "超时错误",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            UpdateStatus = "请求超时";
        }
        catch (InvalidOperationException ex)
        {
            _logger.Error("检查更新失败：服务异常", ex);
            MessageBox.Show(
                $"更新服务异常：{ex.Message}",
                "服务错误",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            UpdateStatus = "服务异常";
        }
        catch (Exception ex)
        {
            _logger.Error("检查更新失败：未知错误", ex);
            MessageBox.Show(
                $"检查更新失败：{ex.Message}",
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            UpdateStatus = "检查失败";
        }
        finally
        {
            _isCheckingUpdate = false;
        }
    }

    private void ImportConfig()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择配置文件",
                Filter = "JSON 文件 (*.json)|*.json",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog() == true)
            {
                _configService.ImportConfig(dialog.FileName);
                MessageBox.Show("配置导入成功，需要重启程序才能生效。是否立即重启？",
                    "重启提示",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("导入配置失败", ex);
            MessageBox.Show($"导入配置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportConfig()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存配置文件",
                Filter = "JSON 文件 (*.json)|*.json",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                FileName = $"AppConfig_{DateTime.Now:yyyyMMdd}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                _configService.ExportConfig(dialog.FileName);
                MessageBox.Show("配置导出成功", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("导出配置失败", ex);
            MessageBox.Show($"导出配置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}