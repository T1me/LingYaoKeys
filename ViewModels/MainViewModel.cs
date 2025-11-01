using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfApp.Views;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using WpfApp.Services.Core;
using WpfApp.Services.Utils;
using WpfApp.Services.Events;
using WpfApp.Services.Models;

namespace WpfApp.ViewModels;

public class MainViewModel : ViewModelBase
{
    private GlobalConfig? _globalConfig;
    private KeyConfigData? _keyConfig;
    private Page? _currentPage;
    private readonly LyKeysService _lyKeysService;
    private readonly Window _mainWindow;
    private readonly IConfigManager _configManager;
    private readonly KeyMappingViewModel _keyMappingViewModel;
    private readonly HotkeyService _hotkeyService;
    private string _statusMessage = "就绪";
    private System.Windows.Media.Brush _statusMessageColor = System.Windows.Media.Brushes.Black;
    private readonly DispatcherTimer _statusMessageTimer;
    private const int STATUS_MESSAGE_TIMEOUT = 3000;  // 状态栏消息显示时间（毫秒）
    private readonly AboutViewModel _aboutViewModel;
    private readonly Dictionary<string, Page> _pageCache = new();
    private readonly Dictionary<string, Storyboard> _fadeInCache = new();
    private readonly Dictionary<string, Storyboard> _fadeOutCache = new();
    private readonly Storyboard? _fadeInStoryboard;
    private readonly Storyboard? _fadeOutStoryboard;
    private bool _isInitializing = true;

    // 状态消息颜色
    private static readonly System.Windows.Media.Brush STATUS_COLOR_NORMAL = System.Windows.Media.Brushes.Black;
    private static readonly System.Windows.Media.Brush STATUS_COLOR_SUCCESS = System.Windows.Media.Brushes.Green;
    private static readonly System.Windows.Media.Brush STATUS_COLOR_WARNING = System.Windows.Media.Brushes.Orange;
    private static readonly System.Windows.Media.Brush STATUS_COLOR_ERROR = System.Windows.Media.Brushes.Red;
    private static readonly System.Windows.Media.Brush STATUS_COLOR_INFO = System.Windows.Media.Brushes.Blue;

    // 状态栏快捷方法
    public void ShowSuccessMessage(string message)
    {
        UpdateStatusMessage(message, STATUS_COLOR_SUCCESS);
    }

    public void ShowWarningMessage(string message)
    {
        UpdateStatusMessage(message, STATUS_COLOR_WARNING);
    }

    public void ShowErrorMessage(string message)
    {
        UpdateStatusMessage(message, STATUS_COLOR_ERROR);
    }

    public void ShowInfoMessage(string message)
    {
        UpdateStatusMessage(message, STATUS_COLOR_INFO);
    }

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

    public KeyConfigData KeyConfig
    {
        get
        {
            if (_keyConfig == null) _keyConfig = _configManager.CurrentKeyConfig;
            return _keyConfig;
        }
    }

    public string WindowTitle => GlobalConfig.AppInfo.Title;
    public string VersionInfo => $"v{GlobalConfig.AppInfo.Version}";
    public string AuthorInfo => $"By: {GlobalConfig.Author} | {VersionInfo}";

    public Page? CurrentPage
    {
        get => _currentPage;
        set
        {
            if (_currentPage != value)
            {
                _currentPage = value;
                OnPropertyChanged();
            }
        }
    }

    public object? CurrentViewModel => CurrentPage?.DataContext;

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public System.Windows.Media.Brush StatusMessageColor
    {
        get => _statusMessageColor;
        set => SetProperty(ref _statusMessageColor, value);
    }

    public ICommand NavigateCommand { get; }

    // 添加KeyMappingViewModel的公共属性
    public KeyMappingViewModel KeyMappingViewModel => _keyMappingViewModel;

    private class PageConfig
    {
        public bool UseCaching { get; set; } = true;
        public bool IsFrequentlyAccessed { get; set; } = false;
        public Func<Page> CreatePageFunc { get; set; }
    }

