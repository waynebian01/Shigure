---
title: Shigure 模块存储、匹配与版本迁移
summary: 说明模块 JSON 模型、递归加载、匹配优先级、安全保存删除，以及 UnitMappingVersion 1→2→3→4 的迁移规则。
aliases:
  - ModuleStore
  - Shigure 模块系统
tags:
  - project/shigure
  - doc/feature
  - area/modules
project: Shigure
doc_type: feature
status: current
authority: source-derived
up: "[[30-Shigure/00-Shigure-MOC]]"
related:
  - "[[30-Shigure/06-Shigure-规则条件与特殊动作]]"
  - "[[30-Shigure/08-Shigure-Keymap解析与按键发送]]"
source_files:
  - Modules/ModuleStore.cs
  - Modules/ReservedUnit.cs
  - UI/ModuleEditorControl.cs
source_symbols:
  - ModuleDefinition
  - ModuleMatch
  - ModuleStore.Reload
  - ModuleStore.FindSelectedOrBestMatch
  - ModuleStore.Normalize
  - MacroConditionText.NormalizeLegacyUnit
verified_at: 2026-08-09
---

# Shigure 模块存储、匹配与版本迁移

> [!abstract] AI 快速摘要
> 模块是本地 JSON 数据，不是可执行代码。`ModuleStore` 递归读取模块目录，规范化并在内存中迁移旧单位编号；选择时先要求职业/专精/队伍/英雄天赋匹配，再按非通配字段数量决定具体度。当前 `CurrentUnitMappingVersion` 是 **4**。一个关键源码事实是模块根级 `Enabled` 目前完全未参与匹配或执行，不能用它停用模块。

## 图谱位置

- 上级：[[30-Shigure/00-Shigure-MOC]]
- 下游规则执行：[[30-Shigure/06-Shigure-规则条件与特殊动作]]
- 单位/宏协议：[[30-Shigure/08-Shigure-Keymap解析与按键发送]]

## 范围与非范围

本页覆盖模块 schema、磁盘生命周期、匹配和迁移。条件解析、动态单位算法和按键输出分别在后续页面；UI 控件本身见 [[30-Shigure/10-Shigure-UI功能地图与数据所有权]]。

## 模块核心结构

| 字段组 | 作用 |
|---|---|
| `Id`, `Name`, `Author`, `Version`, `RecommendedTalent` | 身份和展示元数据 |
| `UnitMappingVersion` | 单位编号/宏条件迁移版本，当前目标为 4 |
| `Enabled` | JSON/UI 元数据；**当前运行时忽略** |
| `Match` | 职业、专精、队伍类型、英雄天赋约束 |
| `Units`, `Counts` | 动态单位与统计定义 |
| `ValueAdjustments` | 条件化 Delta/Formula 调整 |
| `Rules` | 有序的行为规则 |

规则本身包含 `Enabled`、Condition、DelayMs、LogicDelayMs、Unit/UnitName、Spell、MacroCondition、Hotkey、Step 和 SubConditions。规则/数值调整的 `Enabled` 会被执行器检查，与根级模块 `Enabled` 不同。

## 加载与内存快照

1. 创建模块目录（若不存在）。
2. 递归枚举 `*.json`，逐个反序列化、规范化和迁移。
3. 单个文件失败会被静默跳过，其他模块继续加载；这会让“模块消失”而不一定有明显报错。
4. 存储返回克隆快照，避免 UI 和运行逻辑共享可变对象。
5. 迁移只发生在内存；只有用户再次保存模块，磁盘 JSON 才升级到当前版本。

## 匹配和选择

`Match` 可以限制职业、专精、队伍类型和英雄天赋。空值/通配值不计具体度；每个非通配限制贡献一个 specificity。队伍文本会规范化：`0`、`1-40`、`46` 和 1..40 数值范围都可参与匹配。

选择顺序：

1. 过滤出所有当前状态匹配的模块。
2. 如果 UI 指定的模块 ID **也在匹配集合中**，使用它。
3. 否则按 specificity 降序，名称作为稳定次序，选择最具体模块。

