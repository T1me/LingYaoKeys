# Instance 使用分析报告

> **生成时间**: 2025-11-16
> **分析工具**: Claude Code + Grep
> **总Instance调用**: 73次
> **涉及文件**: 30+ 个

---

## 📊 统计总览

| Instance类型 | 调用次数 | 涉及文件数 | 优先级 |
|-------------|---------|-----------|--------|
| `SerilogManager.Instance` | 58 | 29 | P0 (最多) |
| `ConfigManager.Instance` | 12 | 4 | P1 |
| `PathService.Instance` | 3 | 3 | P2 |
| **总计** | **73** | **30+** | - |

---

## 📋 详细清单（按优先级）

### ✨ P0: ViewModels (高优先级 - 4个文件)

#### 1. `ViewModels\MainViewModel.cs`
- `SerilogManager.Instance`: 2次
- **修改策略**: INJECT（已有DI基础）

#### 2. `ViewModels\SettingsViewModel.cs`
- `SerilogManager.Instance`: 1次
- `PathService.Instance`: 1次
- **修改策略**: INJECT

#### 3. `ViewModels\KeyMappingViewModel.cs`
- `ConfigManager.Instance`: 8次
- `SerilogManager.Instance`: 1次
- **修改策略**: INJECT（已有构造函数注入）

#### 4. `ViewModels\KeyConfigurationWindowViewModel.cs`
- `SerilogManager.Instance`: 1次
- **修改策略**: INJECT

---

### ✨ P0: Views (高优先级 - 2个文件)

#### 1. `Views\MainWindow.xaml.cs`
- `ConfigManager.Instance`: 1次
- `SerilogManager.Instance`: 1次
- **修改策略**: INJECT

#### 2. `Views\FloatingStatusWindow.xaml.cs`
- `ConfigManager.Instance`: 2次
- `SerilogManager.Instance`: 1次
- **修改策略**: INJECT

---

### 🔧 P1: Services/Core (中优先级 - 17个文件)

**高频使用（需重点关注）**:

1. **FloatingWindowService.cs** - `SerilogManager.Instance`: 9次
2. **WindowManagementService.cs** - `SerilogManager.Instance`: 9次
3. **KeyListManagementService.cs** - `SerilogManager.Instance`: 10次
4. **CoordinateManagementService.cs** - `SerilogManager.Instance`: 4次

**其他服务**（各1-2次）:
- LyKeysService.cs
- HotkeyService.cs
- AudioService.cs
- KeySequenceExecutor.cs
- InputMethodService.cs
- KeyConfigurationService.cs
- KeyMappingService.cs
- UpdateService.cs
- DriverFactory.cs
- AhkDriver.cs
- LyKeys.cs

---

### 🔧 P2: Services/UI (中优先级 - 2个文件)

1. `Services\UI\CoordinateDragService.cs` - `SerilogManager.Instance`: 1次
2. `Services\UI\CoordinateVisualizationService.cs` - `SerilogManager.Instance`: 1次

---

### 🛠️ P3: Services/Utils + Models (低优先级 - 5个文件)

#### Services/Utils (3个)
1. `ExceptionHandler.cs` - `SerilogManager.Instance`: 1次
2. `Win32WindowHelper.cs` - `SerilogManager.Instance`: 1次
3. `PathService.cs` - `SerilogManager.Instance`: 1次 (自身的兼容属性)

#### Services/Models (2个)
1. `KeyModeBase.cs` - `SerilogManager.Instance`: 1次
2. `CoordinateMarker.cs` - `SerilogManager.Instance`: 1次

---

## 🎯 执行计划

### 阶段1: P0文件清理 (预计30分钟)

**顺序**: ViewModels → Views

1. **MainViewModel.cs**
   - 确认已有 `ISerilogManager _logger` 字段
   - 替换 2处 `SerilogManager.Instance` → `_logger`

2. **SettingsViewModel.cs**
   - 添加字段: `ISerilogManager _logger`, `IPathService _pathService`
   - 修改构造函数注入
   - 替换调用

3. **KeyMappingViewModel.cs** ⚠️ 高频使用
   - 已有部分DI
   - 需要添加 `IConfigManager _configManager`
   - 替换 8处 ConfigManager.Instance

4. **KeyConfigurationWindowViewModel.cs**
   - 简单替换 1处

5. **MainWindow.xaml.cs**
   - 简单替换 2处

6. **FloatingStatusWindow.xaml.cs**
   - 简单替换 3处

### 阶段2: P1服务层清理 (预计1小时)

重点处理高频文件：
1. FloatingWindowService.cs (9次)
2. WindowManagementService.cs (9次)
3. KeyListManagementService.cs (10次)
4. CoordinateManagementService.cs (4次)

### 阶段3: P2+P3清理 (预计30分钟)

处理剩余的UI服务、工具类和模型

### 阶段4: 删除[Obsolete]属性 (预计5分钟)

删除3个兼容属性：
- `ConfigManager.Instance`
- `SerilogManager.Instance`
- `PathService.Instance`

### 阶段5: 编译验证 (预计10分钟)

```bash
dotnet clean
dotnet build --configuration Release
```

### 阶段6: 功能测试 (预计15分钟)

- 启动测试
- 配置加载/保存
- 热键功能
- 日志记录

---

## 📝 标准修改模板

### 模板A: ViewModel修改 (已有DI)

```csharp
// 1. 确认/添加字段
private readonly ISerilogManager _logger;
private readonly IConfigManager _configManager;

// 2. 确认构造函数已注入
public XXXViewModel(
    ISerilogManager logger,
    IConfigManager configManager,
    ...)
{
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
}

// 3. 全局替换 (使用IDE的Find & Replace)
SerilogManager.Instance → _logger
ConfigManager.Instance → _configManager
PathService.Instance → _pathService
```

### 模板B: Service修改 (需创建构造函数)

```csharp
// 1. 添加私有字段
private readonly ISerilogManager _logger;

// 2. 创建构造函数
public XXXService(ISerilogManager logger)
{
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
}

// 3. 替换调用
```

---

## ⚠️ 注意事项

1. **MainViewModel 已部分使用DI** - 检查现有字段，避免重复
2. **KeyMappingViewModel 高频使用** - 8处ConfigManager调用需仔细替换
3. **FloatingWindowService等高频服务** - 9-10次调用，替换时要特别小心
4. **静态类和静态方法** - 暂时保留 .Instance 调用
5. **每个模块修改后立即编译验证**

---

## ✅ 完成标准

- [ ] 0个 `.Instance` 调用（除静态类外）
- [ ] 0个 `[Obsolete]` 属性
- [ ] 编译 0 错误
- [ ] 警告不增加（当前222个）
- [ ] 所有功能测试通过

---

**下一步**: 开始执行阶段1 - P0文件清理
