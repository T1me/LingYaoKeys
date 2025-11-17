using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using WpfApp.Views;
using WpfApp.Services.Core;
using WpfApp.Services.Utils;
using WpfApp.Services.Events;
using WpfApp.Services.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Application = System.Windows.Application;

namespace WpfApp.ViewModels;

/// <summary>
/// 主视图模型，负责页面导航、状态栏消息管理和页面缓存
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IConfigManager _configManager;
    private readonly ISerilogManager _logger;
    private readonly IPathService _pathService;
    private readonly ILyKeysService _lyKeysService;
    private readonly Window _mainWindow;
    private KeyMappingViewModel _keyMappingViewModel = null!; // 将在 SetHotkeyService() 中初始化
    private IHotkeyService _hotkeyService = null!; // 将通过 SetHotkeyService 方法注入
    private readonly AboutViewModel _aboutViewModel;
    private readonly DispatcherTimer _statusMessageTimer;
    private readonly Dictionary<string, Page> _pageCache = new();
    private readonly Dictionary<string, Storyboard> _fadeInCache = new();
    private readonly Dictionary<string, Storyboard> _fadeOutCache = new();
    private readonly Storyboard? _fadeInStoryboard;
    private readonly Storyboard? _fadeOutStoryboard;
    private readonly Dictionary<string, PageConfig> _pageConfigs;
    private GlobalConfig? _globalConfig;
    private bool _isInitializing = true;

    private const int STATUS_MESSAGE_TIMEOUT = 3000;  // 状态栏消息显示时间（毫秒）

    // 状态消息颜色常量
    private static readonly Brush STATUS_COLOR_NORMAL = Brushes.Black;
    private static readonly Brush STATUS_COLOR_SUCCESS = Brushes.Green;
    private static readonly Brush STATUS_COLOR_WARNING = Brushes.Orange;
    private static readonly Brush STATUS_COLOR_ERROR = Brushes.Red;
    private static readonly Brush STATUS_COLOR_INFO = Brushes.Blue;

    /// <summary>
    /// 当前显示的页面
    /// </summary>
    [ObservableProperty]
    private Page? _currentPage;

    /// <summary>
    /// 状态栏消息文本
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "就绪";

    /// <summary>
    /// 状态栏消息颜色
    /// </summary>
    [ObservableProperty]
    private Brush _statusMessageColor = Brushes.Black;

    /// <summary>
    /// 构造函数，初始化主视图模型
    /// </summary>
    public MainViewModel(
        IConfigManager configManager,
        ISerilogManager logger,
        IPathService pathService,
        ILyKeysService lyKeysService,
        Window mainWindow)
    {
        _isInitializing = true;
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _lyKeysService = lyKeysService ?? throw new ArgumentNullException(nameof(lyKeysService));
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));

        // 获取动画资源
        _fadeInStoryboard = mainWindow.FindResource("PageFadeIn") as Storyboard;
        _fadeOutStoryboard = mainWindow.FindResource("PageFadeOut") as Storyboard;

        // 初始化定时器
        _statusMessageTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(STATUS_MESSAGE_TIMEOUT) };
        _statusMessageTimer.Tick += (s, e) =>
        {
            _statusMessageTimer.Stop();
            StatusMessage = "就绪";
            StatusMessageColor = Brushes.Black;
        };

        // 初始化配置
        _globalConfig = _configManager.GlobalConfig;

        mainWindow.DataContext = this;

        _isInitializing = false;

        // 注意：HotkeyService 将通过 SetHotkeyService() 方法注入
        // KeyMappingViewModel 将在 SetHotkeyService() 中初始化
        _aboutViewModel = new AboutViewModel(_configManager, _logger);

        _lyKeysService.StatusMessageChanged += OnDriverStatusMessageChanged;

        // 页面配置
        _pageConfigs = new Dictionary<string, PageConfig>
        {
            ["FrontKeys"] = new PageConfig
            {
                UseCaching = true,
                IsFrequentlyAccessed = true,
                CreatePageFunc = () => new KeyMappingView { DataContext = _keyMappingViewModel }
            },
            ["About"] = new PageConfig
            {
                UseCaching = true,
                CreatePageFunc = () => new AboutView(_aboutViewModel)
            },
            ["Settings"] = new PageConfig
            {
                UseCaching = true,
                CreatePageFunc = () => new SettingsView(new SettingsViewModel(_configManager, _logger, _pathService))
            }
        };

        Navigate("FrontKeys");
    }

    /// <summary>
    /// 全局配置
    /// </summary>
    public GlobalConfig GlobalConfig
    {
        get
        {
            if (_globalConfig == null)
            {
                _globalConfig = _configManager.GlobalConfig;
                OnPropertyChanged();
            }
            return _globalConfig;
        }
    }

    /// <summary>
    /// 窗口标题
    /// </summary>
    public string WindowTitle => GlobalConfig.AppInfo.Title;

    /// <summary>
    /// 版本信息
    /// </summary>
    public string VersionInfo => $"v{GlobalConfig.AppInfo.Version}";

    /// <summary>
    /// 作者信息
    /// </summary>
    public string AuthorInfo => $"By: {GlobalConfig.Author} | {VersionInfo}";

    /// <summary>
    /// 当前页面的 ViewModel
    /// </summary>
    public object? CurrentViewModel => CurrentPage?.DataContext;

    /// <summary>
    /// 按键映射 ViewModel
    /// </summary>
    public KeyMappingViewModel KeyMappingViewModel => _keyMappingViewModel;

    /// <summary>
    /// 获取当前是否处于初始化状态
    /// </summary>
    public bool IsInitializing => _isInitializing;

    /// <summary>
    /// 页面导航命令
    /// </summary>
    [RelayCommand]
    private void Navigate(string? parameter)
    {
        if (string.IsNullOrEmpty(parameter)) return;

        try
        {
            // 停止正在执行的按键操作
            if (CurrentPage?.DataContext is KeyMappingViewModel keyMappingVM && keyMappingVM.IsExecuting)
                keyMappingVM.StopKeyMapping();

            var newPage = GetOrCreatePage(parameter);
            if (newPage == null) return;

            var oldPage = CurrentPage;

            // 播放页面切换动画
            if (oldPage != null && _fadeOutStoryboard != null)
            {
                var fadeOut = GetOrCreateFadeOutAnimation(parameter);
                var fadeIn = GetOrCreateFadeInAnimation(parameter);

                fadeOut.Completed += (s, e) =>
                {
                    CurrentPage = newPage;
                    fadeIn.Begin(newPage);
                };
                fadeOut.Begin(oldPage);
            }
            else
            {
                CurrentPage = newPage;
                GetOrCreateFadeInAnimation(parameter).Begin(newPage);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"页面导航失败: {parameter} ，直接切换页面", ex);
            // 降级处理：直接切换页面
            try { CurrentPage = GetOrCreatePage(parameter); } catch { }
        }
    }

    /// <summary>
    /// 获取或创建页面
    /// </summary>
    private Page? GetOrCreatePage(string parameter)
    {
        if (!_pageConfigs.TryGetValue(parameter, out var pageConfig))
        {
            _logger.Error($"未知页面: {parameter}");
            return null;
        }

        // About 页面强制刷新
        if (parameter == "About")
            _pageCache.Remove(parameter);

        // 不使用缓存，直接创建
        if (!pageConfig.UseCaching)
            return pageConfig.CreatePageFunc();

        // 从缓存获取
        if (_pageCache.TryGetValue(parameter, out var cachedPage))
            return cachedPage;

        // 创建新页面并缓存
        var newPage = pageConfig.CreatePageFunc();
        newPage.Opacity = 0;
        _pageCache[parameter] = newPage;
        return newPage;
    }

    /// <summary>
    /// 预加载常用页面
    /// </summary>
    public void PreloadCommonPages()
    {
        Task.Run(() =>
        {
            var frequentPages = _pageConfigs
                .Where(kvp => kvp.Value.IsFrequentlyAccessed && kvp.Value.UseCaching && !_pageCache.ContainsKey(kvp.Key))
                .Select(kvp => kvp.Key);

            foreach (var pageName in frequentPages)
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(() => GetOrCreatePage(pageName),
                        DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    _logger.Error($"预加载失败: {pageName}", ex);
                }
            }
        });
    }

    /// <summary>
    /// 获取或创建淡入动画
    /// </summary>
    private Storyboard GetOrCreateFadeInAnimation(string parameter)
    {
        if (_fadeInCache.TryGetValue(parameter, out var fadeIn)) return fadeIn;

        var newFadeIn = _fadeInStoryboard!.Clone();
        _fadeInCache[parameter] = newFadeIn;
        return newFadeIn;
    }

    /// <summary>
    /// 获取或创建淡出动画
    /// </summary>
    private Storyboard GetOrCreateFadeOutAnimation(string parameter)
    {
        if (_fadeOutCache.TryGetValue(parameter, out var fadeOut)) return fadeOut;

        var newFadeOut = _fadeOutStoryboard!.Clone();
        _fadeOutCache[parameter] = newFadeOut;
        return newFadeOut;
    }

    /// <summary>
    /// 设置 HotkeyService（由 DI 容器调用）
    /// </summary>
    public void SetHotkeyService(IHotkeyService hotkeyService)
    {
        _hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));

        // 现在可以初始化 KeyMappingViewModel（需要 HotkeyService）
        var lyKeysServiceConcrete = (LyKeysService)_lyKeysService;
        _keyMappingViewModel = new KeyMappingViewModel(
            lyKeysServiceConcrete,
            _hotkeyService,
            this,
            App.AudioService,
            _configManager,
            _logger
        );

        _logger.Debug("MainViewModel: HotkeyService 已注入并初始化 KeyMappingViewModel");
    }

    /// <summary>
    /// 清理资源
    /// </summary>
    public void Cleanup()
    {
        // 清理时保存所有配置
        _keyMappingViewModel?.SaveConfig();
        _hotkeyService?.Dispose();
        _statusMessageTimer.Stop();
        _fadeInCache.Clear();
        _fadeOutCache.Clear();
        _pageCache.Clear();
    }

    /// <summary>
    /// 驱动状态消息变更事件处理
    /// </summary>
    private void OnDriverStatusMessageChanged(object? sender, StatusMessageEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _statusMessageTimer.Stop();
            StatusMessage = e.Message;
            StatusMessageColor = e.IsError ? Brushes.Red : Brushes.Black;
            _statusMessageTimer.Start();

            if (e.IsError)
                _logger.Error($"驱动错误: {e.Message}");
        });
    }

    /// <summary>
    /// 显示成功消息
    /// </summary>
    public void ShowSuccessMessage(string message)
    {
        UpdateStatusMessage(message, STATUS_COLOR_SUCCESS);
    }

    /// <summary>
    /// 显示警告消息
    /// </summary>
    public void ShowWarningMessage(string message)
    {
        UpdateStatusMessage(message, STATUS_COLOR_WARNING);
    }

    /// <summary>
    /// 显示错误消息
    /// </summary>
    public void ShowErrorMessage(string message)
    {
        UpdateStatusMessage(message, STATUS_COLOR_ERROR);
    }

    /// <summary>
    /// 显示信息消息
    /// </summary>
    public void ShowInfoMessage(string message)
    {
        UpdateStatusMessage(message, STATUS_COLOR_INFO);
    }

    /// <summary>
    /// 更新状态消息
    /// </summary>
    public void UpdateStatusMessage(string message, bool isError = false)
    {
        UpdateStatusMessage(message, isError ? STATUS_COLOR_ERROR : STATUS_COLOR_NORMAL);
    }

    /// <summary>
    /// 更新状态消息（带颜色）
    /// </summary>
    public void UpdateStatusMessage(string message, Brush color)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            _statusMessageTimer?.Stop();
            StatusMessage = message;
            StatusMessageColor = color;
            _statusMessageTimer?.Start();
        });
    }

    /// <summary>
    /// 页面配置类
    /// </summary>
    private class PageConfig
    {
        public bool UseCaching { get; set; } = true;
        public bool IsFrequentlyAccessed { get; set; } = false;
        public Func<Page> CreatePageFunc { get; set; }
    }
}
