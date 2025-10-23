using System;
using System.Collections.ObjectModel;
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
        
        /// <summary>
        /// 配置文件（当ChangeType为ConfigFile或ConfigList时有效）
        /// </summary>
        public ConfigFileInfo? ConfigFile { get; }

        public ConfigEventArgs(ConfigChangeType changeType, GlobalConfig? globalConfig = null, 
            KeyConfigData? keyConfig = null, ConfigFileInfo? configFile = null)
        {
            ChangeType = changeType;
            GlobalConfigData = globalConfig;
            KeyConfigData = keyConfig;
            ConfigFile = configFile;
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
        All,
        
        /// <summary>
        /// 配置文件变更（切换当前配置）
        /// </summary>
        ConfigFile,
        
        /// <summary>
        /// 配置文件列表变更（新增、删除、重命名）
        /// </summary>
        ConfigList
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
        /// 获取所有配置文件
        /// </summary>
        ObservableCollection<ConfigFileInfo> ConfigFiles { get; }
        
        /// <summary>
        /// 获取当前配置文件
        /// </summary>
        ConfigFileInfo CurrentConfig { get; }
        
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
        /// 切换当前配置文件
        /// </summary>
        void SwitchConfig(ConfigFileInfo configInfo);
        
        /// <summary>
        /// 创建新配置文件
        /// </summary>
        ConfigFileInfo CreateNewConfig(string configName, bool copyFromCurrent = true);
        
        /// <summary>
        /// 重命名配置文件
        /// </summary>
        void RenameConfig(ConfigFileInfo configInfo, string newName);
        
        /// <summary>
        /// 删除配置文件
        /// </summary>
        void DeleteConfig(ConfigFileInfo configInfo);
        
        /// <summary>
        /// 设置配置文件快捷键
        /// </summary>
        void SetConfigHotkey(ConfigFileInfo configInfo, string hotkeyText);
        
        /// <summary>
        /// 导入配置文件
        /// </summary>
        ConfigFileInfo ImportKeyConfig(string sourceFile, string configName = null);
        
        /// <summary>
        /// 导出配置文件
        /// </summary>
        void ExportKeyConfig(string targetFile, ConfigFileInfo configInfo = null);
        
        /// <summary>
        /// 资源清理
        /// </summary>
        void Cleanup();
    }
} 