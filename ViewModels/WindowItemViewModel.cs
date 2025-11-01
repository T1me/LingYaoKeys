using System;

namespace WpfApp.ViewModels
{
    public class WindowItemViewModel
    {
        public Guid Id { get; }
        public string ProcessName { get; }
        public string Title { get; }
        public string ClassName { get; }

        public WindowItemViewModel(string processName, string title, string className, Guid id)
        {
            Id = id;
            ProcessName = processName;
            Title = title;
            ClassName = className;
        }

        public string DisplayName => $"{Title} ({ProcessName})";
    }
}
