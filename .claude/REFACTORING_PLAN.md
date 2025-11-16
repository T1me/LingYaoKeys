# Services/Core 代码治理重构计划

> **文档创建时间**: 2025-11-17
> **重构目标**: 降低代码复杂度、提升可维护性、遵循 SOLID 原则
> **预计工作量**: 3-5 个工作日

---

## 📊 现状分析

### 代码统计

```
Services/Core/ (27 个文件，5927 行代码)
├── 🔴 严重超载 (2 个文件)
│   ├── HotkeyService.cs         1,002 行
│   └── LyKeys.cs                  690 行
├── 🟡 中度超载 (3 个文件)
│   ├── LyKeysService.cs           561 行
│   ├── ConfigManager.cs           517 行
│   └── KeyConfigurationService.cs 413 行
└── 🟢 正常范围 (22 个文件)      2,744 行
```

### 核心问题

#### 1. HotkeyService.cs (1,002行)
**问题诊断**：
- ❌ 违反单一职责原则（SRP）
- ❌ 混杂 6 种职责：钩子管理、键盘处理、鼠标处理、窗口验证、配置管理、序列控制
- ❌ 测试困难（无法独立测试某个功能）
- ❌ 维护成本高（修改一处影响多处）

**职责拆分**：
```
HotkeyService (1002行) → 5 个独立类
├─ HotkeyService.cs (200行)          // 核心协调器
├─ HookManager.cs (250行)            // Win32 钩子管理
├─ KeyboardHookHandler.cs (200行)   // 键盘事件处理
├─ MouseHookHandler.cs (200行)      // 鼠标事件处理
└─ WindowValidator.cs (150行)       // 窗口验证逻辑
```

#### 2. LyKeys.cs (690行)
**问题诊断**：
- ❌ 严重违反 DRY 原则
- ❌ 约 300 行重复的异常重试逻辑
- ❌ P/Invoke 声明分散（120+ 行）

**重构策略**：
```
LyKeys (690行) → 减少至 ~350 行（减少 50%）
├─ 提取公共逻辑： DriverExecutor.cs (100行)
├─ 集中 P/Invoke： LyKeysNative.cs (150行)
├─ 操作分类拆分： KeyboardOperations.cs (120行)
└─               MouseOperations.cs (120行)
```

#### 3. 其他中度超载文件
- **LyKeysService.cs**: 虚拟键码映射可独立为 `VirtualKeyCodeMapper.cs`
- **ConfigManager.cs**: 加载/保存逻辑可独立为 `ConfigLoader.cs` 和 `ConfigSaver.cs`

---

## 🎯 重构方案

### 阶段一：拆分 HotkeyService（优先级：🔴 高）

#### 1.1 创建 Hooks 子目录

**新建文件**：`Services/Core/Hooks/HookManager.cs`
```csharp
namespace WpfApp.Services.Core.Hooks;

/// <summary>
/// Win32 钩子管理器
/// 负责键盘和鼠标钩子的安装、卸载、生命周期管理
/// </summary>
public class HookManager : IDisposable
{
    private IntPtr _keyboardHookHandle;
    private IntPtr _mouseHookHandle;
    private readonly HookProc _keyboardProcDelegate;
    private readonly HookProc _mouseProcDelegate;

    // Win32 API 声明
    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", ...)]
    private static extern IntPtr SetWindowsHookEx(...);

    // ... 钩子管理方法
}
```

**新建文件**：`Services/Core/Hooks/KeyboardHookHandler.cs`
```csharp
namespace WpfApp.Services.Core.Hooks;

/// <summary>
/// 键盘钩子处理器
/// 处理键盘按键的按下、释放事件，识别热键
/// </summary>
public class KeyboardHookHandler
{
    public event Action<VirtualKeyCode, bool>? KeyStateChanged;

    public IntPtr HandleKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // 处理键盘钩子逻辑
    }
}
```

**新建文件**：`Services/Core/Hooks/MouseHookHandler.cs`
```csharp
namespace WpfApp.Services.Core.Hooks;

/// <summary>
/// 鼠标钩子处理器
/// 处理鼠标按键、滚轮事件
/// </summary>
public class MouseHookHandler
{
    public event Action<VirtualKeyCode, bool>? MouseButtonStateChanged;

    public IntPtr HandleMouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // 处理鼠标钩子逻辑
    }
}
```

