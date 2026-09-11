---
title: Shigure Keymap 解析与按键发送
summary: 说明职业级 Keymap 的查找语义、v3 单位与宏条件、左右 Windows 虚拟键解析，以及 PostMessage 输出的目标和权限边界。
aliases:
  - KeymapService
  - KeySender
  - Shigure 按键输出
tags:
  - project/shigure
  - doc/feature
  - area/input-output
project: Shigure
doc_type: feature
status: current
authority: source-derived
up: "[[30-Shigure/00-Shigure-MOC]]"
related:
  - "[[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]]"
  - "[[30-Shigure/06-Shigure-规则条件与特殊动作]]"
source_files:
  - Input/KeymapService.cs
  - Input/KeymapCatalog.cs
  - Input/KeySender.cs
  - Input/WindowsVirtualKeyMap.cs
  - Input/WindowsTriggerKeyState.cs
  - Modules/ReservedUnit.cs
  - Input/NativeMethods.cs
  - Infrastructure/WowProcessLocator.cs
source_symbols:
  - KeymapService.GetHotkey
  - KeymapCatalog.Load
  - KeySender.Send
  - WindowsVirtualKeyMap.Resolve
  - WindowsTriggerKeyState.IsPressed
  - MacroConditionText.NormalizeLegacyUnit
verified_at: 2026-08-10
---

# Shigure Keymap 解析与按键发送

> [!abstract] AI 快速摘要
> `KeymapService` 把 `(unit, spell, macroCondition)` 映射为 Hotkey；职业或专精变化时重新加载对应 JSON。`KeySender` 通过共享 `WowProcessLocator` 重新取得最靠前候选可见窗口，并与本轮扫描句柄核对，再按修饰键 down、主键 down/up、修饰键逆序 up 的顺序投递 `PostMessage`。

## 图谱位置

- 上级：[[30-Shigure/00-Shigure-MOC]]
- 上游规则：[[30-Shigure/06-Shigure-规则条件与特殊动作]]
- 生产契约：[[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]]
- Fuyutsui 生产者入口：[[20-Fuyutsui/03-Fuyutsui-状态块与编码入口]]；宏与动作条细节见 [[20-Fuyutsui/09-Fuyutsui-动作条键位扫描]]

## 范围与非范围

本页覆盖运行时职业级 Keymap 选择和 Windows 输出。Lua 宏如何转换为最多 1170 个键位项见 [[30-Shigure/09-Shigure-Fuyutsui配置宏编辑与同步]]；触发模式状态机见运行循环页。

## 从规则到按键

```text
ModuleRule(Unit / Spell / MacroCondition)
  -> 动态 UnitName（可选）解析为单位编号
  -> 规则直接 Hotkey（若非空，优先）
  -> KeymapService.GetHotkey(unit, spell, macroCondition)
  -> LogicDecision.Hotkey
  -> KeySender（持有共享 WowProcessLocator）.Send(hotkey, scannedWindow)
  -> WM_KEYDOWN / WM_KEYUP
```

普通规则匹配后即使 Keymap 没找到键，也会返回无键决策并阻止后续规则；这属于规则短路语义，不是 Keymap 自动回退策略。

## Keymap 文件与专精选择

- 配置可为职业指定 Keymap 名，默认 `keymap.json`。
- 相对路径从 Keymap 基目录解析；`.yml`/`.yaml` 名会改写成 `.json`；绝对路径也被接受。找不到指定文件时回退基目录的 `keymap.json`。
- `KeymapService` 缓存当前职业/专精；任一变化时重新加载。
- 若 JSON 有 `专精` 对象且包含当前专精，则只用该专精映射。它不和顶层通用项运行时合并，因此生成器要把通用项复制到每个专精。
- 条目必须有非空 spell 和 hotkey；完全相同键的后出现项覆盖先出现项。
- `KeymapCatalog` 为 UI 展示会汇总顶层和所有专精；它看到的候选集合可能大于当前运行时实际采用的单专精集合。

## 查找和宏条件

精确键由 unit、spell、macroCondition 组成：

- `MacroCondition == null`：走旧式 `(unit, spell)` 回退，重复项以后出现者为准。
- `MacroCondition == ""`：这是一个精确的“空宏条件”，不同于 null。
- 非空条件先规范化再精确匹配。
- 一键辅助强制 unit 0，并可回退到 `nochanneling`。

单位映射当前是 v3：0 无固定单位，1..30 组员，31 player，32 target，33 focus，34 cursor/ground，35 mouseover。旧 36/37 已迁移为 unit 0 加 `channeling`/`nochanneling`，见 [[30-Shigure/05-Shigure-模块存储匹配与版本迁移]]。

