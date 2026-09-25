---
title: Shigure 动态单位、数量统计与公式
summary: 说明动态单位选择器、数量统计、值调整命名空间和受限算术公式的求值顺序与边界。
aliases:
  - UnitSelector
  - FormulaEvaluator
  - Shigure 动态值
tags:
  - project/shigure
  - doc/feature
  - area/dynamic-values
project: Shigure
doc_type: feature
status: current
authority: source-derived
up: "[[30-Shigure/00-Shigure-MOC]]"
related:
  - "[[30-Shigure/06-Shigure-规则条件与特殊动作]]"
  - "[[30-Shigure/03-Shigure-配置合并与GameState构建]]"
source_files:
  - Modules/ModuleUnit.cs
  - Modules/UnitSelector.cs
  - Modules/FormulaEvaluator.cs
  - Modules/ModuleStore.cs
  - UI/UnitEditorForm.cs
source_symbols:
  - ModuleUnit
  - ModuleCountField
  - UnitSelector.Resolve
  - ModuleLogic.ResolveDynamicFields
  - FormulaEvaluator.TryEvaluateInt
verified_at: 2026-08-28
---

# Shigure 动态单位、数量统计与公式

> [!abstract] AI 快速摘要
> 模块可以先按组员生命、角色、光环、驱散或吸收选出“动态单位”，再统计满足条件的数量，并用 Delta/Formula 改写状态。求值有固定顺序：先应用会影响选择阈值的早期调整，再选单位，再算数量，最后应用其余调整。结果发布到 `$units`、`$unithealth`、`$counts` 和 `$dynamicvalues`，供条件、规则目标和 UI 使用。

## 图谱位置

- 上级：[[30-Shigure/00-Shigure-MOC]]
- 上游组员状态：[[30-Shigure/03-Shigure-配置合并与GameState构建]]
- 下游条件/目标：[[30-Shigure/06-Shigure-规则条件与特殊动作]]
- 相关模块 schema：[[30-Shigure/05-Shigure-模块存储匹配与版本迁移]]

## 范围与非范围

本页覆盖模块动态派生数据和算术公式。它不定义组员像素布局，也不定义条件表达式自身；公式和条件是两个不同的受限语言。

## 输入与输出

| 输入 | 算法 | 输出命名空间 |
|---|---|---|
| `GameState.Group[1..40]` + `ModuleUnit` | 生命/角色/光环/驱散/吸收选择器 | `$units.<name>`、`$unithealth.<name>` |
| 组员状态 + `ModuleCountField` | 对满足条件的单位计数 | `$counts.<name>` |
| `ValueAdjustment` | 条件 + Delta 或 Formula | 原状态、spells/auras、阈值或 `$dynamicvalues.<name>` |

动态定义在一个状态和一个模块内会被 memoize，避免同一 tick 重复求值。

## 固定求值顺序

```text
原始 GameState
  -> 影响动态选择阈值的“早期”数值调整
  -> Resolve Units
  -> Resolve Counts
  -> 其余 ValueAdjustments
  -> 规则条件、规则目标与 RenderSnapshot
```

这个顺序是语义，不只是优化。把全部调整提前可能让数量/目标看到本来不应看到的改写；把阈值调整延后则会让选择器使用旧阈值。

## 动态单位选择语义

选择器根据 `ModuleUnit.Kind` 分派，核心类别包括最低生命/低于阈值、按角色选取、按光环/驱散、按治疗吸收等。常见但容易遗漏的细节：

- 最低生命要求 `health < threshold`，会保留并比较 0 和负数；默认阈值是 100，所以满血 100 不参与。若要包含满血，应把阈值设为 101。
- 最低生命路径不会跳过生命 0；扣除治疗吸收后的生命值也不设下限，因此负数同样参与选择。光环、角色、驱散、吸收路径也没有统一的生命 0 过滤。
- 角色选择的 first/last 由 `Reverse` 控制；它本身不排除生命 0。
- 光环选择比较光环数值，取最大值；驱散选择取首个满足项。
- 吸收选择使用严格 `absorb > threshold`，默认阈值 0。
- `RoleNotZero` 仅把能解析且恰好等于 0 的角色视为不合格；缺失/无法解析的角色反而视为合格。
- 若配置明确要求角色 0，角色选择器可以选中它。

因此 README 所说“所有组员统计都会跳过角色 0 或生命 0”不是源码的统一规则。排障时必须看具体 Kind。

