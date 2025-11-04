using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using WpfApp.Services.Models;
using WpfApp.Services.Utils;

namespace WpfApp.Services.Core
{
    /// <summary>
    /// 按键列表管理服务 - 负责按键列表的增删改查和状态同步
    /// </summary>
    public class KeyListManagementService
    {
        private readonly LyKeysService _lyKeysService;
        private readonly HotkeyService? _hotkeyService;
        private readonly CoordinateManagementService _coordinateService;

        public event EventHandler? KeyListChanged;

        public KeyListManagementService(
            LyKeysService lyKeysService,
            HotkeyService? hotkeyService,
            CoordinateManagementService coordinateService)
        {
            _lyKeysService = lyKeysService ?? throw new ArgumentNullException(nameof(lyKeysService));
            _hotkeyService = hotkeyService; // 可选参数，允许为 null
            _coordinateService = coordinateService ?? throw new ArgumentNullException(nameof(coordinateService));
        }

        /// <summary>
        /// 添加键盘按键
        /// </summary>
        public KeyItem AddKeyboardKey(VirtualKeyCode keyCode, int interval, ObservableCollection<KeyItem> keyList, VirtualKeyCode? hotkey)
        {
            // 验证按键
            if (!_lyKeysService.IsValidVirtualKeyCode(keyCode))
            {
                throw new ArgumentException($"无效的按键码: {_lyKeysService.GetKeyDescription(keyCode)}");
            }

            // 检查热键冲突
            if (hotkey.HasValue && keyCode.Equals(hotkey.Value))
            {
                throw new InvalidOperationException($"按键 {_lyKeysService.GetKeyDescription(keyCode)} 与热键冲突");
            }

            // 创建按键项
            var keyItem = new KeyItem(keyCode, _lyKeysService)
            {
                KeyInterval = interval
            };

            // 添加到列表
            keyList.Add(keyItem);

            // 订阅事件
            SubscribeKeyItemEvents(keyItem);

            // 同步到热键服务
            SyncToHotkeyService(keyList);

            SerilogManager.Instance.Debug($"已添加按键: {keyCode}");
            KeyListChanged?.Invoke(this, EventArgs.Empty);

            return keyItem;
        }

        /// <summary>
        /// 添加坐标项
        /// </summary>
        public KeyItem AddCoordinate(int x, int y, int interval, ObservableCollection<KeyItem> keyList)
        {
            // 创建坐标项
            var coordinateItem = _coordinateService.CreateCoordinateItem(x, y, _lyKeysService, interval);

            // 添加到列表
            keyList.Add(coordinateItem);

            // 订阅事件
            SubscribeKeyItemEvents(coordinateItem);

            // 更新坐标索引
            _coordinateService.UpdateCoordinateIndices(keyList);

            // 同步到热键服务
            SyncToHotkeyService(keyList);

            SerilogManager.Instance.Debug($"已添加坐标: ({x}, {y})");
            KeyListChanged?.Invoke(this, EventArgs.Empty);

            return coordinateItem;
        }

