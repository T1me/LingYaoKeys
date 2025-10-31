using System.IO;
using WpfApp.Services.Models;
using WpfApp.Services.Utils;
using System.Windows;
using System.Runtime.InteropServices;
using System.Diagnostics;
using WpfApp.Services.Core;
using WpfApp.ViewModels;
using WpfApp.Views;
using MessageBox = System.Windows.MessageBox;
using System.Windows.Media;
using System.Windows.Interop;
using System.Threading;
using System.Windows.Threading;

// 避免命名空间冲突
using Application = System.Windows.Application;
using AppDomain = System.AppDomain;

namespace WpfApp;

/// <summary>
/// App.xaml 的交互逻辑
/// </summary>
public partial class App : Application
{
    private readonly SerilogManager _logger = SerilogManager.Instance;
    private readonly PathService _pathService = PathService.Instance;

    public static LyKeysService LyKeysDriver { get; private set; }
    public static ConfigManager ConfigService { get; private set; }
    public static AudioService AudioService { get; private set; }
    
    private bool _isShuttingDown;

    [DllImport("Kernel32")]
    private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);

    private delegate bool EventHandler(CtrlType sig);

    private static EventHandler _handler;

    private enum CtrlType
    {
        CTRL_C_EVENT = 0,
        CTRL_BREAK_EVENT = 1,
        CTRL_CLOSE_EVENT = 2,
        CTRL_LOGOFF_EVENT = 5,
        CTRL_SHUTDOWN_EVENT = 6
    }

    // 清理级别
    // Normal 级别：基本资源清理（适用于正常应用退出）
    // Complete 级别：完整清理（包括驱动服务，适用于进程强制终止）
    private enum CleanupLevel
    {
        Normal, // 普通清理
        Complete // 完整清理（包括驱动服务）
    }

    public App()
    {
        // 注册控制台事件处理
        _handler += new EventHandler(Handler);
        SetConsoleCtrlHandler(_handler, true);

        // 注册应用程序退出事件（只注册一次）
        Exit += OnApplicationExit;
    }

    private bool Handler(CtrlType sig)
    {
        switch (sig)
        {
            case CtrlType.CTRL_BREAK_EVENT:
            case CtrlType.CTRL_C_EVENT:
            case CtrlType.CTRL_LOGOFF_EVENT:
            case CtrlType.CTRL_SHUTDOWN_EVENT:
            case CtrlType.CTRL_CLOSE_EVENT:
                CleanupServices();
                return false;
            default:
                return false;
        }
    }

    private void Cleanup(CleanupLevel level = CleanupLevel.Normal)
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;

        try
        {
            _logger.Debug($"开始清理服务 - 级别: {level}");

            // 1. 停止并释放驱动服务
            if (LyKeysDriver != null)
            {
                try
                {
                    LyKeysDriver.Dispose();
                    _logger.Debug("驱动服务已释放");
                }
                catch (Exception ex)
                {
                    _logger.Error("释放驱动服务失败", ex);
                }
                LyKeysDriver = null;
            }

            // 2. 释放音频服务
            if (AudioService != null)
            {
                try
                {
                    AudioService.Dispose();
                    _logger.Debug("音频服务已释放");
                }
                catch (Exception ex)
                {
                    _logger.Error("释放音频服务失败", ex);
                }
                AudioService = null;
            }

            // 3. 清理配置服务引用
            ConfigService = null;

            // 4. 完整清理时卸载驱动
            if (level == CleanupLevel.Complete)
            {
                UnloadDriver();
            }

            _logger.Debug("服务清理完成");
            _logger.Debug("=================================================");

            // 5. 释放日志服务
            _logger.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"清理服务异常: {ex.Message}");
        }
    }

    private void UnloadDriver()
    {
        try
        {
            // 异步卸载驱动，不阻塞退出
            Task.Run(() =>
            {
                try
                {
                    // 停止驱动服务
                    using (var p = new Process())
                    {
                        p.StartInfo.FileName = "sc.exe";
                        p.StartInfo.Arguments = "stop lykeys";
                        p.StartInfo.UseShellExecute = false;
                        p.StartInfo.CreateNoWindow = true;
                        p.Start();
                        p.WaitForExit(1000); // 减少等待时间
                    }

                    Thread.Sleep(100);

                    // 删除驱动服务
                    using (var p = new Process())
                    {
                        p.StartInfo.FileName = "sc.exe";
                        p.StartInfo.Arguments = "delete lykeys";
                        p.StartInfo.UseShellExecute = false;
                        p.StartInfo.CreateNoWindow = true;
                        p.Start();
                        p.WaitForExit(1000);
                    }
                }
                catch { }
            });
        }
        catch { }
    }

    private void CleanupServices()
    {
        Cleanup(CleanupLevel.Complete);
    }

    private bool CheckServiceExists(string serviceName)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"query {serviceName}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit(1000); // 减少超时时间
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private bool StopService(string serviceName)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"stop {serviceName}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit(1000); // 减少超时时间
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private bool DeleteService(string serviceName)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"delete {serviceName}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit(1000); // 减少超时时间
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private void CleanupExistingService()
    {
        const string serviceName = "lykeys";

        try
        {
            _logger.Debug("清理已存在的lykeys服务");

            // 停止服务（减少等待时间）
            if (StopService(serviceName))
            {
                _logger.Debug("停止lykeys服务");
                Thread.Sleep(200); // 减少等待时间
            }

            // 删除服务
            if (DeleteService(serviceName))
            {
                _logger.Debug("删除lykeys服务");
                Thread.Sleep(200);
            }

            // 快速清理进程
            try
            {
                var processes = Process.GetProcessesByName("lykeys");
                foreach (var proc in processes)
                {
                    try
                    {
                        proc.Kill();
                        proc.WaitForExit(500); // 减少等待时间
                    }
                    catch { }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch { }

            Thread.Sleep(200); // 最终等待时间减少
        }
        catch (Exception ex)
        {
            _logger.Error("清理服务失败", ex);
            // 不抛出异常，允许继续启动
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Views.SplashWindow? splashWindow = null;

        try
        {
            System.Diagnostics.Debug.WriteLine("=== 应用启动 ===");

            splashWindow = new Views.SplashWindow();
            splashWindow.Show();
            splashWindow.UpdateProgress("正在初始化应用程序...", 0);

            Directory.CreateDirectory(_pathService.AppDataPath);

            splashWindow.UpdateProgress("正在初始化配置服务...", 20);
            ConfigManager.Instance.Initialize();
            ConfigService = ConfigManager.Instance;

            ConfigureHardwareAcceleration();

            splashWindow.UpdateProgress("正在初始化日志系统...", 30);
            _logger.SetBaseDirectory(_pathService.LogPath);
            _logger.Initialize(ConfigManager.Instance.GlobalConfig.Debug);

            RegisterGlobalExceptionHandlers();

            ConfigManager.Instance.ConfigChanged += (sender, args) =>
            {
                if (args.ChangeType == ConfigChangeType.Global)
                    _logger.UpdateLoggerConfig(args.GlobalConfigData.Debug);
            };

            splashWindow.UpdateProgress("正在准备驱动文件...", 40);
            var driverFile = PrepareDriverFiles();

            if (CheckServiceExists("lykeys"))
            {
                splashWindow.UpdateProgress("正在清理旧驱动...", 50);
                CleanupExistingService();
            }

            splashWindow.UpdateProgress("正在初始化驱动服务...", 60);
            LyKeysDriver = new LyKeysService();

            if (!LyKeysDriver.Initialize(driverFile))
            {
                System.Diagnostics.Debug.WriteLine("驱动初始化失败");
                _logger.Error("驱动加载失败");
                MessageBox.Show("驱动加载失败，请检查是否以管理员身份运行", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Current.Shutdown();
                return;
            }

            splashWindow.UpdateProgress("正在初始化音频服务...", 80);
            AudioService = new AudioService();

            splashWindow.UpdateProgress("正在启动主界面...", 90);
            var mainWindow = new Views.MainWindow();
            Current.MainWindow = mainWindow;

            if (mainWindow.DataContext is MainViewModel mainViewModel)
            {
                try
                {
                    mainViewModel.PreloadCommonPages();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"页面预加载失败: {ex.Message}");
                }
            }

            splashWindow.UpdateProgress("启动完成", 100);
            Thread.Sleep(300);

            mainWindow.Show();
            splashWindow.Close();

            _logger.Debug("应用程序启动完成");
            System.Diagnostics.Debug.WriteLine("=== 启动完成 ===");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"!!! 启动异常: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"!!! 堆栈跟踪: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"!!! 内部异常: {ex.InnerException.Message}");
                System.Diagnostics.Debug.WriteLine($"!!! 内部堆栈: {ex.InnerException.StackTrace}");
            }
            
            _logger.Error("应用程序启动失败", ex);
            MessageBox.Show($"程序启动异常：{ex.Message}\n\n详细信息：{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            splashWindow?.Close();
            Current.Shutdown();
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            _logger.Error("未处理的异常，程序发生致命错误", ex);
        };

        Current.DispatcherUnhandledException += (s, args) =>
        {
            _logger.Error("UI线程异常，界面线程发生异常", args.Exception);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            _logger.Error("任务异常, 异步任务发生异常", args.Exception);
            args.SetObserved();
        };
    }

    private string PrepareDriverFiles()
    {
        try
        {
            var driverFile = _pathService.GetDriverFilePath("lykeys.sys");
            var dllFile = _pathService.GetDriverFilePath("lykeysdll.dll");
            
            _logger.Debug($"驱动文件目录: {_pathService.DriverPath}");

            // 检查文件是否已存在
            bool needsExtraction = !File.Exists(driverFile) || !File.Exists(dllFile);
            
            if (needsExtraction)
            {
                _logger.Debug("驱动文件不存在，开始提取...");
                ExtractEmbeddedResource("WpfApp.Resource.lykeysdll.lykeys.sys", driverFile);
                ExtractEmbeddedResource("WpfApp.Resource.lykeysdll.lykeysdll.dll", dllFile);
                _logger.Debug("驱动文件提取完成");
            }
            else
            {
                _logger.Debug("驱动文件已存在");
            }

            // 验证文件
            if (!File.Exists(driverFile) || !File.Exists(dllFile))
            {
                throw new FileNotFoundException("驱动文件不存在或提取失败");
            }

            return driverFile;
        }
        catch (Exception ex)
        {
            _logger.Error($"准备驱动文件失败: {ex.Message}", ex);
            throw;
        }
    }



    /// <summary>
    /// 从嵌入式资源提取文件
    /// </summary>
    /// <param name="resourceName">资源名称</param>
    /// <param name="outputPath">输出路径</param>
    private void ExtractEmbeddedResource(string resourceName, string outputPath)
    {
        try
        {
            using (var stream = GetType().Assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null) throw new FileNotFoundException($"找不到嵌入式资源: {resourceName}");

                // 使用FileShare.Delete允许其他进程删除文件
                using (var fileStream = new FileStream(
                           outputPath,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.Delete))
                {
                    stream.CopyTo(fileStream);
                }
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"提取资源文件失败: {resourceName}", ex);
        }
    }

    private void OnApplicationExit(object sender, ExitEventArgs e)
    {
        Cleanup(CleanupLevel.Normal);
    }

    /// <summary>
    /// 配置WPF的硬件加速和渲染优化
    /// </summary>
    private void ConfigureHardwareAcceleration()
    {
        try
        {
            // 检查配置中是否启用硬件加速
            bool enableHardwareAcceleration = ConfigService?.GlobalConfig?.EnableHardwareAcceleration ?? true;
            
            if (enableHardwareAcceleration)
            {
                // 启用默认渲染模式（通常会启用硬件加速，如果可用）
                RenderOptions.ProcessRenderMode = RenderMode.Default;
                _logger.Debug("硬件加速已启用");
            }
            else
            {
                // 强制使用软件渲染
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                _logger.Debug("硬件加速已禁用，使用软件渲染");
            }
            
            // 在主线程调度器启动后记录渲染信息
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // 获取当前渲染能力等级
                    int tier = RenderCapability.Tier >> 16;
                    bool isHardwareAccelerated = RenderOptions.ProcessRenderMode != RenderMode.SoftwareOnly;
                    _logger.Debug($"渲染能力: Tier{tier}, 硬件加速状态: {isHardwareAccelerated}");
                    
                    // 不在启动阶段访问MainWindow，以避免初始化顺序问题
                    // 改为通过Loaded事件为各窗口单独设置硬件加速
                }
                catch (Exception ex)
                {
                    _logger.Warning($"检查渲染能力时出错: {ex.Message}");
                }
            }), DispatcherPriority.ApplicationIdle);
            
            _logger.Debug("已配置WPF渲染优化");
        }
        catch (Exception ex)
        {
            _logger.Error("配置硬件加速时出错，将使用默认设置", ex);
        }
    }
}