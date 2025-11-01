using System;
using WpfApp.Services.Models;

namespace WpfApp.Services.Core
{
    /// <summary>
    /// 配置变更事件参数
    /// </summary>
    public class ConfigEventArgs : EventArgs
    {
        /// <summary>
        /// 变更的配置类型
        /// </summary>
        public ConfigChangeType ChangeType { get; }

        /// <summary>
        /// 全局配置（当ChangeType为Global或All时有效）
        /// </summary>
        public GlobalConfig? GlobalConfigData { get; }

        /// <summary>
        /// 按键配置（当ChangeType为Key或All时有效）
        /// </summary>
        public KeyConfigData? KeyConfigData { get; }

        public ConfigEventArgs(ConfigChangeType changeType, GlobalConfig? globalConfig = null,
            KeyConfigData? keyConfig = null)
        {
            ChangeType = changeType;
            GlobalConfigData = globalConfig;
            KeyConfigData = keyConfig;
        }
    }

    /// <summary>
    /// 配置变更类型
    /// </summary>
    public enum ConfigChangeType
    {
        /// <summary>
        /// 全局配置变更
        /// </summary>
        Global,

        /// <summary>
        /// 按键配置变更
        /// </summary>
        Key,

        /// <summary>
        /// 所有配置变更
        /// </summary>
        All
    }
    
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
        /// 资源清理
        /// </summary>
        void Cleanup();
    }
} 