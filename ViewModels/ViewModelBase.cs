using System.ComponentModel;
using System.Runtime.CompilerServices;
using WpfApp.Services.Core;
using WpfApp.Services.Utils;

namespace WpfApp.ViewModels;

public class ViewModelBase : INotifyPropertyChanged
{
    /// <summary>
    /// 统一的日志管理器（所有ViewModel共享）
    /// </summary>
    protected readonly SerilogManager Logger = SerilogManager.Instance;

    /// <summary>
    /// 统一的配置管理器（所有ViewModel共享）
    /// </summary>
    protected readonly IConfigManager ConfigManager = WpfApp.Services.Core.ConfigManager.Instance;

    /// <summary>
    /// 统一的异常处理器（所有ViewModel共享）
    /// </summary>
    protected readonly ExceptionHandler ExceptionHandler = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// 创建RelayCommand的工厂方法
    /// </summary>
    /// <param name="execute">命令执行逻辑</param>
    /// <param name="canExecute">命令是否可执行的判断逻辑</param>
    /// <returns>RelayCommand实例</returns>
    protected RelayCommand CreateCommand(Action execute, Func<bool>? canExecute = null)
    {
        return new RelayCommand(execute, canExecute);
    }

    /// <summary>
    /// 创建泛型RelayCommand的工厂方法
    /// </summary>
    /// <typeparam name="T">命令参数类型</typeparam>
    /// <param name="execute">命令执行逻辑</param>
    /// <param name="canExecute">命令是否可执行的判断逻辑</param>
    /// <returns>RelayCommand&lt;T&gt;实例</returns>
    protected RelayCommand<T> CreateCommand<T>(Action<T> execute, Func<T, bool>? canExecute = null)
    {
        return new RelayCommand<T>(execute, canExecute);
    }
}