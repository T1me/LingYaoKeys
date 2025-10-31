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
        while (_isRunning && !ShouldStop())
        {
            var operation = GetNextOperation();
            if (operation == null) break;

            if (!ExecuteOperation(operation))
                break;
        }

        LogModeEnd();
    }

    protected abstract KeyItemSettings? GetNextOperation();
    protected abstract bool ShouldStop();

    private bool ExecuteOperation(KeyItemSettings operation)
    {
        if (operation.Type == KeyItemType.Keyboard && operation.KeyCode.HasValue)
            return ExecuteSingleKey(operation);

        if (operation.Type == KeyItemType.Coordinates)
            return ExecuteCoordinate(operation.X, operation.Y, operation.Interval);

        return true;
    }

    private bool ExecuteSingleKey(KeyItemSettings operation)
    {
        var key = operation.KeyCode.Value;
        int keyInterval = operation.Interval;

        try
        {
            _driverService.SendKeyDown(key);

            if (KeyPressInterval > 0)
            {
                Thread.Sleep(KeyPressInterval);
                if (ShouldStop())
                {
                    _driverService.SendKeyUp(key);
                    return false;
                }
            }

            _driverService.SendKeyUp(key);

            _logger.Debug($"{GetType().Name} - 执行按键: {key}, 按下时长: {KeyPressInterval}ms, 间隔: {keyInterval}ms");

            if (keyInterval > 0)
            {
                Thread.Sleep(keyInterval);
            }

            return !ShouldStop();
        }
        catch (Exception ex)
        {
            _logger.Error($"执行按键异常: {key}, {ex.Message}", ex);
            _driverService.SendKeyUp(key);
            return false;
        }
    }

    private bool ExecuteCoordinate(int? x, int? y, int interval)
    {
        try
        {
            _driverService.MoveMouseToPosition(x, y);

            if (interval > 0)
            {
                Thread.Sleep(interval);
            }

            return !ShouldStop();
        }
        catch (Exception ex)
        {
            _logger.Error($"执行坐标异常: ({x}, {y}), {ex.Message}", ex);
            return false;
        }
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