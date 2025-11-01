using System;
using System.Collections.Generic;
using System.Linq;
using WpfApp.Services.Models;
using WpfApp.Services.Utils;

namespace WpfApp.Services.Core
{
    /// <summary>
    /// 坐标管理服务 - 负责坐标项的添加、验证和索引管理
    /// </summary>
    public class CoordinateManagementService
    {
        public event EventHandler? CoordinateIndicesUpdated;

        /// <summary>
        /// 验证坐标是否有效
        /// </summary>
        public bool ValidateCoordinate(int? x, int? y, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!x.HasValue || !y.HasValue)
            {
                errorMessage = "X或Y坐标没有填写";
                return false;
            }

            if (x.Value == 0 && y.Value == 0)
            {
                errorMessage = "坐标不能同时为(0,0)";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 创建坐标项
        /// </summary>
        public KeyItem CreateCoordinateItem(int x, int y, LyKeysService lyKeysService, int defaultInterval = 10)
        {
            if (!ValidateCoordinate(x, y, out var errorMessage))
            {
                throw new ArgumentException(errorMessage);
            }

            var coordinateItem = new KeyItem(x, y, lyKeysService)
            {
                KeyInterval = defaultInterval
            };

            SerilogManager.Instance.Debug($"创建坐标项: ({x}, {y}), 间隔: {defaultInterval}ms");
            return coordinateItem;
        }

        /// <summary>
        /// 更新所有坐标项的索引
        /// </summary>
        public void UpdateCoordinateIndices(IEnumerable<KeyItem> keyList)
        {
            try
            {
                var coordinateItems = keyList.Where(item => item.Type == KeyItemType.Coordinates).ToList();

                for (int i = 0; i < coordinateItems.Count; i++)
                {
                    coordinateItems[i].CoordinateIndex = i;
                    SerilogManager.Instance.Debug($"设置坐标索引: 项目={i}, 坐标=({coordinateItems[i].X},{coordinateItems[i].Y})");
                }

                CoordinateIndicesUpdated?.Invoke(this, EventArgs.Empty);
                SerilogManager.Instance.Debug($"坐标索引更新完成，共 {coordinateItems.Count} 个坐标项");
            }
            catch (Exception ex)
            {
                SerilogManager.Instance.Error("更新坐标索引时发生异常", ex);
                throw;
            }
        }

        /// <summary>
        /// 获取坐标项数量
        /// </summary>
        public int GetCoordinateCount(IEnumerable<KeyItem> keyList)
        {
            return keyList.Count(item => item.Type == KeyItemType.Coordinates);
        }

        /// <summary>
        /// 获取所有坐标项
        /// </summary>
        public List<KeyItem> GetCoordinateItems(IEnumerable<KeyItem> keyList)
        {
            return keyList.Where(item => item.Type == KeyItemType.Coordinates).ToList();
        }
    }
}