        /// <summary>
        /// 删除按键项
        /// </summary>
        public void DeleteKey(KeyItem keyItem, ObservableCollection<KeyItem> keyList)
        {
            if (keyItem == null)
                throw new ArgumentNullException(nameof(keyItem));

            // 从列表中移除
            keyList.Remove(keyItem);

            // 如果是坐标类型，更新索引
            if (keyItem.Type == KeyItemType.Coordinates)
            {
                _coordinateService.UpdateCoordinateIndices(keyList);
            }

            // 同步到热键服务
            SyncToHotkeyService(keyList);

            SerilogManager.Instance.Debug($"已删除按键: {(keyItem.Type == KeyItemType.Keyboard ? keyItem.KeyCode.ToString() : $"坐标({keyItem.X},{keyItem.Y})")}");
            KeyListChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 同步按键列表到热键服务
        /// </summary>
        public void SyncToHotkeyService(ObservableCollection<KeyItem> keyList)
        {
            try
            {
                // 如果没有热键服务（例如在配置对话框中），跳过同步
                if (_hotkeyService == null)
                {
                    SerilogManager.Instance.Debug("热键服务未初始化，跳过同步");
                    return;
                }

                var selectedItems = keyList.Where(k => k.IsSelected).ToList();

                if (!selectedItems.Any())
                {
                    SerilogManager.Instance.Debug("没有选中的按键，跳过同步");
                    return;
                }

                var operations = new List<KeyItemSettings>();

                foreach (var item in selectedItems)
                {
                    if (item.Type == KeyItemType.Keyboard)
                    {
                        operations.Add(KeyItemSettings.CreateKeyboard(item.KeyCode, item.KeyInterval));
                    }
                    else if (item.Type == KeyItemType.Coordinates)
                    {
                        operations.Add(KeyItemSettings.CreateCoordinates(item.X, item.Y, item.KeyInterval));
                    }
                }

                _hotkeyService.SetKeySequence(operations);

                SerilogManager.Instance.Debug($"已同步操作列表 - 总数: {operations.Count}, 键盘: {operations.Count(o => o.Type == KeyItemType.Keyboard)}, 坐标: {operations.Count(o => o.Type == KeyItemType.Coordinates)}");
            }
            catch (Exception ex)
            {
                SerilogManager.Instance.Error("同步按键列表到热键服务失败", ex);
                throw;
            }
        }

        /// <summary>
        /// 订阅按键项事件
        /// </summary>
        private void SubscribeKeyItemEvents(KeyItem keyItem)
        {
            keyItem.SelectionChanged += (s, isSelected) =>
            {
                KeyListChanged?.Invoke(this, EventArgs.Empty);
            };

            keyItem.KeyIntervalChanged += (s, newInterval) =>
            {
                KeyListChanged?.Invoke(this, EventArgs.Empty);
            };
        }

        /// <summary>
        /// 从配置加载按键列表
        /// </summary>
        public void LoadFromConfig(List<KeyConfig> keyConfigs, ObservableCollection<KeyItem> keyList)
        {
            keyList.Clear();

            if (keyConfigs == null || keyConfigs.Count == 0)
            {
                SerilogManager.Instance.Debug("没有按键配置需要加载");
                return;
            }

            foreach (var key in keyConfigs)
            {
                KeyItem item = null;

                if (key.Type == KeyItemType.Keyboard && key.Code.HasValue)
                {
                    item = new KeyItem(key.Code.Value, _lyKeysService)
                    {
                        IsSelected = key.IsSelected,
                        KeyInterval = key.KeyInterval
                    };
                }
                else if (key.Type == KeyItemType.Coordinates && key.X.HasValue && key.Y.HasValue)
                {
                    item = new KeyItem(key.X.Value, key.Y.Value, _lyKeysService)
                    {
                        IsSelected = key.IsSelected,
                        KeyInterval = key.KeyInterval
                    };
                }

                if (item != null)
                {
                    SubscribeKeyItemEvents(item);
                    keyList.Add(item);
                }
            }

            // 更新坐标索引
            _coordinateService.UpdateCoordinateIndices(keyList);

            SerilogManager.Instance.Debug($"已加载按键列表，总数: {keyList.Count}");
        }

        /// <summary>
        /// 转换为配置格式
        /// </summary>
        public List<KeyConfig> ToConfigFormat(ObservableCollection<KeyItem> keyList)
        {
            var keyConfigs = new List<KeyConfig>();

            foreach (var item in keyList)
            {
                KeyConfig itemConfig;

                if (item.Type == KeyItemType.Keyboard)
                {
                    itemConfig = new KeyConfig(item.KeyCode, item.IsSelected)
                    {
                        KeyInterval = item.KeyInterval,
                        Type = KeyItemType.Keyboard,
                        X = null,
                        Y = null
                    };
                }
                else // Coordinates
                {
                    int? x = item.X;
                    int? y = item.Y;

                    if ((x ?? 0) == 0 && (y ?? 0) == 0)
                    {
                        SerilogManager.Instance.Warning($"修正无效的坐标配置: ({x}, {y}) => (1, 1)");
                        x = 1;
                        y = 1;
                    }

                    itemConfig = new KeyConfig(x ?? 1, y ?? 1, item.IsSelected)
                    {
                        KeyInterval = item.KeyInterval,
                        Type = KeyItemType.Coordinates,
                        Code = null
                    };
                }

                keyConfigs.Add(itemConfig);
            }

            return keyConfigs;
        }
    }
}
