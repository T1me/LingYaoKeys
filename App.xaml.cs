using System.IO;
using WpfApp.Services.Models;
using WpfApp.Services.Utils;
using System.Windows;
using System.Runtime.InteropServices;
using System.Diagnostics;
using WpfApp.Services.Core;
using WpfApp.ViewModels;
using WpfApp.Views;
using MessageBox = HandyControl.Controls.MessageBox;
using System.Windows.Media;
using System.Windows.Interop;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// 避免命名空间冲突
using Application = System.Windows.Application;
using AppDomain = System.AppDomain;

namespace WpfApp;

/// <summary>
/// App.xaml 的交互逻辑
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private ISerilogManager _logger;
    private IPathService _pathService;

    public static LyKeysService LyKeysDriver { get; set; }
    public static IConfigManager ConfigService { get; private set; }
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

        // 配置 DI 容器
        ConfigureServices();
    }

    private void ConfigureServices()
    {
        var builder = Host.CreateDefaultBuilder();

        builder.ConfigureServices((context, services) =>
        {
            // 注册工具服务（Singleton）
            services.AddSingleton<ISerilogManager, SerilogManager>();
            services.AddSingleton<IPathService, PathService>();

            // 注册核心服务（Singleton）
            services.AddSingleton<IConfigManager, ConfigManager>();
            services.AddSingleton<ILyKeysService>(sp => LyKeysDriver);
            services.AddSingleton<IAudioService>(sp => AudioService);
            services.AddSingleton<IInputMethodService, InputMethodService>();

            // 注册重构后的热键相关服务（Singleton）
            services.AddSingleton<Services.Core.Hooks.IHookManager, Services.Core.Hooks.HookManager>();
            services.AddSingleton<Services.Core.Window.IWindowValidator, Services.Core.Window.WindowValidator>();
            // KeySequenceExecutor 将在 MainWindow 工厂中手动创建（需要具体类型 LyKeysService）

            // 注册 ViewModels
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<AboutViewModel>();

            // 注册 Views（MainWindow 需要特殊处理，使用工厂模式管理复杂依赖）
            services.AddSingleton<MainWindow>(sp =>
            {
                var logger = sp.GetRequiredService<ISerilogManager>();
                var configManager = sp.GetRequiredService<IConfigManager>();
                var pathService = sp.GetRequiredService<IPathService>();
                var lyKeysService = sp.GetRequiredService<ILyKeysService>();

                // 转换为具体类型（KeySequenceExecutor 需要）
                var lyKeysServiceConcrete = (LyKeysService)lyKeysService;
                var inputMethodService = lyKeysService.InputMethodService;

                // 先创建 MainWindow 实例
                var mainWindow = new MainWindow(logger, configManager);

                // 创建 MainViewModel（传入 MainWindow）
                var mainViewModel = new MainViewModel(configManager, logger, pathService, lyKeysService, mainWindow);

                // 创建并注册 StatusMessageService（依赖 MainViewModel）
                var statusMessageService = new Services.UI.StatusMessageService(mainViewModel);

                // 创建 KeySequenceExecutor（需要具体类型）
                var keySequenceExecutor = new Services.Core.KeySequenceExecutor(
                    logger,
                    lyKeysServiceConcrete,
                    inputMethodService,
                    AudioService,
                    configManager
                );

                // 创建 HotkeyRegistry（依赖 StatusMessageService）
                var hotkeyRegistry = new Services.Core.Hotkey.HotkeyRegistry(
                    logger,
                    configManager,
                    statusMessageService
                );

                // 创建 HotkeyService（组合所有服务）
                var hotkeyService = new Services.Core.HotkeyService(
                    logger,
                    sp.GetRequiredService<Services.Core.Hooks.IHookManager>(),
                    sp.GetRequiredService<Services.Core.Window.IWindowValidator>(),
                    hotkeyRegistry,
                    keySequenceExecutor,  // 使用手动创建的实例
                    lyKeysService,
                    configManager,
                    statusMessageService
                );

                // 将 HotkeyService 设置到 MainViewModel（通过公开方法）
                mainViewModel.SetHotkeyService(hotkeyService);

                // 设置 DataContext
                mainWindow.SetViewModel(mainViewModel);
                mainWindow.DataContext = mainViewModel;

                return mainWindow;
            });
            services.AddTransient<KeyMappingView>();
            services.AddTransient<SettingsView>();
            services.AddTransient<AboutView>();
        });

        _host = builder.Build();
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

            if (!CheckServiceExists(serviceName))
            {
                _logger.Debug("lykeys服务不存在，无需清理");
                return;
            }
            else
            {
                // 停止服务
                StopService(serviceName);

                // 删除服务
                DeleteService(serviceName);

                _logger.Debug("已清理的lykeys服务");
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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // 启动 Host
            await _host.StartAsync();

            // 从 DI 容器获取服务实例
            _logger = _host.Services.GetRequiredService<ISerilogManager>();
            _pathService = _host.Services.GetRequiredService<IPathService>();
            ConfigService = _host.Services.GetRequiredService<IConfigManager>();

            Directory.CreateDirectory(_pathService.AppDataPath);
            ConfigService.Initialize();

            ConfigureHardwareAcceleration();

            _logger.SetBaseDirectory(_pathService.LogPath);
            _logger.Initialize(ConfigService.GlobalConfig.Debug);

            RegisterGlobalExceptionHandlers();

            ConfigService.ConfigChanged += (sender, args) =>
            {
                if (args.ChangeType == ConfigChangeType.Global)
                    _logger.UpdateLoggerConfig(args.GlobalConfigData.Debug);
            };

            var selectedDriver = ConfigService.GlobalConfig.SelectedDriver ?? "LyKeys";
            _logger.Debug($"选择的驱动: {selectedDriver}");

            var driverFile = DriverFactory.PrepareDriverFiles(_logger, selectedDriver, _pathService, ExtractEmbeddedResource);

            if (selectedDriver.Equals("LyKeys", StringComparison.OrdinalIgnoreCase))
            {
                if (CheckServiceExists("lykeys"))
                {
                    CleanupExistingService();
                }
            }

            var driver = DriverFactory.CreateDriver(_logger, selectedDriver, driverFile);
            LyKeysDriver = new LyKeysService(_logger, ConfigService, driver);

            if (!LyKeysDriver.Initialize(driverFile))
            {
                _logger.Error("驱动加载失败");
                MessageBox.Error($"驱动加载失败({selectedDriver})，请检查是否以管理员身份运行", "错误");
                Current.Shutdown();
                return;
            }

            AudioService = new AudioService(_logger, _pathService);

            // 从 DI 容器获取 MainWindow
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            Current.MainWindow = mainWindow;

            if (mainWindow.DataContext is MainViewModel mainViewModel)
            {
                mainViewModel.PreloadCommonPages();
            }

            Thread.Sleep(400);

            mainWindow.Show();
        }
        catch (Exception ex)
        {
            _logger.Error("应用程序启动失败", ex);
            MessageBox.Error($"程序启动异常：{ex.Message}", "错误");
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




    /// <summary>
    /// 从嵌入式资源提取文件
    /// </summary>
    /// <param name="resourceName">资源名称</param>
    /// <param name="outputPath">输出路径</param>
    public static void ExtractEmbeddedResource(string resourceName, string outputPath)
    {
        try
        {
            using (var stream = typeof(App).Assembly.GetManifestResourceStream(resourceName))
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

    private async void OnApplicationExit(object sender, ExitEventArgs e)
    {
        Cleanup(CleanupLevel.Normal);

        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
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
                    
                    // 不在启动阶段访问MainWindow，避免初始化顺序问题
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