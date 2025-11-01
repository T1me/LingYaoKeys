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
    /// 窗口管理服务 - 负责目标窗口的选择、监控和状态管理
    /// </summary>
    public class WindowManagementService : IDisposable
    {
        private readonly HotkeyService _hotkeyService;
        private readonly object _windowCheckLock = new();
        private System.Timers.Timer? _windowCheckTimer;
        private System.Timers.Timer? _activeWindowCheckTimer;

        // 窗口信息
        private IntPtr _selectedWindowHandle = IntPtr.Zero;
        private string _selectedWindowTitle = "空";
        private string _selectedWindowClassName = string.Empty;
        private string _selectedWindowProcessName = string.Empty;
        private bool _isTargetWindowActive;

        // 事件
        public event Action<IntPtr>? WindowHandleChanged;
        public event Action<bool>? TargetWindowActiveChanged;
        public event Action<string, string, string, string>? WindowInfoChanged;

        // 属性
        public IntPtr SelectedWindowHandle
        {
            get => _selectedWindowHandle;
            private set
            {
                if (_selectedWindowHandle != value)
                {
                    _selectedWindowHandle = value;
                    WindowHandleChanged?.Invoke(value);
                    _hotkeyService.TargetWindowHandle = value;
                    SerilogManager.Instance.Debug($"窗口句柄已更新: {value}");
                }
            }
        }

        public string SelectedWindowTitle
        {
            get => _selectedWindowTitle;
            private set => _selectedWindowTitle = value;
        }

        public string SelectedWindowClassName
        {
            get => _selectedWindowClassName;
            private set => _selectedWindowClassName = value;
        }

        public string SelectedWindowProcessName
        {
            get => _selectedWindowProcessName;
            private set => _selectedWindowProcessName = value;
        }

        public bool IsTargetWindowActive
        {
            get => _isTargetWindowActive;
            private set
            {
                if (_isTargetWindowActive != value)
                {
                    _isTargetWindowActive = value;
                    TargetWindowActiveChanged?.Invoke(value);
                    _hotkeyService.IsTargetWindowActive = value;
                }
            }
        }

        public WindowManagementService(HotkeyService hotkeyService)
        {
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
        /// 更新选中的窗口
        /// </summary>
        public void UpdateSelectedWindow(IntPtr handle, string title, string className, string processName)
        {
            SelectedWindowHandle = handle;
            SelectedWindowClassName = className;
            SelectedWindowProcessName = processName;
            SelectedWindowTitle = $"{title} (句柄: {handle.ToInt64()})";

            WindowInfoChanged?.Invoke(handle.ToString(), title, className, processName);

            StartWindowCheck();
            SerilogManager.Instance.Info($"已选择窗口: {title}, 句柄: {handle.ToInt64()}, 类名: {className}, 进程名: {processName}");
        }

        /// <summary>
        /// 清除选中的窗口
        /// </summary>
        public void ClearSelectedWindow()
        {
            StopWindowCheck();

            _selectedWindowHandle = IntPtr.Zero;
            _selectedWindowTitle = "空";
            _selectedWindowClassName = string.Empty;
            _selectedWindowProcessName = string.Empty;

            _hotkeyService.TargetWindowHandle = IntPtr.Zero;

            WindowInfoChanged?.Invoke(IntPtr.Zero.ToString(), "空", string.Empty, string.Empty);
            SerilogManager.Instance.Debug("已清除窗口信息");
        }

        /// <summary>
        /// 从配置加载窗口信息
        /// </summary>
        public bool LoadWindowFromConfig(string processName, string title)
        {
            if (string.IsNullOrEmpty(processName))
            {
                SerilogManager.Instance.Debug("没有保存的窗口进程信息，跳过加载");
                return false;
            }

            _selectedWindowProcessName = processName;
            _selectedWindowTitle = title;

            var windows = FindWindowsByProcessName(processName, title);
            if (windows != null && windows.Count > 0)
            {
                var window = windows[0];
                UpdateSelectedWindow(window.Handle, window.Title, window.ClassName, window.ProcessName);
                return true;
            }
            else
            {
                SerilogManager.Instance.Warning($"未找到进程 {processName} 的窗口");
                SelectedWindowHandle = IntPtr.Zero;
                SelectedWindowTitle = $"{title} (进程未运行)";
                _hotkeyService.TargetWindowHandle = IntPtr.Zero;
                StartWindowCheck();
                return false;
            }
        }

        /// <summary>
        /// 根据进程名查找窗口
        /// </summary>
        private List<WindowInfo> FindWindowsByProcessName(string processName, string targetTitle = null)
        {
            var result = new List<WindowInfo>();
            if (string.IsNullOrEmpty(processName) && string.IsNullOrEmpty(targetTitle)) return result;

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
                            var title = GetWindowTitle(process.MainWindowHandle);
                            var className = GetWindowClassName(process.MainWindowHandle);

                            if (!string.IsNullOrEmpty(targetTitle))
                            {
                                if (title.Contains(targetTitle, StringComparison.OrdinalIgnoreCase))
                                {
                                    result.Add(new WindowInfo(process.MainWindowHandle, title, className, process.ProcessName));
                                }
                            }
                            else
                            {
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
                SerilogManager.Instance.Error($"查找窗口时发生异常: {ex.Message}");
            }

            return result;
        }

        private void StartWindowCheck()
        {
            _windowCheckTimer?.Start();
            SerilogManager.Instance.Debug("开始定时检查窗口状态");
        }

        private void StopWindowCheck()
        {
            _windowCheckTimer?.Stop();
            SerilogManager.Instance.Debug("停止定时检查窗口状态");
        }

        private void WindowCheckTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (string.IsNullOrEmpty(SelectedWindowProcessName)) return;

            try
            {
                lock (_windowCheckLock)
                {
                    var originalTitle = SelectedWindowTitle.Split(new[] { " (句柄:", " (进程未运行)" }, StringSplitOptions.None)[0];
                    var windows = FindWindowsByProcessName(SelectedWindowProcessName, originalTitle);

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (windows.Any())
                        {
                            var targetWindow = windows.First();
                            if (targetWindow.Handle != SelectedWindowHandle)
                            {
                                UpdateSelectedWindow(targetWindow.Handle, targetWindow.Title, targetWindow.ClassName, targetWindow.ProcessName);
                            }
                        }
                        else if (SelectedWindowHandle != IntPtr.Zero)
                        {
                            SelectedWindowHandle = IntPtr.Zero;
                            SelectedWindowTitle = $"{originalTitle} (进程未运行)";
                            _hotkeyService.TargetWindowHandle = IntPtr.Zero;
                            SerilogManager.Instance.Warning($"进程 {SelectedWindowProcessName} 已关闭");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                SerilogManager.Instance.Error("检查窗口状态时发生异常", ex);
            }
        }

        private void ActiveWindowCheckTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            try
            {
                if (SelectedWindowHandle == IntPtr.Zero)
                {
                    if (IsTargetWindowActive)
                    {
                        IsTargetWindowActive = false;
                    }
                    return;
                }

                var activeWindow = GetForegroundWindow();
                var isActive = activeWindow == SelectedWindowHandle;

                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    IsTargetWindowActive = isActive;
                }));
            }
            catch (Exception ex)
            {
                SerilogManager.Instance.Error("检查活动窗口状态时发生异常", ex);
            }
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
