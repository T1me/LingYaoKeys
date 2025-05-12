using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfApp.Services.Models
{
    /// <summary>
    /// 配置文件信息类
    /// </summary>
    public class ConfigFileInfo : INotifyPropertyChanged
    {
        private string _name = "默认配置";
        private string _filePath = string.Empty;
        private string _configHotkey = string.Empty;
        private bool _isDefault = false;
        private DateTime? _lastEditTime;
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        /// <summary>
        /// 触发属性变更事件
        /// </summary>
        /// <param name="propertyName"></param>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        
        /// <summary>
        /// 设置属性值并通知变更
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="field"></param>
        /// <param name="value"></param>
        /// <param name="propertyName"></param>
        /// <returns></returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        
        /// <summary>
        /// 配置文件名称
        /// </summary>
        public string Name 
        { 
            get => _name;
            set => SetProperty(ref _name, value);
        }
        
        /// <summary>
        /// 配置文件路径
        /// </summary>
        public string FilePath 
        { 
            get => _filePath; 
            set => SetProperty(ref _filePath, value);
        }
        
        /// <summary>
        /// 配置快捷键
        /// </summary>
        public string ConfigHotkey 
        { 
            get => _configHotkey; 
            set
            {
                if (SetProperty(ref _configHotkey, value))
                {
                    OnPropertyChanged(nameof(HasConfigHotkey));
                }
            }
        }
        
        /// <summary>
        /// 是否配置快捷键
        /// </summary>
        public bool HasConfigHotkey => !string.IsNullOrEmpty(_configHotkey);
        
        public bool IsDefault 
        { 
            get => _isDefault; 
            set => SetProperty(ref _isDefault, value);
        }
        
        /// <summary>
        /// 配置文件最后编辑时间
        /// </summary>
        public DateTime? LastEditTime 
        { 
            get => _lastEditTime; 
            set => SetProperty(ref _lastEditTime, value);
        }
        
        /// <summary>
        /// 构造函数
        /// </summary>
        public ConfigFileInfo() 
        {
            LastEditTime = DateTime.Now;
        }
        
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="name">配置文件名称</param>
        /// <param name="filePath">配置文件路径</param>
        /// <param name="isDefault">是否为默认配置</param>
        public ConfigFileInfo(string name, string filePath, bool isDefault = false)
        {
            _name = name;
            _filePath = filePath;
            _isDefault = isDefault;
            _lastEditTime = DateTime.Now;
        }
        
        /// <summary>
        /// 更新最后编辑时间
        /// </summary>
        public void UpdateEditTime()
        {
            LastEditTime = DateTime.Now;
        }
    }
} 