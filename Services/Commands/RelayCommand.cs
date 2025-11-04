using System.Windows.Input;

namespace WpfApp.Services.Utils;

public class RelayCommand<T> : ICommand
{
    private readonly Action<T> _execute;
    private readonly Func<T, bool>? _canExecute;

    // 构造函数
    public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    // 命令是否可用
    public bool CanExecute(object? parameter)
    {
        if (_canExecute == null)
            return true;

        // 安全地转换参数
        if (parameter == null && default(T) == null)
            return _canExecute((T)(object?)null!);

        if (parameter is T typedParameter)
            return _canExecute(typedParameter);

        return false;
    }

    // 执行命令
    public void Execute(object? parameter)
    {
        // 安全地转换参数
        if (parameter == null && default(T) == null)
        {
            _execute((T)(object?)null!);
            return;
        }

        if (parameter is T typedParameter)
        {
            _execute(typedParameter);
            return;
        }

        throw new ArgumentException($"Parameter must be of type {typeof(T).Name}", nameof(parameter));
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke() ?? true;
    }

    public void Execute(object? parameter)
    {
        _execute();
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}