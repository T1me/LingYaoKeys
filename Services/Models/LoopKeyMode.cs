using WpfApp.Services.Core;
using WpfApp.Services.Utils;

namespace WpfApp.Services.Models;

/// <summary>
/// 循环模式 - 按一次开始热键开始循环执行，按一次停止热键停止执行
/// </summary>
public class LoopKeyMode : KeyModeBase
{
    private int _currentIndex;
    private volatile bool _shouldStop;
    private readonly object _stateLock = new();
    private bool _isExecuting;

    public LoopKeyMode(ISerilogManager logger, LyKeysService driverService) : base(logger, driverService)
    {
    }

    protected override void StartInternal()
    {
        lock (_stateLock)
        {
            if (_isExecuting) return;
            _isExecuting = true;
        }

        if (_operationList.Count == 0)
        {
            lock (_stateLock) { _isExecuting = false; }
            return;
        }

        _isRunning = true;
        _shouldStop = false;
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
                _logger.Error($"循环模式执行异常: {ex.Message}", ex);
            }
            finally
            {
                lock (_stateLock) { _isExecuting = false; }
                _isRunning = false;
                NotifyCompleted();
            }
        })
        {
            IsBackground = true,
            Name = "LoopKeyMode-Thread"
        };

        thread.Start();
    }

    public override void Stop()
    {
        _shouldStop = true;
        base.Stop();
    }

    protected override KeyItemSettings? GetNextOperation()
    {
        // 循环模式：到达列表末尾后回到开始
        if (_currentIndex >= _operationList.Count)
            _currentIndex = 0;

        var operation = _operationList[_currentIndex];
        _currentIndex++;
        return operation;
    }

    protected override bool ShouldStop()
    {
        return _shouldStop || !_isRunning || _cts?.Token.IsCancellationRequested == true;
    }

    protected override void LogModeStart()
    {
        _logger.Info($"循环模式启动 - 操作数: {_operationList.Count}, 将持续循环直到停止");
    }

    protected override void LogModeEnd()
    {
        _logger.Info("循环模式停止");
    }
}
