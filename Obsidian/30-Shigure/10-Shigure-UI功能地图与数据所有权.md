---
title: Shigure UI 功能地图与数据所有权
summary: 梳理 MainForm、StatusForm 和三个编辑器的职责、状态来源、写入目标、跨线程事件与关闭顺序。
aliases:
  - Shigure WinForms UI
  - Shigure 数据所有权
tags:
  - project/shigure
  - doc/feature
  - area/ui
project: Shigure
doc_type: feature
status: current
authority: source-derived
up: "[[30-Shigure/00-Shigure-MOC]]"
related:
  - "[[30-Shigure/04-Shigure-运行循环触发模式与快照]]"
  - "[[30-Shigure/09-Shigure-Fuyutsui配置宏编辑与同步]]"
source_files:
  - UI/MainForm.cs
  - UI/StatusForm.cs
  - UI/ModuleEditorControl.cs
  - UI/ClassConfigEditorControl.cs
  - UI/ClassMacrosEditorControl.cs
  - UI/ConditionEditorForm.cs
  - UI/UnitEditorForm.cs
  - UI/UiTheme.cs
source_symbols:
  - MainForm
  - StatusForm
  - ModuleEditorControl
  - ClassConfigEditorControl
  - ClassMacrosEditorControl
verified_at: 2026-08-10
---

# Shigure UI 功能地图与数据所有权

> [!abstract] AI 快速摘要
> `MainForm` 是一个无边框、置顶、可拖动的 680×64 浮条，拥有运行启停、设置入口、模块选择、生成/部署任务队列和会话事件过滤。`StatusForm` 是九页只读/诊断窗口；模块、ClassBlocks 和 ClassMacros 三个编辑器分别写模块 JSON、项目内置 ClassBlocks Lua、项目内置 ClassMacros Lua。后台状态只通过 `RenderSnapshot` 进入 UI。

## 图谱位置

- 上级：[[30-Shigure/00-Shigure-MOC]]
- 运行时数据来源：[[30-Shigure/04-Shigure-运行循环触发模式与快照]]
- 编辑/同步链：[[30-Shigure/09-Shigure-Fuyutsui配置宏编辑与同步]]

## 范围与非范围

本页按用户可见功能和数据所有权描述 WinForms，不逐个控件罗列坐标或样式。协议、规则语法和文件路径仅在需要解释所有权时引用。

## 窗口与功能地图

| UI 区域 | 主要职责 | 读取 | 写入/命令 |
|---|---|---|---|
| `MainForm` 浮条 | 启停、打开设置/状态、关闭、拖动 | 会话状态、UI 缓存 | `SetEnabled`、重启/停止、窗口偏好 |
| 通用设置 | 触发键、模式、模块选择、配置生成与插件部署 | `AppOptions`、模块目录、项目 Fuyutsui | 缓存/设置、会话重启、同步队列 |
| `StatusForm` | 十个状态/诊断页 | `RenderSnapshot`、日志 | 不拥有运行状态 |
| 模块编辑器 | 匹配、规则、动态单位、数量、调整/公式、排序 | 模块快照 | `{MyDocuments}/Shigure/module` |
| ClassBlocks 编辑器 | 职业/专精状态块编辑 | `Fuyutsui/class/*.lua` 项目源 | ClassBlocks table、config、当前 Lua 游戏部署 |
| ClassMacros 编辑器 | 职业动态/static/special 宏编辑 | `Fuyutsui/core/classmacros.lua` 项目源 | ClassMacros table、keymap、当前 Lua 游戏部署 |
| 条件/单位对话框 | 构造受限条件和动态定义 | 当前草稿 | 返回编辑器内存模型 |

`UiTheme` 集中定义主题和控件样式；素材由项目资源嵌入，不是运行时业务数据。

## MainForm 生命周期

1. 构造 `StatusForm` 和三个编辑器，注入项目 Fuyutsui 路径/保存回调，订阅协调器事件。
2. `OnShown` 先尝试把项目插件全量部署到游戏，再自动启动运行会话；部署失败只记录日志。
3. 设置更新排入串行配置尾任务；启动/重启先等待该队列。
4. 后台事件通过 WinForms invoke/post 切回 UI 线程，并用 session ID 丢弃旧会话快照。
5. 首次关闭请求被取消，异步停止/释放运行时并等待配置队列；完成后再次关闭并释放窗口。

这个顺序防止窗口句柄销毁后仍有后台回调，也避免新会话在配置写完前加载旧文件。

## StatusForm 的只读快照

监控区新增“姓名板”页面，与“队伍”页面使用相同的固定宽度布局，始终展示 20 个敌对姓名板槽位及其配置字段摘要。

状态窗有 10 个标签页，用于拆分总览、状态、法术、光环、组员、姓名板、动态派生值和日志等诊断数据。它只在窗口可见时刷新列表，降低隐藏窗口的 UI 成本。日志保存在内存；文本超过约 24k 字符时截到约 18k，不是完整审计日志。

关闭 StatusForm 默认只是隐藏，不结束主程序。运行时仍扫描和执行，取决于 MainForm 启用状态。

## 数据所有权

