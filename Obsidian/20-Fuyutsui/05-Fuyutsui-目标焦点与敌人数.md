---
title: Fuyutsui 目标焦点与敌人数
summary: 说明目标/焦点的单位类型、驱散类型、距离、生命和施法状态，以及姓名板驱动的敌人数与仇恨统计。
aliases:
  - Fuyutsui Target Focus
  - Fuyutsui 敌人数
tags:
  - project/fuyutsui
  - doc/feature
  - area/targeting
project: Fuyutsui
doc_type: feature
status: current
authority: source-derived
up:
  - "[[20-Fuyutsui/00-Fuyutsui-MOC]]"
related:
  - "[[20-Fuyutsui/02-Fuyutsui-事件与刷新调度]]"
  - "[[20-Fuyutsui/08-Fuyutsui-光环容器本地集成]]"
  - "[[30-Shigure/03-Shigure-配置合并与GameState构建]]"
source_files:
  - Fuyutsui/core/target.lua
  - Fuyutsui/core/stateblocks.lua
  - Fuyutsui/core/block.lua
  - Fuyutsui/core/spells.lua
  - Fuyutsui/core/events.lua
  - Fuyutsui/core/core.lua
source_symbols:
  - Fuyutsui:GetUnitRange
  - Fuyutsui:UpdateUnitFullInfo
  - Fuyutsui:UpdateUnitType
  - Fuyutsui:UpdateUnitRangeBlock
  - GetUnitDispelStateIndex
  - AddUnitDispelSlots
  - Fuyutsui:AddNameplate
  - Fuyutsui:UpdateEnemyCount
verified_at: 2026-08-19
---

# Fuyutsui 目标焦点与敌人数

上级：[[20-Fuyutsui/00-Fuyutsui-MOC]]

相关：[[20-Fuyutsui/02-Fuyutsui-事件与刷新调度]] · [[20-Fuyutsui/08-Fuyutsui-光环容器本地集成]] · [[30-Shigure/03-Shigure-配置合并与GameState构建]]

## AI 快速摘要

> `core/target.lua` 为 `target` 和 `focus` 分别维护缓存，输出类型、距离、死亡、生命和施法状态；`core/block.lua` 的 AuraContainer 写入与类型相邻的目标/焦点驱散类型槽。类型值通过 `UnitIsUnit()` 识别实际对应的 raid/player/party/boss 单位，友方在基础编号上加 100；驱散类型则按当前可处理光环的类别输出固定编号。姓名板事件维护另一张敌人缓存，0.2 秒轮询距离与战斗状态后输出总敌人数，并在仇恨事件中拆分有/无仇恨数量。

## 范围与非范围

本页覆盖单位快照、目标/焦点类型与驱散类型编码，以及姓名板聚合。驱散类型在这里只记录字段关系和生产契约；AuraContainer 的完整过滤、布局和生命周期见 [[20-Fuyutsui/08-Fuyutsui-光环容器本地集成]]。队伍距离和治疗范围见 [[20-Fuyutsui/07-Fuyutsui-队伍与治疗吸收]]。

## 输入与输出

| 输入 | 缓存/计算 | 输出字段族 |
|---|---|---|
| `target`、`focus` 单位 API | `Fuyutsui.target`、`Fuyutsui.focus` | 类型、距离、生命、施法/引导、可打断 |
| AuraContainer + 当前驱散能力 | 目标/焦点友方 HARMFUL、敌方 HELPFUL 驱散槽 | 目标驱散类型、焦点驱散类型 |
| LibRangeCheck | `minRange`、`maxRange`、`inRange` | 目标/焦点距离 |
| 姓名板增删事件 | `Fuyutsui.nameplate[unit]` | 当前可计数单位集合 |
| 仇恨和战斗 API | `threatStatus`、`affectingCombat` | 敌人数、有仇恨、无仇恨 |
| 地图/遭遇状态 | 特例过滤 | 是否把特定单位计入聚合 |

所有计数以 `count/255` 写入蓝通道；单位生命使用 `curve100` 的蓝通道。

## 执行链路

### 目标与焦点

```text
PLAYER_TARGET_CHANGED / PLAYER_FOCUS_CHANGED
  -> UpdateUnitFullInfo(unit)
     -> UpdateUnitCanAttack(unit) -> UpdateUnitType(unit)
     -> UpdateUnitDeathStatus(unit) -> UpdateUnitType(unit)
     -> UpdateUnitHealthBlock(unit)

每 0.2 秒
  -> UpdateUnitRangeBlock(target/focus)
     -> LibRangeCheck -> cache -> 距离像素
```

