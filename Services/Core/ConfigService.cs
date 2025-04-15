using System.IO;
using System.Diagnostics;
using Newtonsoft.Json;
using WpfApp.Services.Utils;
using WpfApp.Services.Models;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfApp.Services.Core;

/// <summary>
/// 配置文件信息类
/// </summary>
public class ConfigFileInfo : INotifyPropertyChanged
{
    private string _name = "默认配置";
    private string _filePath = string.Empty;
    private string _configHotkey = string.Empty;
    private bool _isDefault = false;
    private DateTime? _lastEditTime;
    
    public event PropertyChangedEventHandler PropertyChanged;
    
    // 触发属性变更事件
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
    // 设置属性值并通知变更
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
    
    public string Name 
    { 
        get => _name;
        set => SetProperty(ref _name, value);
    }
    
    public string FilePath 
    { 
        get => _filePath; 
        set => SetProperty(ref _filePath, value);
    }
    
    public string ConfigHotkey 
    { 
        get => _configHotkey; 
        set
        {
            if (SetProperty(ref _configHotkey, value))
            {
                OnPropertyChanged(nameof(HasConfigHotkey));
            }
        }
    }
    
    public bool HasConfigHotkey => !string.IsNullOrEmpty(_configHotkey);
    
    public bool IsDefault 
    { 
        get => _isDefault; 
        set => SetProperty(ref _isDefault, value);
    }
    
    /// <summary>
    /// 配置文件最后编辑时间
    /// </summary>
    public DateTime? LastEditTime 
    { 
        get => _lastEditTime; 
        set => SetProperty(ref _lastEditTime, value);
    }
    
    public ConfigFileInfo() 
    {
        LastEditTime = DateTime.Now;
    }
    
    public ConfigFileInfo(string name, string filePath, bool isDefault = false)
    {
        _name = name;
        _filePath = filePath;
        _isDefault = isDefault;
        _lastEditTime = DateTime.Now;
    }
    
    /// <summary>
    /// 更新最后编辑时间
    /// </summary>
    public void UpdateEditTime()
    {
        LastEditTime = DateTime.Now;
    }
}

/// <summary>
/// 配置服务 - 负责管理配置文件
/// </summary>
public class ConfigService
{
    private readonly SerilogManager _logger = SerilogManager.Instance;
    private readonly PathService _pathService = PathService.Instance;

    private readonly string _configDir;
    private const int MAX_BACKUP_FILES = 5;
    private const string CONFIG_INDEX_FILE = "config_index.json";
    private Dictionary<string, object> _settings;
    private ObservableCollection<ConfigFileInfo> _configFiles;
    private ConfigFileInfo _currentConfig;
    
    public event EventHandler<ConfigFileInfo> ConfigFileChanged;
    public event EventHandler ConfigListChanged;

    // 触发ConfigFileChanged事件的辅助方法
    private void RaiseConfigFileChanged(ConfigFileInfo configInfo)
    {
        _logger.Debug($"触发ConfigFileChanged事件: {configInfo?.Name ?? "null"}");
        ConfigFileChanged?.Invoke(this, configInfo);
    }
    
    // 触发ConfigListChanged事件的辅助方法
    private void OnConfigListChanged()
    {
        _logger.Debug("触发ConfigListChanged事件");
        ConfigListChanged?.Invoke(this, EventArgs.Empty);
    }
    
    public ObservableCollection<ConfigFileInfo> ConfigFiles 
    { 
        get => _configFiles; 
        private set => _configFiles = value; 
    }
    
    public ConfigFileInfo CurrentConfig
    {
        get => _currentConfig;
        set
        {
            if (_currentConfig != value)
            {
                var oldConfig = _currentConfig?.Name;
                _currentConfig = value;
                _logger.Debug($"CurrentConfig已更改: {oldConfig} -> {_currentConfig?.Name}");
                RaiseConfigFileChanged(_currentConfig);
            }
        }
    }

    public ConfigService()
    {
        _configDir = _pathService.ConfigPath;
        _configFiles = new ObservableCollection<ConfigFileInfo>();
        _settings = LoadSettings();
        
        // 初始化配置文件列表
        InitializeConfigFiles();
    }
    
