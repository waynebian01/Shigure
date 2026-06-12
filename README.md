# Shigure

Shigure 是一个 Windows WinForms 桌面程序，用于扫描目标游戏窗口状态、根据职业 keymap 选择按键，并把实时状态、队伍信息、逻辑输出和运行日志集中显示在 UI 中。

项目已经从旧名称 `Fuyutsui_C#` 迁移为 `Shigure`，当前为根目录单项目结构，入口项目文件是 `Shigure.csproj`。

## 当前界面

- 置顶浮动条：无边框小窗口，显示 Shigure 标题和逻辑状态，右侧有 `开关`（文字在 `开启`/`关闭` 间切换）、`设置`、`✕` 三个按钮；窗口可拖动、可从边缘缩放，启动后会自动启动运行循环。
- 设置窗口：点击 `设置` 打开，集中包含 `通用`、`模块`、`状态`、`队伍`、`逻辑`、`日志`、`关于` 七个页签。
- 通用页：可设置触发键和发送模式。点击触发键按钮后按下目标键即可录入，支持键盘按键、`XBUTTON1`、`XBUTTON2`，不支持 `ALT`。
- 模块页：左侧列出全部模块，右侧可新建、编辑、删除模块，并编辑模块名称、启用状态、匹配条件（职业 / 专精 / 队伍类型 / 英雄天赋）和判断规则。规则表的“技能”“目标”列为下拉菜单（选项来自当前选中职业的 keymap），“条件”列为只读，点击后弹出可视化条件编辑器。
- 状态页：显示当前匹配的模块名以及扫描后构建出的状态字段，右侧并列显示 `spells` 技能状态。
- 队伍页：显示 `group` 队伍成员摘要。
- 逻辑页：显示模块或职业逻辑返回的诊断信息（命中条件、动作技能、动作按键等）。
- 日志页：记录启动、停止、职业识别、模块匹配、逻辑状态变化、步骤变化和异常信息。
- 关于页：显示产品名、版本、运行目录、模块目录和配置文件路径。

触发键、发送模式的修改会立即重启运行循环。模块会保存到 `module` 文件夹，修改并保存后同样会重启运行循环以加载最新模块。

## 运行环境

- Windows
- .NET 10 SDK

## 运行

在仓库根目录运行：

```powershell
dotnet run --project .\Shigure.csproj
```

也可以传入启动参数：

```powershell
dotnet run --project .\Shigure.csproj -- --window 魔兽世界 --toggle XBUTTON2 --mode switch --logic-ms 100 --render-ms 100
```

参数说明：

- `--window`：目标窗口标题，默认 `魔兽世界`。
- `--toggle`：触发键，默认 `XBUTTON2`。
- `--mode`：发送模式，支持 `switch`、`click`、`hold`。
- `--logic-ms`：逻辑循环间隔，默认 `100`，最小 `50`。
- `--render-ms`：UI 刷新间隔，默认 `100`，最小 `100`。

发送模式：

- `switch`：按一次触发键开启逻辑，再按一次关闭。
- `click`：按一次触发键只执行一轮逻辑。
- `hold`：按住触发键时运行，松开后停止。

## 构建

```powershell
dotnet build .\Shigure.csproj
```

应用图标来自：

```text
Assets\shigure-emblem-arasaka.ico
```

## 项目结构

```text
Shigure.csproj          项目文件，复制 Assets/config/keymap/module 到输出目录
Program.cs              程序入口，校验 Windows 平台并启动 MainForm
AppOptions.cs           启动参数解析与发送模式定义
MainForm.cs             置顶浮动条、运行控制、触发键录入、窗口缓存
StatusForm.cs           设置与状态诊断窗口（七个页签）
ShigureRuntime.cs       扫描、状态构建、逻辑运行、按键发送主循环
PixelScanner.cs         目标窗口像素扫描（顶部行数据与左侧标记行数据）
StateBuilder.cs         将扫描结果按 config 转换为 GameState
GameState.cs            运行状态模型（状态字段、spells、group）
RenderSnapshot.cs       传递给 UI 的不可变状态快照
ConfigService.cs        读取 config.json 并按职业/专精合并状态配置
KeymapService.cs        按职业读取并选择 keymap，提供技能到按键的查询
KeySender.cs            向目标窗口发送按键（支持组合键与鼠标侧键）
ClassNames.cs           职业、专精、英雄天赋 ID 到名称的映射
LogicRegistry.cs        模块优先、C# 职业逻辑回退的逻辑调度
ModuleStore.cs          模块 JSON 存储、匹配、规则执行与条件求值
ModuleEditorControl.cs  模块 UI 编辑器
ConditionFieldCatalog.cs 从 config.json 构建条件编辑器的字段目录
ConditionEditorForm.cs  可视化条件编辑弹窗与表达式双向转换
KeymapCatalog.cs        从职业 keymap 构建技能/目标下拉目录
UiTheme.cs              深色主题、窗口特效和控件样式
UiCacheStore.cs         窗口位置/大小与上次触发键的缓存读写
NativeMethods.cs        Win32 API 互操作声明
JsonHelpers.cs          JsonNode 取值辅助方法
Assets\                 图标与品牌资源
keymap\                 各职业按键映射
module\                 UI 可编辑模块
```

