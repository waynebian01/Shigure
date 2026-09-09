---
title: Shigure 配置合并与 GameState 构建
summary: 解释 common/class/spec 配置的浅合并、步骤与动作条映射，以及扫描结果如何归一化为 GameState。
aliases:
  - StateBuilder
  - Shigure config
tags:
  - project/shigure
  - doc/feature
  - area/state
project: Shigure
doc_type: feature
status: current
authority: source-derived
up: "[[30-Shigure/00-Shigure-MOC]]"
related:
  - "[[30-Shigure/02-Shigure-像素扫描与协议解码]]"
  - "[[40-跨项目/02-Shigure-ClassBlocks到config同步契约]]"
source_files:
  - Infrastructure/ConfigService.cs
  - Runtime/StateBuilder.cs
  - Runtime/GameState.cs
  - config/common.json
source_symbols:
  - ConfigService.LoadFromBaseDirectory
  - ConfigService.BuildStateConfig
  - ConfigService.GetFailedSpells
  - StateBuilder.Build
  - StateBuilder.BuildGroup
  - GameState.GetValue
verified_at: 2026-08-28
---

# Shigure 配置合并与 GameState 构建

> [!abstract] AI 快速摘要
> `ConfigService` 优先读取拆分后的 `config/common.json` 与 13 个职业文件，根据像素步骤 2/3 选择职业和专精并进行浅合并。`StateBuilder` 再把顶行、动作条和吸收字典转换为 `GameState`：标量、法术、光环和固定 30 个组员槽位。配置中存在但扫描值缺失时会归一化为 false/空字符串/0，这会直接影响后续条件判断。

## 图谱位置

- 上级：[[30-Shigure/00-Shigure-MOC]]
- 上游扫描：[[30-Shigure/02-Shigure-像素扫描与协议解码]]
- 上游配置生成：[[40-跨项目/02-Shigure-ClassBlocks到config同步契约]]
- 下游规则：[[30-Shigure/06-Shigure-规则条件与特殊动作]]

## 范围与非范围

本页定义运行时 JSON 配置如何被选中、合并和解释，不描述 Lua 编辑器如何生成这些文件；转换流程见 [[30-Shigure/09-Shigure-Fuyutsui配置宏编辑与同步]]。也不定义模块匹配与公式。

## 输入与输出

| 输入 | 处理 | 输出 |
|---|---|---|
| `RowData[2]` / `[3]` | 先读取职业/专精原始整数 | 选择职业配置和专精覆盖 |
| `config/common.json` + 13 个职业 JSON | 浅合并 | `JsonObject` 状态配置 |
| 顶行、Bar、HealAbsorb 字典 | 按 `step`、`type`、组员相对步解释 | `GameState` |
| 配置中的 `一键法术` | 建立值到法术名映射 | 插入法术和一键辅助特殊动作共用的映射 |

## 配置发现与合并

1. 若存在 `config/` 目录，则走拆分配置；否则回退旧的单文件 `config.json`。
2. 拆分配置要求 `common.json` 和全部 13 个职业文件都存在。职业文件先尝试英文名，再尝试数字名；缺任意一个会抛错，而不是只加载当前职业。
3. JSON 允许注释和尾逗号。
4. 合并先复制根级带 `step` 的字段以及可选根 `state`，然后用当前专精对象的所有字段整体覆盖。
5. `锚点`、`职业`、`专精` 始终从 common 配置恢复，防止专精覆盖协议基础字段。

这是**浅合并**而不是递归合并。同名对象由专精完整覆盖，修改 schema 时不能假定子字段会自动保留。

## GameState 构建链路

当专精配置包含 `nameplates` 时，`StateBuilder` 会把扫描到的 20 个固定槽位映射为 `nameplates.<槽位>.<字段>`，字段包括 `存在`、`生命值`、`距离` 和配置光环的剩余时间；这些路径可直接用于模块条件。

```text
RowData[2,3]
  -> BuildStateConfig(classId, specId)
  -> 标量字段
  -> spells / auras
  -> group[1..30]
  -> GameState(fields, spells, auras, group)
```

### 标量与类型

- `step` 为整数时读取 `RowData[step]`。
- `step: "bar"` 时按配置中的 bar 索引读取 `BarData`。
- `type: "bool"`：非 0 为 true。
- `type: "string"`：整数转十进制文本。
- 其他类型：保留整数。
- 已配置字段的原始值缺失时分别得到 false、`""`、0，而不是 null。

