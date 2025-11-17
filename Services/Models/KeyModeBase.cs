using WpfApp.Services.Core;
using WpfApp.Services.Utils;

// 按键模式基类
namespace WpfApp.Services.Models;

public abstract class KeyModeBase
{
    protected readonly LyKeysService _driverService;
    protected readonly ISerilogManager _logger;
    protected List<KeyItemSettings> _operationList;
    protected bool _isRunning;
    protected CancellationTokenSource? _cts;
    protected int KeyPressInterval => _driverService.KeyPressInterval;
    protected KeyConfiguration? _config;

    protected Action? _onCompleted;

    protected KeyModeBase(ISerilogManager logger, LyKeysService driverService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _driverService = driverService ?? throw new ArgumentNullException(nameof(driverService));
        _operationList = new List<KeyItemSettings>();
    }

    public void SetConfiguration(KeyConfiguration? config)
    {
        _config = config;
    }

    public virtual void SetOperationList(List<KeyItemSettings> operations)
    {
        _operationList = operations == null ? new List<KeyItemSettings>() : new List<KeyItemSettings>(operations);
    }

    public void Start(Action? onCompleted = null)
    {
        _onCompleted = onCompleted;
        StartInternal();
    }

    protected abstract void StartInternal();

    protected void NotifyCompleted()
    {
        _onCompleted?.Invoke();
    }

    public virtual void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
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
            return ExecuteCoordinate(operation);

        return true;
    }

    private bool ExecuteSingleKey(KeyItemSettings operation)
    {
        var key = operation.KeyCode.Value;
        int keyInterval = operation.Interval;

        // 计算实际按压时长：基础HoldDuration + 降低卡位的5ms
        int actualHoldDuration = operation.HoldDuration;
        if (_config?.IsReduceKeyStuck == true)
        {
            actualHoldDuration += 5;
        }

        try
        {
            _driverService.SendKeyDown(key);

            // 使用实际按压时长
            if (actualHoldDuration > 0)
            {
                Thread.Sleep(actualHoldDuration);
                if (ShouldStop())
                {
                    _driverService.SendKeyUp(key);
                    return false;
                }
            }

            _driverService.SendKeyUp(key);

            if (keyInterval > 0)
            {
                Thread.Sleep(keyInterval);
            }

            return !ShouldStop();
        }
        catch (Exception ex)
        {
            _logger.Error($"执行按键异常: {key}", ex);
            _driverService.SendKeyUp(key);
            return false;
        }
    }

    private bool ExecuteCoordinate(KeyItemSettings operation)
    {
        int? x = operation.X;
        int? y = operation.Y;
        int interval = operation.Interval;

        try
        {
            // 移动鼠标到目标位置（仅移动，不点击）
            _driverService.MoveMouseToPosition(x, y);

            // 等待延迟时间
            if (interval > 0)
            {
                Thread.Sleep(interval);
            }

            return !ShouldStop();
        }
        catch (Exception ex)
        {
            _logger.Error($"执行坐标异常: ({x}, {y})", ex);
            return false;
        }
    }


    protected virtual void LogModeStart()
    {
        _logger.Info($"{GetType().Name} 启动 - 操作数: {_operationList.Count}");
    }

    protected virtual void LogModeEnd()
    {
        _logger.Info($"{GetType().Name} 停止");
    }
}