## 配置文件

- `config.json`：状态扫描配置和职业 keymap 引用。
- `keymap/*.json`：职业按键映射，运行时按职业 ID 选择。
- `Assets/*.ico|*.png|*.svg`：程序图标和品牌资源，构建时会复制到输出目录。
- `cache/window-state.json`：UI 缓存，保存置顶浮动条位置、设置窗口位置/大小和上次触发键。

### config.json 结构

`config.json` 描述了如何把扫描到的像素数据翻译成状态字段。每个字段是一个对象：

- `step`：在顶部行数据中的列索引；特殊值 `"bar"` 表示从左侧标记行数据读取，并配合 `bar` 字段指定段索引。
- `type`：字段类型，支持 `int`、`bool`、`string`。

顶层包含 `锚点`、`职业`、`专精` 等基础字段和一个 `state` 公共状态块；其余以职业 ID 为键（如 `"1"`、`"2"`），每个职业下：

- `keymap`：该职业使用的 keymap 文件名。
- 以专精 ID 为键（如 `"1"`、`"2"`、`"3"`）的对象，包含该专精特有的字段、`spells` 技能块和可选的 `group` 队伍块。

运行时 `ConfigService` 会把 `锚点/职业/专精/state` 与当前 `职业/专精` 对应的配置合并，再由 `StateBuilder` 构建出 `GameState`。`group` 块用 `start`（起始列）和 `num`（每个成员占用的列数）描述队伍成员数据的排布。

## 模块系统

模块用于把“识别到的角色环境”和“要执行的判断逻辑”拆成可编辑 JSON。运行时每轮识别到 `职业`、`专精`、`队伍类型`、`英雄天赋` 后，会在 `module` 文件夹中寻找匹配模块。

匹配字段：

- `ClassId`：职业 ID，对应状态字段 `职业`。
- `SpecId`：专精 ID，对应状态字段 `专精`。
- `PartyType`：队伍类型，对应状态字段 `队伍类型`。模块页使用下拉菜单：`0` 表示单人，`1-40` 表示团队，`46` 表示队伍。
- `HeroTalent`：英雄天赋，对应状态字段 `英雄天赋`。

字段留空（界面上选择 `任意 (*)`）表示任意值。多个模块同时匹配时，程序会优先使用匹配字段更多的模块（`Specificity` 越大优先级越高），相同时按名称排序。
旧模块中 `PartyType` 如果保存为数字仍可读取；保存团队模块时会归一化写入 `"1-40"`。

模块以 `模块名.json` 的形式平铺保存在 `module` 文件夹（文件名取自模块名称，因此模块名称不能重复）。加载时会递归扫描 `module` 下的所有子目录，旧版本保存在嵌套子文件夹中的模块文件仍可正常读取。

模块文件示例：

```json
{
  "Id": "戒律-20260612100954681",
  "Name": "戒律",
  "Enabled": true,
  "Match": {
    "ClassId": 5,
    "SpecId": 1,
    "PartyType": "46",
    "HeroTalent": 1
  },
  "Rules": [
    {
      "Enabled": true,
      "Condition": "一键辅助 == 10",
      "Unit": 0,
      "Spell": "一键辅助",
      "Hotkey": "",
      "Step": ""
    }
  ]
}
```

`Id` 由界面在新建时自动生成（`名称-时间戳`），用于唯一标识模块。

