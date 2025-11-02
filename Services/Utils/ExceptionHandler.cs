using System;
using System.Threading.Tasks;
using System.Windows;

namespace WpfApp.Services.Utils
{
    /// <summary>
    /// 统一的异常处理器，用于简化和标准化异常处理逻辑
    /// </summary>
    public class ExceptionHandler
    {
        private readonly SerilogManager _logger = SerilogManager.Instance;

        /// <summary>
        /// 执行同步操作并处理异常
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="action">要执行的操作</param>
        /// <param name="operationName">操作名称，用于日志记录</param>
        /// <param name="defaultValue">发生异常时的默认返回值</param>
        /// <param name="customHandler">自定义异常处理逻辑</param>
        /// <param name="showMessageBox">是否显示错误消息框</param>
        /// <returns>操作结果或默认值</returns>
        public T Execute<T>(
            Func<T> action,
            string operationName,
            T defaultValue = default,
            Action<Exception>? customHandler = null,
            bool showMessageBox = true)
        {
            try
            {
                return action();
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                _logger.Error($"{operationName}失败：网络连接问题", ex);
                customHandler?.Invoke(ex);

                if (showMessageBox)
                {
                    ShowErrorMessage($"网络错误：{ex.Message}", "网络连接失败");
                }

                return defaultValue;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.Error($"{operationName}失败：权限不足", ex);
                customHandler?.Invoke(ex);

                if (showMessageBox)
                {
                    ShowErrorMessage("操作失败：权限不足，请以管理员身份运行程序", "权限错误");
                }

                return defaultValue;
            }
            catch (System.IO.IOException ex)
            {
                _logger.Error($"{operationName}失败：文件IO错误", ex);
                customHandler?.Invoke(ex);

                if (showMessageBox)
                {
                    ShowErrorMessage($"文件操作失败：{ex.Message}", "文件错误");
                }

                return defaultValue;
            }
            catch (Exception ex)
            {
                _logger.Error($"{operationName}异常", ex);
                customHandler?.Invoke(ex);

                if (showMessageBox)
                {
                    ShowErrorMessage($"操作失败：{ex.Message}", "错误");
                }

                return defaultValue;
            }
        }

        /// <summary>
        /// 执行同步操作（无返回值）并处理异常
        /// </summary>
        /// <param name="action">要执行的操作</param>
        /// <param name="operationName">操作名称，用于日志记录</param>
        /// <param name="customHandler">自定义异常处理逻辑</param>
        /// <param name="showMessageBox">是否显示错误消息框</param>
        public void Execute(
            Action action,
            string operationName,
            Action<Exception>? customHandler = null,
            bool showMessageBox = true)
        {
            Execute<object>(
                () => { action(); return null; },
                operationName,
                null,
                customHandler,
                showMessageBox);
        }

        /// <summary>
        /// 执行异步操作并处理异常
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="action">要执行的异步操作</param>
        /// <param name="operationName">操作名称，用于日志记录</param>
        /// <param name="defaultValue">发生异常时的默认返回值</param>
        /// <param name="customHandler">自定义异常处理逻辑</param>
        /// <param name="showMessageBox">是否显示错误消息框</param>
        /// <returns>操作结果或默认值</returns>
        public async Task<T> ExecuteAsync<T>(
            Func<Task<T>> action,
            string operationName,
            T defaultValue = default,
            Action<Exception>? customHandler = null,
            bool showMessageBox = true)
        {
            try
            {
                return await action();
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                _logger.Error($"{operationName}失败：网络连接问题", ex);
                customHandler?.Invoke(ex);

                if (showMessageBox)
                {
                    ShowErrorMessage($"网络错误：{ex.Message}", "网络连接失败");
                }

                return defaultValue;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.Error($"{operationName}失败：权限不足", ex);
                customHandler?.Invoke(ex);

                if (showMessageBox)
                {
                    ShowErrorMessage("操作失败：权限不足，请以管理员身份运行程序", "权限错误");
                }

                return defaultValue;
            }
            catch (System.IO.IOException ex)
            {
                _logger.Error($"{operationName}失败：文件IO错误", ex);
                customHandler?.Invoke(ex);

                if (showMessageBox)
                {
                    ShowErrorMessage($"文件操作失败：{ex.Message}", "文件错误");
                }

                return defaultValue;
            }
            catch (Exception ex)
            {
                _logger.Error($"{operationName}异常", ex);
                customHandler?.Invoke(ex);

                if (showMessageBox)
                {
                    ShowErrorMessage($"操作失败：{ex.Message}", "错误");
                }

                return defaultValue;
            }
        }

        /// <summary>
        /// 执行异步操作（无返回值）并处理异常
        /// </summary>
        /// <param name="action">要执行的异步操作</param>
        /// <param name="operationName">操作名称，用于日志记录</param>
        /// <param name="customHandler">自定义异常处理逻辑</param>
        /// <param name="showMessageBox">是否显示错误消息框</param>
        public async Task ExecuteAsync(
            Func<Task> action,
            string operationName,
            Action<Exception>? customHandler = null,
            bool showMessageBox = true)
        {
            await ExecuteAsync<object>(
                async () => { await action(); return null; },
                operationName,
                null,
                customHandler,
                showMessageBox);
        }

        /// <summary>
        /// 显示错误消息框
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <param name="title">消息框标题</param>
        private void ShowErrorMessage(string message, string title = "错误")
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                HandyControl.Controls.MessageBox.Error(message, title);
            });
        }
    }
}
