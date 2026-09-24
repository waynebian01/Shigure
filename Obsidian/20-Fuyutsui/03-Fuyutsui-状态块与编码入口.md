---
title: Fuyutsui 状态块与编码入口
summary: 说明 ClassBlocks 如何被展开为稳定像素索引，以及主状态行、横向计数条和治疗吸收网格三类视觉输出的编码契约。
aliases:
  - Fuyutsui 像素协议
  - Fuyutsui 状态块布局
tags:
  - project/fuyutsui
  - doc/feature
  - area/protocol
project: Fuyutsui
doc_type: feature
status: current
authority: source-derived
up:
  - "[[20-Fuyutsui/00-Fuyutsui-MOC]]"
related:
  - "[[50-参考资料/TEXTURE_LAYOUT_zh-CN]]"
  - "[[40-跨项目/01-Shigure-像素生产消费契约]]"
  - "[[30-Shigure/02-Shigure-像素扫描与协议解码]]"
source_files:
  - Fuyutsui/main.lua
  - Fuyutsui/core/block.lua
  - Fuyutsui/core/stateblocks.lua
  - Fuyutsui/core/curves.lua
source_symbols:
  - Fuyutsui:LoadPlayerBlocks
  - Fuyutsui:UpdatePlayerBlocks
  - EncodeBlockChannels
  - Fuyutsui:CreateTexture
  - Fuyutsui:CreateAutoLayoutBar
  - Fuyutsui:UpdateStateBlock
verified_at: 2026-08-09
---

# Fuyutsui 状态块与编码入口

上级：[[20-Fuyutsui/00-Fuyutsui-MOC]]

相关：[[50-参考资料/TEXTURE_LAYOUT_zh-CN]] · [[40-跨项目/01-Shigure-像素生产消费契约]] · [[30-Shigure/02-Shigure-像素扫描与协议解码]]

## AI 快速摘要

> `ClassBlocks[specIndex]` 是布局输入，`LoadPlayerBlocks()` 按固定顺序把它展开到 `Fuyutsui.blocks`。主状态行按实际占用在 510/765/1020 格之间选择档位，每 255 格切换一次红通道方案，`B` 承载状态值。插件另有横向 `CountBars` 和 40 槽治疗吸收网格，共三类输出，不能当成一条连续索引流。

## 范围与非范围

本页负责布局生成、RGB 索引、三类输出和跨进程不变量。

本页不解释每个 `B` 值的业务含义；玩家、目标、法术和队伍语义分别见 [[20-Fuyutsui/04-Fuyutsui-玩家状态]]、[[20-Fuyutsui/05-Fuyutsui-目标焦点与敌人数]]、[[20-Fuyutsui/06-Fuyutsui-法术与物品冷却]]、[[20-Fuyutsui/07-Fuyutsui-队伍与治疗吸收]]。

## 输入与输出

| 输入 | 处理 | 输出 |
|---|---|---|
| 当前专精的 `ClassBlocks` | 依次展开状态、光环、法术、物品和队伍定义 | `Fuyutsui.blocks` 索引表 |
| 状态 getter 返回的归一化数值 | `CreateTexture(index, b)` | 顶部主状态行 RGB 像素 |
| `castCount`、`maxCharge`、光环 `maxApps` | 自动预留水平单元 | `FuyutsuiCountBars` |
| 队伍治疗吸收百分比 | 按成员槽位绘制锚点、100 单元条身和终点 | `FuyutsuiHealAbsorbBars` |
| 屏幕像素 | Shigure 精确取色 | `GameState`、冷却条和吸收量 |

## 执行链路

```text
ClassBlocks[spec]
  -> LoadPlayerBlocks(spec)
     -> blocks.state / blocks.auras / blocks.spells / blocks.items / blocks.groups
     -> 释放并重建 AuraContainer
  -> UpdatePlayerBlocks() 与各事件处理器
     -> UpdateStateBlock(category, name)
     -> getter() -> B
     -> CreateTexture(index, B)
  -> Shigure PixelScanner 精确解码 R/G，读取 B
```

`main.lua:39-195` 是布局唯一入口。专精重建发生后，旧光环容器会先释放再重建；普通状态槽仍通过同一组 510 个预创建纹理复用。

## 主状态行的索引布局

### 固定前缀

`LoadPlayerBlocks()` 从索引 1 开始，并先占用：

| 索引 | 含义 |
|---:|---|
| 1 | 锚点；初始化时 `B=0` |
| 2 | 职业 |
| 3 | 专精 |

这些位置是 Lua 生产端和 C# 消费端共同依赖的协议，不是展示偏好。

### 专精可变段

从索引 4 开始严格按以下顺序追加：

1. 状态分类：`状态`、`能量`、`配置开关`、`目标`、`焦点`、`鼠标`、`宠物`、`首领1`～`首领5`。
2. 玩家光环。
3. 目标有害、目标增益、焦点有害、焦点增益光环。
4. 法术冷却；普通法术占 1 槽，带 `charge=true` 的法术连续占冷却与充能 2 槽。
5. 顶层 `items` 按 `itemId` 升序占位，每项同时进入 `blocks.state` 与 `blocks.items`。
6. 队伍块只记录此时的 `start`；成员 `n` 的字段偏移为 `start + (n-1)*group.num + offset`。

前三个状态分类使用裸名称作为 `blocks.state` 键；单位分类使用 `分类 .. 名称`。物品名称同样是裸键，因此不能与已有裸状态重名。

## 510 槽 RGB 契约