规则按顺序判断，第一条命中的规则会执行。`Spell` 会通过当前职业 keymap 查找按键；如果填写了 `Hotkey`，则直接发送该按键（优先于 `Spell`）。`Unit` 表示技能对应的单位（默认为 `0`，即自身/当前单位）。

模块页规则表中“技能”和“目标”列为下拉菜单，选项取自当前选中职业的 keymap：技能按名称去重（keymap 中同名技能的多个 `unit` 条目只显示一次），目标列出该 keymap 中出现过的全部 `unit` 编号，留空表示不填。职业选“任意 (*)”时回退读取 `keymap/keymap.json`。旧模块中不在 keymap 里的技能或目标会保留为额外下拉选项，不会丢失。

判断条件支持：

- 状态字段：`生命值 < 50`、`战斗 == true`。
- 技能字段：`spells.圣疗术 == 0`。
- 队伍字段：`group.1.生命值 < 60`。
- 组合条件：`生命值 < 50 && spells.圣疗术 == 0`、`目标类型 == 1 || 首领战 > 0`。
- 布尔简写：直接写字段名表示“为真”，前面加 `!` 表示“为假”，例如 `战斗`、`!移动`。
- 条件留空表示始终命中。

字符串字面量可用单引号或双引号包裹；`true/yes/是`、`false/no/否`、`null/nil/空` 会被识别为对应的布尔值或空值。

### 可视化条件编辑器

模块页规则表的“条件”列为只读，单击单元格会弹出条件编辑窗口（在新行占位符上点击并保存后会自动追加一条规则）。窗口中每行是一个比较项：

- 连接：第二行起可选 `且`（`&&`）或 `或`（`||`），决定与上一行的组合方式。
- 字段：下拉菜单，内容来自 `config.json` 并按模块当前选中的职业/专精过滤；技能字段以 `技能: 名称` 显示（保存为 `spells.名称`）。原条件中不在目录里的字段（如 `group.1.生命值` 或手写字段）会保留为“自定义”项，不会丢失。
- 判断：数字字段提供 `==`、`!=`、`>`、`>=`、`<`、`<=`；布尔和字符串字段只提供 `==`、`!=`。
- 值：按字段类型自适应——数字字段用数字输入框，布尔字段用 `是/否` 下拉，字符串和自定义字段用文本框。
- 每行末尾有删除按钮，底部有“添加条件”按钮和生成表达式的实时预览。

注意：表达式中 `&&` 的优先级高于 `||`（先算“且”再算“或”），编辑器按此语义把行序列分组，不支持括号嵌套。布尔简写（如 `战斗`、`!移动`）在编辑器中会归一化为显式比较（`战斗 == true`、`移动 == false`），含义不变。

## 默认逻辑

如果没有任何匹配的模块，程序会回退到 `LogicRegistry.cs` 中按职业 ID 注册的 C# 逻辑；若该职业也没有注册逻辑，则使用 `DefaultClassLogic`：当状态字段 `一键辅助 == 10` 且 keymap 中存在 `一键辅助` 时发送对应按键，否则提示“C# 职业逻辑尚未迁移”。

你可以在 `module` 文件夹中放置一个匹配字段全部留空的模块作为通配模块，让它在任何职业下都能命中，从而替代上述默认逻辑。

## 迁移职业逻辑

如果逻辑可以用简单条件表达式描述，优先在 UI 的 `模块` 页维护；如果需要复杂算法，再迁移为 C# 职业逻辑。

新增 C# 职业逻辑时，建议新建一个实现 `IClassLogic` 的类：

```csharp
public sealed class PriestLogic : IClassLogic
{
    public LogicDecision Run(GameState state, string? specName)
    {
        var health = state.GetInt("生命值");
        return new LogicDecision(null, $"当前生命值: {health}", new Dictionary<string, object?>());
    }
}
```

然后在 `LogicRegistry` 中按职业 ID 注册：

```csharp
_logicByClass[5] = new PriestLogic();
```

逻辑内可以通过 `GameState.GetInt(...)`、`GameState.GetBool(...)`、`state.Spells`、`state.Group` 读取状态，并返回 `LogicDecision`：

- `Hotkey`：需要发送的按键；为空则不发送。
- `Step`：当前逻辑步骤，会显示到 UI 和日志中。
- `UnitInfo`：诊断信息，会显示在 `逻辑` 页。
- `ModuleName`：命中的模块名（C# 逻辑通常留空，由模块逻辑填写）。
