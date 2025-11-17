using System;
using System.Collections.Generic;

namespace WpfApp.Services.Core.Window;

/// <summary>
/// 窗口验证器接口 - 负责目标窗口状态检查和热键触发条件验证
/// </summary>
public interface IWindowValidator
{
    /// <summary>
    /// 窗口状态枚举
    /// </summary>
    enum WindowState
    {
        /// <summary>未设置目标窗口</summary>
        NoTargetWindow,
        /// <summary>目标窗口已失效</summary>
        WindowInvalid,
        /// <summary>目标窗口未激活</summary>
        WindowInactive,
        /// <summary>目标窗口已激活</summary>
        WindowActive
    }

    /// <summary>
    /// 目标窗口是否激活
    /// </summary>
    bool IsTargetWindowActive { get; set; }

    /// <summary>
    /// 设置目标窗口句柄列表
    /// </summary>
    /// <param name="handles">窗口句柄集合</param>
    void SetTargetWindows(IEnumerable<IntPtr> handles);

    /// <summary>
    /// 检查是否有有效的目标窗口
    /// </summary>
    bool HasValidWindows();

    /// <summary>
    /// 获取当前窗口状态
    /// </summary>
    WindowState GetWindowState();

    /// <summary>
    /// 检查窗口状态是否有效（允许热键执行）
    /// </summary>
    bool IsWindowStateValid(WindowState state);

    /// <summary>
    /// 检查是否可以触发热键
    /// </summary>
    /// <param name="isHotkeyControlEnabled">热键控制是否启用</param>
    /// <param name="isRegisteringHotkey">是否正在注册热键模式</param>
    bool CanTriggerHotkey(bool isHotkeyControlEnabled, bool isRegisteringHotkey);
}
