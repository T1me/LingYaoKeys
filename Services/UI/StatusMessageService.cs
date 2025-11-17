using System.Windows.Media;
using WpfApp.ViewModels;

namespace WpfApp.Services.UI;

/// <summary>
/// 状态消息服务实现 - 通过 MainViewModel 显示状态消息
/// </summary>
public class StatusMessageService : IStatusMessageService
{
    private readonly MainViewModel _mainViewModel;

    public StatusMessageService(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
    }

    public void UpdateStatusMessage(string message, bool isError = false)
    {
        _mainViewModel.UpdateStatusMessage(message, isError);
    }

    public void UpdateStatusMessage(string message, System.Windows.Media.Brush color)
    {
        _mainViewModel.UpdateStatusMessage(message, color);
    }
}
