---
title: Shigure Fuyutsui 配置、宏编辑与同步
summary: 说明以内置 Fuyutsui 为权威源进行 Lua round-trip、config/keymap 生成、SHA-256 游戏部署和串行同步。
aliases:
  - Shigure Lua 转换器
  - Fuyutsui 同步
tags:
  - project/shigure
  - doc/feature
  - area/conversion
project: Shigure
doc_type: feature
status: current
authority: source-derived
up: "[[30-Shigure/00-Shigure-MOC]]"
related:
  - "[[40-跨项目/02-Shigure-ClassBlocks到config同步契约]]"
  - "[[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]]"
source_files:
  - Infrastructure/FuyutsuiAddonSyncService.cs
  - Infrastructure/WowAddonLocator.cs
  - Infrastructure/WowProcessLocator.cs
  - Infrastructure/LuaLiteParser.cs
  - Infrastructure/ClassBlocksStore.cs
  - Infrastructure/ClassMacrosStore.cs
  - Infrastructure/FuyutsuiConfigConverter.cs
  - Infrastructure/FuyutsuiKeymapConverter.cs
  - UI/MainForm.cs
source_symbols:
  - FuyutsuiAddonSyncService.SynchronizeAll
  - FuyutsuiAddonSyncService.SynchronizeFile
  - WowAddonLocator.FindAddOnsDirectory
  - WowProcessLocator.FindFrontmostProcessPath
  - LuaLiteParser.TryExtractAssignedTable
  - ClassBlocksStore.Save
  - ClassMacrosStore.Save
  - FuyutsuiConfigConverter.UpdateFromClassDirectory
  - FuyutsuiKeymapConverter.UpdateFromClassMacros
  - MainForm.UpdateConfigFromProjectCoreAsync
verified_at: 2026-08-10
---

# Shigure Fuyutsui 配置、宏编辑与同步

> [!abstract] AI 快速摘要
> Shigure 始终以 `AppPaths.BaseDirectory/Fuyutsui` 为插件权威源，用受限 Lua 数据解析器重写 ClassBlocks/ClassMacros，再生成 `config/*.json` 和 `keymap/*.json`。启动与“更新配置”会全量部署插件，编辑器保存会部署当前 Lua；部署按 SHA-256 跳过相同文件，不删除游戏额外文件。生成和部署由 MainForm 尾任务队列串行执行，但跨多个 Lua/JSON/游戏文件仍不是事务。

## 图谱位置

- 上级：[[30-Shigure/00-Shigure-MOC]]
- Fuyutsui 状态生产者：[[20-Fuyutsui/03-Fuyutsui-状态块与编码入口]]
- ClassBlocks 契约：[[40-跨项目/02-Shigure-ClassBlocks到config同步契约]]
- ClassMacros 契约：[[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]]

## 范围与非范围

本页覆盖项目源定位、游戏部署目录发现、Lua 数据子集、编辑回写、两类转换器及同步并发。像素实时扫描和最终按键发送分别见 [[30-Shigure/02-Shigure-像素扫描与协议解码]]、[[30-Shigure/08-Shigure-Keymap解析与按键发送]]。

## 项目源与游戏部署目录

项目源固定为 `Path.Combine(AppPaths.BaseDirectory, "Fuyutsui")`。配置页读取 `Fuyutsui/class`，宏页读取 `Fuyutsui/core/classmacros.lua`；两者不再把游戏 AddOns 目录当编辑源。

游戏部署目标按以下过程计算：

1. `WowProcessLocator` 每次读取业务根目录下的 `wow_process.txt`，忽略空行、`#`/`;` 注释，并去掉可选 `.exe` 后缀。
2. 枚举这些名称对应的进程 ID，再按 Windows 顶层窗口 Z 顺序取第一个候选进程的可见窗口。
3. 由该窗口 PID 查询进程 EXE 路径，从 EXE 目录逐级向上寻找 `Interface/AddOns` 或 `Interface`。
4. 找到后把目标固定为其下的 `Fuyutsui`；即使 AddOns/Fuyutsui 尚不存在，也可在部署时创建。若祖先中没有 Interface，则回退到游戏 EXE 同级的预期 `Interface/AddOns`。

`FuyutsuiAddonSyncService` 只允许部署项目源内的文件。全量同步递归枚举源目录；单文件同步先验证路径没有逃出源根。目标同名文件 SHA-256 相同则跳过，不同则覆盖，缺失目录自动创建；游戏目录额外文件不会删除或反向合并。

## LuaLiteParser 的安全与语法边界

解析器只把 Lua 当作数据文本：

