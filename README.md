# Shigure

Shigure 是一个 Windows WinForms 桌面程序，用于扫描目标游戏窗口状态、根据职业 keymap 选择按键，并把实时状态、队伍信息、逻辑输出和运行日志集中显示在 UI 中。

项目已经从旧名称 `Fuyutsui_C#` 迁移为 `Shigure`，当前为根目录单项目结构，入口项目文件是 `Shigure.csproj`。

## 当前界面

- 置顶浮动条：无边框小窗口，显示 Shigure 标题、逻辑状态，以及 `开启/关闭`、`状态`、`设置`、`关闭` 按钮；可拖动，启动后会自动启动运行循环。
- 设置窗口：点击 `设置` 打开，可设置触发键和发送模式。触发键支持键盘按键、`XBUTTON1`、`XBUTTON2`，不支持 `ALT`。
- 状态与模块窗口：点击 `状态` 打开，包含 `模块`、`状态`、`队伍`、`逻辑`、`日志` 五个页签。
- 模块页：可新建、编辑、删除模块，并编辑模块的匹配条件和判断规则。
- 状态页：显示扫描后构建出的状态字段与 `spells` 技能状态。
- 队伍页：显示 `group` 队伍成员摘要。
- 逻辑页：显示职业逻辑返回的诊断信息或推荐目标。
- 日志页：记录启动、停止、职业识别、逻辑状态变化、步骤变化和异常信息。

模块会保存到 `module` 文件夹。

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
Shigure.csproj          项目文件，复制 Assets/config/keymap 到输出目录
Program.cs              程序入口
MainForm.cs             置顶浮动条、运行控制、触发键录入
StatusForm.cs           设置与状态诊断窗口
ShigureRuntime.cs       扫描、状态构建、逻辑运行、按键发送主循环
PixelScanner.cs         目标窗口像素扫描
StateBuilder.cs         将扫描结果转换为 GameState
GameState.cs            运行状态模型
ConfigService.cs        读取 config.json
KeymapService.cs        读取并选择 keymap
KeySender.cs            向目标窗口发送按键
LogicRegistry.cs        职业逻辑注册与默认逻辑
ModuleStore.cs          模块 JSON 存储、匹配和规则执行
ModuleEditorControl.cs  模块 UI 编辑器
UiTheme.cs              深色主题、窗口特效和控件样式
Assets\                 图标与品牌资源
keymap\                 各职业按键映射
module\                 UI 可编辑模块
```

## 配置文件

- `config.json`：状态扫描配置和职业 keymap 引用。
- `keymap/*.json`：职业按键映射，运行时按职业 ID 选择。
- `Assets/*.ico|*.png|*.svg`：程序图标和品牌资源，构建时会复制到输出目录。

## 模块系统

模块用于把“识别到的角色环境”和“要执行的判断逻辑”拆成可编辑 JSON。运行时每轮识别到 `职业`、`专精`、`队伍类型`、`英雄天赋` 后，会在 `module` 文件夹中寻找匹配模块。

匹配字段：

- `ClassId`：职业 ID，对应状态字段 `职业`。
- `SpecId`：专精 ID，对应状态字段 `专精`。
- `PartyType`：队伍类型，对应状态字段 `队伍类型`。
- `HeroTalent`：英雄天赋，对应状态字段 `英雄天赋`。

字段留空表示任意值。多个模块同时匹配时，程序会优先使用匹配字段更多的模块。

模块文件示例：

```json
{
  "Id": "default-one-key-assist",
  "Name": "默认一键辅助",
  "Enabled": true,
  "Match": {
    "ClassId": 2,
    "SpecId": 1,
    "PartyType": 0,
    "HeroTalent": 0
  },
  "Rules": [
    {
      "Enabled": true,
      "Condition": "一键辅助 == 10",
      "Unit": 0,
      "Spell": "一键辅助",
      "Step": "施放 一键辅助"
    }
  ]
}
```

规则按顺序判断，第一条命中的规则会执行。`Spell` 会通过当前职业 keymap 查找按键；如果填写了 `Hotkey`，则直接发送该按键。

判断条件支持：

- 状态字段：`生命值 < 50`、`战斗 == true`。
- 技能字段：`spells.圣疗术 == 0`。
- 队伍字段：`group.1.生命值 < 60`。
- 组合条件：`生命值 < 50 && spells.圣疗术 == 0`、`目标类型 == 1 || 首领战 > 0`。

默认提供一个通配模块：

```text
module\any\any\any\any\默认一键辅助-default-one-key-assist.json
```

它等同于旧的默认逻辑：当 `一键辅助 == 10` 且 keymap 中存在 `一键辅助` 时发送对应按键。

## 迁移职业逻辑

模块逻辑会优先于 C# 职业逻辑执行。没有匹配模块时，程序才会回退到 `LogicRegistry.cs` 中注册的 C# 逻辑。

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

逻辑内可以通过 `GameState.GetInt(...)`、`state.Spells`、`state.Group` 读取状态，并返回：

- `Hotkey`：需要发送的按键；为空则不发送。
- `Step`：当前逻辑步骤，会显示到 UI 和日志中。
- `UnitInfo`：诊断信息，会显示在 `逻辑` 页。

如果逻辑可以用简单条件表达式描述，优先在 UI 的 `模块` 页维护；如果需要复杂算法，再迁移为 C# 职业逻辑。
