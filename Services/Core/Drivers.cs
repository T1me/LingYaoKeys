using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.ComponentModel;
using WpfApp.Services.Utils;

namespace WpfApp.Services.Core;

#region 接口与枚举定义

/// <summary>
/// 驱动接口 - 定义所有驱动必须实现的操作
/// </summary>
public interface IDriver : IDisposable
{
    bool Initialize();
    bool SendKeyDown(ushort vkCode);
    bool SendKeyUp(ushort vkCode);
    bool SendKeyPress(ushort vkCode, int duration = 100);
    bool MoveMouseAbsolute(int x, int y);
    bool SendMouseButton(MouseButtonType button, bool isDown);
    DeviceStatus GetLastStatus();
}

/// <summary>
/// 鼠标按键类型
/// </summary>
public enum MouseButtonType
{
    Left,
    Right,
    Middle,
    XButton1,
    XButton2,
    WheelUp,
    WheelDown
}

/// <summary>
/// 设备状态
/// </summary>
public enum DeviceStatus
{
    Unknown = 0,
    Ready = 1,
    Error = 2,
    NoKeyboard = 3,
    NoMouse = 4,
    InitFailed = 5
}

#endregion

#region 驱动工厂

/// <summary>
/// 驱动工厂 - 根据配置创建相应的驱动实例
/// </summary>
public static class DriverFactory
{
    /// <summary>
    /// 创建驱动实例
    /// </summary>
    public static IDriver CreateDriver(ISerilogManager logger, string driverType, string driverPath)
    {
        logger.Debug($"创建驱动实例: {driverType}");

        return driverType?.ToUpperInvariant() switch
        {
            "LYKEYS" => new LyKeys(logger, driverPath),
            "AHK" => new AhkDriver(logger),
            _ => throw new ArgumentException($"不支持的驱动类型: {driverType}")
        };
    }

    /// <summary>
    /// 准备驱动文件
    /// </summary>
    public static string PrepareDriverFiles(ISerilogManager logger, string driverType, IPathService pathService, Action<string, string> extractResource)
    {
        logger.Debug($"准备驱动文件: {driverType}");

        switch (driverType?.ToUpperInvariant())
        {
            case "LYKEYS":
                return PrepareLyKeysDriver(logger, pathService, extractResource);

            case "AHK":
                return string.Empty;

            default:
                throw new ArgumentException($"不支持的驱动类型: {driverType}");
        }
    }

    private static string PrepareLyKeysDriver(ISerilogManager logger, IPathService pathService, Action<string, string> extractResource)
    {
        var driverFile = pathService.GetDriverFilePath("lykeys.sys");
        var dllFile = pathService.GetDriverFilePath("lykeysdll.dll");

        logger.Debug($"驱动文件目录: {pathService.DriverPath}");

        bool needsExtraction = !File.Exists(driverFile) || !File.Exists(dllFile);

        if (needsExtraction)
        {
            logger.Debug("驱动文件不存在，开始提取...");
            extractResource("WpfApp.Resource.lykeysdll.lykeys.sys", driverFile);
            extractResource("WpfApp.Resource.lykeysdll.lykeysdll.dll", dllFile);
            logger.Debug("驱动文件提取完成");
        }

        if (!File.Exists(driverFile) || !File.Exists(dllFile))
        {
            throw new FileNotFoundException("驱动文件不存在或提取失败");
        }

        return driverFile;
    }
}

#endregion

#region AhkDriver 驱动实现

/// <summary>
/// AHK 驱动空实现（测试阶段）
/// </summary>
public class AhkDriver : IDriver
{
    private readonly ISerilogManager _logger;

    public AhkDriver(ISerilogManager logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool Initialize()
    {
        _logger.Debug("AHK 驱动初始化（测试模式）");
        return true;
    }

    public bool SendKeyDown(ushort vkCode)
    {
        _logger.Debug($"AHK SendKeyDown: {vkCode}（测试模式）");
        return true;
    }

    public bool SendKeyUp(ushort vkCode)
    {
        _logger.Debug($"AHK SendKeyUp: {vkCode}（测试模式）");
        return true;
    }

    public bool SendKeyPress(ushort vkCode, int duration = 100)
    {
        _logger.Debug($"AHK SendKeyPress: {vkCode}, duration: {duration}（测试模式）");
        return true;
    }

    public bool MoveMouseAbsolute(int x, int y)
    {
        _logger.Debug($"AHK MoveMouseAbsolute: ({x}, {y})（测试模式）");
        return true;
    }

    public bool SendMouseButton(MouseButtonType button, bool isDown)
    {
        _logger.Debug($"AHK SendMouseButton: {button}, isDown: {isDown}（测试模式）");
        return true;
    }

    public DeviceStatus GetLastStatus()
    {
        return DeviceStatus.Ready;
    }

    public void Dispose()
    {
        _logger.Debug("AHK 驱动释放（测试模式）");
    }
}

#endregion

#region LyKeys 驱动实现

/// <summary>
/// LyKeys驱动接口封装类
/// </summary>
public sealed class LyKeys : IDriver
{
    #region 字段和属性

