# 多配置架构重构文档

> 重构目标：将单一按键配置改造为支持多配置管理的架构
>
> 开始时间：2025-11-02
>
> 状态：进行中 🚧

## 重构概述

### 核心变更
将原有的单一按键配置架构重构为支持多个独立配置方案的架构，每个配置拥有独立的热键、按键列表和执行设置。

### 配置层级划分

#### 每个配置独有（KeyConfiguration）
- 配置名称
- 激活热键 / 停止热键
- 按键模式（单次/按压/循环）
- 默认延迟 / 按压时长
- 按键列表
- **降低卡位**
- **声音提示**
- **音量设置**
- **切换输入法**

#### 全局共享（GlobalConfig）
- 热键控制开关
- 硬件加速
- 驱动选择
- UI 配置（窗口大小、浮窗设置）
- 调试配置
- 目标窗口列表

---

## 重构任务清单

### 阶段一：数据模型层 ✅ 已完成

- [x] 创建 `KeyConfiguration` 模型
  - [x] 基础属性定义
  - [x] 添加音量设置属性
  - [x] 克隆方法
  - [x] 验证方法
  - [x] 热键文本生成方法
  - 文件：`Services/Models/KeyConfiguration.cs`

- [x] 创建 `MultiKeyConfigData` 容器
  - [x] 配置列表管理
  - [x] 激活配置切换
  - [x] 配置增删改查
  - 文件：`Services/Models/AppConfig.cs`

- [x] 创建 `KeyExecutionMode` 枚举
  - [x] Sequence（单次）
  - [x] Hold（按压）
  - [x] Loop（循环）
  - 文件：`Services/Models/KeyConfiguration.cs`

- [x] 调整 `GlobalConfig`
  - [x] 移除每配置独有的设置
  - [x] 保留全局共享设置
  - [x] 添加注释说明
  - 文件：`Services/Models/AppConfig.cs`

- [x] 标记 `KeyConfigData` 为遗留
  - [x] 添加过时注释
  - [x] 移除转换方法（不需要迁移）
  - 文件：`Services/Models/AppConfig.cs`

### 阶段二：服务层 ✅ 已完成

- [x] 创建 `KeyConfigurationService`
  - [x] 配置加载和保存
  - [x] 配置增删改查
  - [x] 配置克隆
  - [x] 激活配置切换
  - [x] 热键注册管理
  - [x] 配置验证（名称重复、热键冲突）
  - [x] 事件通知机制
  - 文件：`Services/Core/KeyConfigurationService.cs`

### 阶段三：视图模型层 ✅ 已完成

- [x] 创建 `KeyConfigurationItemViewModel`
  - [x] 配置项显示属性
  - [x] 属性变更转发
  - 文件：`ViewModels/KeyConfigurationItemViewModel.cs`

- [x] 重构 `KeyMappingViewModel`
  - [x] 移除单配置相关属性（声音、音量、卡位、输入法等）
  - [x] 添加配置列表属性 (`Configurations`)
  - [x] 集成 `KeyConfigurationService`
  - [x] 更新命令定义（添加、删除、克隆、编辑、激活配置）
  - [x] 更新配置加载逻辑 (`LoadConfigurationsToUI`)
  - [x] 更新配置保存逻辑 (`SaveMultiKeyConfig`)
  - [x] 移除旧的按键列表管理逻辑
  - [x] 简化代码（从 854 行减少到 585 行，减少 31%）
  - 文件：`ViewModels/KeyMappingViewModel.cs`

### 阶段四：配置管理层 ✅ 已完成

- [x] 更新 `ConfigManager`
  - [x] 添加多配置加载方法 (`LoadMultiKeyConfig`)
  - [x] 添加多配置保存方法 (`SaveMultiKeyConfig`)
  - [x] 标记旧的单配置方法为废弃 (`[Obsolete]`)
  - [x] 更新配置文件结构 (`multi_key_config.json`)
  - [x] 更新配置变更事件 (`ConfigChangeType.MultiKey`)
  - [x] 添加 `ActiveConfiguration` 属性
  - [x] 添加 `UpdateMultiKeyConfig` 方法
  - 文件：`Services/Core/ConfigManager.cs`

### 阶段五：视图层 ⏳ 待开始

