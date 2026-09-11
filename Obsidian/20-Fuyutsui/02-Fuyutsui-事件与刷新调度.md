---
title: Fuyutsui 事件与刷新调度
summary: 说明统一事件分发、领域事件链、每帧/0.2 秒/1 秒分频刷新，以及不经 OnUpdate 的输出路径。
aliases:
  - Fuyutsui 事件调度
  - Fuyutsui OnUpdate
tags:
  - project/fuyutsui
  - doc/feature
  - area/runtime
project: Fuyutsui
doc_type: feature
status: current
authority: source-derived
up:
  - "[[20-Fuyutsui/00-Fuyutsui-MOC]]"
related:
  - "[[20-Fuyutsui/01-Fuyutsui-加载与生命周期]]"
  - "[[20-Fuyutsui/04-Fuyutsui-玩家状态]]"
source_files:
  - Fuyutsui/core/core.lua
  - Fuyutsui/core/events.lua
  - Fuyutsui/main.lua
source_symbols:
  - Fuyutsui:RegisterEvent
  - Fuyutsui:StartFrameUpdates
  - Fuyutsui:OnUpdate
  - Fuyutsui:GROUP_ROSTER_UPDATE
  - Fuyutsui:UNIT_SPELLCAST_SUCCEEDED
verified_at: 2026-08-09
---

# Fuyutsui 事件与刷新调度

上级：[[20-Fuyutsui/00-Fuyutsui-MOC]]

相关：[[20-Fuyutsui/01-Fuyutsui-加载与生命周期]] · [[20-Fuyutsui/04-Fuyutsui-玩家状态]]

> [!summary] AI 快速摘要
> 一个原生 Frame 接收所有插件事件，并按事件名调用 `Fuyutsui[event]`。`OnUpdate` 每帧更新读条和一个队伍成员；累计超过 0.2 秒更新冷却、辅助、符文、距离、敌人数和物品；累计超过 1 秒更新战斗时间和天启骑士数量。AuraContainer 自己响应光环变化，不由该 `OnUpdate` 轮询。

## 范围

本页负责“什么时候刷新”。各字段如何计算分别见：[[20-Fuyutsui/04-Fuyutsui-玩家状态]]、[[20-Fuyutsui/05-Fuyutsui-目标焦点与敌人数]]、[[20-Fuyutsui/06-Fuyutsui-法术与物品冷却]]、[[20-Fuyutsui/07-Fuyutsui-队伍与治疗吸收]]。

不覆盖 AuraContainer 内部事件调度，见 [[20-Fuyutsui/08-Fuyutsui-光环容器本地集成]]。

## 事件框架

姓名板事件 `NAME_PLATE_UNIT_ADDED` / `NAME_PLATE_UNIT_REMOVED` 维护固定的 `nameplate1..20` 槽位；0.2 秒刷新周期同步更新敌对姓名板的生命值、距离和配置光环像素。

`core/core.lua:6-19` 创建 `FuyutsuiEventFrame` 并暴露注册/注销包装。分发器在 `core/core.lua:190-220`：

```text
WoW event
  → FuyutsuiEventFrame.OnEvent
  → handler = Fuyutsui[event]
  → handler(Fuyutsui, event, ...)
```

没有同名处理函数的已注册事件会被安全忽略。当前 `UNIT_AURA`、`SPELL_UPDATE_CHARGES` 等在总事件框架注册，但没有同名领域处理函数；前者由 AuraContainer 接管，后者还会由具体横向条 Frame 自行接收。

## 事件驱动链路

| 事件族 | 主要动作 | 输出/下游 |
| --- | --- | --- |
| 世界/区域 | 刷新地图、英雄天赋、军备、坐骑、延迟重建队伍 | 玩家与环境状态 |
| 天赋 | 清主色条并重建专精、法术、宏、队伍 | 几乎所有输出 |
| 生死/坐骑/移动/聊天焦点 | 重算玩家有效性 | `有效性`、`移动` |
| 战斗开始/结束 | 记录战斗布尔和开始时间 | 1 秒档的战斗时间 |
| 玩家施法/引导/蓄力 | 维护施法状态、目标、治疗预估、插入法术、坐骑施法 | 每帧读条与技能/目标像素 |
| 生命/能量 | 更新玩家、目标、焦点或队伍死亡状态 | 主色条；吸收条另有自己的 Frame |
| 队伍变化 | 1 秒防抖后重建 `group`/`groupList`、人数、类型 | 队伍像素、光环、吸收网格 |
| 目标/焦点变化 | 刷新完整单位信息并重绑光环过滤 | 单位状态和 AuraContainer |
| 姓名板/仇恨 | 维护敌人缓存与威胁计数 | 0.2 秒敌人数输出 |
| 按键/法术书/动作条网格 | 延迟重扫动作条 | `Fuyutsui.keybindings` |
| 过场/影片结束 | 1 秒后重新绑定所有光环 SpellID 过滤 | AuraContainer |

## 施法状态机

`core/events.lua:81-188` 分开处理：

- `UNIT_SPELLCAST_SENT`：尝试把目标名称映射到 `group` 成员索引。
- `START/STOP`：维护 `casting`、治疗预估、坐骑施法和当前技能。
- `CHANNEL_START/STOP`：维护 `channeling` 与当前技能。
- `EMPOWER_START/STOP`：维护 `empowering` 与当前技能。
- `SUCCEEDED`：更新喝水、清除已成功的插入法术；两个特殊法术触发完整条重建。