    /// <summary>
    /// 初始化配置文件列表
    /// </summary>
    private void InitializeConfigFiles()
    {
        try
        {
            var indexPath = Path.Combine(_configDir, CONFIG_INDEX_FILE);
            
            if (!File.Exists(indexPath))
            {
                // 创建默认配置文件索引
                CreateDefaultConfigIndex();
            }
            else
            {
                // 加载现有配置文件索引
                var json = File.ReadAllText(indexPath);
                var files = JsonConvert.DeserializeObject<List<ConfigFileInfo>>(json);
                
                if (files == null || files.Count == 0)
                {
                    // 索引文件为空或无效，创建默认配置
                    CreateDefaultConfigIndex();
                }
                else
                {
                    _configFiles.Clear();
                    foreach (var file in files)
                    {
                        // 检查配置文件是否存在
                        if (File.Exists(file.FilePath))
                        {
                            _configFiles.Add(file);
                        }
                    }
                    
                    // 如果没有默认配置，设置第一个为默认
                    if (!_configFiles.Any(c => c.IsDefault))
                    {
                        if (_configFiles.Count > 0)
                        {
                            _configFiles[0].IsDefault = true;
                        }
                        else
                        {
                            // 如果没有任何有效配置，创建默认配置
                            CreateDefaultConfigIndex();
                        }
                    }
                }
            }
            
            // 设置当前配置为默认配置
            _currentConfig = _configFiles.FirstOrDefault(c => c.IsDefault) ?? _configFiles.FirstOrDefault();
            
            // 保存配置索引
            SaveConfigIndex();
        }
        catch (Exception ex)
        {
            _logger.Error("初始化配置文件列表失败", ex);
            CreateDefaultConfigIndex();
        }
    }
    
    /// <summary>
    /// 创建默认配置文件索引
    /// </summary>
    private void CreateDefaultConfigIndex()
    {
        _configFiles.Clear();
        
        // 添加默认配置
        var defaultConfig = new ConfigFileInfo
        {
            Name = "默认配置",
            FilePath = _pathService.GetKeyConfigPath(),
            IsDefault = true
        };
        
        _configFiles.Add(defaultConfig);
        _currentConfig = defaultConfig;
        
        // 保存配置索引
        SaveConfigIndex();
    }
    
    /// <summary>
    /// 保存配置文件索引
    /// </summary>
    private void SaveConfigIndex()
    {
        try
        {
            var indexPath = Path.Combine(_configDir, CONFIG_INDEX_FILE);
            var json = JsonConvert.SerializeObject(_configFiles, Formatting.Indented);
            File.WriteAllText(indexPath, json);
        }
        catch (Exception ex)
        {
            _logger.Error("保存配置文件索引失败", ex);
        }
    }

    private Dictionary<string, object> LoadSettings()
    {
        try
        {
            var appConfigPath = _pathService.GetGlobalConfigPath();
            if (File.Exists(appConfigPath))
            {
                var json = File.ReadAllText(appConfigPath);
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(json)
                       ?? new Dictionary<string, object>();
            }
        }
        catch (Exception ex)
        {
            _logger.Error("加载设置失败", ex);
        }

        return new Dictionary<string, object>();
    }

    public T GetSetting<T>(string key, T defaultValue)
    {
        if (_settings.TryGetValue(key, out var value))
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }

