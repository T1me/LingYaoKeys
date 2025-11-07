using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WpfApp.Services.Core;
using WpfApp.Services.Utils;

namespace WpfApp.ViewModels;

/// <summary>
/// 关于页面视图模型
/// </summary>
public partial class AboutViewModel : ObservableObject
{
    private readonly IConfigManager _configManager;
    private readonly ISerilogManager _logger;
    private readonly ExceptionHandler _exceptionHandler;

    public AboutViewModel(
        IConfigManager configManager,
        ISerilogManager logger)
    {
        _configManager = configManager;
        _logger = logger;
        _exceptionHandler = new ExceptionHandler();
    }

    /// <summary>
    /// GitHub 仓库链接
    /// </summary>
    public string GitHubUrl => _configManager.GlobalConfig.AppInfo.GitHubUrl;

    /// <summary>
    /// 官网链接
    /// </summary>
    public string WebsiteUrl => "https://zyphrZero.github.io/LingYaoKeys/";

    /// <summary>
    /// 打开 GitHub 仓库
    /// </summary>
    [RelayCommand]
    private void OpenGitHub()
    {
        _exceptionHandler.Execute(
            () =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = GitHubUrl,
                    UseShellExecute = true
                };
                Process.Start(psi);
                _logger.Debug("成功打开GitHub仓库链接");
            },
            "打开GitHub仓库链接");
    }

    /// <summary>
    /// 打开官网
    /// </summary>
    [RelayCommand]
    private void OpenWebsite()
    {
        _exceptionHandler.Execute(
            () =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = WebsiteUrl,
                    UseShellExecute = true
                };
                Process.Start(psi);
                _logger.Debug("成功打开官网链接");
            },
            "打开官网链接");
    }
}
