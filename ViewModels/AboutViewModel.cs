using System.Windows;
using System.Windows.Input;
using System.Diagnostics;

namespace WpfApp.ViewModels;

public class AboutViewModel : ViewModelBase
{
    private readonly string _githubUrl;
    private readonly string _websiteUrl = "https://cassianvale.github.io/LingYaoKeys/";

    public ICommand OpenGitHubCommand { get; }
    public ICommand OpenWebsiteCommand { get; }

    public AboutViewModel()
    {
        _githubUrl = ConfigManager.GlobalConfig.AppInfo.GitHubUrl;

        // 使用统一的命令初始化模式
        OpenGitHubCommand = CreateCommand(OpenGitHub);
        OpenWebsiteCommand = CreateCommand(OpenWebsite);
    }

    private void OpenWebsite()
    {
        ExceptionHandler.Execute(
            () =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _websiteUrl,
                    UseShellExecute = true
                };
                Process.Start(psi);
                Logger.Debug("成功打开官网链接");
            },
            "打开官网链接");
    }

    private void OpenGitHub()
    {
        ExceptionHandler.Execute(
            () =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _githubUrl,
                    UseShellExecute = true
                };
                Process.Start(psi);
                Logger.Debug("成功打开GitHub仓库链接");
            },
            "打开GitHub仓库链接");
    }
}