## 数量统计

`ModuleCountField` 使用与单位选择相近的筛选维度，但输出数量而非单位编号。数量可在条件中直接按名字读取，也会物化为 `$counts.<name>`。若 ValueAdjustment 目标是已有 count 或 unit-health 名称，调整器可覆盖其派生数值；否则可能写到原状态或 `$dynamicvalues`。

低生命数量字段仍要求 `0 < health < threshold`，所以 0 和负数不会计入数量；这与最低生命动态单位的候选范围不同。

## ValueAdjustment

每项调整有 `Enabled`、Condition、Field、Delta 和 Formula：

1. `Enabled=false` 跳过。
2. Condition 用模块条件求值器；不匹配不修改。
3. Formula 非空时优先于 Delta。
4. 公式或 Delta 可写法术、光环、已有 count、动态单位生命、原始状态字段。
5. 公式目标若不是以上已知位置，则写入 `$dynamicvalues.<Field>`；它不会无条件污染原始状态字典。

结果是每 tick 的派生状态，不回写 config 或 module JSON。

## 公式语言

支持：

- 二元 `+ - * /`，一元 `+ -`，括号。
- `int(x)`、`round(x)`、`floor(x)`、`ceil(x)`、`min(...)`、`max(...)`。
- 标识符引用条件系统可解析的值；标识符首字符允许字母、中文、`_`、`$`，后续还允许数字和点。
- 行尾 `#` 注释；也接受 `name = formula` 形式并只计算等号右侧。

限制：

- 除上述函数外没有任意函数、字符串运算、布尔运算或代码执行。
- 除零返回错误；NaN/Infinity 被拒绝。
- 最终结果强制转换为 `int`，即向 0 截断；`round` 使用 .NET 默认的 midpoint-to-even，不是恒定四舍五入离零。
- 公式解析和条件解析是两套语法；不要在条件里写算术，也不要在公式里写 AND/OR。

## 失败模式与排障

| 症状 | 优先检查 |
|---|---|
| 满血单位从不被最低生命选中 | 默认阈值 100 且比较是严格 `<`；设 101 |
| 离线/死亡单位仍被最低生命、光环或角色规则选中 | 这些 Kind 没有统一检查 health > 0；最低生命明确允许 0 和负数 |
| 角色缺失的单位意外参与 | `RoleNotZero` 对不可解析值返回合格 |
| 数值调整没影响目标选择 | 该字段未被归类为选择阈值的早期调整，或 Condition 未命中 |
| 公式小数结果少 1 | 最终强转 int 向 0 截断 |
| `round(2.5)` 与预期不同 | .NET midpoint-to-even |
| 动态字段在状态页有但原始 GameState 没有 | `$dynamicvalues` 是模块派生层，不是 `GameState` 原生字段 |

## 修改影响

- 改求值顺序等同于改模块语言语义，必须迁移/回归所有依赖阈值和动态值的模块。
- 新增选择器 Kind 要定义生命 0、角色 0、阈值是否严格、并列排序和 `Reverse` 语义。
- 新增公式函数需校验参数数量、除零/溢出/NaN，并同步编辑器提示。
- 新增动态名称时应沿用 UI 验证：非空、不能含 `.`/`$`、不能全数字、不能与其他动态名冲突。

## 源码索引

- `Modules/ModuleUnit.cs:6-116`：动态单位与数量定义。
- `Modules/UnitSelector.cs:15-109`：选择/计数分派。
- `Modules/UnitSelector.cs:112-286`：生命、角色、光环、驱散和吸收算法。
- `Modules/UnitSelector.cs:317-327`：`RoleNotZero` 的精确语义。
- `Modules/ModuleStore.cs:828-896`：固定求值顺序和动态命名空间。
- `Modules/ModuleStore.cs:898-1095`：Condition、Formula/Delta 和写入目标。
- `Modules/FormulaEvaluator.cs:7-299`：公式解析、函数、标识符和整数结果。
- `UI/UnitEditorForm.cs:801-828`：动态名称约束。

## 知识图谱链接

- 数据来源：[[30-Shigure/03-Shigure-配置合并与GameState构建]]
- 规则消费者：[[30-Shigure/06-Shigure-规则条件与特殊动作]]
- UI 编辑入口：[[30-Shigure/10-Shigure-UI功能地图与数据所有权]]
- 插件组员语义：[[20-Fuyutsui/07-Fuyutsui-队伍与治疗吸收]]
