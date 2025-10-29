using System.Diagnostics;
using WpfApp.Services.Core;
using WpfApp.Services.Utils;

// 按键模式基类
namespace WpfApp.Services.Models;

public abstract class KeyModeBase
{
    protected readonly LyKeysService _driverService;
    protected readonly SerilogManager _logger;
    protected List<KeyItemSettings> _operationList;
    protected bool _isRunning;
    protected CancellationTokenSource? _cts;
    protected int KeyPressInterval => _driverService.KeyPressInterval;

    protected KeyModeBase(LyKeysService driverService)
    {
        _driverService = driverService;
        _logger = SerilogManager.Instance;
        _operationList = new List<KeyItemSettings>();
    }

    public virtual void SetOperationList(List<KeyItemSettings> operations)
    {
        _operationList = operations == null ? new List<KeyItemSettings>() : new List<KeyItemSettings>(operations);
    }

    public abstract void Start();

    public virtual void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
        Thread.Sleep(50);

        foreach (var op in _operationList)
            if (op.Type == KeyItemType.Keyboard && op.KeyCode.HasValue)
                _driverService.SendKeyUp(op.KeyCode.Value);
    }

    protected void ExecuteLoop()
    {
        var stopwatch = new Stopwatch();
        var spinWait = new SpinWait();

        while (_isRunning && !ShouldStop())
        {
            var operation = GetNextOperation();
            if (operation == null) break;

            if (!ExecuteOperation(operation, stopwatch, spinWait))
                break;
        }

        LogModeEnd();
    }

    protected abstract KeyItemSettings? GetNextOperation();
    protected abstract bool ShouldStop();

    private bool ExecuteOperation(KeyItemSettings operation, Stopwatch stopwatch, SpinWait spinWait)
    {
        if (operation.Type == KeyItemType.Keyboard && operation.KeyCode.HasValue)
            return ExecuteSingleKey(operation.KeyCode.Value, stopwatch, spinWait);

        if (operation.Type == KeyItemType.Coordinates)
            return ExecuteCoordinate(operation.X, operation.Y, operation.Interval, stopwatch, spinWait);

        return true;
    }

    private bool ExecuteSingleKey(LyKeysCode key, Stopwatch stopwatch, SpinWait spinWait)
    {
        int keyInterval = _driverService.GetKeyInterval(key);
        stopwatch.Restart();

        try
        {
            _driverService.SendKeyDown(key);
            if (KeyPressInterval > 0 && !HighPrecisionDelay(KeyPressInterval))
            {
                _driverService.SendKeyUp(key);
                return false;
            }
            _driverService.SendKeyUp(key);

            _logger.Debug($"{GetType().Name} - 执行按键: {key}, 按下时长: {KeyPressInterval}ms, 间隔: {keyInterval}ms");

            var remainingDelay = Math.Max(0, keyInterval - stopwatch.ElapsedMilliseconds);
            return remainingDelay <= 0 || HighPrecisionDelay(remainingDelay);
        }
        catch (Exception ex)
        {
            _logger.Error($"执行按键异常: {key}, {ex.Message}", ex);
            _driverService.SendKeyUp(key);
            return false;
        }
    }

    private bool ExecuteCoordinate(int? x, int? y, int interval, Stopwatch stopwatch, SpinWait spinWait)
    {
        stopwatch.Restart();
        try
        {
            _driverService.MoveMouseToPosition(x, y);
            var remainingDelay = Math.Max(0, interval - stopwatch.ElapsedMilliseconds);
            return remainingDelay <= 0 || HighPrecisionDelay(remainingDelay);
        }
        catch (Exception ex)
        {
            _logger.Error($"执行坐标异常: ({x}, {y}), {ex.Message}", ex);
            return false;
        }
    }

    private bool HighPrecisionDelay(long delayMs)
    {
        if (delayMs <= 0) return true;
        var sw = Stopwatch.StartNew();
        if (delayMs > 15)
        {
            if (ShouldStop()) return false;
            Thread.Sleep((int)(delayMs - 5));
        }
        var spinWait = new SpinWait();
        while (sw.ElapsedMilliseconds < delayMs)
        {
            if (ShouldStop()) return false;
            spinWait.SpinOnce();
        }
        return true;
    }

    protected virtual void LogModeStart()
    {
        _logger.SequenceEvent("开始", $"模式: {GetType().Name} | 操作数: {_operationList.Count}");
    }

    protected virtual void LogModeEnd()
    {
        _logger.SequenceEvent("结束", $"模式: {GetType().Name} 已停止");
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing) _cts?.Dispose();
    }
}