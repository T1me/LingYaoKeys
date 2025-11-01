using System;
using System.Windows;
using WpfApp.ViewModels;
using WpfApp.Views;
using WpfApp.Services.Utils;
using Application = System.Windows.Application;

namespace WpfApp.Services.Core
{
    /// <summary>
    /// 浮窗管理服务 - 负责浮动状态窗口的生命周期和状态同步
    /// </summary>
    public class FloatingWindowService : IDisposable
    {
        private FloatingStatusWindow? _floatingWindow;
        private FloatingStatusViewModel? _floatingViewModel;
        private readonly object _updateLock = new();
        private DateTime _lastUpdateTime = DateTime.MinValue;
        private const int UPDATE_THROTTLE_MS = 200;

        // 状态属性
        private bool _isEnabled;
        private bool _isExecuting;
        private bool _isHotkeyControlEnabled = true;
        private double _opacity = 0.8;

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    if (value)
                        Show();
                    else
                        Hide();
                }
            }
        }

        public bool IsExecuting
        {
            get => _isExecuting;
            set
            {
                if (_isExecuting != value)
                {
                    _isExecuting = value;
                    UpdateStatus();
                }
            }
        }

        public bool IsHotkeyControlEnabled
        {
            get => _isHotkeyControlEnabled;
            set
            {
                if (_isHotkeyControlEnabled != value)
                {
                    _isHotkeyControlEnabled = value;
                    UpdateStatus();
                }
            }
        }

        public double Opacity
        {
            get => _opacity;
            set
            {
                if (Math.Abs(_opacity - value) > 0.01)
                {
                    _opacity = value;
                    if (_floatingViewModel != null)
                    {
                        _floatingViewModel.Opacity = value;
                    }
                }
            }
        }

        /// <summary>
        /// 初始化浮窗(需要在UI线程调用)
        /// </summary>
        public void Initialize(MainWindow ownerWindow)
        {
            if (_floatingWindow != null) return;

            try
            {
                _floatingViewModel = new FloatingStatusViewModel
                {
                    IsHotkeyControlEnabled = _isHotkeyControlEnabled,
                    IsExecuting = _isExecuting,
                    Opacity = _opacity
                };

                _floatingWindow = new FloatingStatusWindow(ownerWindow)
                {
                    DataContext = _floatingViewModel
                };

                if (_isEnabled)
                {
                    _floatingWindow.Show();
                }

            }
            catch (Exception ex)
            {
                SerilogManager.Instance.Error("初始化浮窗失败", ex);
            }
        }

        /// <summary>
        /// 显示浮窗
        /// </summary>
        public void Show()
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_floatingWindow == null)
                    {
                        SerilogManager.Instance.Warning("浮窗未初始化，无法显示");
                        return;
                    }

                    _floatingWindow.Show();
                    UpdateStatus();
                    SerilogManager.Instance.Debug("浮窗已显示");
                }
                catch (Exception ex)
                {
                    SerilogManager.Instance.Error("显示浮窗失败", ex);
                }
            }));
        }

        /// <summary>
        /// 隐藏浮窗
        /// </summary>
        public void Hide()
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    _floatingWindow?.Hide();
                    SerilogManager.Instance.Debug("浮窗已隐藏");
                }
                catch (Exception ex)
                {
                    SerilogManager.Instance.Error("隐藏浮窗失败", ex);
                }
            }));
        }

        /// <summary>
        /// 更新浮窗状态(带节流控制)
        /// </summary>
        public void UpdateStatus(bool forceUpdate = false)
        {
            lock (_updateLock)
            {
                var now = DateTime.Now;
                if (!forceUpdate && (now - _lastUpdateTime).TotalMilliseconds < UPDATE_THROTTLE_MS)
                {
                    SerilogManager.Instance.Debug("浮窗状态更新被节流控制跳过");
                    return;
                }
                _lastUpdateTime = now;
            }

            Application.Current?.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (_floatingViewModel != null)
                    {
                        _floatingViewModel.IsHotkeyControlEnabled = _isHotkeyControlEnabled;
                        _floatingViewModel.IsExecuting = _isExecuting;
                        SerilogManager.Instance.Debug($"浮窗状态已更新: IsExecuting={_isExecuting}, IsHotkeyControlEnabled={_isHotkeyControlEnabled}");
                    }
                }
                catch (Exception ex)
                {
                    SerilogManager.Instance.Error("更新浮窗状态失败", ex);
                }
            });
        }

        public void Dispose()
        {
            try
            {
                _floatingWindow?.Close();
                _floatingWindow = null;
                _floatingViewModel = null;
                SerilogManager.Instance.Debug("浮窗服务已释放");
            }
            catch (Exception ex)
            {
                SerilogManager.Instance.Error("释放浮窗服务失败", ex);
            }
        }
    }
}