    // 页面配置字典，用于存储不同页面的配置
    private readonly Dictionary<string, PageConfig> _pageConfigs;

    public MainViewModel(LyKeysService lyKeysService, Window mainWindow)
    {
        _isInitializing = true;
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
            StatusMessageColor = System.Windows.Media.Brushes.Black;
        };

        // 初始化配置
        _configManager = App.ConfigService ?? WpfApp.Services.Core.ConfigManager.Instance
            ?? throw new InvalidOperationException("ConfigManager未初始化");
        _globalConfig = _configManager.GlobalConfig;

        mainWindow.DataContext = this;

        // 创建服务
        var executor = new KeySequenceExecutor(lyKeysService, lyKeysService._inputMethodService, App.AudioService, _configManager);
        _hotkeyService = new HotkeyService(mainWindow, executor, lyKeysService, _configManager);

        _isInitializing = false;

        // 初始化 ViewModels
        _keyMappingViewModel = new KeyMappingViewModel(_lyKeysService, _hotkeyService, this, App.AudioService);
        _aboutViewModel = new AboutViewModel();

        NavigateCommand = new RelayCommand<string>(Navigate);
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
                CreatePageFunc = () => new AboutView { DataContext = _aboutViewModel }
            },
            ["Settings"] = new PageConfig
            {
                UseCaching = true,
                CreatePageFunc = () => new SettingsView { DataContext = new SettingsViewModel() }
            }
        };

        Navigate("FrontKeys");
    }

    /// <summary>
    /// 获取当前是否处于初始化状态
    /// </summary>
    public bool IsInitializing => _isInitializing;

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
            Logger.Error($"页面导航失败: {parameter} ，直接切换页面", ex);
            // 降级处理：直接切换页面
            try { CurrentPage = GetOrCreatePage(parameter); } catch { }
        }
    }

    private Page? GetOrCreatePage(string parameter)
    {
        if (!_pageConfigs.TryGetValue(parameter, out var pageConfig))
        {
            Logger.Error($"未知页面: {parameter}");
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
                    System.Windows.Application.Current.Dispatcher.Invoke(() => GetOrCreatePage(pageName),
                        System.Windows.Threading.DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    Logger.Error($"预加载失败: {pageName}", ex);
                }
            }
        });
    }

    private Storyboard GetOrCreateFadeInAnimation(string parameter)
    {
        if (_fadeInCache.TryGetValue(parameter, out var fadeIn)) return fadeIn;

        var newFadeIn = _fadeInStoryboard!.Clone();
        _fadeInCache[parameter] = newFadeIn;
        return newFadeIn;
    }

    private Storyboard GetOrCreateFadeOutAnimation(string parameter)
    {
        if (_fadeOutCache.TryGetValue(parameter, out var fadeOut)) return fadeOut;

        var newFadeOut = _fadeOutStoryboard!.Clone();
        _fadeOutCache[parameter] = newFadeOut;
        return newFadeOut;
    }

    public void Cleanup()
    {
        _keyMappingViewModel.SaveConfig();
        _hotkeyService?.Dispose();
        _statusMessageTimer.Stop();
        _fadeInCache.Clear();
        _fadeOutCache.Clear();
        _pageCache.Clear();
    }

    private void OnDriverStatusMessageChanged(object? sender, StatusMessageEventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _statusMessageTimer.Stop();
            StatusMessage = e.Message;
            StatusMessageColor = e.IsError ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.Black;
            _statusMessageTimer.Start();

            if (e.IsError)
                Logger.Error($"驱动错误: {e.Message}");
        });
    }

    public void UpdateStatusMessage(string message, bool isError = false)
    {
        UpdateStatusMessage(message, isError ? STATUS_COLOR_ERROR : STATUS_COLOR_NORMAL);
    }

    public void UpdateStatusMessage(string message, System.Windows.Media.Brush color)
    {
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            _statusMessageTimer?.Stop();
            StatusMessage = message;
            StatusMessageColor = color;
            _statusMessageTimer?.Start();
        });
    }
}