#### 1.2 创建 Validation 子目录

**新建文件**：`Services/Core/Validation/WindowValidator.cs`
```csharp
namespace WpfApp.Services.Core.Validation;

/// <summary>
/// 窗口验证器
/// 负责验证目标窗口是否有效、是否激活
/// </summary>
public class WindowValidator
{
    private HashSet<IntPtr> _targetWindowHandles = new();

    public enum WindowState
    {
        NoTargetWindow,   // 未选择目标窗口（允许全局触发）
        WindowInvalid,    // 窗口无效
        WindowInactive,   // 窗口未激活
        WindowActive      // 窗口激活
    }

    public void SetTargetWindows(IEnumerable<IntPtr> handles);
    public WindowState GetWindowState();
    public bool CanTriggerHotkey();
}
```

#### 1.3 重构后的 HotkeyService.cs

**文件大小**：从 1,002 行减少到约 200 行

```csharp
namespace WpfApp.Services.Core;

public class HotkeyService : IHotkeyService, IDisposable
{
    // 依赖注入的组件
    private readonly HookManager _hookManager;
    private readonly KeyboardHookHandler _keyboardHandler;
    private readonly MouseHookHandler _mouseHandler;
    private readonly WindowValidator _windowValidator;
    private readonly KeySequenceExecutor _executor;
    private readonly LyKeysService _lyKeysService;

    // 事件定义
    public event Action? StartHotkeyPressed;
    public event Action? StartHotkeyReleased;

    public HotkeyService(...)
    {
        // 初始化各组件
        _hookManager = new HookManager();
        _keyboardHandler = new KeyboardHookHandler();
        _mouseHandler = new MouseHookHandler();
        _windowValidator = new WindowValidator();

        // 订阅事件
        _keyboardHandler.KeyStateChanged += OnKeyStateChanged;
        _mouseHandler.MouseButtonStateChanged += OnMouseButtonStateChanged;
    }

    private void OnKeyStateChanged(VirtualKeyCode key, bool isDown)
    {
        if (!_windowValidator.CanTriggerHotkey()) return;
        // 协调逻辑...
    }
}
```

**优势**：
- ✅ 符合单一职责原则
- ✅ 每个类可独立测试
- ✅ 修改某功能不影响其他部分
- ✅ 代码结构清晰，易于理解

---

### 阶段二：优化 LyKeys 驱动封装（优先级：🔴 高）

#### 2.1 创建 Drivers 子目录

**新建文件**：`Services/Core/Drivers/Interop/LyKeysNative.cs`
```csharp
namespace WpfApp.Services.Core.Drivers.Interop;

/// <summary>
/// LyKeys 驱动原生接口声明
/// 集中管理所有 P/Invoke 声明
/// </summary>
public static class LyKeysNative
{
    // 驱动管理函数
    [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool SetHandle();

    [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool LoadNTDriver(string lpszDriverName, string lpszDriverPath);

    // 键盘操作函数
    [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void KeyDown(ushort vkCode);

    // 鼠标操作函数
    [DllImport("lykeysdll.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void MouseMoveABSOLUTE(int x, int y);

    // ... 所有 P/Invoke 声明
}
```

**新建文件**：`Services/Core/Drivers/Operations/DriverExecutor.cs`
```csharp
namespace WpfApp.Services.Core.Drivers.Operations;

/// <summary>
/// 驱动操作执行器
/// 统一处理异常重试逻辑，避免代码重复
/// </summary>
public static class DriverExecutor
{
    /// <summary>
    /// 执行驱动操作，失败时自动重试一次
    /// </summary>
    public static bool ExecuteWithRetry(
        Action operation,
        ISerilogManager logger,
        Func<DeviceStatus> getStatus,
        bool isInitialized,
        bool isDisposed,
        string operationName,
        params object[] args)
    {
        if (isDisposed || !isInitialized) return false;

        try
        {
            operation();
            return true;
        }
        catch (Exception ex)
        {
            logger.Debug($"{operationName} 首次执行失败，尝试重试");

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
                        logger.Error($"{operationName} 重试后仍然失败");
                    }
                }
            }
            catch { /* 忽略异常 */ }

            return false;
        }
    }
}
```

