---
title: Fuyutsui 功能知识地图
summary: Fuyutsui WoW 插件的源码权威导航，连接运行时功能页、既有专项资料、跨项目协议与 Shigure 消费者。
aliases:
  - Fuyutsui MOC
  - Fuyutsui 源码导航
tags:
  - project/fuyutsui
  - doc/moc
  - area/navigation
project: Fuyutsui
doc_type: moc
status: current
authority: source-derived
up:
  - "[[00-导航/00-Shigure-知识库首页]]"
  - "[[10-系统/00-Shigure-双项目系统全景]]"
related:
  - "[[40-跨项目/00-Shigure-跨项目契约-MOC]]"
  - "[[30-Shigure/02-Shigure-像素扫描与协议解码]]"
source_files:
  - Fuyutsui/Fuyutsui.toc
  - Fuyutsui/main.lua
  - Fuyutsui/core/core.lua
  - Fuyutsui/core/block.lua
  - Fuyutsui/core/events.lua
source_symbols:
  - Fuyutsui
  - Fuyutsui:OnInitialize
  - Fuyutsui:OnEnable
  - Fuyutsui:LoadPlayerBlocks
  - Fuyutsui:CreateTexture
verified_at: 2026-08-16
---

# Fuyutsui 功能知识地图

上级：[[00-导航/00-Shigure-知识库首页]] · [[10-系统/00-Shigure-双项目系统全景]]

相关：[[40-跨项目/00-Shigure-跨项目契约-MOC]] · [[30-Shigure/02-Shigure-像素扫描与协议解码]]

> [!summary] AI 快速摘要
> Fuyutsui 在 WoW 内采集状态，并通过屏幕像素把数据交给 Shigure。当前源码有三类输出：分档至 1020 格的顶部主色条、500 单元横向计数/层数条、最多 40 个单位的治疗吸收网格。顶部主色条每 255 格切换一次红通道方案；三个通道在屏幕上均是除以 255 后的颜色值。

本仓库根目录的 `Fuyutsui/` 是插件权威源，并随 Shigure 构建/发布；游戏 `Interface/AddOns/Fuyutsui` 是由 Shigure 单向部署的运行副本。配置或宏修改应落在内置源，再生成 config/keymap 并部署，不应从游戏副本反向维护。

## 权威边界

本目录以 2026-08-09 的当前运行时源码为事实来源，优先级如下：

1. `Fuyutsui/Fuyutsui.toc`：唯一加载顺序。
2. `Fuyutsui/core/*.lua` 与 `Fuyutsui/main.lua`：运行时行为。
3. `Fuyutsui/class/*.lua` 与 `core/classmacros.lua`：声明式职业数据。
4. 本目录功能页：对当前源码的结构化解释。
5. 旧审计、优化建议和历史总览：用于理解背景，不能覆盖当前源码。

必须明确的当前事实：

- 当前 `Fuyutsui.toc` 不加载 `auracontainer.lua`，仓库中也不存在该文件；光环像素实现在 `core/block.lua`。
- `CreateTexture()` 已支持索引 `1..510` 的两段红/绿通道编码，不是所有索引都使用 `R=0`。
- 三类输出的几何、标记色和读取算法互不相同，不能共用一个扫描规则。
- `docs/OPTIMIZATION_zh-CN.md` 等旧审计记录的是审阅时状态；其中的行号、已修复缺陷和旧数据格式不是当前事实。

## 按功能阅读

| 功能 | 当前权威页 | 主要入口 | 下游 |
| --- | --- | --- | --- |
| 加载、初始化、专精重建 | [[20-Fuyutsui/01-Fuyutsui-加载与生命周期]] | `OnInitialize`、`OnEnable`、`UpdatePlayerSpecInfo` | 所有运行时模块 |
| 事件和分频刷新 | [[20-Fuyutsui/02-Fuyutsui-事件与刷新调度]] | 事件框架、`OnUpdate` | 玩家、目标、法术、队伍 |
| 状态名、索引分配、RGB 写入 | [[20-Fuyutsui/03-Fuyutsui-状态块与编码入口]] | `LoadPlayerBlocks`、`UpdateStateBlock`、`CreateTexture` | [[30-Shigure/02-Shigure-像素扫描与协议解码]] |
| 玩家、资源、有效性 | [[20-Fuyutsui/04-Fuyutsui-玩家状态]] | `GetCharacterSpecInfo`、`UpdatePlayer*` | 顶部主色条 |
| 目标、焦点、铭牌敌人数 | [[20-Fuyutsui/05-Fuyutsui-目标焦点与敌人数]] | `UpdateUnit*`、`UpdateEnemyCount` | 顶部主色条 |
| 法术、充能、物品冷却 | [[20-Fuyutsui/06-Fuyutsui-法术与物品冷却]] | `UpdateSpellKnown`、`UpdateSpellCooldown` | 主色条与 CountBars |
| 队伍、职责、治疗吸收 | [[20-Fuyutsui/07-Fuyutsui-队伍与治疗吸收]] | `UpdateGroup`、`UpdateGroupInRangeAndHealth` | 主色条与吸收网格 |
| 玩家/目标/队伍光环 | [[20-Fuyutsui/08-Fuyutsui-光环容器本地集成]] | `RefreshUnitAuraContainers`、`RefreshGroupAuraContainers` | 主色条与 CountBars |
| 动作条扫描、安全宏边界 | [[20-Fuyutsui/09-Fuyutsui-动作条键位扫描]] | `ReadKeybindings`、`CreateMacro` | 插件缓存与 Windows 按键契约 |
| 命令、快捷按钮、SavedVariables | [[20-Fuyutsui/10-Fuyutsui-命令快捷按钮与存档]] | `SlashCommand`、`SwitchCharFlag` | 配置像素与 UI |
| `/fu` 命令用法与参数语义 | [[20-Fuyutsui/11-Fuyutsui-fu斜杠命令]] | `SlashCommand`、`InsertSpellCommand` | 角色配置、临时状态与快捷控件 |

