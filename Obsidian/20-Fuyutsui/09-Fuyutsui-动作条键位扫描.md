---
title: Fuyutsui 动作条键位扫描
summary: 说明游戏内 1..180 动作槽扫描、本地 spellID→按键缓存，以及安全宏按钮键池与外部 keymap 的边界。
aliases:
  - Fuyutsui Keybindings
  - Fuyutsui 动作条扫描
tags:
  - project/fuyutsui
  - doc/feature
  - area/keybindings
project: Fuyutsui
doc_type: feature
status: current
authority: source-derived
up:
  - "[[20-Fuyutsui/00-Fuyutsui-MOC]]"
related:
  - "[[50-参考资料/CLASSMACROS_AI_Reference_zh-CN]]"
  - "[[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]]"
  - "[[30-Shigure/08-Shigure-Keymap解析与按键发送]]"
source_files:
  - Fuyutsui/core/keybinds.lua
  - Fuyutsui/core/config.lua
  - Fuyutsui/core/macro.lua
  - Fuyutsui/core/classmacros.lua
  - Fuyutsui/main.lua
  - Fuyutsui/core/events.lua
source_symbols:
  - ProcessActionSlot
  - Fuyutsui:ReadKeybindings
  - Fuyutsui.actionBars
  - Fuyutsui.keymap
  - Fuyutsui:CreateMacro
  - Fuyutsui:LoadPlayerMacros
verified_at: 2026-08-09
---

# Fuyutsui 动作条键位扫描

上级：[[20-Fuyutsui/00-Fuyutsui-MOC]]

相关：[[50-参考资料/CLASSMACROS_AI_Reference_zh-CN]] · [[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]] · [[30-Shigure/08-Shigure-Keymap解析与按键发送]]

## AI 快速摘要

> `ReadKeybindings()` 延迟 0.5 秒扫描动作槽 1..180，把可识别的 macro/spell 记录为 `Fuyutsui.keybindings[spellID] = {key, slot, keycode, icon, name}`。这是游戏内诊断缓存；Shigure 的实际发送映射来自 `ClassMacros` 转换出的职业级 keymap，而不是读取该 Lua 表。插件自己的安全宏按钮使用 26 组左右修饰键×45 个基础键，共 1170 个顺序槽。

## 范围与非范围

本页同时说明两个容易混淆但相邻的机制：

1. 扫描玩家现有动作条并建立本地缓存。
2. Fuyutsui 为 `ClassMacros` 创建不可见安全按钮并绑定固定键池。

本页不说明 `/fu` 配置和插入法术命令，见 [[20-Fuyutsui/10-Fuyutsui-命令快捷按钮与存档]]；Windows 端实际如何解析和发送按键见 [[30-Shigure/08-Shigure-Keymap解析与按键发送]]。

## 动作条扫描输入与输出

| 输入 | 处理 | 输出 |
|---|---|---|
| `Fuyutsui.actionBars` | 槽范围映射到绑定命令前缀 | 例如 `ACTIONBUTTON1` |
| `GetActionInfo(slot)` | 只接收 `macro` 或 `spell` | 动作类型与第二返回值 |
| `C_Spell.GetSpellInfo(id)` | 读取图标与名称 | 元数据 |
| `GetBindingKey(command)` | 读取第一个绑定键 | 字符串 key |
| `Fuyutsui.keymap[key]` | 转换为 Windows 风格 keycode | `keybindings[spellID].keycode` |

## 动作条扫描链路

```text
OnEnable / UPDATE_BINDINGS / SPELLS_CHANGED / ACTIONBAR_*GRID
  -> ReadKeybindings()
     -> wipe(local keybindings)
     -> C_Timer.After(0.5)
        -> for slot = 1, 180
           -> ProcessActionSlot(slot)
           -> actionBars 查 binding command
           -> GetBindingKey(command)
           -> keybindings[spellID] = latest match
```

`Fuyutsui.actionBars`（`core/config.lua:228-243`）当前列出 14 个范围。多数范围是 12 槽，但 `121..143` 一段有 23 槽，且槽 144 没有任何范围；扫描循环仍遍历 1..180，只有命中范围的槽才会构造绑定命令。延迟用于等待 WoW 在绑定/法术/动作条变化后稳定 API 状态。

## 本地缓存语义

缓存以 SpellID 为 key，因此：

- 同一技能出现在多个槽时，循环后遇到的已绑定槽会覆盖先前记录；代码保留了注释掉的 `break`。
- 保存的是 `GetBindingKey()` 的第一个返回值，不保留第二绑定。
- `keycode` 只在 `Fuyutsui.keymap` 有完全匹配字符串时存在。
- 表在延迟任务开始前就 `wipe`，0.5 秒窗口内观察者会看到空表。

仓库内没有把该运行时表序列化给 Shigure 的路径。跨项目自动化使用的是下一节的确定性安全宏槽顺序。

## 安全宏键池

`core/macro.lua` 生成固定键池：