**新建文件**：`Services/Core/Drivers/Operations/KeyboardOperations.cs`
```csharp
namespace WpfApp.Services.Core.Drivers.Operations;

/// <summary>
/// 键盘操作扩展方法
/// </summary>
public static class KeyboardOperations
{
    public static bool SendKeyDown(
        this LyKeys driver,
        ushort vkCode,
        ISerilogManager logger)
    {
        return DriverExecutor.ExecuteWithRetry(
            () => LyKeysNative.KeyDown(vkCode),
            logger,
            driver.GetStatus,
            driver.IsInitialized,
            driver.IsDisposed,
            "SendKeyDown",
            vkCode
        );
    }

    public static bool SendKeyUp(this LyKeys driver, ushort vkCode, ISerilogManager logger)
    {
        return DriverExecutor.ExecuteWithRetry(
            () => LyKeysNative.KeyUp(vkCode),
            logger,
            driver.GetStatus,
            driver.IsInitialized,
            driver.IsDisposed,
            "SendKeyUp",
            vkCode
        );
    }

    // 其他键盘操作...
}
```

#### 2.2 重构后的 LyKeys.cs

**文件大小**：从 690 行减少到约 200 行（减少 71%）

```csharp
namespace WpfApp.Services.Core.Drivers;

public sealed class LyKeys : IDriver
{
    private readonly ISerilogManager _logger;
    private bool _isDisposed;
    private bool _isInitialized;
    private IntPtr _dllHandle = IntPtr.Zero;
    private DeviceStatus _lastStatus = DeviceStatus.Unknown;

    public LyKeys(ISerilogManager logger, string driverPath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _driverPath = driverPath ?? throw new ArgumentNullException(nameof(driverPath));
    }

    public bool Initialize()
    {
        // 初始化逻辑（使用 LyKeysNative）
        _dllHandle = LyKeysNative.LoadLibrary(dllPath);
        LyKeysNative.LoadNTDriver(DriverName, _driverPath);
        // ...
    }

    // 公共接口实现（委托给扩展方法）
    public bool SendKeyDown(ushort vkCode) =>
        this.SendKeyDown(vkCode, _logger);

    public bool SendKeyUp(ushort vkCode) =>
        this.SendKeyUp(vkCode, _logger);

    public bool MoveMouseAbsolute(int x, int y) =>
        this.MoveMouseAbsolute(x, y, _logger);

    // ...
}
```

**优势**：
- ✅ 代码量减少 71%
- ✅ 消除重复的异常处理逻辑
- ✅ P/Invoke 声明集中管理
- ✅ 符合 DRY 原则

---

### 阶段三：拆分 LyKeysService（优先级：🟡 中）

#### 3.1 创建 Mapping 和 Converters 子目录

**新建文件**：`Services/Core/Configuration/Mapping/VirtualKeyCodeMapper.cs`
```csharp
namespace WpfApp.Services.Core.Configuration.Mapping;

/// <summary>
/// 虚拟键码映射器
/// 负责虚拟键码的映射、验证、描述获取
/// </summary>
public class VirtualKeyCodeMapper
{
    private readonly Dictionary<int, VirtualKeyCode> _virtualKeyMap;

    public VirtualKeyCodeMapper()
    {
        _virtualKeyMap = InitializeVirtualKeyMap();
    }

    private Dictionary<int, VirtualKeyCode> InitializeVirtualKeyMap()
    {
        // 映射初始化逻辑
    }

    public bool IsValidVirtualKeyCode(VirtualKeyCode code)
    {
        return _virtualKeyMap.ContainsValue(code);
    }

    public string GetKeyDescription(VirtualKeyCode code)
    {
        // 键码描述逻辑
    }
}
```

**新建文件**：`Services/Core/Configuration/Converters/MouseButtonConverter.cs`
```csharp
namespace WpfApp.Services.Core.Configuration.Converters;

/// <summary>
/// 鼠标按键转换器
/// 负责虚拟键码与鼠标按键类型的转换
/// </summary>
public static class MouseButtonConverter
{
    public static bool IsMouseButton(VirtualKeyCode keyCode)
    {
        return keyCode == VirtualKeyCode.VK_LBUTTON ||
               keyCode == VirtualKeyCode.VK_RBUTTON ||
               keyCode == VirtualKeyCode.VK_MBUTTON ||
               keyCode == VirtualKeyCode.VK_XBUTTON1 ||
               keyCode == VirtualKeyCode.VK_XBUTTON2 ||
               keyCode == VirtualKeyCode.VK_WHEELUP ||
               keyCode == VirtualKeyCode.VK_WHEELDOWN;
    }

    public static MouseButtonType ConvertToMouseButtonType(VirtualKeyCode keyCode)
    {
        return keyCode switch
        {
            VirtualKeyCode.VK_LBUTTON => MouseButtonType.Left,
            VirtualKeyCode.VK_RBUTTON => MouseButtonType.Right,
            // ...
            _ => throw new ArgumentException($"非法的鼠标按键类型: {keyCode}")
        };
    }
}
```