## 虚拟键解析

- 命名键包含 CTRL/ALT/SHIFT、鼠标侧键、F1..F12、数字小键盘、导航键（INSERT/DELETE/HOME/END/PAGEUP/PAGEDOWN）、方向键和常见标点（含 `-`）。
- 单字符可通过 `VkKeyScan` 转为主键及隐含修饰键。
- chord 从左消费 CTRL/ALT/SHIFT 前缀，剩余整段当主键；因此 `CTRL--` 的主键是 `-`，不能按 `-` 切开。
- UI 不允许把 ALT 设为**触发键**，并会把已有 ALT 触发配置回退到 XBUTTON2；这不影响把 ALT 用作**输出 Hotkey 修饰键**。

## Windows 消息输出边界

1. 每次发送都通过 `WowProcessLocator` 重新选择 `wow_process.txt` 候选进程中最靠前的可见窗口。
2. 运行时传入本轮扫描得到的窗口句柄；若发送时目标已切换，放弃发送并等待重新扫描。
3. 依次投递修饰键按下、主键按下、主键抬起、修饰键逆序抬起。
4. 使用 `WM_KEYDOWN` / `WM_KEYUP`，不会主动激活或切到前台。
5. 小键盘除号、导航六键和四个方向键设置 extended-key 标志；发送错误 5 通常指向完整性级别/UIPI 权限差异。

目标先按配置进程名限定 PID，再按 Z 顺序选择窗口；它不验证签名或固定某个进程实例。多个候选实例之间切换会触发句柄防陈旧检查；目标程序是否处理后台 `PostMessage` 仍取决于其消息循环和权限。

## 触发键与输出键的区别

`WindowsTriggerKeyState` 用全局 `GetAsyncKeyState` 轮询用户物理按键，驱动 Switch/Click/Hold。`KeySender` 则向指定窗口投递消息。这两个通道不共享焦点语义，也不保证目标程序把投递消息视作物理输入。

## 失败模式与排障

| 症状 | 优先检查 |
|---|---|
| 规则匹配但显示无键 | unit、spell、null/空/非空 macroCondition 是否完全一致 |
| 顶层通用键在某专精失效 | 运行时选专精 map 后不合并顶层；重新用转换器生成完整专精 map |
| UI 能看到某键但运行时不用 | `KeymapCatalog` 汇总范围大于当前 `KeymapService` 范围 |
| 发送到错误窗口 | `wow_process.txt` 配置过宽，或多个候选实例的 Z 顺序不符合预期 |
| 提示目标窗口已切换 | 扫描与发送之间前台候选发生变化；等待下一轮重新扫描 |
| Win32 error 5 | Shigure 与目标进程完整性级别不同 |
| ALT 不能作为触发 | UI 明确拒绝；ALT 仍可作为输出修饰键 |
| 某些特殊键无响应 | 虚拟键或 extended bit/lParam 不满足目标程序预期 |

## 修改影响

- 改单位编号、宏条件或 Keymap 主键结构必须同步转换器、模块迁移、Fuyutsui 宏和跨项目契约。
- 改“专精覆盖而不合并”的策略会改变生成文件要求，应兼容旧 Keymap 或统一重生成。
- 改目标身份模型时必须让扫描器、发送器和游戏插件目录定位继续共用同一个 `WowProcessLocator`。
- 新增输出方式时通过 `IKeyOutput` 接入，避免把 Win32 细节扩散到运行循环。

## 源码索引

- `Input/KeymapService.cs:26-107`：职业/专精缓存、专精 map 和精确/旧式查找。
- `Input/KeymapCatalog.cs:125-145`：路径解析、扩展名改写和默认回退。
- `Modules/ReservedUnit.cs:10-97`：v3 单位和通道宏条件。
- `Input/WindowsVirtualKeyMap.cs:5-86`：命名键和单字符解析。
- `Input/WindowsTriggerKeyState.cs:3-8`：全局物理键轮询。
- `Input/KeySender.cs:25-142`：目标重查/句柄核对、chord、消息顺序和 extended bit。
- `Infrastructure/WowProcessLocator.cs`：进程配置、候选 PID 和 Z 顺序窗口选择。
- `UI/MainForm.cs:781-905`、`1556-1559`：ALT 触发键拒绝与回退。

## 知识图谱链接

- 宏生产者：[[20-Fuyutsui/09-Fuyutsui-动作条键位扫描]]
- 跨项目契约：[[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]]
- 上游逻辑：[[30-Shigure/06-Shigure-规则条件与特殊动作]]
- 运行时调用：[[30-Shigure/04-Shigure-运行循环触发模式与快照]]