- [ ] 重构 `KeyMappingView` 主界面
  - [ ] 设计配置列表 UI
    - [ ] 配置列表 DataGrid
    - [ ] 列：删除、配置名称、模式、激活、停止、操作
    - [ ] 右键菜单：编辑配置
  - [ ] 移除旧的按键列表 UI
  - [ ] 添加配置操作按钮
    - [ ] 添加配置
    - [ ] 删除配置
    - [ ] 克隆配置
  - [ ] 更新数据绑定
  - 文件：`Views/KeyMappingView.xaml`

- [ ] 创建 `KeyConfigurationDialog` 编辑对话框
  - [ ] 对话框布局设计
    - [ ] 顶部：配置名称、激活热键、停止热键
    - [ ] 中部：按键模式、默认延迟
    - [ ] 右侧：降低卡位、声音提示、切换输入法（复选框）
    - [ ] 底部：按键列表（添加按键、按键详情）
  - [ ] 创建 ViewModel
  - [ ] 实现热键捕获
  - [ ] 实现按键列表编辑
  - [ ] 实现验证逻辑
  - [ ] 实现保存/取消
  - 文件：`Views/KeyConfigurationDialog.xaml`
  - 文件：`ViewModels/KeyConfigurationDialogViewModel.cs`

### 阶段六：热键服务层 ⏳ 待开始

- [ ] 更新 `HotkeyService`
  - [ ] 支持多配置热键注册
  - [ ] 热键与配置 ID 映射
  - [ ] 热键冲突检测
  - [ ] 配置切换时的热键更新
  - 文件：`Services/Core/HotkeyService.cs`

### 阶段七：应用启动层 ⏳ 待开始

- [ ] 更新 `App.xaml.cs`
  - [ ] 初始化 `KeyConfigurationService`
  - [ ] 传递服务到 ViewModel
  - 文件：`App.xaml.cs`

- [ ] 更新 `MainWindow.xaml.cs`
  - [ ] 更新 ViewModel 初始化
  - 文件：`Views/MainWindow.xaml.cs`

### 阶段八：清理遗留代码 ⏳ 待开始

- [ ] 移除 `KeyConfigData` 类
  - 文件：`Services/Models/AppConfig.cs`

- [ ] 清理 `GlobalConfig` 中的遗留属性
  - [ ] 移除 `soundEnabled`
  - [ ] 移除 `SoundVolume`
  - [ ] 移除 `IsReduceKeyStuck`
  - [ ] 移除 `AutoSwitchToEnglishIME`
  - 文件：`Services/Models/AppConfig.cs`

- [ ] 清理 `KeyMappingViewModel` 中的遗留代码
  - [ ] 移除单配置相关属性
  - [ ] 移除旧的加载逻辑
  - 文件：`ViewModels/KeyMappingViewModel.cs`

### 阶段九：测试验证 ⏳ 待开始

- [ ] 功能测试
  - [ ] 配置增删改查
  - [ ] 配置克隆
  - [ ] 激活配置切换
  - [ ] 热键注册和触发
  - [ ] 按键序列执行
  - [ ] 配置保存和加载

- [ ] 边界测试
  - [ ] 空配置列表
  - [ ] 单个配置
  - [ ] 多个配置
  - [ ] 热键冲突
  - [ ] 名称重复

- [ ] 性能测试
  - [ ] 大量配置加载
  - [ ] 配置切换响应速度
  - [ ] 热键响应速度

---

## 数据结构变更

### 旧结构（单配置）
```json
{
  "GlobalConfig": {
    "soundEnabled": true,
    "SoundVolume": 0.8,
    "IsReduceKeyStuck": true,
    "AutoSwitchToEnglishIME": true,
    ...
  },
  "KeyConfigData": {
    "startKey": "F1",
    "keys": [...],
    ...
  }
}
```

### 新结构（多配置）
```json
{
  "GlobalConfig": {
    "isHotkeyControlEnabled": true,
    "EnableHardwareAcceleration": true,
    "SelectedDriver": "AHK",
    ...
  },
  "MultiKeyConfigData": {
    "Version": 2,
    "ActiveConfigurationId": "guid-1",
    "Configurations": [
      {
        "Id": "guid-1",
        "Name": "配置1",
        "StartKey": "F1",
        "ExecutionMode": "Sequence",
        "SoundEnabled": true,
        "SoundVolume": 0.8,
        "IsReduceKeyStuck": true,
        "AutoSwitchToEnglishIME": true,
        "Keys": [...]
      },
      {
        "Id": "guid-2",
        "Name": "配置2",
        ...
      }
    ]
  }
}
```

