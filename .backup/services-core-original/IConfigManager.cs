using System;
using WpfApp.Services.Models;

namespace WpfApp.Services.Core
{
    /// <summary>
    /// 统一配置管理接口
    /// </summary>
    public interface IConfigManager
    {
        /// <summary>
        /// 配置变更事件
        /// </summary>
        event EventHandler<ConfigEventArgs> ConfigChanged;

        /// <summary>
        /// 获取全局配置
        /// </summary>
        GlobalConfig GlobalConfig { get; }

        /// <summary>
        /// 获取当前按键配置
        /// </summary>
        KeyConfigData CurrentKeyConfig { get; }

        /// <summary>
        /// 获取多配置数据
        /// </summary>
        MultiKeyConfigData MultiKeyConfigData { get; }

        /// <summary>
        /// 初始化配置管理器
        /// </summary>
        void Initialize();

        /// <summary>
        /// 更新全局配置
        /// </summary>
        void UpdateGlobalConfig(Action<GlobalConfig> updateAction);

        /// <summary>
        /// 更新当前按键配置
        /// </summary>
        void UpdateKeyConfig(Action<KeyConfigData> updateAction);

        /// <summary>
        /// 更新多配置数据
        /// </summary>
        void UpdateMultiKeyConfig(Action<MultiKeyConfigData> updateAction);

        /// <summary>
        /// 资源清理
        /// </summary>
        void Cleanup();
    }
} 