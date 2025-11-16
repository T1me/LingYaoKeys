using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Timers;
using WpfApp.Services.Models;
using WpfApp.Services.Utils;
using Application = System.Windows.Application;

namespace WpfApp.Services.Core
{
    /// <summary>
    /// 窗口管理服务 - 负责多个目标窗口的选择、监控和状态管理
    /// </summary>
    public class WindowManagementService : IDisposable
    {
        private readonly ISerilogManager _logger;
        private readonly HotkeyService _hotkeyService;
        private readonly object _windowCheckLock = new();
        private System.Timers.Timer? _windowCheckTimer;
        private System.Timers.Timer? _activeWindowCheckTimer;

        // 多窗口信息
        private readonly List<TargetWindow> _selectedWindows = new();
        private readonly Dictionary<Guid, IntPtr> _windowHandles = new();
        private bool _isAnyTargetWindowActive;

        // 事件
        public event Action? WindowListChanged;
        public event Action<bool>? TargetWindowActiveChanged;

        // 属性
        public IReadOnlyList<TargetWindow> SelectedWindows => _selectedWindows.AsReadOnly();

        public bool IsAnyTargetWindowActive
        {
            get => _isAnyTargetWindowActive;
            private set
            {
                if (_isAnyTargetWindowActive != value)
                {
                    _isAnyTargetWindowActive = value;
                    TargetWindowActiveChanged?.Invoke(value);
                    _hotkeyService.IsTargetWindowActive = value;
                }
            }
        }

        public WindowManagementService(ISerilogManager logger, HotkeyService hotkeyService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
            InitializeTimers();
        }

        private void InitializeTimers()
        {
            // 窗口状态检查定时器(5秒)
            _windowCheckTimer = new System.Timers.Timer(5000);
            _windowCheckTimer.Elapsed += WindowCheckTimer_Elapsed;

            // 活动窗口检查定时器(500ms)
            _activeWindowCheckTimer = new System.Timers.Timer(500);
            _activeWindowCheckTimer.Elapsed += ActiveWindowCheckTimer_Elapsed;
            _activeWindowCheckTimer.Start();
        }

        /// <summary>
        /// 添加窗口
        /// </summary>
        public void AddWindow(string processName, string title, string className)
        {
            var window = new TargetWindow
            {
                ProcessName = processName,
                Title = title,
                ClassName = className
            };

            _selectedWindows.Add(window);

            var handles = FindWindowsByProcessAndClass(processName, className);
            if (handles?.Count > 0)
            {
                _windowHandles[window.Id] = handles[0].Handle;
            }

            WindowListChanged?.Invoke();

            if (_selectedWindows.Count == 1)
            {
                StartWindowCheck();
            }

            SyncToHotkeyService();
            _logger.Info($"已添加窗口: {title}, 进程名: {processName}");
        }

        /// <summary>
        /// 删除窗口
        /// </summary>
        public void RemoveWindow(Guid windowId)
        {
            _selectedWindows.RemoveAll(w => w.Id == windowId);
            _windowHandles.Remove(windowId);

            WindowListChanged?.Invoke();

            if (_selectedWindows.Count == 0)
            {
                StopWindowCheck();
            }

            SyncToHotkeyService();
            _logger.Info($"已删除窗口: {windowId}");
        }

        /// <summary>
        /// 清除所有窗口
        /// </summary>
        public void ClearAllWindows()
        {
            _selectedWindows.Clear();
            _windowHandles.Clear();
            StopWindowCheck();
            WindowListChanged?.Invoke();
            SyncToHotkeyService();
            _logger.Info("已清除所有窗口");
        }

        /// <summary>
        /// 获取窗口句柄
        /// </summary>
        public IntPtr GetWindowHandle(Guid windowId)
        {
            return _windowHandles.TryGetValue(windowId, out var handle) ? handle : IntPtr.Zero;
        }

        /// <summary>
        /// 获取所有窗口句柄
        /// </summary>
        public List<IntPtr> GetAllWindowHandles()
        {
            return _windowHandles.Values.Where(h => h != IntPtr.Zero).ToList();
        }