- 先用 ordinal `IndexOf` 找赋值名，再找其后的 `=` 和第一个 table；因此同名注释或子字符串可能误命中。
- 支持 `{}` table、键值项、数组项、字符串、十进制/指数数字、true/false/nil 和裸标识符。
- 支持 `--` 行注释、Lua 双方括号块注释和基础引号转义，并捕获条目尾注释。
- 不支持表达式、函数调用、十六进制、完整 Lua 语义或通用 long-bracket 字符串。
- 不执行插件代码，因此不存在通过 Lua 表触发 C# 动态执行的路径。

任何在支持子集之外的合法 Lua，都可能无法解析或在 round-trip 后改变表示。

## ClassBlocks round-trip

- Store 读取指定赋值表，得到专精、状态块和 `spellsList`。
- 只要任一专精含命名 table，整个文档就被判为 modern。
- 保存时只替换源文件中该 table literal，表外文本保留；表内部按 Store 支持的 schema 重新序列化，不承诺保留未知字段或原始格式。
- 保存是直接写回源 Lua，不是临时文件原子替换，也不自动备份。
- 旧稀疏专精会返回空编辑数据；若整个文档不是 modern，Store 拒绝保存。混合 modern/legacy 文件尤其危险：全局可被判 modern，但 legacy 专精仍为空，保存可能造成数据损失。
- `spellsList` 在 ClassBlocks UI 中可编辑索引 1–100 的法术 ID、索引和名称；保存时原位更新对应 Lua 条目，保留未显示的索引 101+ 条目与表内注释。

## ClassBlocks → config

转换器遍历 13 个职业 Lua 文件：缺失文件记录警告并跳过；每个成功文件直接覆盖对应职业 JSON。因此这是可部分完成的批处理。

步骤编号从 1 顺序累加，主要顺序是：

1. 固定通用字段；它们在职业 JSON 中可跳过写入，但仍占步骤。
2. 专精普通状态；目标/焦点嵌套字段带前缀。
3. 玩家、目标和焦点的光环，按 harmful/helpful 固定顺序。
4. 每法术冷却；充能法术再占一行。
5. 每个 spell ID 的 maxCharge/castCount 动作条项。
6. 玩家光环 maxApps 动作条项。
7. 组员块，记录 `start`、每单位字段数 `num` 和相对步骤。

特殊规范化包括：光环必须有 ID；仅锚点/有效性/移动按 bool；旧 `法术失败` 改名 `插入法术`。转换器没有在写出前验证最终步骤不超过 510，同名字段还会覆盖 JSON 键但继续消耗步骤，需人工/契约验证。

## ClassMacros round-trip

- Store 同样只替换目标 table literal，表外文本保留、表内按 canonical 格式重写。
- 支持职业级动态宏数组、静态/特殊宏数组和受支持的尾注释。
- 未建模的表内字段或格式不保证保留。
- 仍是直接覆盖 Lua 文件，无原子替换、备份或跨文件事务。

原始宏结构说明见 [[50-参考资料/CLASSMACROS_AI_Reference_zh-CN]]。

## ClassMacros → keymap

- 固定键池为 26 个左右修饰组合 × 45 个主键 = **1170** 项。主键与 `Fuyutsui/core/macro.lua` 当前列表一致；刻意不含 F4、反引号、`NUMPADENTER`、数字主键和斜杠主键。Lua `macroKind` 与 C# `FuyutsuiKeymapConverter` 必须逐项相同。
- 动态宏项每项预留/消耗 30 个单位位置，然后是静态宏和特殊宏；超过容量会警告并截断。
- 只转换职业级 flat 格式；输入仍含 common/spec 动态宏时直接报错，避免静默丢失。
- party1..4 映射组员槽 2..5；原始 `@player` 动态语义映射组员槽 1；显式中文/player 保留单位 31；raid1..30 映射 1..30。
- dynamic/static 仍按宏文本生成映射，方括号中非 `@` 条件汇总为 macroCondition，尾注释可以覆盖推导出的 spell 名。
- special 不再分析宏正文：尾注释必须作为手工技能名，映射固定为 `unit=0`、空 macroCondition；宏编辑器因此只显示可编辑技能名和完整宏，不显示目标与条件。
- 能识别 stopcasting、castsequence、最后一个 cast 和 item 等受支持形式；不等于完整 WoW 宏解释器。
- 输出包含全部 1170 项，包括空项，以维持索引位置。

## 同步队列和运行时重启

