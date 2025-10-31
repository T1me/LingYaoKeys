using WpfApp.Services.Models;
using WpfApp.Services.Utils;

namespace WpfApp.Services.Core;

/// <summary>
/// 按键序列执行器 - 统一管理按键执行的状态和副作用
/// </summary>
public class KeySequenceExecutor
{
    private readonly LyKeysService _driverService;
    private readonly InputMethodService _inputMethodService;
    private readonly AudioService _audioService;
    private readonly IConfigManager _configManager;
    private readonly SerilogManager _logger;

    private KeyModeBase? _currentMode;
    private bool _isRunning;
    private Action? _onCompleted;
    private bool _isHoldMode;

    public KeySequenceExecutor(
        LyKeysService driverService,
        InputMethodService inputMethodService,
        AudioService audioService,
        IConfigManager configManager)
    {
        _driverService = driverService;
        _inputMethodService = inputMethodService;
        _audioService = audioService;
        _configManager = configManager;
        _logger = SerilogManager.Instance;
    }

    /// <summary>
    /// 启动按键序列执行
    /// </summary>
    public void Start(List<KeyItemSettings> operations, bool isHoldMode, Action? onCompleted = null)
    {
        if (_isRunning || operations == null || operations.Count == 0) return;

        _isRunning = true;
        _isHoldMode = isHoldMode;
        _onCompleted = onCompleted;

        // 副作用：输入法切换
        if (_configManager.GlobalConfig.AutoSwitchToEnglishIME == true)
        {
            _inputMethodService.StoreCurrentLayout();
            _inputMethodService.SwitchToEnglish();
        }

        // 副作用：播放启动音效
        if (_configManager.GlobalConfig.soundEnabled == true)
        {
            _audioService.PlayStartSound();
        }

        // 创建并启动模式
        _currentMode = isHoldMode
            ? new HoldKeyMode(_driverService)
            : new SequenceKeyMode(_driverService);

        _currentMode.SetOperationList(operations);
        _currentMode.Start(() => OnExecutionCompleted());
    }

    /// <summary>
    /// 停止按键序列执行
    /// </summary>
    public void Stop()
    {
        if (!_isRunning) return;

        _currentMode?.Stop();
        OnExecutionCompleted();
    }

    /// <summary>
    /// 紧急停止
    /// </summary>
    public void EmergencyStop()
    {
        _currentMode?.Stop();
        OnExecutionCompleted();
    }

    /// <summary>
    /// 执行完成处理
    /// </summary>
    private void OnExecutionCompleted()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _currentMode = null;

        // 副作用：恢复输入法
        if (_configManager.GlobalConfig.AutoSwitchToEnglishIME == true)
        {
            _inputMethodService.RestorePreviousLayout();
        }

        // 副作用：播放停止音效
        if (_configManager.GlobalConfig.soundEnabled == true)
        {
            _audioService.PlayStopSound();
        }

        // 回调通知
        _onCompleted?.Invoke();
    }

    /// <summary>
    /// 获取当前运行状态
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// 获取当前模式
    /// </summary>
    public bool IsHoldMode => _isHoldMode;
}