        /// <summary>
        /// 从配置加载窗口列表
        /// </summary>
        public void LoadWindowsFromConfig(List<TargetWindow> windows)
        {
            ClearAllWindows();

            foreach (var window in windows)
            {
                _selectedWindows.Add(window);

                var handles = FindWindowsByProcessAndClass(window.ProcessName, window.ClassName);
                if (handles?.Count > 0)
                {
                    _windowHandles[window.Id] = handles[0].Handle;
                }
            }

            if (_selectedWindows.Count > 0)
            {
                StartWindowCheck();
            }

            WindowListChanged?.Invoke();
            SyncToHotkeyService();
        }

        /// <summary>
        /// 根据进程名和类名查找窗口
        /// </summary>
        private List<WindowInfo> FindWindowsByProcessAndClass(string processName, string targetClassName)
        {
            var result = new List<WindowInfo>();
            if (string.IsNullOrEmpty(processName)) return result;

            try
            {
                var processes = Process.GetProcessesByName(processName);
                if (processes.Length == 0) return result;

                foreach (var process in processes)
                {
                    try
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            var className = GetWindowClassName(process.MainWindowHandle);

                            // 优先使用类名匹配（类名通常固定）
                            if (!string.IsNullOrEmpty(targetClassName) && className == targetClassName)
                            {
                                var title = GetWindowTitle(process.MainWindowHandle);
                                result.Add(new WindowInfo(process.MainWindowHandle, title, className, process.ProcessName));
                            }
                            else if (string.IsNullOrEmpty(targetClassName))
                            {
                                var title = GetWindowTitle(process.MainWindowHandle);
                                result.Add(new WindowInfo(process.MainWindowHandle, title, className, process.ProcessName));
                            }
                        }
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"查找窗口时发生异常: {ex.Message}");
            }

            return result;
        }

        private void StartWindowCheck()
        {
            _windowCheckTimer?.Start();
            _logger.Debug("开始定时检查窗口状态");
        }

        private void StopWindowCheck()
        {
            _windowCheckTimer?.Stop();
            _logger.Debug("停止定时检查窗口状态");
        }

        private void WindowCheckTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            try
            {
                lock (_windowCheckLock)
                {
                    foreach (var window in _selectedWindows)
                    {
                        var handles = FindWindowsByProcessAndClass(window.ProcessName, window.ClassName);

                        if (handles?.Count > 0)
                        {
                            var handle = handles[0].Handle;
                            if (!_windowHandles.ContainsKey(window.Id) || _windowHandles[window.Id] != handle)
                            {
                                _windowHandles[window.Id] = handle;
                                _logger.Info($"窗口句柄已更新: {window.ProcessName}");
                            }
                        }
                        else
                        {
                            _windowHandles.Remove(window.Id);
                        }
                    }

                    SyncToHotkeyService();
                }
            }
            catch (Exception ex)
            {
                _logger.Error("检查窗口状态时发生异常", ex);
            }
        }

        private void ActiveWindowCheckTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            try
            {
                var activeWindow = GetForegroundWindow();
                var isAnyActive = _windowHandles.Values.Contains(activeWindow);

                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    IsAnyTargetWindowActive = isAnyActive;
                }));
            }
            catch (Exception ex)
            {
                _logger.Error("检查活动窗口状态时发生异常", ex);
            }
        }

        private void SyncToHotkeyService()
        {
            var handles = GetAllWindowHandles();
            _hotkeyService.SetTargetWindows(handles);
        }

        // Win32 API
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private string GetWindowTitle(IntPtr hWnd)
        {
            var title = new StringBuilder(256);
            GetWindowText(hWnd, title, title.Capacity);
            return title.ToString().Trim();
        }

        private string GetWindowClassName(IntPtr hWnd)
        {
            var className = new StringBuilder(256);
            GetClassName(hWnd, className, className.Capacity);
            return className.ToString().Trim();
        }

        public void Dispose()
        {
            _windowCheckTimer?.Stop();
            _windowCheckTimer?.Dispose();
            _activeWindowCheckTimer?.Stop();
            _activeWindowCheckTimer?.Dispose();
        }

        private class WindowInfo
        {
            public IntPtr Handle { get; set; }
            public string Title { get; set; }
            public string ClassName { get; set; }
            public string ProcessName { get; set; }

            public WindowInfo(IntPtr handle, string title, string className, string processName)
            {
                Handle = handle;
                Title = title;
                ClassName = className;
                ProcessName = processName;
            }
        }
    }
}
