using System.Collections.ObjectModel;
using System.Windows.Input;
using WpfApp.Services.Models;
using WpfApp.Services.Utils;

// 按键映射服务
namespace WpfApp.Services.Core;

public class KeyMappingService
{
    private readonly SerilogManager _logger = SerilogManager.Instance;
    private readonly LyKeysService _lyKeysService;
    private readonly HotkeyService _hotkeyService;
    private ObservableCollection<KeyItem> _keyList;
    private VirtualKeyCode? _hotkey;    // 触发热键-主按键
    private ModifierKeys _modifiers = ModifierKeys.None; // 触发热键-修饰键

    public event Action<bool>? ExecutionStateChanged;
    public event Action? KeyListChanged;

    public KeyMappingService(
        LyKeysService lyKeysService,
        HotkeyService hotkeyService
        )
    {
        _lyKeysService = lyKeysService;
        _hotkeyService = hotkeyService;
        _keyList = new ObservableCollection<KeyItem>();

        InitializeEventHandlers();
    }

    public ObservableCollection<KeyItem> KeyList => _keyList;

    public bool IsExecuting { get; private set; }

    /// <summary>
    /// 全局默认按键间隔（仅用于新添加的按键）
    /// </summary>
    public int KeyInterval
    {
        get => _lyKeysService.KeyInterval;
        set => _lyKeysService.KeyInterval = value;
    }

    private void InitializeEventHandlers()
    {
        _hotkeyService.StartHotkeyPressed += OnStartHotkeyPressed;
        _hotkeyService.StartHotkeyReleased += OnStartHotkeyReleased; // 订阅热键释放事件
    }

    public void AddKey(VirtualKeyCode keyCode)
    {
        if (IsKeyInList(keyCode) || IsHotkeyConflict(keyCode))
        {
            _logger.Warning($"按键 {keyCode} 已存在或与热键冲突");
            return;
        }

        var newKeyItem = new KeyItem(keyCode, _lyKeysService);
        SubscribeKeyItemEvents(newKeyItem);

        _keyList.Add(newKeyItem);
        UpdateHotkeyServiceKeyList();
        KeyListChanged?.Invoke();
    }

    public void RemoveKey(KeyItem keyItem)
    {
        if (_keyList.Remove(keyItem))
        {
            UpdateHotkeyServiceKeyList();
            KeyListChanged?.Invoke();
        }
    }

    // 合并热键设置方法
    public void SetHotkey(VirtualKeyCode keyCode, ModifierKeys modifiers)
    {
        if (IsKeyInList(keyCode))
        {
            _logger.Warning($"热键 {keyCode} 与现有按键冲突");
            return;
        }

        if (_hotkeyService.RegisterHotkey(keyCode, modifiers))
        {
            _hotkey = keyCode;
            _modifiers = modifiers;
            _logger.Debug($"设置热键: {keyCode}, 修饰键: {modifiers}");
        }
    }

    public void StartKeyMapping(bool isHoldMode = false)
    {
        if (IsExecuting) return;

        var selectedItems = GetSelectedItems();

        if (selectedItems.Count == 0)
        {
            _logger.Warning("没有选中的按键");
            return;
        }

        try
        {
            // 创建按键设置，支持不同类型
            var keySettings = CreateKeySettings(selectedItems);

            // 传递给HotkeyService所有类型的按键
            _hotkeyService.SetKeySequence(keySettings);

            _lyKeysService.IsHoldMode = isHoldMode;

            IsExecuting = true;
            ExecutionStateChanged?.Invoke(true);
            _logger.Debug($"开始按键映射: 模式={isHoldMode}, 按键数={selectedItems.Count}");
        }
        catch (Exception ex)
        {
            _logger.Error("启动按键映射失败", ex);
            StopKeyMapping();
        }
    }

    public void StopKeyMapping()
    {
        if (!IsExecuting) return;

        try
        {
            _hotkeyService.StopSequence();
            _lyKeysService.IsHoldMode = false;

            IsExecuting = false;
            ExecutionStateChanged?.Invoke(false);
            _logger.Debug("停止按键映射");
        }
        catch (Exception ex)
        {
            _logger.Error("停止按键映射失败", ex);
        }
    }