#### 3.2 重构后的 LyKeysService.cs

**文件大小**：从 561 行减少到约 250 行

```csharp
namespace WpfApp.Services.Core;

public class LyKeysService : ILyKeysService, IDisposable
{
    private readonly VirtualKeyCodeMapper _keyMapper;
    private readonly IDriver _driver;
    private readonly ISerilogManager _logger;

    public LyKeysService(...)
    {
        _keyMapper = new VirtualKeyCodeMapper();
        // ...
    }

    public bool SendKeyDown(VirtualKeyCode keyCode)
    {
        if (!_keyMapper.IsValidVirtualKeyCode(keyCode))
        {
            _logger.Error($"无效的键码: {keyCode}");
            return false;
        }

        if (MouseButtonConverter.IsMouseButton(keyCode))
        {
            return _driver.SendMouseButton(
                MouseButtonConverter.ConvertToMouseButtonType(keyCode),
                true
            );
        }

        return _driver.SendKeyDown((ushort)keyCode);
    }

    public string GetKeyDescription(VirtualKeyCode code) =>
        _keyMapper.GetKeyDescription(code);
}
```

---

### 阶段四：优化 ConfigManager（优先级：🟡 中）

#### 4.1 创建 Configuration/IO 子目录

**新建文件**：`Services/Core/Configuration/IO/ConfigLoader.cs`
```csharp
namespace WpfApp.Services.Core.Configuration.IO;

/// <summary>
/// 配置加载器
/// 负责从磁盘加载全局配置和多配置数据
/// </summary>
public class ConfigLoader
{
    private readonly ISerilogManager _logger;
    private readonly IPathService _pathService;

    public GlobalConfig LoadGlobalConfig();
    public MultiKeyConfigData LoadMultiKeyConfig();
    public GlobalConfig CreateDefaultGlobalConfig();
    public MultiKeyConfigData CreateDefaultMultiKeyConfig();
}
```

**新建文件**：`Services/Core/Configuration/IO/ConfigSaver.cs`
```csharp
namespace WpfApp.Services.Core.Configuration.IO;

/// <summary>
/// 配置保存器
/// 负责将配置保存到磁盘
/// </summary>
public class ConfigSaver
{
    private readonly ISerilogManager _logger;

    public void SaveGlobalConfig(GlobalConfig config, string path);
    public void SaveMultiKeyConfig(MultiKeyConfigData config, string path);
}
```

#### 4.2 重构后的 ConfigManager.cs

**文件大小**：从 517 行减少到约 250 行

```csharp
namespace WpfApp.Services.Core;

public class ConfigManager : IConfigManager
{
    private readonly ConfigLoader _loader;
    private readonly ConfigSaver _saver;

    public ConfigManager(ISerilogManager logger, IPathService pathService)
    {
        _loader = new ConfigLoader(logger, pathService);
        _saver = new ConfigSaver(logger);
    }

    public void Initialize()
    {
        _globalConfig = _loader.LoadGlobalConfig();
        _multiKeyConfigData = _loader.LoadMultiKeyConfig();
    }

    public void UpdateGlobalConfig(Action<GlobalConfig> updateAction)
    {
        updateAction(_globalConfig);
        _saver.SaveGlobalConfig(_globalConfig, _globalConfigPath);
        RaiseConfigChanged(ConfigChangeType.Global, _globalConfig, null);
    }
}
```

---

## 📁 重构后的最终目录结构