| 数据 | 所有者 | UI 获得方式 | UI 可否原地修改 |
|---|---|---|---|
| `_enabled`、当前状态、冷却、暂停 | `ShigureRuntime` 单循环 | `RenderSnapshot` | 否；只能发命令 |
| 当前/候选模块 | `ModuleStore` | 克隆快照 | 编辑器编辑副本后保存 |
| session 生命周期 | `RuntimeSessionCoordinator` | 带 session ID 的事件 | 只能请求 start/restart/stop |
| 配置/Keymap 同步次序 | `MainForm._configUpdateTail` | UI 任务链 | 由排队 API 串行追加 |
| Lua 文件 | 项目 `Fuyutsui/` | Store 解析 | Store 替换 table 后直接写回，再由部署服务复制到游戏 |
| config/keymap JSON | 转换器 | 刷新 catalog/新运行会话 | 不应由状态页直接修改 |

## 编辑器的重要限制

- 模块编辑器覆盖模块、Match、规则、动态单位、数量和数值调整，并支持排序。
- 模块执行器支持规则 `Hotkey` 和 `Step`，但 `ModuleEditorControl.ReadRules` 保存时强制把二者写成空字符串。手工 JSON 中的非空值经 UI 打开再保存会丢失。
- 模块根级 `Enabled` 可被 UI/JSON保存，但运行时忽略它；见模块页。
- ClassBlocks 编辑器恢复前三个固定状态名为锚点/职业/专精；`spellsList` 可编辑索引 1–100 的条目并原位回写 Lua。
- 旧稀疏 ClassBlocks 不是可靠的只读预览：旧专精可显示为空，Store 会拒绝非 modern 文档保存；混合格式需特别谨慎。
- ClassMacros 和 ClassBlocks 保存都会 canonical 重写目标表内部，未知字段/排版不保证保留。
- ClassMacros 特殊宏的技能名由用户手工填写并保存为 Lua 行尾注释；界面不展示目标和条件，转换器也不从特殊宏正文推导这些字段。
- 条件编辑器只能表达当前简单 AND/OR 语言，不支持括号；动态名称禁止空、`.`、`$`、纯数字和冲突名称。

## 模块选择的 UI 语义

下拉列表会实时按当前状态过滤模块。若手动选择的模块暂时不匹配，界面可显示为自动选择，但内部 `_selectedModuleId` 仍保留；将来状态再次匹配时该手选模块可重新生效。这是选择记忆，不应误判为协调器残留。

## 失败模式与排障

| 症状 | 优先检查 |
|---|---|
| 状态页不刷新 | 窗口是否可见、渲染间隔、session ID 是否仍是当前会话 |
| 关闭后仍短暂存在进程 | 正在等待运行时取消和配置尾任务完成 |
| 模块手工 Hotkey 消失 | UI 保存明确清空 `Hotkey`/`Step` |
| “禁用模块”仍匹配 | 根级 `Enabled` 未被运行时消费 |
| 保存 Lua 后大量格式变化 | table 内 canonical round-trip 是当前设计 |
| 保存成功但游戏插件未更新 | 检查 `wow_process.txt`、游戏目录权限、日志中的单文件部署结果和 WoW 重载 |
| 模块下拉选择“自动跳回” | 当前状态不匹配，但手动 ID 仍缓存等待以后匹配 |
| 日志历史不完整 | StatusForm 主动截断内存文本 |

## 修改影响

- 新状态页只应读取快照；不要让 UI 跨线程持有可变 `GameState`/模块执行对象。
- 新持久化设置应明确所有者和队列，避免与 Lua/config 转换并发写。
- 修复 `Hotkey`/`Step` 丢失时需给编辑控件或透传模型增加字段，并验证旧手工模块 round-trip。
- 改窗口关闭或事件订阅必须保留异步两阶段关闭和 stale session 过滤。

## 源码索引

- `UI/MainForm.cs:74-194`：子窗口/编辑器组合、自动启动和异步关闭。
- `UI/MainForm.cs:238-353`：浮条外观、拖动和主要按钮。
- `UI/MainForm.cs:434-666`：通用设置、模块选择和配置尾队列。
- `UI/MainForm.cs:668-1008`：同步、会话重启、事件 marshaling 与 session 过滤。
- `UI/MainForm.cs:1010-1144`：模块实时过滤和手选 ID。
- `UI/StatusForm.cs:55-139`、`690-727`：九页、隐藏行为、可见刷新和日志截断。
- `UI/ModuleEditorControl.cs:2668-3070`：模型回填/保存及 `Hotkey`/`Step` 清空。
- `UI/ClassConfigEditorControl.cs:997-1004`、`1741-1751`：legacy 提示和固定字段。
- `UI/ClassMacrosEditorControl.cs:621-693`、`908-1003`：宏加载/保存回调。

## 知识图谱链接

- 状态来源：[[30-Shigure/04-Shigure-运行循环触发模式与快照]]
- 模块数据：[[30-Shigure/05-Shigure-模块存储匹配与版本迁移]]
- Lua 同步：[[30-Shigure/09-Shigure-Fuyutsui配置宏编辑与同步]]
- 路径与缓存：[[30-Shigure/11-Shigure-本地数据路径构建与验证]]