        return defaultValue;
    }

    public void SaveSetting(string key, object value)
    {
        _settings[key] = value;
        SaveSettings();
    }

    private void SaveSettings()
    {
        try
        {
            var appConfigPath = _pathService.GetGlobalConfigPath();
            Directory.CreateDirectory(_configDir);
            var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
            File.WriteAllText(appConfigPath, json);
        }
        catch (Exception ex)
        {
            _logger.Error("保存设置失败", ex);
        }
    }
    
    /// <summary>
    /// 创建新的配置文件
    /// </summary>
    /// <param name="configName">配置文件名称</param>
    /// <param name="copyFromCurrent">是否从当前配置复制</param>
    /// <returns>新创建的配置文件信息</returns>
    public ConfigFileInfo CreateNewConfig(string configName, bool copyFromCurrent = true)
    {
        try
        {
            // 验证配置名称
            var validName = ValidateConfigName(configName);
            
            // 创建新配置文件路径
            var newConfigPath = Path.Combine(_configDir, $"{validName}.json");
            
            // 创建新配置信息
            var newConfig = new ConfigFileInfo(validName, newConfigPath);
            
            // 如果当前有配置且选择复制
            if (copyFromCurrent && _currentConfig != null)
            {
                // 复制当前配置文件内容
                File.Copy(_currentConfig.FilePath, newConfigPath);
            }
            else
            {
                // 创建空配置
                var emptyConfig = new KeyConfigData();
                var json = JsonConvert.SerializeObject(emptyConfig, Formatting.Indented);
                File.WriteAllText(newConfigPath, json);
            }
            
            // 设置最后编辑时间
            newConfig.LastEditTime = DateTime.Now;
            
            // 添加到配置列表
            _configFiles.Add(newConfig);
            
            // 保存配置索引
            SaveConfigIndex();
            
            // 通知列表变更
            OnConfigListChanged();
            
            return newConfig;
        }
        catch (Exception ex)
        {
            _logger.Error($"创建新配置失败: {configName}", ex);
            throw;
        }
    }
    
    /// <summary>
    /// 重命名配置文件
    /// </summary>
    /// <param name="configInfo">要重命名的配置文件信息</param>
    /// <param name="newName">新名称</param>
    public void RenameConfig(ConfigFileInfo configInfo, string newName)
    {
        if (configInfo == null) return;
        
        try
        {
            // 验证配置名称
            newName = ValidateConfigName(newName, configInfo);
            
            // 保存旧文件路径
            string oldFilePath = configInfo.FilePath;
            
            // 生成新文件路径
            string newFilePath = Path.Combine(_configDir, $"{newName}.json");
            
            // 检查文件是否存在
            if (File.Exists(oldFilePath))
            {
                // 检查目标文件是否已存在（防止路径冲突）
                if (File.Exists(newFilePath) && !string.Equals(oldFilePath, newFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"目标文件已存在: {newFilePath}");
                }
                
                // 重命名文件
                if (!string.Equals(oldFilePath, newFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Move(oldFilePath, newFilePath);
                    _logger.Debug($"重命名文件: {oldFilePath} -> {newFilePath}");
                }
            }
            else
            {
                _logger.Warning($"源文件不存在，无法重命名物理文件: {oldFilePath}");
            }
            
            // 更新名称和文件路径
            configInfo.Name = newName;
            configInfo.FilePath = newFilePath;
            configInfo.UpdateEditTime(); // 更新编辑时间
            
            // 保存索引
            SaveConfigIndex();
            
            // 通知列表变更
            OnConfigListChanged();
        }
        catch (Exception ex)
        {
            _logger.Error($"重命名配置文件失败: {configInfo.Name} -> {newName}", ex);
            throw;
        }
    }
    
    /// <summary>
    /// 删除配置文件
    /// </summary>
    /// <param name="configInfo">要删除的配置文件信息</param>
    public void DeleteConfig(ConfigFileInfo configInfo)
    {
        if (configInfo == null) return;
        
        try
        {
            // 不能删除默认配置
            if (configInfo.IsDefault)
            {
                throw new InvalidOperationException("无法删除默认配置");
            }
            
            // 移除文件
            if (File.Exists(configInfo.FilePath))
            {
                File.Delete(configInfo.FilePath);
            }
            
            // 移除配置信息
            _configFiles.Remove(configInfo);
            
            // 如果删除的是当前配置，切换到默认配置
            if (_currentConfig == configInfo)
            {
                _currentConfig = _configFiles.FirstOrDefault(c => c.IsDefault) ?? _configFiles.FirstOrDefault();
                RaiseConfigFileChanged(_currentConfig);
            }
            
            // 保存索引
            SaveConfigIndex();
            
            // 通知列表变更
            OnConfigListChanged();
        }
        catch (Exception ex)
        {
            _logger.Error($"删除配置文件失败: {configInfo.Name}", ex);
            throw;
        }
    }
    
    /// <summary>
    /// 切换当前配置
    /// </summary>
    /// <param name="configInfo">要切换到的配置文件信息</param>
    public void SwitchConfig(ConfigFileInfo configInfo)
    {
        if (configInfo == null || _currentConfig == configInfo) return;
        
        try
        {
            _logger.Debug($"切换配置: {_currentConfig?.Name} -> {configInfo.Name}");
            
            // 设置当前配置
            CurrentConfig = configInfo;
        }
        catch (Exception ex)
        {
            _logger.Error($"切换配置文件失败: {configInfo.Name}", ex);
            throw;
        }
    }
    
    /// <summary>
    /// 设置配置文件快捷键
    /// </summary>
    /// <param name="configInfo">配置文件信息</param>
    /// <param name="hotkeyText">快捷键文本</param>
    public void SetConfigHotkey(ConfigFileInfo configInfo, string hotkeyText)
    {
        if (configInfo == null) return;
        
        try
        {
            // 更新快捷键
            configInfo.ConfigHotkey = hotkeyText;
            
            // 保存索引
            SaveConfigIndex();
            
            // 通知列表变更
            OnConfigListChanged();
        }
        catch (Exception ex)
        {
            _logger.Error($"设置配置文件快捷键失败: {configInfo.Name} -> {hotkeyText}", ex);
            throw;
        }
    }
    
    /// <summary>
    /// 获取配置文件内容
    /// </summary>
    /// <param name="configInfo">配置文件信息</param>
    /// <returns>配置文件内容</returns>
    public KeyConfigData GetKeyConfigData(ConfigFileInfo configInfo = null)
    {
        configInfo ??= _currentConfig;
        
        try
        {
            if (configInfo != null && File.Exists(configInfo.FilePath))
            {
                var json = File.ReadAllText(configInfo.FilePath);
                return JsonConvert.DeserializeObject<KeyConfigData>(json) ?? new KeyConfigData();
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"获取配置文件内容失败: {configInfo?.Name}", ex);
        }
        
        return new KeyConfigData();
    }
    
    /// <summary>
    /// 保存配置文件内容
    /// </summary>
    /// <param name="keyConfig">按键配置数据</param>
    /// <param name="configInfo">配置文件信息，null表示当前配置</param>
    public void SaveKeyConfigData(KeyConfigData keyConfig, ConfigFileInfo configInfo = null)
    {
        try
        {
            configInfo ??= _currentConfig;
            var json = JsonConvert.SerializeObject(keyConfig, Formatting.Indented);
            File.WriteAllText(configInfo.FilePath, json);
            
            // 更新配置文件的最后编辑时间
            configInfo.UpdateEditTime();
            SaveConfigIndex(); // 保存索引以更新编辑时间
            
            _logger.Debug($"配置数据已保存到 {configInfo.FilePath}");
        }
        catch (Exception ex)
        {
            _logger.Error($"保存配置数据失败: {ex.Message}", ex);
            throw;
        }
    }
    
    /// <summary>
    /// 验证配置名称，确保唯一性
    /// </summary>
    /// <param name="name">配置名称</param>
    /// <param name="excludeConfig">要排除的配置（用于重命名）</param>
    /// <returns>有效的配置名称</returns>
    private string ValidateConfigName(string name, ConfigFileInfo excludeConfig = null)
    {
        // 移除不允许的字符
        var validName = Regex.Replace(name, @"[\\/:*?""<>|]", "_");
        validName = validName.Trim();
        
        // 如果为空，使用默认名称
        if (string.IsNullOrWhiteSpace(validName))
        {
            validName = "新配置";
        }
        
        // 检查名称是否存在（排除自身）
        string baseName = validName;
        int suffix = 1;
        
        while (_configFiles.Any(c => c != excludeConfig && c.Name.Equals(validName, StringComparison.OrdinalIgnoreCase)))
        {
            validName = $"{baseName} ({suffix})";
            suffix++;
        }
        
        return validName;
    }

    public void ImportConfig(string sourceFile)
    {
        try
        {
            var configContent = File.ReadAllText(sourceFile);
            var appConfigPath = _pathService.GetGlobalConfigPath();

            Directory.CreateDirectory(_configDir);

            if (File.Exists(appConfigPath))
            {
                var backupPath = Path.Combine(
                    _configDir,
                    $"GlobalConfig_backup_{DateTime.Now:yyyyMMddHHmmss}.json");
                File.Copy(appConfigPath, backupPath);
                CleanupOldBackups();
            }

            File.WriteAllText(appConfigPath, configContent);
            _settings = JsonConvert.DeserializeObject<Dictionary<string, object>>(configContent)
                        ?? new Dictionary<string, object>();
            RestartApplication();
        }
        catch (Exception ex)
        {
            _logger.Error("导入配置文件失败", ex);
            throw;
        }
    }
    
    /// <summary>
    /// 导入按键配置文件
    /// </summary>
    /// <param name="sourceFile">源文件</param>
    /// <param name="configName">配置名称，如果为null则使用文件名</param>
    public ConfigFileInfo ImportKeyConfig(string sourceFile, string configName = null)
    {
        try
        {
            // 检查源文件是否存在
            if (!File.Exists(sourceFile))
            {
                throw new FileNotFoundException($"找不到源文件: {sourceFile}");
            }
            
            // 获取配置名称
            if (string.IsNullOrEmpty(configName))
            {
                configName = Path.GetFileNameWithoutExtension(sourceFile);
            }
            
            // 验证配置名称
            var validName = ValidateConfigName(configName);
            
            // 创建新配置文件路径
            var newConfigPath = Path.Combine(_configDir, $"{validName}.json");
            
            // 创建新配置信息
            var newConfig = new ConfigFileInfo(validName, newConfigPath);
            
            // 复制配置文件
            File.Copy(sourceFile, newConfigPath, true);
            
            // 设置最后编辑时间
            newConfig.LastEditTime = DateTime.Now;
            
            // 添加到配置列表
            _configFiles.Add(newConfig);
            
            // 保存配置索引
            SaveConfigIndex();
            
            // 通知列表变更
            OnConfigListChanged();
            
            return newConfig;
        }
        catch (Exception ex)
        {
            _logger.Error($"导入配置失败: {sourceFile}", ex);
            throw;
        }
    }

    public void ExportConfig(string targetFile)
    {
        try
        {
            var appConfigPath = _pathService.GetGlobalConfigPath();
            if (!File.Exists(appConfigPath)) throw new FileNotFoundException("配置文件不存在", appConfigPath);
            File.Copy(appConfigPath, targetFile, true);
        }
        catch (Exception ex)
        {
            _logger.Error("导出配置文件失败", ex);
            throw;
        }
    }
    
    /// <summary>
    /// 导出按键配置文件
    /// </summary>
    /// <param name="targetFile">目标文件</param>
    /// <param name="configInfo">配置文件信息，null表示当前配置</param>
    public void ExportKeyConfig(string targetFile, ConfigFileInfo configInfo = null)
    {
        configInfo ??= _currentConfig;
        
        try
        {
            if (configInfo != null && File.Exists(configInfo.FilePath))
            {
                File.Copy(configInfo.FilePath, targetFile, true);
            }
            else
            {
                throw new FileNotFoundException("配置文件不存在");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"导出按键配置文件失败: {configInfo?.Name}", ex);
            throw;
        }
    }

    private void CleanupOldBackups()
    {
        try
        {
            var backupFiles = Directory.GetFiles(_configDir, "GlobalConfig_backup_*.json")
                .OrderByDescending(f => f)
                .Skip(MAX_BACKUP_FILES);

            foreach (var file in backupFiles)
                try
                {
                    File.Delete(file);
                    _logger.Debug($"已删除旧的备份文件: {file}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"删除备份文件失败: {file}", ex);
                }
        }
        catch (Exception ex)
        {
            _logger.Error("清理备份文件失败", ex);
        }
    }

    private void RestartApplication()
    {
        try
        {
            var appPath = Process.GetCurrentProcess().MainModule?.FileName
                          ?? throw new InvalidOperationException("无法获取应用程序路径");

            var startInfo = new ProcessStartInfo
            {
                FileName = appPath,
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(startInfo);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _logger.Error("重启应用程序失败", ex);
            throw;
        }
    }
}