UI 记住的手动模块 ID 即使当前不匹配也可能保留；状态以后重新匹配时它会再次生效。模块根级 `Enabled` 没有出现在过滤条件中。

## 单位映射版本 4

当前保留单位：

| 编号 | 含义 |
|---:|---|
| 0 | 无固定单位/由宏条件决定 |
| 1..40 | 小队/团队槽位 |
| 41 | player |
| 42 | target |
| 43 | focus |
| 44 | cursor/ground |
| 45 | mouseover |
| 46..50 | boss1..5 |
| 51..55 | arena1..5 |

迁移按版本递进：

- `< 2`：旧编号 31 调整为 cursor 34，旧编号 34 调整为 player 31。
- `< 3`：旧编号 36/37 不再是独立单位，改成 unit 0，并分别补为 `channeling` / `nochanneling` 宏条件。
- `< 4`：v3 的保留单位 31..35 调整为 41..45，为团队槽 31..40 让出编号。
- 旧动作名 `插入法术` 规范化为 `自动插入法术`。

README 曾写映射版本 2，已于 2026-08-09 修正；旧副本可能仍含该历史值。新增编号或改变含义必须同时更新 `ReservedUnit`、迁移器、Keymap 转换器、模块编辑器和跨项目宏契约。

## 保存、重命名与删除边界

- 新文件名由模块名清洗生成，限制为安全顶层名称且最大 64 字符；名称需保持唯一。
- 保存先写临时文件，再以替换/移动落盘，单文件更新具有较好的原子性。
- 重命名写新文件后才删除旧文件；旧文件删除失败会尝试回滚新文件。
- 删除和旧文件清理前验证目标规范路径位于模块目录内，防止路径逃逸。
- 模块编辑器保存时把应用版本写入模块版本字段。

## 失败模式与排障

| 症状 | 优先检查 |
|---|---|
| 模块文件存在但列表里没有 | JSON 解析失败被逐文件静默跳过；检查格式和 schema |
| `Enabled:false` 仍运行 | 根级 `ModuleDefinition.Enabled` 当前是死元数据 |
| 手选模块未运行 | 手选项也必须匹配当前职业/专精/队伍/英雄天赋 |
| 自动选择了意外模块 | 比较 specificity；同具体度再按名称排序 |
| 旧单位目标错位 | 检查 `UnitMappingVersion` 和 1→2→3→4 迁移后的内存值 |
| 保存后手工字段消失 | 模块 UI 不保留规则 `Hotkey`/`Step`；见 UI 页面 |

## 修改影响

- 让模块 `Enabled` 真正生效会改变现有数据语义；应明确是在匹配前过滤还是只阻止规则，并增加 UI 提示/迁移说明。
- 新增 Match 维度需要同步 specificity、UI、状态来源和文档。
- 扩展模块 JSON 应保持旧字段可读；当前坏文件静默跳过，建议未来补结构化诊断而非改变全局容错。
- 改保存/删除代码必须保留路径归属检查和单文件原子写入。

## 源码索引

- `Modules/ModuleStore.cs:9-29`：版本常量与模块定义。
- `Modules/ModuleStore.cs:74-187`：匹配字段、队伍规范化和 specificity。
- `Modules/ModuleStore.cs:189-261`：规则和数值调整 schema。
- `Modules/ModuleStore.cs:281-365`：递归加载、规范化、克隆和选择。
- `Modules/ModuleStore.cs:368-443`：保存、重命名与删除。
- `Modules/ModuleStore.cs:503-650`：安全文件名、路径边界与版本迁移。
- `Modules/ReservedUnit.cs:10-97`：v3 编号、宏条件与旧编号迁移。
- `UI/ModuleEditorControl.cs:2909-2935`：编辑器保存的版本元数据。

## 知识图谱链接

- 规则消费者：[[30-Shigure/06-Shigure-规则条件与特殊动作]]
- 动态数据：[[30-Shigure/07-Shigure-动态单位数量与公式]]
- Keymap 契约：[[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]]
- 相关 UI：[[30-Shigure/10-Shigure-UI功能地图与数据所有权]]