```
Services/Core/
├── Interfaces/                    // 接口定义
│   ├── IDriver.cs
│   ├── ILyKeysService.cs
│   ├── IConfigManager.cs
│   ├── IHotkeyService.cs
│   ├── IKeySequenceExecutor.cs
│   ├── IAudioService.cs
│   ├── IInputMethodService.cs
│   └── IStatusMessageService.cs
│
├── Hooks/                         // 🆕 钩子管理（从 HotkeyService 拆分）
│   ├── HookManager.cs             (250行) Win32 钩子安装/卸载
│   ├── KeyboardHookHandler.cs     (200行) 键盘钩子处理
│   └── MouseHookHandler.cs        (200行) 鼠标钩子处理
│
├── Validation/                    // 🆕 验证逻辑（从 HotkeyService 拆分）
│   └── WindowValidator.cs         (150行) 窗口句柄验证
│
├── Drivers/                       // 🆕 驱动实现（重组 LyKeys 和 AhkDriver）
│   ├── LyKeys.cs                  (200行) ⬇️ 从 690 行减少
│   ├── AhkDriver.cs               (62行)
│   ├── Interop/
│   │   └── LyKeysNative.cs        (150行) 🆕 P/Invoke 声明集中
│   └── Operations/
│       ├── DriverExecutor.cs      (100行) 🆕 统一异常处理
│       ├── KeyboardOperations.cs  (120行) 🆕 键盘操作扩展
│       └── MouseOperations.cs     (120行) 🆕 鼠标操作扩展
│
├── Configuration/                 // 🆕 配置管理（从 ConfigManager 拆分）
│   ├── IO/
│   │   ├── ConfigLoader.cs        (150行) 🆕 配置加载
│   │   └── ConfigSaver.cs         (100行) 🆕 配置保存
│   ├── Mapping/
│   │   └── VirtualKeyCodeMapper.cs (150行) 🆕 虚拟键码映射
│   └── Converters/
│       └── MouseButtonConverter.cs (100行) 🆕 鼠标按键转换
│
├── HotkeyService.cs               (200行) ⬇️ 从 1,002 行减少 80%
├── LyKeysService.cs               (250行) ⬇️ 从 561 行减少 55%
├── ConfigManager.cs               (250行) ⬇️ 从 517 行减少 52%
├── KeySequenceExecutor.cs         (133行) ✅ 保持不变
├── AudioService.cs                (299行) ✅ 保持不变
├── InputMethodService.cs          (93行)  ✅ 保持不变
├── DriverFactory.cs               (79行)  ✅ 保持不变
├── KeyConfigurationService.cs     (413行) 🔄 可选优化
├── FloatingWindowService.cs       (205行) ✅ 保持不变
├── WindowManagementService.cs     (342行) ✅ 保持不变
├── CoordinateManagementService.cs (105行) ✅ 保持不变
├── KeyListManagementService.cs    (279行) ✅ 保持不变
├── KeyMappingService.cs           (294行) ✅ 保持不变
├── UpdateService.cs               (174行) ✅ 保持不变
├── PageCacheService.cs            (43行)  ✅ 保持不变
└── VirtualKeyCode.cs              (171行) ✅ 保持不变
```

---

## 📊 重构效果预估

### 代码量对比

| 文件 | 重构前 | 重构后 | 减少 |
|------|--------|--------|------|
| **HotkeyService.cs** | 1,002 行 | 200 行 | ⬇️ 80% |
| **LyKeys.cs** | 690 行 | 200 行 | ⬇️ 71% |
| **LyKeysService.cs** | 561 行 | 250 行 | ⬇️ 55% |
| **ConfigManager.cs** | 517 行 | 250 行 | ⬇️ 52% |
| **新增文件** | - | ~1,890 行 | - |
| **总计** | 5,927 行 | ~6,200 行 | ⬆️ 5% |

> **注意**：虽然总代码量略有增加（+5%），但通过拆分后：
> - ✅ 单文件最大行数从 1,002 减少到 300（⬇️ 70%）
> - ✅ 代码重复率大幅降低（消除 ~300 行重复代码）
> - ✅ 可维护性显著提升
> - ✅ 测试性大幅增强

### 质量指标提升

| 指标 | 重构前 | 重构后 | 改善 |
|------|--------|--------|------|
| **单一职责原则 (SRP)** | ❌ 差 | ✅ 优 | 100% |
| **开闭原则 (OCP)** | 🟡 中 | ✅ 优 | 60% |
| **代码重复率** | 🔴 高 | 🟢 低 | ⬇️ 80% |
| **可测试性** | 🟡 中 | ✅ 高 | 70% |
| **维护成本** | 🔴 高 | 🟢 低 | ⬇️ 60% |
| **最大文件行数** | 1,002 | ~300 | ⬇️ 70% |