### 法术、光环与组员

- 法术和光环形成按名称访问的字典，供 `spell.*` / `aura.*` 条件和特殊动作使用。
- 组员配置默认 `start=26`、`num=5`，但以生成的配置为准。
- 固定生成 30 个单位；第 `i` 个单位某字段的绝对步骤为 `start + (i-1)*num + relativeStep`。
- 组员字段若 `step: "bar"`，读取的是全局动作条索引，不是组员步长。
- `HealAbsorbData[i]` 注入组员；因插件生命值包含吸收，构建器从 `生命值` 中减去吸收。结果不设下限，可以为 0 或负数，供最低生命值选择器比较。

### GameState 查询

`GameState` 提供字段、法术、光环和组员集合，以及基础类型转换。它理解 `state.*`、`spell.*`、`spells.*`、`aura.*`、`auras.*` 前缀；`group.N.field`、动态单位和 `$dynamicvalues` 是条件求值器扩展的路径，不由 `GameState` 本体解析。

## 核心不变量和语义陷阱

- `common.json` 当前步骤 1/2/3 是锚点/职业/专精。
- 顶行步骤位置来自 Fuyutsui ClassBlocks 的**序列顺序**；名字相同不代表可以任意移动。
- 转换器遇到固定通用字段时虽然不在职业 JSON 重复写入，它们仍消耗步骤号。
- 配置字段缺失和扫描字段缺失不同：前者在条件层可能是未知；后者若配置已声明，通常已变成 0/false/空。
- `GetFailedSpells` 与 `GetOneKeySpells` 都读取配置键 `一键法术`，所以“自动插入法术”和“一键辅助”使用同一个值到法术映射源。
- 插件健康值包含吸收、Shigure 状态健康值扣除吸收，这是跨项目必须保持一致的语义。

## 失败模式与排障

| 症状 | 可能原因 |
|---|---|
| 启动时配置加载失败 | 13 个职业文件不齐、JSON 语法或根结构错误 |
| 职业/专精显示 0 | 顶行步骤 2/3 未输出或颜色读取失败 |
| 状态名存在但值一直 0 | `step` 错位、超过 510、插件未画该值、bar 索引错误 |
| 专精字段意外消失 | 浅合并中同名对象被完整覆盖 |
| 组员字段错位 | `start`、`num` 或 relative step 与 ClassBlocks 顺序不一致 |
| 生命值比插件显示低 | Shigure 有意扣除了治疗吸收；先核对协议语义 |

README 中“缺失/非数字关系比较为 false”的概括不能直接套用到已配置扫描字段。例如生命值步骤缺失会先成为 0，之后 `生命值 < 50` 可能为真。

## 修改影响

- 新增状态字段：修改 Fuyutsui ClassBlocks、状态目录、转换器并重新生成 config；再检查条件编辑器是否要提供该字段。
- 改组员布局：同时修改生产端、ClassBlocks 到 config 契约、`StateBuilder` 解释以及最多 510 步容量。
- 改健康/吸收定义：同步 [[20-Fuyutsui/07-Fuyutsui-队伍与治疗吸收]] 和像素契约。
- 改合并算法：现有职业 JSON 可能依赖“专精整体覆盖”，必须做迁移或兼容读取。

## 源码索引

- `Infrastructure/ConfigService.cs:25-30`：拆分目录和旧单文件回退。
- `Infrastructure/ConfigService.cs:49-88`：13 职业完整性与宽松 JSON 解析。
- `Infrastructure/ConfigService.cs:105-139`：浅合并和固定通用字段恢复。
- `Infrastructure/ConfigService.cs:142-184`：Keymap 文件名和特殊法术映射。
- `Runtime/StateBuilder.cs:14-51`：职业/专精选择和总体构建。
- `Runtime/StateBuilder.cs:54-146`：标量、组员、bar 和类型默认值。
- `Runtime/GameState.cs:12-91`：状态容器和基础路径/类型读取。
- `config/common.json:1-13`：步骤 1/2/3 的当前事实。

## 知识图谱链接

- 上游布局：[[20-Fuyutsui/03-Fuyutsui-状态块与编码入口]]、[[40-跨项目/02-Shigure-ClassBlocks到config同步契约]]
- 下游求值：[[30-Shigure/06-Shigure-规则条件与特殊动作]]、[[30-Shigure/07-Shigure-动态单位数量与公式]]
- 编辑与同步：[[30-Shigure/09-Shigure-Fuyutsui配置宏编辑与同步]]
