---
title: Fuyutsui 队伍与治疗吸收
summary: 说明队伍成员排序、成员块地址、分帧生命/职责刷新、队伍光环入口，以及独立治疗吸收网格协议。
aliases:
  - Fuyutsui Group State
  - Fuyutsui 治疗吸收
tags:
  - project/fuyutsui
  - doc/feature
  - area/group
project: Fuyutsui
doc_type: feature
status: current
authority: source-derived
up:
  - "[[20-Fuyutsui/00-Fuyutsui-MOC]]"
related:
  - "[[20-Fuyutsui/08-Fuyutsui-光环容器本地集成]]"
  - "[[40-跨项目/01-Shigure-像素生产消费契约]]"
  - "[[30-Shigure/03-Shigure-配置合并与GameState构建]]"
source_files:
  - Fuyutsui/core/group.lua
  - Fuyutsui/core/block.lua
  - Fuyutsui/main.lua
  - Fuyutsui/core/events.lua
source_symbols:
  - Fuyutsui:IterateGroupMembers
  - Fuyutsui:UpdateGroup
  - Fuyutsui:UpdateGroupInRangeAndHealth
  - Fuyutsui:UpdateUnitHealthInfo
  - Fuyutsui:RefreshGroupHealAbsorbBars
  - Fuyutsui:UpdateGroupHealAbsorbBar
verified_at: 2026-08-28
---

# Fuyutsui 队伍与治疗吸收

上级：[[20-Fuyutsui/00-Fuyutsui-MOC]]

相关：[[20-Fuyutsui/08-Fuyutsui-光环容器本地集成]] · [[40-跨项目/01-Shigure-像素生产消费契约]] · [[30-Shigure/03-Shigure-配置合并与GameState构建]]

## AI 快速摘要

> `UpdateGroup()` 按小队/团队单位顺序重建 `groupList` 与 `group`，每名成员占 `ClassBlocks.group.num` 个顶部像素。总 `OnUpdate` 每帧只刷新一名成员的生命、职责和有效范围，以摊平成本；队伍光环/驱散由本地 AuraContainer 写入同一成员块。治疗吸收不是成员块字段，而是独立的 40 槽、5 列×8 行网格。

## 范围与非范围

本页负责队伍成员模型、地址公式、分帧状态和吸收网格。队伍 AuraContainer 的过滤、持续时间和驱散类型绘制见 [[20-Fuyutsui/08-Fuyutsui-光环容器本地集成]]；Shigure 的屏幕扫描几何见 [[40-跨项目/01-Shigure-像素生产消费契约]]。

## 输入与输出

| 输入 | 运行时对象 | 输出 |
|---|---|---|
| 小队/团队 roster | `Fuyutsui.groupList`、`Fuyutsui.group[unit]` | 稳定成员序号 |
| `ClassBlocks.group` | `blocks.groups.start/num/offset` | 顶部成员块地址 |
| 生命、职责、距离、死亡 API | 成员缓存 | healthPercent、role 等成员像素 |
| 施法目标与治疗技能 | `inComingHeals` 曲线偏移 | 预估后的生命曲线 |
| AuraContainer | 每成员容器 | 指定增益、驱散类型像素 |
| UnitHealPredictionCalculator | 每槽 calculator | 独立治疗吸收 StatusBar 网格 |

## 成员顺序与地址

`IterateGroupMembers()`（`core/group.lua:21-35`）的顺序是协议的一部分：

- 团队：`raid1` 到 `raidN`。
- 小队：从 `player` 开始，再到 `party1..partyN`。
- 非团队路径总从 `player` 开始；即使不在队伍中也会得到玩家槽，随后才遍历 `party1..partyN`。

`LoadPlayerBlocks()` 在状态、光环和法术之后记录 `blocks.groups.start`。第 `memberIndex` 名成员的字段地址为：

```text
pixel = group.start + (memberIndex - 1) * group.num + offset
```

`healthPercent`、`role`、`dispel` 和 `aura[offset]` 都是 1 基偏移。`group.num` 是每名成员的固定宽度，必须覆盖最大偏移。

## 执行链路

```text
GROUP_ROSTER_UPDATE / 专精重建
  -> 延迟 UpdateGroup()
     -> 清空并按顺序重建 groupList/group
     -> 初始化每名成员健康与有效性
     -> RefreshGroupAuraContainers()
     -> RefreshGroupHealAbsorbBars()

每帧
  -> UpdateGroupInRangeAndHealth()
     -> 只取 groupList[updateIndex]
     -> 健康曲线 -> healthPercent 像素
     -> 死亡、协助、视线、职责 -> role/有效性像素
     -> updateIndex 循环加一

吸收相关单位事件
  -> UpdateGroupHealAbsorbBar(unit)
```

## 成员状态语义

