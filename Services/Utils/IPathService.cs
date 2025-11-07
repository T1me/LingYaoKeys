namespace WpfApp.Services.Utils;

/// <summary>
/// 路径服务接口
/// </summary>
public interface IPathService
{
    /// <summary>
    /// 应用数据根目录
    /// </summary>
    string AppDataPath { get; }

    /// <summary>
    /// 配置文件目录
    /// </summary>
    string ConfigPath { get; }

    /// <summary>
    /// 日志文件目录
    /// </summary>
    string LogPath { get; }

    /// <summary>
    /// 资源文件目录
    /// </summary>
    string ResourcePath { get; }

    /// <summary>
    /// 音频文件目录
    /// </summary>
    string SoundPath { get; }

    /// <summary>
    /// 驱动文件目录
    /// </summary>
    string DriverPath { get; }

    /// <summary>
    /// 获取配置文件的完整路径
    /// </summary>
    string GetConfigFilePath(string fileName);

    /// <summary>
    /// 获取全局配置文件路径
    /// </summary>
    string GetGlobalConfigPath();

    /// <summary>
    /// 获取按键配置文件路径
    /// </summary>
    string GetKeyConfigPath();

    /// <summary>
    /// 获取日志文件的目录路径
    /// </summary>
    string GetLogDirectoryPath();

    /// <summary>
    /// 获取资源文件的完整路径
    /// </summary>
    string GetResourcePath(string relativePath);

    /// <summary>
    /// 获取音频文件的完整路径
    /// </summary>
    string GetSoundFilePath(string fileName);

    /// <summary>
    /// 获取驱动文件的完整路径
    /// </summary>
    string GetDriverFilePath(string fileName);
}