`MainForm` 用 `_configUpdateTail` 形成串行尾任务，避免多个配置保存、转换和部署同时写文件。“更新配置”会生成全部 config/keymap 并全量部署插件；配置/宏编辑器保存仍会重新生成全部 config/keymap，但只部署刚保存的 Lua。随后刷新目录并在已有会话时重启运行时；UI 日志只展示前 20 条转换警告或部署失败。

`OnShown` 在启动运行会话前先单独执行一次全量插件部署；它不重新生成 config/keymap。找不到游戏进程或启动部署失败只记日志，程序继续启动。

启动或重启运行会先等待当前配置队列，确保新会话读取到已排队更新。窗口关闭也等待运行时释放和配置队列完成。但是队列只解决**进程内并发顺序**，不把多个 Lua/JSON 文件变成事务；中途异常之前的写入仍然存在。

## 失败模式与排障

| 症状 | 优先检查 |
|---|---|
| 项目 Fuyutsui 不存在 | 发布目录是否完整包含 `Fuyutsui/`，csproj 的复制规则是否生效 |
| 无法定位游戏部署目录 | `wow_process.txt`、候选进程可见窗口、进程查询权限和实际游戏路径 |
| 项目保存成功但游戏未变化 | 游戏未运行、目标不可写、单文件部署失败，或 WoW 尚未重载插件 |
| Lua 看似合法却解析失败 | 是否用了函数/表达式/hex/long string 等不支持语法；赋值名是否被注释先命中 |
| 保存后注释/排版变化 | 表内部会 canonical 重写；仅表外文本有较强保留保证 |
| 旧 ClassBlocks 页面为空 | legacy sparse 专精不被完整建模；不要保存混合文件 |
| config 部分职业是新、部分是旧 | 13 文件转换非事务，某个文件失败后可能已有部分覆盖 |
| 某状态永远为 0 | 步骤顺序、重复名覆盖、是否超过 510 |
| Keymap 后半被截断 | 动态项每项消耗 30，职业级宏整体超过 1170 |
| 刚保存但运行时仍用旧数据 | 等待同步队列结束并确认会话已重启 |
| 游戏目录手工修改消失 | 游戏副本不是权威源；下次全量或同文件部署会以项目源覆盖 |

## 修改影响

- 改 Lua schema 必须先扩展 parser/store 的受支持模型和 serializer，再允许 UI 保存，否则会丢未知数据。
- 改 ClassBlocks 顺序要同步 Fuyutsui 生产者、config 转换器、StateBuilder 和 510 容量检查。
- 改键池顺序、数量或目标映射要同步 Fuyutsui 动作条扫描、Keymap 转换器和 v3 单位契约。
- 若需要可靠回滚，应为 Lua 和批量 JSON 引入备份/临时文件/事务清单，不能只依赖 UI 队列。
- 改发布或路径规则时必须同时验证 `Shigure.csproj`、随机副本的 `AppPaths.BaseDirectory`、AddonSyncService 和 `wow_process.txt`。

## 源码索引

- `Infrastructure/FuyutsuiAddonSyncService.cs`：项目源校验、SHA-256 全量/单文件部署和逐文件失败记录。
- `Infrastructure/WowProcessLocator.cs`：进程名配置、Z 顺序窗口选择和进程路径查询。
- `Infrastructure/WowAddonLocator.cs`：由目标进程路径推导 `Interface/AddOns`。
- `Infrastructure/LuaLiteParser.cs:90-480`：赋值定位、table/值/字符串/数字/注释子集。
- `Infrastructure/ClassBlocksStore.cs:102-207`：modern 判定、替换保存与 legacy 行为。
- `Infrastructure/ClassMacrosStore.cs:63-354`：宏表加载、直接写回和 canonical 序列化。
- `Infrastructure/FuyutsuiConfigConverter.cs:44-483`：13 职业、步骤顺序、组员与字段规范化。
- `Infrastructure/FuyutsuiKeymapConverter.cs`：26×45、共 1170 个键位的职业级键池、单位映射、宏/注释解析。
- `UI/MainForm.cs:624-867`：启动部署、串行更新尾、转换、全量/单文件部署、刷新和告警。
- `UI/MainForm.cs:781-958`：运行前等待队列并重启会话。

## 知识图谱链接

- 生产者入口：[[20-Fuyutsui/03-Fuyutsui-状态块与编码入口]]
- 状态契约：[[40-跨项目/02-Shigure-ClassBlocks到config同步契约]]
- 按键契约：[[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]]
- 消费端：[[30-Shigure/03-Shigure-配置合并与GameState构建]]、[[30-Shigure/08-Shigure-Keymap解析与按键发送]]