`core/block.lua:67-73,109-115` 的实际编码是：

| 逻辑索引 | R 字节 | G 字节 | 反解 |
|---:|---:|---:|---:|
| 1..255 | 0 | `index` | `G` |
| 256..510 | 1 | `index - 255` | `255 + G` |

Lua 传给 `SetColorTexture` 时再除以 255。因此第 255 槽是 `(0,255,B)`，第 256 槽是 `(1,1,B)`。不能把全部 510 槽描述成单一 `R=0` 方案，也不能用近似颜色匹配替代字节级判断。

`B` 是负载通道。常见值包括布尔 `0/1`、`count/255`、冷却秒数曲线、类型枚举和生命百分比曲线。具体字段的解释由 `stateBlockGetters` 和领域更新器决定。

## 三类输出

### 1. 顶部主状态行

- Frame：`FuyutsuiColorBars`。
- 510 槽，单槽宽为屏幕宽度除以 510，高度为 1 像素。
- 状态与冷却用普通纹理；定时光环按钮会在同一索引位置覆盖并动态改变 `B`。

### 2. 横向 CountBars

- Frame：`FuyutsuiCountBars`。
- 500 个水平单元，高度 2 像素。
- 依次容纳技能施放/充能计数条与玩家光环层数条，末尾总有 `(200,200,200)` 终点色块。
- 每条按 `maxValue + 3` 预留空间，属于几何扫描协议，不使用顶部 1..510 的索引反解。

### 3. 队伍治疗吸收网格

- Frame：`FuyutsuiHealAbsorbBars`。
- 最多 40 个成员，5 列、8 行；每槽为 1 个成员锚点、100 个条身单元和 1 个终点。
- 玩家锚点值为 1，`party1..4` 为 2..5，`raidN` 为 N；锚点帮助消费端确认槽身份。

详细扫描几何见 [[40-跨项目/01-Shigure-像素生产消费契约]]。

## 核心数据与不变量

- Lua 与 C# 必须使用完全相同的状态分类顺序、光环顺序和法术占槽规则。
- 主行每个有效索引必须在 1..510；超界时 `createTextureByIndex()` 返回 `nil`，不会自动扩容。
- `blocks.spells` 以 SpellID 为键；同一专精内 SpellID 必须唯一，否则后项覆盖前项索引。
- 裸状态键在 `状态/能量/配置开关/items` 之间必须唯一；单位分类因带前缀可以同名。
- 横向条的预留顺序必须与 C# 配置转换和扫描顺序一致。
- 队伍容量必须同时满足 `group.start + memberCount*group.num - 1 <= 510` 与消费端支持的成员数。目前 Shigure 构建固定 30 人，并不覆盖 WoW 团队最多 40 人。
- 旧审计记录只说明历史问题；判断当前契约必须回到上述源文件与当前生成配置。

## 失败模式

1. **状态键覆盖。** 当前死亡骑士专精 1 在“状态”和“能量”都声明裸键“符文”（`class/DeathKnight.lua:27,33`），后写入的索引会覆盖先前映射。
2. **SpellID 覆盖。** 当前唤魔师专精 2 连续两项都使用 `374251`（`class/Evoker.lua:153-154`）；第一槽仍被占用，但 `blocks.spells[374251].index` 指向第二槽，形成永不刷新的洞。
3. **生产/消费布局漂移。** 修改职业表但未重新生成 Shigure 配置，会让同一屏幕坐标被解释为另一个字段。
4. **超出 510。** 普通槽不再绘制；队伍计算索引也可能落到主行之外。
5. **缩放、抗锯齿或色彩处理。** 扫描器依赖精确 RGB 字节，任何 UI 缩放或截图链路颜色变换都可能破坏锚点识别。

## 修改影响

- 改 `LoadPlayerBlocks()` 的任何顺序，都必须同步 [[30-Shigure/03-Shigure-配置合并与GameState构建]] 和配置生成器。
- 改 `EncodeBlockChannels()`、510 槽数量、行高或锚点色，必须同步 [[30-Shigure/02-Shigure-像素扫描与协议解码]]。
- 改 CountBars 预留算法或治疗吸收几何，同样属于跨项目协议变更。
- 新增职业字段前先检查裸键唯一性、SpellID 唯一性、最终 group 末索引及 C# 字段名唯一性。

## 源码索引

- `Fuyutsui/main.lua:39-195`：`ClassBlocks` 展开顺序和 group 起点。
- `Fuyutsui/core/block.lua:8-126`：510 主行、两段索引编码和纹理写入。
- `Fuyutsui/core/block.lua:134-284`：CountBars 预留、绘制和清理。
- `Fuyutsui/core/block.lua`：40 槽治疗吸收网格。
- `Fuyutsui/core/stateblocks.lua:84-291`：状态键规则、getter 路由和写入入口。
- `Fuyutsui/class/DeathKnight.lua:27-33`、`Fuyutsui/class/Evoker.lua:153-154`：当前重复键实例。

## 知识图谱

本页把 [[20-Fuyutsui/01-Fuyutsui-加载与生命周期]] 产生的专精运行时对象映射为视觉协议；字段由 [[20-Fuyutsui/04-Fuyutsui-玩家状态]] 至 [[20-Fuyutsui/09-Fuyutsui-动作条键位扫描]] 的领域逻辑写入，跨进程契约由 [[40-跨项目/01-Shigure-像素生产消费契约]] 约束，最终由 [[30-Shigure/02-Shigure-像素扫描与协议解码]] 消费。