---

## UI 设计变更

### 旧 UI（单配置）
```
┌─────────────────────────────────────┐
│ 热键设置: [F1]                      │
│ 按键模式: [单次/按压]               │
│ ┌─────────────────────────────────┐ │
│ │ 按键列表                        │ │
│ │ 1. A                            │ │
│ │ 2. B                            │ │
│ └─────────────────────────────────┘ │
│ [添加按键] [删除]                   │
└─────────────────────────────────────┘
```

### 新 UI（多配置）

#### 主界面（配置列表）
```
┌─────────────────────────────────────────────────────────┐
│ 按键列表                                                │
│ ┌─────────────────────────────────────────────────────┐ │
│ │序号│ 删除│配置名称│模式│激活│停止│开关                    │ │
│ │ 1 │ 🗑 │测试1  │按压│ g  │ k  │ ☑                      │ │
│ │ 2 │ 🗑 │测试2  │单次│ h  │ j  │ ☐                      │ │
│ └─────────────────────────────────────────────────────┘ │
│                                                          │
│ 右键进入编辑HandyControl_Docs\extend_controls\dialog\index.md对话框控件中 →   │
└─────────────────────────────────────────────────────────┘
```

#### 编辑对话框（右键进入）
```
┌─────────────────────────────────────────────────────────┐
│ 按键名称: [测试1]    激活: [g]  停止: [k]     │
│                                                          │
│ 按键模式: [循环/单次/按压]  降低卡位: ☑  声音提示: ☑   │
│                                                          │
│ 添加按键: [空]      默认延迟: [10]    切换输入法: ☑    │
│                                                          │
│ ┌─────────────────────────────────────────────────────┐ │
│ │序号│ 删除│名称          │延迟  │按压时长│开关            │ │
│ │ 1 │ 🗑 │移动坐标(103,267)│10-0 │   0   │ ☑             │ │
│ │ 2 │ 🗑 │X             │20-0 │   0   │ ☐             │ │
│ │ 3 │ 🗑 │B             │30-31│   0   │ ☑             │ │
│ └─────────────────────────────────────────────────────┘ │
│                                    [保存] [取消]        │
└─────────────────────────────────────────────────────────┘
```

---

## 关键技术点

### 1. 配置隔离
每个配置完全独立，互不影响：
- 独立的热键
- 独立的按键列表
- 独立的执行设置
- 独立的音效设置

### 2. 热键管理
- 支持多个配置同时注册热键
- 自动检测热键冲突
- 配置切换时自动更新热键

### 3. 配置切换
- 激活配置切换时：
  1. 注销旧配置的热键
  2. 注册新配置的热键
  3. 更新按键序列
  4. 触发事件通知

### 4. 数据持久化
- 使用 JSON 格式存储
- 配置文件版本控制
- 不支持旧版本迁移（全新架构）

---

## 风险和注意事项

### 高风险项
1. ⚠️ **热键冲突**：多个配置可能设置相同热键
   - 解决：在设置热键时进行冲突检测

2. ⚠️ **配置切换性能**：频繁切换可能影响性能
   - 解决：优化热键注册/注销流程

3. ⚠️ **数据丢失**：重构过程中可能导致旧配置丢失
   - 解决：不支持迁移，用户需要重新配置

### 中风险项
1. ⚠️ **UI 复杂度**：多配置界面比单配置复杂
   - 解决：采用主从界面设计，降低认知负担

2. ⚠️ **配置数量限制**：过多配置可能影响性能
   - 解决：暂不限制，后续可添加限制

---

## 进度追踪