---

## 🚀 执行计划

### 分阶段实施（推荐）

#### 第一阶段（2 天）：拆分 HotkeyService
- [ ] 创建 `Hooks/HookManager.cs`
- [ ] 创建 `Hooks/KeyboardHookHandler.cs`
- [ ] 创建 `Hooks/MouseHookHandler.cs`
- [ ] 创建 `Validation/WindowValidator.cs`
- [ ] 重构 `HotkeyService.cs` 为协调器
- [ ] 单元测试各组件
- [ ] 集成测试验证功能

#### 第二阶段（1.5 天）：优化 LyKeys 驱动
- [ ] 创建 `Drivers/Interop/LyKeysNative.cs`
- [ ] 创建 `Drivers/Operations/DriverExecutor.cs`
- [ ] 创建 `Drivers/Operations/KeyboardOperations.cs`
- [ ] 创建 `Drivers/Operations/MouseOperations.cs`
- [ ] 重构 `LyKeys.cs` 使用新架构
- [ ] 驱动功能测试

#### 第三阶段（1 天）：拆分 LyKeysService 和 ConfigManager
- [ ] 创建 `Configuration/Mapping/VirtualKeyCodeMapper.cs`
- [ ] 创建 `Configuration/Converters/MouseButtonConverter.cs`
- [ ] 创建 `Configuration/IO/ConfigLoader.cs`
- [ ] 创建 `Configuration/IO/ConfigSaver.cs`
- [ ] 重构 `LyKeysService.cs`
- [ ] 重构 `ConfigManager.cs`
- [ ] 配置管理测试

#### 第四阶段（0.5 天）：文档和清理
- [ ] 更新 CLAUDE.md 文档
- [ ] 更新接口文档
- [ ] 代码审查
- [ ] 性能测试

**总工作量**：5 个工作日

---

## ⚠️ 风险控制

### 潜在风险

1. **依赖注入复杂度增加**
   - **风险**：拆分后需要配置更多的依赖关系
   - **缓解**：使用 DI 容器自动管理依赖

2. **性能影响**
   - **风险**：增加一层抽象可能影响性能
   - **缓解**：通过基准测试验证，必要时优化热路径

3. **回归 Bug**
   - **风险**：重构可能引入新 Bug
   - **缓解**：充分的单元测试和集成测试

### 回退策略

1. **Git 分支隔离**
   ```bash
   git checkout -b refactor/services-core
   ```

2. **分阶段提交**
   - 每个阶段完成后独立提交
   - 失败时可回滚到前一阶段

3. **保留旧代码**
   - 使用 `[Obsolete]` 标记旧方法
   - 一段时间后再删除

---

## 📝 测试策略

### 单元测试

```csharp
// 示例：测试 DriverExecutor
[Test]
public void ExecuteWithRetry_ShouldRetryOnce_WhenFirstAttemptFails()
{
    var callCount = 0;
    Action operation = () =>
    {
        callCount++;
        if (callCount == 1) throw new Exception();
    };

    var result = DriverExecutor.ExecuteWithRetry(
        operation, logger, () => DeviceStatus.Ready, true, false, "Test");

    Assert.AreEqual(2, callCount);
    Assert.IsTrue(result);
}
```

### 集成测试

```csharp
[Test]
public void HotkeyService_ShouldTriggerSequence_WhenHotkeyPressed()
{
    var service = new HotkeyService(...);
    service.RegisterHotkey(VirtualKeyCode.VK_F9, ModifierKeys.None);

    // 模拟 F9 按下
    SimulateKeyPress(VirtualKeyCode.VK_F9);

    Assert.IsTrue(sequenceStarted);
}
```

---

## 📚 参考资料

- [SOLID 原则详解](https://docs.microsoft.com/zh-cn/dotnet/architecture/modern-web-apps-azure/architectural-principles#solid)
- [C# 编码规范](https://docs.microsoft.com/zh-cn/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- [Clean Code 最佳实践](https://github.com/ryanmcdermott/clean-code-javascript)

---

**文档版本**: 1.0
**创建时间**: 2025-11-17
**维护者**: AI Assistant