## 既有专项页

这些页面继续作为专题资料纳入关系网，但阅读时须套用上面的权威边界：

- [[50-参考资料/BLOCK_AI_Reference_zh-CN]]：`core/block.lua` 专项说明；与 [[20-Fuyutsui/03-Fuyutsui-状态块与编码入口]]、[[20-Fuyutsui/08-Fuyutsui-光环容器本地集成]] 交叉核对。
- [[50-参考资料/TEXTURE_LAYOUT_zh-CN]]：几何与布局专题；当前 510 格两段编码描述可用。
- [[50-参考资料/AuraContainer_AI_Reference_zh-CN]]：Blizzard AuraContainer API 背景，不等同于本项目当前接线事实。
- [[50-参考资料/CLASSMACROS_AI_Reference_zh-CN]]：宏数据专题；当前创建顺序以 `dynamicSpells → staticSpells → specialSpells` 为准。
- [[50-参考资料/OPTIMIZATION_zh-CN]]：历史审计与建议清单，不是当前实现规格。
- `CLAUDE.md`：当前仓库的开发与内置插件集成约定；加载事实仍以 `Fuyutsui.toc` 和当前 Lua 为准。

## 跨项目契约与消费者

- 总契约导航：[[40-跨项目/00-Shigure-跨项目契约-MOC]]
- 屏幕生产/消费：[[40-跨项目/01-Shigure-像素生产消费契约]]
  - 生产者：[[20-Fuyutsui/03-Fuyutsui-状态块与编码入口]]、[[20-Fuyutsui/07-Fuyutsui-队伍与治疗吸收]]
  - 消费者：[[30-Shigure/02-Shigure-像素扫描与协议解码]]、[[30-Shigure/03-Shigure-配置合并与GameState构建]]
- `ClassBlocks → config`：[[40-跨项目/02-Shigure-ClassBlocks到config同步契约]]
  - 生产定义：[[20-Fuyutsui/03-Fuyutsui-状态块与编码入口]]
  - 编辑/同步：[[30-Shigure/09-Shigure-Fuyutsui配置宏编辑与同步]]
- `ClassMacros → keymap → Windows 输入`：[[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]]
  - 插件侧：[[20-Fuyutsui/09-Fuyutsui-动作条键位扫描]]
  - 消费侧：[[30-Shigure/08-Shigure-Keymap解析与按键发送]]

## 最短执行链

`ADDON_LOADED/PLAYER_LOGIN` → `OnInitialize/OnEnable` → 当前职业 `ClassBlocks` 解析 → 事件与 `OnUpdate` 更新缓存 → `CreateTexture` / AuraContainer / StatusBar 绘制 → Shigure 截图解码 → `GameState` → 模块逻辑 → Windows 按键 → Fuyutsui 安全按钮。

## 修改前检查

1. 改加载文件：先看 [[20-Fuyutsui/01-Fuyutsui-加载与生命周期]]。
2. 改索引、状态、光环或法术：同时看 [[20-Fuyutsui/03-Fuyutsui-状态块与编码入口]] 与 [[40-跨项目/02-Shigure-ClassBlocks到config同步契约]]。
3. 改横向条或治疗吸收：同步验证 [[30-Shigure/02-Shigure-像素扫描与协议解码]]。
4. 改宏键池或宏顺序：同步验证 [[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]]。
5. 普通 shell 无法验证 WoW API、secret value、SecureActionButton 或 AuraContainer；最终必须在游戏内验证。