    private readonly ISerilogManager _logger;
    private bool _isDisposed;
    private bool _isInitialized;
    private const string DriverName = "lykeys";
    private readonly string _driverPath;
    private IntPtr _dllHandle = IntPtr.Zero;
    private DeviceStatus _lastStatus = DeviceStatus.Unknown;

    #endregion

    #region 驱动状态定义

    private enum NTSTATUS : uint
    {
        STATUS_SUCCESS = 0x00000000,
        STATUS_UNSUCCESSFUL = 0xC0000001,
        STATUS_NOT_SUPPORTED = 0xC00000BB,
        STATUS_INVALID_PARAMETER = 0xC000000D,
        STATUS_INSUFFICIENT_RESOURCES = 0xC000009A,
        STATUS_DEVICE_NOT_CONNECTED = 0xC000009D
    }

    #endregion

    #region 构造函数和初始化

    public LyKeys(ISerilogManager logger, string driverPath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _driverPath = driverPath ?? throw new ArgumentNullException(nameof(driverPath));
    }

    public bool Initialize()
    {
        try
        {
            if (!IsAdministrator())
            {
                _logger.Error("需要管理员权限运行");
                throw new SecurityException("需要管理员权限运行");
            }

            var driverDirectory =
                Path.GetDirectoryName(_driverPath) ?? throw new InvalidOperationException("无法获取驱动文件目录");
            var dllPath = Path.Combine(driverDirectory, "lykeysdll.dll");

            // 检查文件是否存在
            if (!File.Exists(_driverPath))
            {
                _logger.Error($"sys驱动文件不存在: {_driverPath}");
                throw new FileNotFoundException($"sys驱动文件不存在: {_driverPath}");
            }

            if (!File.Exists(dllPath))
            {
                _logger.Error($"dll文件不存在: {dllPath}");
                throw new FileNotFoundException($"dll文件不存在: {dllPath}");
            }

            // 检查文件权限
            try
            {
                File.OpenRead(_driverPath).Dispose();
                File.OpenRead(dllPath).Dispose();
            }
            catch (Exception ex)
            {
                _logger.Error($"无法访问文件: {ex.Message}");
                throw new UnauthorizedAccessException($"无法访问文件: {ex.Message}", ex);
            }

            // 加载DLL
            _logger.Debug($"开始加载DLL: {dllPath}");
            _dllHandle = LyKeysNative.LoadLibrary(dllPath);
            if (_dllHandle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                var errorMessage = new Win32Exception(error).Message;
                _logger.Error($"DLL加载失败 - 错误代码: {error}, 错误信息: {errorMessage}");
                throw new Win32Exception(error, $"DLL加载失败: {errorMessage}");
            }

            _logger.Debug("DLL加载成功");

            // 加载驱动
            _logger.Debug($"======开始初始化加载驱动: {DriverName}======");
            var loadResult = LyKeysNative.LoadNTDriver(DriverName, _driverPath);
            if (!loadResult)
            {
                var error = Marshal.GetLastWin32Error();
                var errorMessage = new Win32Exception(error).Message;
                _logger.Error($"驱动加载失败 - 错误代码: {error}, 错误信息: {errorMessage}");
                return false;
            }

            // 初始化设备句柄
            _logger.Debug("开始初始化设备句柄");
            var handleResult = LyKeysNative.SetHandle();
            _logger.Debug($"设备句柄初始化结果: {handleResult}");
            if (!handleResult)
            {
                var error = Marshal.GetLastWin32Error();
                var errorMessage = new Win32Exception(error).Message;
                _logger.Error($"初始化设备句柄失败 - 错误代码: {error}, 错误信息: {errorMessage}");
                throw new InvalidOperationException($"初始化设备句柄失败: {errorMessage}");
            }

            // 检查设备状态
            LyKeysNative.CheckDeviceStatus();
            var status = LyKeysNative.GetDriverStatus();
            _lastStatus = status;
            _logger.Debug($"设备状态: {status}");

            if (status != DeviceStatus.Ready)
            {
                // 尝试重新初始化句柄
                _logger.Debug("设备状态不正确，尝试重新初始化句柄");
                if (!LyKeysNative.SetHandle())
                    throw new InvalidOperationException("重新初始化设备句柄失败");

                // 根据设备状态提供不同的错误信息
                if (status != DeviceStatus.Ready)
                {
                    string errorMessage;
                    switch (status)
                    {
                        case DeviceStatus.NoKeyboard:
                            errorMessage = "找不到键盘设备，请检查您的键盘连接。";
                            break;
                        case DeviceStatus.NoMouse:
                            errorMessage = "找不到鼠标设备，请检查您的鼠标连接。";
                            break;
                        case DeviceStatus.InitFailed:
                            errorMessage = "驱动初始化失败，请尝试重新启动计算机或重新安装驱动。";
                            break;
                        default:
                            errorMessage = $"设备状态异常: {status}";
                            break;
                    }
                    _logger.Error(errorMessage);
                    throw new InvalidOperationException(errorMessage);
                }
            }
            _logger.Debug("======驱动初始化成功======");
            _isInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"驱动初始化失败: {ex.Message}", ex);
            return false;
        }
    }