- 健康使用 `curve100`，并可叠加 `inComingHeals` 的近似偏移；这是预测值，不是纯 `UnitHealthPercent`。
- `valid` 由“未死亡、可协助、在视线内”组成。
- 职责来自 `UnitGroupRolesAssigned()`；玩家用当前专精职责覆盖 API 结果。
- 每名成员另有一个独立职责覆盖 AuraContainer：存在 `27827`（救赎之魂）时，在职责像素上覆盖 `B=0`；光环消失后覆盖槽隐藏，重新显示底层原职责。
- `inSight` 在收到中文 `UI_ERROR_MESSAGE`“目标不在视野中”时对最近施法目标置为 false，1.5 秒后恢复 true；它是错误消息驱动的启发式，不等价于射线检测。
- 死亡既可按 unit token 更新，也可由战斗日志 GUID 标记。

## 队伍光环字段

如果 `group.aura` 存在，每个 offset 建一个 `HELPFUL|PLAYER` 槽；如果 `group.dispel` 存在，则按玩家当前实际会驱散的类型建立一个减益槽。两者都落在成员块地址公式内。

职责覆盖使用独立容器和 `HELPFUL` 过滤，因此 `27827` 可以来自队伍中的其他牧师，也不会与配置中已有的“救赎之魂”普通光环槽争用同一个 aura 实例。

光环持续时间使用顶部像素 `B`，驱散槽使用固定类型色。定义与刷新细节见 [[20-Fuyutsui/08-Fuyutsui-光环容器本地集成]]。

## 治疗吸收网格协议

`FuyutsuiHealAbsorbBars` 是第三类独立视觉输出：

- 最多 40 槽，5 列、8 行。
- 每槽宽 102 单元：1 个前锚点、100 个条身、1 个灰色终点。
- 行号和单位编号编码在 RGB 中，以便扫描器找到槽并验证身份。
- 前锚点：`R=row, G=unitValue, B=0`（字节语义）。
- 条身：`R=row, G=relativeIndex, B=unitValue`。
- 单位编号：`player=1`、`party1..4=2..5`、`raidN=N`。
- StatusBar 最大值为该单位最大生命，当前值为 `GetHealAbsorbs()`。

刷新器把 `groupList` 前 30 名绑定到槽；多余槽会清零并隐藏。单位生命、最大生命、治疗预测和吸收量事件只更新已绑定单位。

## 核心数据与不变量

- `groupList` 顺序必须与 Lua 成员块、AuraContainer、吸收网格和 C# `GroupMemberN` 顺序一致。
- `group.num >= max(healthPercent, role, dispel, aura offsets)`。
- 对 30 名外部消费者，最后一个成员地址必须不超过顶部索引 510。
- 顶部队伍块和吸收网格是两套坐标系，不可用同一个 index 解释。
- Shigure 当前只构建 30 名成员，而 WoW 团队可达到 40 人；30 是当前跨项目契约上限，不是游戏上限。
- roster 变化后会先清除完整 40 人成员区并重置轮转游标，避免上一阵容泄漏。
- roster 重建后的职责像素先保持为 0，再由每帧轮转逐个恢复；未重新确认有效性的成员不会提前进入 Shigure 的目标选择。

## 失败模式与当前风险

1. **40 人被截断或未消费。** Lua 成员表可能包含 40 人，但吸收网格和 Shigure 固定 30；需要明确选择截断策略。
2. **角色偏移缺失。** 职业 group 表若没有 `role` 或 `healthPercent`，更新器做索引算术时可能报错。
3. **异步 roster 竞态。** roster 更新使用延迟回调；光环容器和吸收槽在短窗口内可能仍绑定旧 unit。
4. **视线语义是启发式。** 将 `valid` 当作权威 LoS 会导致错误治疗决策。

已修复：`UpdateGroup()` 现在会在重建前清空完整 40 人顶部成员区并把 `updateIndex` 重置为 1；每帧更新也会主动归一越界或异常游标，不再因缩编停在无效槽位。

## 修改影响

- 队伍重建必须继续保持“先清成员块、再重置游标、最后重建 roster”的顺序；不要在新 roster 写入后再清理。
- 改成员顺序、最大人数或字段偏移，必须同步 [[30-Shigure/03-Shigure-配置合并与GameState构建]]。
- 改吸收网格的列数、槽宽、锚点 RGB 或终点色，必须同步 [[30-Shigure/02-Shigure-像素扫描与协议解码]]。
- 新增 group aura/dispel 字段时检查 `group.num` 和第 30 人末索引。

## 源码索引

- `Fuyutsui/main.lua:171-180`：成员块起点与字段定义。
- `Fuyutsui/core/group.lua:22-97`：成员遍历、健康与每帧轮转。
- `Fuyutsui/core/group.lua:100-149`：死亡、视线与治疗预估。
- `Fuyutsui/core/group.lua:151-202`：清理和 roster 重建。
- `Fuyutsui/core/block.lua:299-457`：治疗吸收网格、绑定和事件。
- `Fuyutsui/core/block.lua:1136-1404`：队伍光环、职责覆盖与驱散成员槽。

## 知识图谱

本页从 [[20-Fuyutsui/02-Fuyutsui-事件与刷新调度]] 接收 roster 与分帧更新，通过 [[20-Fuyutsui/03-Fuyutsui-状态块与编码入口]] 输出成员块，并与 [[20-Fuyutsui/08-Fuyutsui-光环容器本地集成]] 共同填充每名成员；跨进程几何由 [[40-跨项目/01-Shigure-像素生产消费契约]] 约束。