读条进度并不在事件到来时计算，而是在每帧调用 Duration 对象的曲线绑定结果。

## `OnUpdate` 分频

`StartFrameUpdates()`（`core/events.lua:406-414`）只创建一个转发 Frame。

### 每帧

`core/events.lua:419-423`：

- 玩家施法、引导、蓄力和蓄力阶段。
- 目标/焦点施法与可打断状态。
- 每帧只轮询一个队伍成员的血量、职责和范围，然后轮转索引。

### 超过 0.2 秒

`core/events.lua:425-435`：

- 所有当前已知法术的冷却和充能回充。
- 官方一键辅助推荐。
- 符文。
- 目标/焦点距离。
- 姓名板敌人数与威胁分组。
- 物品冷却。

计时器达到阈值后直接归零，不保留超出的 elapsed；卡顿时不会补跑漏掉的周期。

### 超过 1 秒

`core/events.lua:437-442`：战斗时长和天启骑士数量。

### 不走总 `OnUpdate`

- 玩家、目标、焦点和队伍光环：Blizzard AuraContainer。
- 法术层数/施法次数 StatusBar：各 bar 自己监听 `SPELL_UPDATE_USES`、`PLAYER_ENTERING_WORLD`、`SPELL_UPDATE_CHARGES`。
- 治疗吸收 StatusBar：`FuyutsuiHealAbsorbBars` 自己监听生命/治疗预测事件。
- 配置 delay：独立 `C_Timer.NewTimer`。

## 核心数据与不变量

- 事件处理函数签名必须保留 `self, event, ...` 语义；漏掉 event 占位会错位读取参数。
- 高频函数应只写自己拥有的缓存/像素，避免每帧全表重建。
- `timeElapsed` 与 `timeElapsed1` 是独立累计器。
- 队伍每帧轮转依赖 `core/group.lua` 的局部 `updateIndex` 有效。
- AuraContainer 不应再被改成 `UNIT_AURA + OnUpdate` 的自建光环状态机。
- secret value 必须在用于表索引、字符串比较、算术或打印前处理；当前只在部分施法/事件参数上有显式保护。

## 失败模式与风险

1. **队伍缩编可能卡住轮转。** `updateIndex` 在重建队伍时不归一；若它大于新的 `#groupList`，`UpdateGroupInRangeAndHealth()` 会在递增前直接返回。详见 [[20-Fuyutsui/07-Fuyutsui-队伍与治疗吸收]]。
2. **战斗开始时间可能为空。** 初始 `UpdatePlayerCombat()` 只写战斗布尔；若启用时已经在战斗且没有收到 `PLAYER_REGEN_DISABLED`，1 秒档可能用 nil 做减法。
3. **冷却事件包含调试输出。** `SPELL_UPDATE_COOLDOWN` 对每个新见 spellID 打印一次链接，却不直接更新冷却；实际冷却依赖 0.2 秒轮询。
4. **空处理事件增加认知噪音。** `SPELL_UPDATE_USES`、范围检查、时间线事件等存在空函数；不能因“已注册”就推断功能已实现。
5. **字符串本地化依赖。** `UI_ERROR_MESSAGE` 只匹配中文“目标不在视野中”，其他客户端不会触发视野退避。
6. **定时器回调跨状态。** 队伍、英雄天赋、光环重绑和专精重建均有延迟回调；回调执行时应读取当前状态，避免捕获旧专精对象。

## 修改影响

- 新增事件前判断能否使用现有 0.2/1 秒档；每帧路径对战斗性能影响最大。
- 改施法事件参数时核对当前 WoW API 签名和 secret value。
- 改刷新频率会改变 Shigure 看到的状态延迟，需同步 [[40-跨项目/01-Shigure-像素生产消费契约]]。
- 改队伍轮询必须同时验证缩编、扩编、离队和 30 人边界。
- 改光环刷新策略前先读 [[50-参考资料/AuraContainer_AI_Reference_zh-CN]] 与 [[20-Fuyutsui/08-Fuyutsui-光环容器本地集成]]。

## 源码索引

- `Fuyutsui/core/core.lua:6-19`：事件注册包装。
- `Fuyutsui/core/core.lua:86-147`：注册清单。
- `Fuyutsui/core/core.lua:188-220`：启动事件与统一分发。
- `Fuyutsui/core/events.lua:9-405`：领域事件处理。
- `Fuyutsui/core/events.lua:406-443`：OnUpdate 转发与分频。
- `Fuyutsui/core/block.lua:147-247`：横向条自有事件。
- `Fuyutsui/core/block.lua:449-457`：治疗吸收自有事件。

## 知识图谱

本页从 [[20-Fuyutsui/01-Fuyutsui-加载与生命周期]] 接收启用入口，向 [[20-Fuyutsui/04-Fuyutsui-玩家状态]]、[[20-Fuyutsui/05-Fuyutsui-目标焦点与敌人数]]、[[20-Fuyutsui/06-Fuyutsui-法术与物品冷却]] 和 [[20-Fuyutsui/07-Fuyutsui-队伍与治疗吸收]] 分发刷新时机；最终输出由 [[30-Shigure/02-Shigure-像素扫描与协议解码]] 消费。