    public void UnloadDriver()
    {
        if (_isDisposed || !_isInitialized)
            return;

        try
        {
            if (_isInitialized)
            {
                LyKeysNative.UnloadNTDriver(DriverName);
                _isInitialized = false;
            }

            if (_dllHandle != IntPtr.Zero)
            {
                LyKeysNative.FreeLibrary(_dllHandle);
                _dllHandle = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"驱动卸载失败: {ex.Message}", ex);
            throw;
        }
    }

    private bool IsAdministrator()
    {
        using (var identity = WindowsIdentity.GetCurrent())
        {
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    #endregion

    #region IDisposable实现

    public void Dispose()
    {
        if (_isDisposed)
            return;

        try
        {
            if (_isInitialized)
            {
                LyKeysNative.UnloadNTDriver(DriverName);
                _isInitialized = false;
            }

            if (_dllHandle != IntPtr.Zero)
            {
                LyKeysNative.FreeLibrary(_dllHandle);
                _dllHandle = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("卸载驱动或DLL失败", ex);
        }
        finally
        {
            _isDisposed = true;
        }
    }

    #endregion

    #region 公共接口实现（使用 DriverExecutor 消除重复）

    /// <summary>
    /// 按下按键
    /// </summary>
    public bool SendKeyDown(ushort vkCode) =>
        DriverExecutor.Execute(
            () => LyKeysNative.KeyDown(vkCode),
            () => LyKeysNative.GetDriverStatus(),
            _isDisposed,
            _isInitialized,
            _logger,
            "SendKeyDown"
        );

    /// <summary>
    /// 释放按键
    /// </summary>
    public bool SendKeyUp(ushort vkCode) =>
        DriverExecutor.Execute(
            () => LyKeysNative.KeyUp(vkCode),
            () => LyKeysNative.GetDriverStatus(),
            _isDisposed,
            _isInitialized,
            _logger,
            "SendKeyUp"
        );

    /// <summary>
    /// 按下并释放按键
    /// </summary>
    public bool SendKeyPress(ushort vkCode, int duration = 100)
    {
        if (_isDisposed || !_isInitialized)
            return false;

        try
        {
            if (!SendKeyDown(vkCode))
                return false;

            Thread.Sleep(duration);
            return SendKeyUp(vkCode);
        }
        catch (Exception ex)
        {
            _logger.Error($"按键操作失败, vkCode: {vkCode}", ex);
            return false;
        }
    }

    /// <summary>
    /// 鼠标绝对位移
    /// </summary>
    public bool MoveMouseAbsolute(int x, int y) =>
        DriverExecutor.Execute(
            () => LyKeysNative.MouseMoveABSOLUTE(x, y),
            () => LyKeysNative.GetDriverStatus(),
            _isDisposed,
            _isInitialized,
            _logger,
            $"MoveMouseAbsolute({x}, {y})"
        );

    /// <summary>
    /// 鼠标按键操作
    /// </summary>
    public bool SendMouseButton(MouseButtonType button, bool isDown)
    {
        if (_isDisposed || !_isInitialized)
            return false;

        Action operation = button switch
        {
            MouseButtonType.Left => () => { if (isDown) LyKeysNative.MouseLeftButtonDown(); else LyKeysNative.MouseLeftButtonUp(); },
            MouseButtonType.Right => () => { if (isDown) LyKeysNative.MouseRightButtonDown(); else LyKeysNative.MouseRightButtonUp(); },
            MouseButtonType.Middle => () => { if (isDown) LyKeysNative.MouseMiddleButtonDown(); else LyKeysNative.MouseMiddleButtonUp(); },
            MouseButtonType.XButton1 => () => { if (isDown) LyKeysNative.MouseXButton1Down(); else LyKeysNative.MouseXButton1Up(); },
            MouseButtonType.XButton2 => () => { if (isDown) LyKeysNative.MouseXButton2Down(); else LyKeysNative.MouseXButton2Up(); },
            MouseButtonType.WheelUp => () => { if (isDown) LyKeysNative.MouseWheelUp(120); },
            MouseButtonType.WheelDown => () => { if (isDown) LyKeysNative.MouseWheelDown(120); },
            _ => () => throw new ArgumentException($"不支持的鼠标按键类型: {button}")
        };

        return DriverExecutor.Execute(
            operation,
            () => LyKeysNative.GetDriverStatus(),
            _isDisposed,
            _isInitialized,
            _logger,
            $"SendMouseButton({button}, {isDown})"
        );
    }

    /// <summary>
    /// 获取最后一次设备状态检查的结果
    /// </summary>
    public DeviceStatus GetLastStatus()
    {
        return _lastStatus;
    }

    #endregion

    #region 私有嵌套类：DriverExecutor（统一异常重试逻辑）

    /// <summary>
    /// 驱动操作执行器 - 统一处理异常重试逻辑，消除代码重复
    /// </summary>
    private static class DriverExecutor
    {
        /// <summary>
        /// 执行驱动操作，失败时自动重试一次
        /// </summary>
        public static bool Execute(
            Action operation,
            Func<DeviceStatus> getStatus,
            bool isDisposed,
            bool isInitialized,
            ISerilogManager logger,
            string operationName)
        {
            // 快速检查
            if (isDisposed || !isInitialized)
                return false;

            try
            {
                // 首次尝试执行
                operation();
                return true;
            }
            catch (Exception)
            {
                // 捕获异常时才进行完整状态检查和重试
                try
                {
                    var status = getStatus();
                    if (status == DeviceStatus.Ready)
                    {
                        try
                        {
                            operation();
                            return true;
                        }
                        catch
                        {
                            // 已重试，忽略第二次异常
                        }
                    }
                }
                catch
                {
                    // 忽略状态检查异常
                }

                return false;
            }
        }
    }

    #endregion

    #region 私有嵌套类：LyKeysNative（P/Invoke 声明集中管理）

    /// <summary>
    /// LyKeys 驱动原生接口声明
    /// 集中管理所有 P/Invoke 声明
    /// </summary>
    private static class LyKeysNative
    {
        // 驱动管理函数
        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool SetHandle();

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool LoadNTDriver(string lpszDriverName, string lpszDriverPath);

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool UnloadNTDriver(string szSvrName);

        // 状态管理函数
        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void CheckDeviceStatus();

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern DeviceStatus GetDriverStatus();

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetDetailedErrorCode();

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong GetLastCheckTime();

        // 键盘操作函数
        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void KeyDown(ushort vkCode);

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void KeyUp(ushort vkCode);

        // 鼠标操作函数
        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void MouseLeftButtonDown();

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void MouseLeftButtonUp();

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void MouseRightButtonDown();

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void MouseRightButtonUp();

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void MouseMiddleButtonDown();

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void MouseMiddleButtonUp();

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void MouseXButton1Down();

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void MouseXButton1Up();

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void MouseXButton2Down();

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void MouseXButton2Up();

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void MouseMoveRELATIVE(int dx, int dy);

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void MouseMoveABSOLUTE(int x, int y);

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void MouseWheelUp(ushort wheelDelta);

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void MouseWheelDown(ushort wheelDelta);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetDriverHandle")]
        public static extern IntPtr GetDriverHandle();
    }

    #endregion
}

#endregion