    private void OnStartHotkeyPressed()
    {
        // 根据当前模式决定是启动还是停止
        if (!_lyKeysService.IsHoldMode)
        {
            // 单次模式下，根据当前执行状态决定启动或停止
            if (IsExecuting)
            {
                StopKeyMapping();
            }
            else
            {
                StartKeyMapping();
            }
        }
        else
        {
            // 按压模式下，按下时启动
            StartKeyMapping(true);
        }
    }

    private void OnStartHotkeyReleased()
    {
        // 在按压模式下，释放热键时停止
        if (_lyKeysService.IsHoldMode && IsExecuting)
        {
            StopKeyMapping();
        }
    }

    private void UpdateHotkeyServiceKeyList()
    {
        try
        {
            // 获取选中的按键列表
            var selectedItems = GetSelectedItems();

            // 创建KeyItemSettings
            var keySettings = CreateKeySettings(selectedItems);

            // 将所有设置（包括键盘和坐标）传递给HotkeyService
            _hotkeyService.SetKeySequence(keySettings);

            int keyboardCount = keySettings.Count(k => k.Type == KeyItemType.Keyboard);
            _logger.Debug($"更新按键列表 - 选中按键数: {selectedItems.Count}, 键盘按键数: {keyboardCount}");
        }
        catch (Exception ex)
        {
            _logger.Error("更新按键列表失败", ex);
        }
    }

    /// <summary>
    /// 获取所有选中的按键（包括键盘和坐标类型）
    /// </summary>
    private List<KeyItem> GetSelectedItems()
    {
        return _keyList.Where(k => k.IsSelected).ToList();
    }

    private bool IsKeyInList(VirtualKeyCode keyCode)
    {
        return _keyList.Any(k => k.Type == KeyItemType.Keyboard && k.KeyCode.Equals(keyCode));
    }

    private bool IsHotkeyConflict(VirtualKeyCode keyCode)
    {
        // 简化为只检查单一热键
        return _hotkey.HasValue && keyCode.Equals(_hotkey.Value);
    }

    /// <summary>
    /// 从 KeyItem 列表创建 KeyItemSettings 列表
    /// </summary>
    private List<KeyItemSettings> CreateKeySettings(List<KeyItem> items)
    {
        return items.Select(k =>
        {
            if (k.Type == KeyItemType.Keyboard)
            {
                return KeyItemSettings.CreateKeyboard(k.KeyCode, k.KeyInterval);
            }
            else // KeyItemType.Coordinates
            {
                return KeyItemSettings.CreateCoordinates(k.X, k.Y, k.KeyInterval);
            }
        }).ToList();
    }

    /// <summary>
    /// 为 KeyItem 订阅事件
    /// </summary>
    private void SubscribeKeyItemEvents(KeyItem keyItem)
    {
        keyItem.SelectionChanged += (s, isSelected) =>
        {
            UpdateHotkeyServiceKeyList();
            KeyListChanged?.Invoke();
        };
    }

    public void LoadConfiguration(KeyConfigData config)
    {
        if (config.keys == null) return;

        _keyList.Clear();
        foreach (var keyConfig in config.keys)
        {
            KeyItem keyItem;
            
            // 根据类型创建不同的KeyItem
            if (keyConfig.Type == KeyItemType.Keyboard && keyConfig.Code.HasValue)
            {
                // 创建键盘按键
                keyItem = new KeyItem(keyConfig.Code.Value, _lyKeysService)
                {
                    IsSelected = keyConfig.IsSelected,
                    KeyInterval = keyConfig.KeyInterval
                };
            }
            else if (keyConfig.Type == KeyItemType.Coordinates)
            {
                // 创建坐标按键 - 处理可空类型
                int xValue = keyConfig.X ?? 1;
                int yValue = keyConfig.Y ?? 1;
                keyItem = new KeyItem(xValue, yValue, _lyKeysService)
                {
                    IsSelected = keyConfig.IsSelected,
                    KeyInterval = keyConfig.KeyInterval
                };
            }
            else
            {
                // 跳过无效的配置项
                _logger.Warning($"跳过无效的按键配置: 类型={keyConfig.Type}, Code={keyConfig.Code}");
                continue;
            }

            SubscribeKeyItemEvents(keyItem);
            _keyList.Add(keyItem);
        }

        // 只设置一个热键
        if (config.startKey.HasValue) SetHotkey(config.startKey.Value, config.startMods);

        UpdateHotkeyServiceKeyList();
    }

}