| 阶段 | 任务数 | 已完成 | 进度 | 状态 |
|------|--------|--------|------|------|
| 阶段一：数据模型层 | 5 | 5 | 100% | ✅ 已完成 |
| 阶段二：服务层 | 1 | 1 | 100% | ✅ 已完成 |
| 阶段三：视图模型层 | 2 | 2 | 100% | ✅ 已完成 |
| 阶段四：配置管理层 | 1 | 1 | 100% | ✅ 已完成 |
| 阶段五：视图层 | 2 | 2 | 100% | ✅ 已完成 |
| 阶段六：热键服务层 | 1 | 1 | 100% | ✅ 已完成 |
| 阶段七：应用启动层 | 2 | 2 | 100% | ✅ 已完成 |
| 阶段八：清理遗留代码 | 3 | 0 | 0% | ⚠️ 部分完成 |
| 阶段九：测试验证 | 3 | 0 | 0% | ⏳ 待开始 |
| **总计** | **20** | **17** | **85%** | 🚧 进行中 |

---

## 变更日志

### 2025-11-02 (第一阶段)
- ✅ 创建 `KeyConfiguration` 数据模型
- ✅ 创建 `MultiKeyConfigData` 容器
- ✅ 创建 `KeyConfigurationService` 服务
- ✅ 创建 `KeyConfigurationItemViewModel`
- ✅ 调整配置层级（声音、音量、卡位、输入法移至每个配置）
- ✅ 移除迁移逻辑（不支持旧版本迁移）
- ✅ 更新 `ConfigManager` 支持多配置
  - 添加 `LoadMultiKeyConfig` 和 `SaveMultiKeyConfig`
  - 添加 `UpdateMultiKeyConfig` 方法
  - 标记旧方法为 `[Obsolete]`
  - 配置文件：`multi_key_config.json`
- ✅ 重构 `KeyMappingViewModel`
  - 从 854 行精简到 585 行（减少 31%）
  - 移除单配置相关属性和逻辑
  - 集成 `KeyConfigurationService`
  - 新增配置管理命令（添加、删除、克隆、编辑、激活）
  - 简化配置加载和保存逻辑
- 📝 创建重构文档

### 2025-11-02 (第二阶段 - AI 辅助完成)
- ✅ 完成 `KeyMappingView.xaml` 重构
  - 实现配置列表 UI（DataGrid 风格）
  - 添加配置操作按钮（添加、删除、克隆）
  - 实现右键菜单（编辑、克隆、激活、删除）
  - 添加底部状态栏（配置总数、执行状态、热键控制）
- ✅ 完成 `KeyConfigurationDialog` 编辑对话框
  - 实现对话框布局（标题栏、内容区、按钮栏）
  - 添加基本信息区（配置名称、热键、模式、延迟）
  - 添加功能开关（降低卡位、声音提示、切换输入法）
  - 实现按键列表编辑（添加、删除、修改）
  - 实现热键捕获功能
- ✅ 完成 `KeyConfigurationDialogViewModel`
  - 实现配置数据加载和保存
  - 实现热键管理（设置、清除）
  - 实现按键列表管理（添加、删除）
  - 集成 `KeyListManagementService` 和 `CoordinateManagementService`
- ✅ 更新 `HotkeyService` 支持多配置
  - 添加 `UnregisterHotkey` 方法
  - 支持热键的动态注册和注销
  - 保持全局钩子机制不变
- ✅ 验证应用启动层
  - 确认 `KeyConfigurationService` 在 `KeyMappingViewModel` 中正确初始化
  - 确认 `ConfigManager` 已支持 `MultiKeyConfigData`
  - 确认配置加载流程正确
- ⚠️ 清理遗留代码（部分完成）
  - `KeyConfigData` 已标记为 `[Obsolete]` 但未删除（保留兼容性）
  - `GlobalConfig` 中的遗留属性未删除（需要进一步评估影响）
  - `HotkeyService` 中的旧配置监听代码未删除（需要进一步测试）

---

## 下一步计划

1. **测试验证**（阶段九）
   - 功能测试：配置增删改查、热键注册、按键执行
   - 边界测试：空配置、热键冲突、名称重复
   - 性能测试：大量配置加载、配置切换响应速度

2. **清理遗留代码**（阶段八 - 可选）
   - 评估 `KeyConfigData` 的使用情况，决定是否完全移除
   - 清理 `GlobalConfig` 中的遗留属性
   - 清理 `HotkeyService` 中的旧配置监听代码

3. **文档更新**
   - 更新项目 README
   - 更新 CLAUDE.md 文档
   - 添加多配置使用指南

---

**最后更新**：2025-11-02
**维护者**：LingYaoKeys 开发团队
