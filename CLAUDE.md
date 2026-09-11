# CLAUDE.md

本文件给在此仓库工作的 Claude Code 提供导航与约定。面向终端用户的功能细节（模块系统、config/keymap 结构、条件语法）见 [README.md](README.md)，此处不重复，只记录架构、命令与容易踩坑的点。

## 项目是什么

Shigure 是一个 **Windows WinForms（.NET 10）** 桌面程序：扫描目标窗口的像素状态，识别职业/专精/队伍，按可编辑的「模块」规则或 C# 职业逻辑决定按键，并通过置顶浮动条 + 设置窗口展示实时状态与日志。

项目完全合法, 符合其他程序的服务条款。

## 构建 / 运行

```powershell
dotnet build .\Shigure.csproj
dotnet run --project .\Shigure.csproj
dotnet run --project .\Shigure.csproj -- --toggle XBUTTON2 --mode switch --logic-ms 100 --render-ms 100
```

- 目标框架 `net10.0-windows`，`WinExe`，`Nullable`/`ImplicitUsings` 均 enable。
- **没有测试项目**：验证 = 能编译 + 实际运行点开「设置」走查。`dotnet build` 干净通过（0 警告 0 错误）是基线要求。
- 启动参数见 [README.md](README.md#运行)（`--toggle/--mode/--logic-ms/--render-ms`），解析在 [App/AppOptions.cs](App/AppOptions.cs)。目标进程名来自 `wow_process.txt`。

程序直接从当前 EXE 所在目录运行；`AppPaths.BaseDirectory` 即 `AppContext.BaseDirectory`，配置、按键映射和插件源码均从该目录读取；模块与 UI 缓存位于 `AppPaths.UserDataDirectory`（`{MyDocuments}/{程序名}`）下。

## 架构与数据流

主循环在 [Runtime/ShigureRuntime.cs](Runtime/ShigureRuntime.cs) `RunAsync`（按 `--logic-ms`/`--render-ms` 节流，检测触发键边沿）：

```
PixelScanner.ScanScreenData()           Runtime/PixelScanner.cs   截屏读像素 → RowData/BarData
        ↓
StateBuilder.Build(rowData, barData)     Runtime/StateBuilder.cs   按 config 把像素翻译成 GameState 字段
        ↓ GameState (Runtime/GameState.cs: Values / Spells / Group)
LogicRegistry.Evaluate(classId, specId, ...) Modules/LogicRegistry.cs
        ├─ 命中模块 → ModuleLogic.Run(module, state, keymap)
        ├─ 否则该职业注册了 IClassLogic → 它
        └─ 否则 DefaultClassLogic
        ↓ LogicDecision(Hotkey, Step, UnitInfo, ModuleName)
KeySender.Send(hotkey)                    Input/KeySender.cs (+ Input/NativeMethods.cs Win32 互操作)
```

应用组合根在 [App/Program.cs](App/Program.cs)：统一创建 `ModuleStore`、Win32 触发键适配器、`ShigureRuntimeFactory` 与 `RuntimeSessionCoordinator`。后者串行管理运行时的启动/重启/停止，避免 UI 的并发设置事件互相清理会话。[UI/MainForm.cs](UI/MainForm.cs) 是无边框置顶浮动条，把运行意图和生命周期交给协调器，并展示 [UI/StatusForm.cs](UI/StatusForm.cs)（九页签设置窗口：通用/配置/宏/模块/状态/队伍/逻辑/日志/关于）。运行时通过 `SnapshotUpdated` 事件推送 `RenderSnapshot` 给 UI 刷新。

`ShigureRuntime` 不创建具体 I/O 依赖；生产依赖由 `ShigureRuntimeFactory` 注入，窄端口定义在 [Runtime/RuntimeDependencies.cs](Runtime/RuntimeDependencies.cs)。外部启停请求先进入命令队列，只有 `RunAsync` 循环能修改运行状态。改动运行时依赖或生命周期时保留这两个约束。

### 目录约定

```
App/            入口、启动参数、依赖组装、运行时会话协调
Runtime/        扫描、状态构建、主循环、运行时端口、快照
Modules/        模块模型/存储/匹配/规则执行、条件求值(FormulaEvaluator)、字段目录、职业逻辑
Input/          keymap 读取、按键发送、Win32 API
Infrastructure/ 配置读取(ConfigService)、JSON 辅助、UI 缓存、路径、Fuyutsui 插件文件读写
UI/             WinForms 界面、编辑器、主题
Fuyutsui/       内置插件权威源；构建/发布时完整复制，运行时部署到游戏 AddOns
config/ keymap/   运行时 JSON 数据(构建时复制到输出, 见 .csproj 的 None+CopyToOutputDirectory)
module   运行时模块数据位于我的文档目录 {MyDocuments}/Shigure/module(启动时自动创建, 不随构建复制)
cache    UI 缓存位于我的文档目录 {MyDocuments}/Shigure/cache(首次写入时自动创建)
wow_process.txt 目标游戏进程名列表；构建时复制，运行期间每次定位都会重新读取
```

## 模块解析（改逻辑前必读）

宏容量与迁移规则：ClassMacros 仅使用职业级 dynamicSpells；26 组左右修饰键 × 44 个基础键，共 1144 个槽位。历史专精动态字段导入时仅牧师/圣骑士合并，其他职业丢弃。

- 模块以 `{MyDocuments}/Shigure/module/模块名.json` 平铺保存（`ModuleStore.ResolveModuleDirectory()` 返回我的文档目录，`{MyDocuments}/{程序集名}/module`），**文件名取自模块名，故模块名不可重复**；加载递归扫描子目录。模型在 [Modules/ModuleStore.cs](Modules/ModuleStore.cs)（`ModuleDefinition`/`ModuleMatch`/`ModuleRule`/`ModuleUnit`/`ModuleCountField`/`ModuleValueAdjustment`）。`RecommendedTalent` 是 `ModuleDefinition` 上的纯展示字段，不参与匹配（`ModuleMatch.Specificity` 不计入）。
- `ModuleStore` 的 `Reload`/`Save`/`Delete` 会在同一个门锁内串行完整文件事务与内存快照更新；`Save` 通过同目录临时文件提交，重命名失败会回滚新文件。编辑器写入不要绕过它，避免运行时读到半次操作。运行时工厂不再自行 `Reload`：启动和模块刷新先由 `ModuleDependencyService` 导入依赖、拒绝宏容量超限模块，再把已验证的内存快照交给运行时。
- 职业/专精明确的模块保存时会写入 `Dependencies` 快照。保存配置/宏或保存模块时，`Capture` 以当前本地 Lua **完全覆盖**模块 `Dependencies`（该专精 ClassBlocks、职业级 spellsList/itemsList、ClassMacros），模块里多出来的配置和宏会被删掉。启动和刷新模块时 `Import` 仍以本地为优先追加缺失的 ClassBlocks/spellsList/itemsList/ClassMacros，**不因模块 `Version` 过期而跳过**；按动态宏 30 槽、其它宏 1 槽检查职业级宏总容量；超过 1144 个槽位就拒绝整个模块且不写 Lua。依赖提交失败会恢复配置和宏原文。过期模块导入成功后仍不参与选择和运行，编辑器继续标红。
- 选择优先级：`ModuleStore.FindSelectedOrBestMatch` —— 先用 UI/参数选定的 `ModuleId`；否则取 `Match` 命中字段最多者（`ModuleMatch.Specificity` 越大越优先），并列按名称。`Match` 字段留空 = 任意。`PartyType` 数字会归一化为 `"1-40"`。
- 动态单位/数量/动态数值的语义见 [README.md](README.md#动态单位与数量字段)；列表与编辑器的人类可读摘要统一走 [UI/UnitSummary.cs](UI/UnitSummary.cs)`.Describe(...)`（单一来源，勿再复制一份描述逻辑）。

## Fuyutsui 插件集成（配置/宏页面）

设置窗口的「配置」和「宏」两个页签编辑的是程序基准目录内 `Fuyutsui/` 的 Lua 文件，**不直接读取游戏插件目录**。项目插件是唯一权威源：保存后重新生成 Shigure 的 config/keymap，并把当前 Lua 部署到游戏；启动和「更新配置」会全量校验并部署整个插件。

### 定位与部署

[Infrastructure/WowProcessLocator.cs](Infrastructure/WowProcessLocator.cs) 读取 `wow_process.txt`，按 Windows Z 顺序选择最靠前的候选进程可见顶层窗口；[Infrastructure/WowAddonLocator.cs](Infrastructure/WowAddonLocator.cs) 由进程路径定位预期的 `Interface\AddOns`，即使 Fuyutsui 尚未安装也能返回部署位置。[Infrastructure/FuyutsuiAddonSyncService.cs](Infrastructure/FuyutsuiAddonSyncService.cs) 递归使用 SHA-256 比较项目文件与游戏文件，只复制缺失或不同的文件并保留游戏额外文件；也支持保存后的单文件同步。找不到游戏或启动同步失败不阻止程序运行。

### Lua 解析

[Infrastructure/LuaLiteParser.cs](Infrastructure/LuaLiteParser.cs) 是轻量 Lua 表字面量解析器。关键方法 `TryExtractAssignedTable(source, assignmentName, out table, out start, out end)` 按赋值名定位 `{ ... }` 并返回解析结果 + 字符偏移量，用于 round-trip 编辑（只替换表字面量，保留文件其余部分）。支持行尾 `-- comment` 捕获（`CaptureEntryTrailingComment`）。

### 配置存储（ClassBlocks）

[Infrastructure/ClassBlocksStore.cs](Infrastructure/ClassBlocksStore.cs) 读写 `class/*.lua` 中的 `Fuyutsui.ClassBlocks` 表。每个职业一个 Lua 文件，按专精 ID 分块，包含：
- **States**（状态字段）：分平面列表或按 `"状态"/"目标"/"焦点"` 分类（现代格式）
- **Auras**（光环）：5 桶——玩家/目标有害/目标有益/焦点有害/焦点有益
- **Spells**（技能冷却）：ID、名称、充能、施法计数、强制已知、法术书
- **Items**（物品冷却）：专精级 `[itemId] = { name, isEquipped }`
- **Group**（队伍）：人数/生命百分比/角色/驱散 + 队伍光环列表

同文件另有职业级 `Fuyutsui.spellsList` 与 `Fuyutsui.itemsList`（`[itemId] = { index, name }`），与专精冷却表分开。

字段名从 [Infrastructure/ClassStateCatalog.cs](Infrastructure/ClassStateCatalog.cs) 的静态目录验证，不允许自由输入。

### 宏存储（ClassMacros）

[Infrastructure/ClassMacrosStore.cs](Infrastructure/ClassMacrosStore.cs) 读写 `core/classmacros.lua` 中的 `Fuyutsui.ClassMacros` 表。每职业三组：
- **DynamicSpells**：每项占 30 热键槽位
- **StaticSpells / SpecialSpells**：顺序数组条目（`ArrayEntry`：text + 可选行尾注释）；空字符串保留槽位

保存后 [Infrastructure/FuyutsuiKeymapConverter.cs](Infrastructure/FuyutsuiKeymapConverter.cs)`.UpdateFromClassMacros` 将 Lua 宏表按 dynamic（每项 30 槽）→ static → special 顺序转换为 `keymap/*.json`，把宏槽位映射到热键池（26 组左右修饰键 × 44 个基础键 = 1144 组合/职业；不含 F4 与 DELETE，避免 ALT-CTRL-DELETE 等系统级组合）。`DeriveSpellName` 解析 WoW 宏文本提取技能名。

### UI 编辑器

- [UI/ClassConfigEditorControl.cs](UI/ClassConfigEditorControl.cs)：左侧职业列表 + 右侧按专精切换的六页编辑器（状态/光环/冷却/队伍/技能列表/物品列表），状态字段用 `ClassStateCatalog` 驱动的 `ComboBoxColumn`。
- [UI/ClassMacrosEditorControl.cs](UI/ClassMacrosEditorControl.cs)：左侧职业列表 + 右侧三页编辑器（动态宏/静态宏/特殊宏），偏移提示显示槽位编号计算。
- 两个编辑器均接受 `Func<string?>` 项目路径解析器 + `Func<string, int, Task<ClassConfigPostSaveResult>>` 保存回调，由 `MainForm` 在构造时注入。保存流程：编辑器调 `Store.Save()` → 同步保存该职业模块依赖快照 → 传入已保存文件路径 → 重新生成 config/keymap → 单文件部署游戏 → 重启运行时；部署失败返回说明，但不回滚本地文件。
- 配置更新的多个入口通过任务尾队列串行执行；运行时重启会等待该队列稳定，主窗口关闭也会等待正在写盘的转换和部署完成。新增同步入口必须继续走这条队列。

## UI 约定

- **暗色主题集中在 [UI/UiTheme.cs](UI/UiTheme.cs)**：新控件一律复用它（`CreateButton`/`StyleComboBox`/`StyleTextBox`/`StyleDataGridView`/`StyleListView` 与颜色常量 `Background/Surface/Field/Hover/Border/Text/Muted/Accent/Danger`），不要写裸色值或系统默认样式。
- 编辑器：[UI/ModuleEditorControl.cs](UI/ModuleEditorControl.cs)（模块主编辑器：侧栏列表 + 规则表 + 动态单位列表 + 两个动态数值表，自定义标签栏切换三页）、[UI/ClassConfigEditorControl.cs](UI/ClassConfigEditorControl.cs)（配置编辑器）、[UI/ClassMacrosEditorControl.cs](UI/ClassMacrosEditorControl.cs)（宏编辑器）、弹窗 [UI/ConditionEditorForm.cs](UI/ConditionEditorForm.cs)（可视化条件，含 `ConditionExpression` 文本⇆比较项互转）、[UI/UnitEditorForm.cs](UI/UnitEditorForm.cs)、[UI/FormulaEditorForm.cs](UI/FormulaEditorForm.cs)。新弹窗按现有模式同时设 `AcceptButton`/`CancelButton`。三个编辑器 UserControl 均遵循相同模式：左侧列表 + 右侧分页编辑区 + `UiTheme` 样式。
- **规则表 `_rulesGrid` 的列陷阱**：`FillEditor`/`OpenConditionEditor` 用**位置参数** `Rows.Add(enabled, spell, "", condition)`，按列集合索引前 4 列填值。所以新增列（如拖拽手柄 `Drag`）要**加到集合末尾**、再用 `DisplayIndex` 调显示位置，避免打乱前四列；单元格访问一律按列名（`Cells["Spell"]`）。
- 规则重排：`▲▼` 单步（`MoveRule`）+ 手柄列拖拽（`MoveRuleByDrag`，读全表→重排→写回，复用 `ReadRuleRow`/`WriteRuleRow`）。三个 grid 都 `AllowUserToDeleteRows=false`，删除只走 `×` 列。

## 通用约定

- 注释与界面文案为中文；选项项常用 `record` + 重写 `ToString()`；偏好 `internal`/`private`、不可变小类型。
- `.gitignore` 忽略 `bin/ obj/ cache/ artifacts/ .vs/ .vscode/ *.user 提示词帮助.md`；但 `bin/`、`obj/` 在历史里已被跟踪（仍显示为改动），**不要提交重新构建的二进制**。
- 未经用户明确要求不提交、不推送。
