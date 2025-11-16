using WpfApp.Services.Core;
using WpfApp.Services.Utils;

// 按键按压模式
namespace WpfApp.Services.Models;

/// <summary>
/// 按压模式 - 按住热键时持续循环执行按键序列
/// </summary>
public class HoldKeyMode : KeyModeBase
{
    private volatile bool _isKeyHeld;
    private readonly object _stateLock = new();
    private bool _isExecuting;
    private int _currentIndex;

    public event Action<string, bool>? OnStatusMessageUpdated;

    public HoldKeyMode(ISerilogManager logger, LyKeysService driverService) : base(logger, driverService)
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
        _isKeyHeld = true;
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
                _logger.Error($"按压模式执行异常: {ex.Message}", ex);
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
            Name = "HoldKeyMode-Thread"
        };

        thread.Start();
    }

    public void HandleKeyPress()
    {
        if (!_isExecuting)
        {
            _isKeyHeld = true;
            Start();
        }
    }

    public void HandleKeyRelease()
    {
        _isKeyHeld = false;
        Stop();
    }

    public override void Stop()
    {
        _isKeyHeld = false;
        base.Stop();
    }

    protected override KeyItemSettings? GetNextOperation()
    {
        if (_currentIndex >= _operationList.Count)
            _currentIndex = 0;

        var operation = _operationList[_currentIndex];
        _currentIndex++;
        return operation;
    }

    protected override bool ShouldStop()
    {
        return !_isKeyHeld || !_isRunning || _cts?.Token.IsCancellationRequested == true;
    }
}