施法开始/停止事件会单独调用 `UpdateUnitCastingOrChannelingInfo()`；它刷新施法、可打断、引导和引导可打断四个状态，不重建整个单位快照。

### 姓名板敌人数

```text
NAME_PLATE_UNIT_ADDED
  -> AddNameplate(unit)
  -> 缓存名称、GUID、阵营、距离等

UNIT_THREAT_SITUATION_UPDATE
  -> UpdateNameplateThreat(unit)
  -> UpdateThreatEnemyCounts()

每 0.2 秒
  -> UpdateEnemyCount()
  -> 重读范围/战斗状态 -> count/255

NAME_PLATE_UNIT_REMOVED
  -> 从缓存删除
```

## 目标/焦点类型与驱散类型

这两组字段在状态布局中分别成对出现，但编码含义彼此独立：

| 目标字段 | 焦点字段 | 数据来源 | 含义 |
|---|---|---|---|
| `目标类型` | `焦点类型` | `UpdateUnitType()` | 单位 token 编号，并以 `+100` 区分友方 |
| `目标驱散类型` | `焦点驱散类型` | AuraContainer dispel 槽 | 当前可处理光环的驱散类别 |

### 类型编码

`UpdateUnitType()` 先通过 `UnitIsUnit(unit1, unit2)` 判断 `target` 或 `focus` 实际对应的单位 token，再编码为 `index/255`。匹配按 raid、player、party、boss、arena 的顺序执行；全部不匹配时使用“其他”编号。不存在或死亡的单位输出 0。

| 实际单位 | 基础 index | 非友方/敌方输出 | 友方 index（基础值 + 100） | 友方输出 |
|---|---:|---:|---:|---:|
| `raid1..raid40` | 1..40 | `1..40` | 101..140 | `101..140` |
| `player` | 41 | `41` | 141 | `141` |
| `party1..party4` | 42..45 | `42..45` | 142..145 | `142..145` |
| `boss1..boss5` | 46..50 | `46..50` | 146..150 | `146..150` |
| `arena1..arena5` | 51..55 | `51..55` | 151..155 | `151..155` |
| 其他 | 56 | `56` | 156 | `156` |

友方判定直接使用 `UnitCanAssist("player", unit)`；类型值不再包含距离、驱散能力或法术已知状态。由于 raid 优先匹配，玩家或队友处于团队中时输出对应的 raid 编号，而不是 player/party 编号。

### 驱散类型编码

`目标驱散类型` 与 `焦点驱散类型` 的普通状态 getter 固定返回 0；实际颜色由 AuraContainer 直接覆盖对应像素。`GetUnitDispelStateIndex()` 找到这两个状态槽，`AddUnitDispelSlots()` 为每个单位各建立两条互斥链路：

- 友方单位读取 `HARMFUL` 光环，只启用玩家当前 `includeDispelTypes` 能处理的防御驱散类型。
- 敌方单位读取 `HELPFUL` 光环，只启用玩家当前 `includeOffensiveDispelTypes` 能处理的进攻驱散类型。
- 没有匹配光环、单位反应不符或玩家无对应驱散能力时保持 0。
- 多个候选按到期时间排序；像素表达被选中光环的驱散类别，不表达剩余时间。

| 驱散类别 | index | 输出 |
|---|---:|---:|
| Magic | 1 | `1` |
| Curse | 2 | `2` |
| Disease | 3 | `3` |
| Poison | 4 | `4` |
| Enrage | 9 | `9` |
| Bleed | 11 | `11` |

两类输出不能混读：例如 `1/255` 在“类型”槽表示 `raid1`，在“驱散类型”槽表示 Magic。目标与焦点使用相同编码表，区别仅由各自的状态槽位置决定。

## 距离、生命与施法

- `GetUnitRange()` 直接返回 LibRangeCheck 的最小/最大估计。
- 对敌人，`maxRange <= state.specRange` 才标记专精射程内；对友方，阈值固定为 40。
- 距离状态 getter 输出缓存的最大距离并归一化；无法估计时应视为未知，不应自行解释为 0 米。
- 生命值使用 `UnitHealthPercent(..., curve100)`。
- 施法进度与可打断状态共享 [[20-Fuyutsui/04-Fuyutsui-玩家状态]] 描述的 Duration/Curve 机制。

## 敌人数口径

姓名板缓存不是“附近所有敌人”的权威列表，而是当前客户端已经创建姓名板且通过 `IsCountedEnemy()` 过滤的单位：

