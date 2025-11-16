using System.IO;
using WpfApp.Services.Utils;

namespace WpfApp.Services.Core;

/// <summary>
/// 驱动工厂 - 根据配置创建相应的驱动实例
/// </summary>
public static class DriverFactory
{
    /// <summary>
    /// 创建驱动实例
    /// </summary>
    /// <param name="logger">日志服务</param>
    /// <param name="driverType">驱动类型</param>
    /// <param name="driverPath">驱动文件路径</param>
    /// <returns>驱动实例</returns>
    public static IDriver CreateDriver(ISerilogManager logger, string driverType, string driverPath)
    {
        logger.Debug($"创建驱动实例: {driverType}");

        return driverType?.ToUpperInvariant() switch
        {
            "LYKEYS" => new LyKeys(logger, driverPath),
            "AHK" => new AhkDriver(logger),
            _ => throw new ArgumentException($"不支持的驱动类型: {driverType}")
        };
    }

    /// <summary>
    /// 准备驱动文件
    /// </summary>
    /// <param name="logger">日志服务</param>
    /// <param name="driverType">驱动类型</param>
    /// <param name="pathService">路径服务</param>
    /// <param name="extractResource">资源提取委托</param>
    /// <returns>驱动文件路径</returns>
    public static string PrepareDriverFiles(ISerilogManager logger, string driverType, IPathService pathService, Action<string, string> extractResource)
    {
        logger.Debug($"准备驱动文件: {driverType}");

        switch (driverType?.ToUpperInvariant())
        {
            case "LYKEYS":
                return PrepareLyKeysDriver(logger, pathService, extractResource);

            case "AHK":
                return string.Empty;

            default:
                throw new ArgumentException($"不支持的驱动类型: {driverType}");
        }
    }

    private static string PrepareLyKeysDriver(ISerilogManager logger, IPathService pathService, Action<string, string> extractResource)
    {
        var driverFile = pathService.GetDriverFilePath("lykeys.sys");
        var dllFile = pathService.GetDriverFilePath("lykeysdll.dll");

        logger.Debug($"驱动文件目录: {pathService.DriverPath}");

        bool needsExtraction = !File.Exists(driverFile) || !File.Exists(dllFile);

        if (needsExtraction)
        {
            logger.Debug("驱动文件不存在，开始提取...");
            extractResource("WpfApp.Resource.lykeysdll.lykeys.sys", driverFile);
            extractResource("WpfApp.Resource.lykeysdll.lykeysdll.dll", dllFile);
            logger.Debug("驱动文件提取完成");
        }

        if (!File.Exists(driverFile) || !File.Exists(dllFile))
        {
            throw new FileNotFoundException("驱动文件不存在或提取失败");
        }

        return driverFile;
    }
}