- 修饰键顺序：`CTRL`、`ALT`、`SHIFT`、`ALT-CTRL`、`ALT-SHIFT`、`CTRL-SHIFT`、`ALT-CTRL-SHIFT`。
- 基础键 50 个：小键盘、F 键（无 F4）、标点、部分数字，以及末尾的 `-`、`INSERT`、`DELETE`、`HOME`、`END`、`PAGEUP`、`PAGEDOWN`、`UP`、`DOWN`、`LEFT`、`RIGHT`。不加反引号、`NUMPADENTER`。
- 总容量 `26 × 45 = 1170`。
- 第 `i` 槽创建名为 `s{i}` 的 `SecureActionButtonTemplate`，通过 `SetOverrideBindingClick()` 绑定对应按键。

宏体解析顺序：先查 `Fuyutsui.MacroBodies` 的命名宏体；字符串以 `/` 开头则原样使用；否则包装为 `/cast <字符串>`。

## ClassMacros 到按钮的确定顺序

`LoadPlayerMacros()` 读取当前职业的 dynamic 数组后，`CreateMacro()` 按实际源码顺序消费最多 1170 个槽：

1. `dynamicSpells`：每个定义固定占 30 槽，展开 raid1..raid30，并兼容 party/player 条件。
2. `staticSpells`：每项占 1 槽；空字符串只保留占位，不创建按钮。
3. `specialSpells`：每项占 1 槽，接在 static 之后。

`specialSpells` 不是“按同索引覆盖 static”的优先层。任何仍描述为覆盖关系的旧审计均不代表当前实现。C# `FuyutsuiKeymapConverter` 必须复现上述追加顺序。

## 核心数据与不变量

- 安全宏和 override binding 不能在战斗锁定中创建、清理或改属性；代码遇到 `InCombatLockdown()` 会直接返回。
- Lua `macroKind[i]` 与 C# 生成 keymap 的第 `i` 项必须逐项相同，包括修饰键文本顺序。
- 每个 dynamic 条目始终占 30 槽，即使某些宏体为空。
- static/special 空项同样占位；删除占位会让后续所有键左移。
- 总展开数不得超过 1170；超出部分 `nextSlot()` 静默不创建。
- 游戏内动作条 `Fuyutsui.keybindings` 不是跨进程契约源，不能拿它替换生成 keymap。

## 失败模式与当前风险

1. **宏动作的第二返回值未必是 SpellID。** `ProcessActionSlot()` 对 `actionType == "macro"` 仍把 `GetActionInfo()` 第二值传给 `C_Spell.GetSpellInfo()`；若 API 返回宏 ID，则宏槽会被跳过或误识别。
2. **修饰键 keycode 可能为空。** `Fuyutsui.keymap` 主要列基础键；`GetBindingKey()` 返回 `CTRL-X` 等组合时精确查表可能失败。
3. **重复技能覆盖。** 同 SpellID 多槽时最后一次扫描胜出，可能不是玩家期望的主动作条键。
4. **动作条范围异常。** `121..143` 会生成 `ACTIONBUTTON1..23`，后半通常没有对应绑定；槽 144 永远不处理。
5. **延迟任务乱序。** 高频触发会排队多个不可取消的 `C_Timer.After(0.5)` 扫描，旧触发的任务也会执行。
6. **战斗中专精/宏更新失败。** `ClearMacros()` 和 `createMacro()` 都拒绝战斗锁定；表面状态可能已切换，但按钮仍是旧布局。
7. **键池溢出静默。** 没有错误或容量报告，C# 仍可能生成不可用的尾部映射。

## 修改影响

- 修改 `modifiers`、`keys` 或三段展开顺序时，必须同步 [[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]] 与 Shigure 转换器。
- 修改 `ClassMacros` 后使用 Shigure 的配置/宏同步流程，见 [[30-Shigure/09-Shigure-Fuyutsui配置宏编辑与同步]]。
- 修复动作条宏识别时应按 WoW 当前 API 先解析宏信息/宏体或宏所示技能，再调用 Spell API。
- 若要让游戏内缓存成为外部契约，需新增明确序列化与版本字段；当前没有这条链路。

## 源码索引

- `Fuyutsui/core/config.lua:228-345`：动作条范围与 keycode 表。
- `Fuyutsui/core/keybinds.lua:1-53`：1..180 扫描和本地缓存。
- `Fuyutsui/core/events.lua:314-328`：重新扫描触发事件。
- `Fuyutsui/core/macro.lua`：1170 键池、安全按钮和三段展开顺序。
- `Fuyutsui/core/classmacros.lua:1-7`：命名宏体。
- `Fuyutsui/main.lua:197-236`：当前职业 dynamic 数组与宏创建入口。

## 知识图谱

本页从 [[50-参考资料/CLASSMACROS_AI_Reference_zh-CN]] 的职业宏 schema 构造游戏内安全按钮，并通过 [[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]] 与 [[30-Shigure/08-Shigure-Keymap解析与按键发送]] 对齐；运行时动作条扫描则只形成 Fuyutsui 内部诊断缓存。