- 必须是可攻击目标。
- 必须有可用最大距离且 `maxRange <= state.specRange`，并处于战斗关联状态；硬编码地图/遭遇可绕过战斗关联要求。
- `testMap` 与 `testEncounter` 对地图 `2393`、遭遇 `2563` 有硬编码特例。
- 仇恨统计把 `threatStatus >= 2` 记为有仇恨，其余可计数单位记为无仇恨。

因此输出适合战斗策略提示，不等价于战斗日志意义上的全场敌人总数。

## 核心数据与不变量

- `GetUnitCache()` 当前只为 `target` 和 `focus` 返回缓存；虽然 `unitZHMap` 列出 `boss1..boss5`，这些 boss token 目前不会进入该通用更新链。
- 状态键为 `目标类型`、`焦点类型` 这类“分类+名称”组合，必须与 `LoadPlayerBlocks()` 完全一致。
- 单位身份比较统一经过 `isSameUnit()`，其内部先保存 `local isSame = UnitIsUnit(unit1, unit2)` 再返回结果。
- raid 必须先于 player/party 匹配；否则同一团队成员可能被编码到不同编号空间。
- 类型槽与驱散类型槽必须按各自字段解释；相同蓝通道值不代表相同业务语义。
- 驱散类型槽由 AuraContainer 覆盖，普通 getter 必须保持 0，避免无匹配光环时残留旧值。
- 姓名板缓存必须在移除事件删除；否则单位 token 重用会污染计数。
- 敌人数除以 255，消费端不能把 `B` 直接当原始整数。

## 失败模式与当前风险

1. **距离未知时像素陈旧。** LibRangeCheck 可返回 `nil`；getter/更新器没有为所有未知分支强制写哨兵，旧距离像素可能保留。
2. **boss 缓存仍不是独立入口。** 目标/焦点类型可以识别其是否与 `boss1..boss5` 相同，但 `GetUnitCache()` 仍只支持 `target` 和 `focus`；不能据此直接为 boss token 刷新完整状态。
3. **驱散能力可能过时。** 学会、遗忘技能或切换专精后若未运行 `UpdateSpellKnown()`，目标/焦点驱散槽仍会使用旧的能力集合。
4. **姓名板覆盖不完整。** 未显示姓名板、客户端未创建或刚被移除的敌人不会计数。
5. **硬编码副本规则会过时。** `testMap`/`testEncounter` 需要随版本验证，不能泛化为所有副本。
6. **受保护值。** 新版 WoW 的 secret value 若被直接比较、索引或打印会报错；新增单位逻辑需沿用安全检查策略。

## 修改影响

- 增加 boss 单位支持需同时新增缓存、状态声明、事件入口和 C# 配置字段。
- 更改类型编号、匹配优先级或友方偏移时同步 Shigure 的业务判断。
- 更改驱散类型编号或过滤能力时同步 [[20-Fuyutsui/06-Fuyutsui-法术与物品冷却]]、[[20-Fuyutsui/08-Fuyutsui-光环容器本地集成]] 与 Shigure 的业务判断。
- 更改敌人数口径时记录地图、战斗和范围边界；它会直接改变 AoE/目标选择策略。
- 更改目标/焦点字段名或顺序需重新生成配置，遵循 [[40-跨项目/02-Shigure-ClassBlocks到config同步契约]]。

## 源码索引

- `Fuyutsui/core/target.lua:10-69`：距离 API、单位名映射、缓存、单位身份匹配和类型编码。
- `Fuyutsui/core/target.lua:71-145`：类型、射程、施法、死亡和生命通用更新。
- `Fuyutsui/core/target.lua:148-195`：target/focus 包装入口。
- `Fuyutsui/core/target.lua:197-265`：姓名板缓存、仇恨与敌人数。
- `Fuyutsui/core/stateblocks.lua:235-261`：目标/焦点类型、驱散类型及其他 getter。
- `Fuyutsui/core/block.lua:738-807,907-947`：目标/焦点驱散过滤、颜色编号与槽创建。
- `Fuyutsui/core/spells.lua:152-207`：防御/进攻驱散类型名称和当前能力集合。
- `Fuyutsui/core/events.lua:204-250,361-389,425-435`：相关事件与 0.2 秒轮询。

## 知识图谱

本页由 [[20-Fuyutsui/02-Fuyutsui-事件与刷新调度]] 驱动，通过 [[20-Fuyutsui/03-Fuyutsui-状态块与编码入口]] 输出单位像素；目标/焦点驱散能力来自 [[20-Fuyutsui/06-Fuyutsui-法术与物品冷却]]，AuraContainer 写入机制见 [[20-Fuyutsui/08-Fuyutsui-光环容器本地集成]]。
