using WpfApp.Services.Core;

namespace WpfApp.Services.Models;

/// <summary>
/// 顺序模式 - 按一次热键执行一次完整序列
/// </summary>
public class SequenceKeyMode : KeyModeBase
{
    private int _currentIndex;
    private bool _emergencyStop;

    public SequenceKeyMode(LyKeysService driverService) : base(driverService)
    {
    }

    public override void Start()
    {
        if (_operationList.Count == 0)
        {
            _logger.Warning("顺序模式: 操作列表为空，无法启动");
            return;
        }

        _isRunning = true;
        _emergencyStop = false;
        _currentIndex = 0;
        _cts = new CancellationTokenSource();

        LogModeStart();

        var thread = new Thread(() =>
        {
            try
            {
                ExecuteLoop();
            }
            catch (Exception ex)
            {
                _logger.Error($"顺序模式执行异常: {ex.Message}", ex);
            }
            finally
            {
                _isRunning = false;
            }
        })
        {
            IsBackground = true,
            Name = "SequenceKeyMode-Thread"
        };

        thread.Start();
    }

    public override void Stop()
    {
        _emergencyStop = true;
        base.Stop();
    }

    protected override KeyItemSettings? GetNextOperation()
    {
        if (_currentIndex >= _operationList.Count)
            return null;

        var operation = _operationList[_currentIndex];
        _currentIndex++;
        return operation;
    }

    protected override bool ShouldStop()
    {
        return _emergencyStop || !_isRunning || _cts?.Token.IsCancellationRequested == true;
    }
}
