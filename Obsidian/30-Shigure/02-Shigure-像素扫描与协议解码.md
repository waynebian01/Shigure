---
title: Shigure 像素扫描与协议解码
summary: 说明 PixelScanner 如何从可见客户区精确颜色像素解出 510 步状态、动作条计数、30 人治疗吸收和 20 个姓名板槽位。
aliases:
  - PixelScanner 协议
  - Shigure 像素消费者
tags:
  - project/shigure
  - doc/feature
  - area/protocol
project: Shigure
doc_type: feature
status: current
authority: source-derived
up: "[[30-Shigure/00-Shigure-MOC]]"
related:
  - "[[40-跨项目/01-Shigure-像素生产消费契约]]"
  - "[[30-Shigure/03-Shigure-配置合并与GameState构建]]"
source_files:
  - Runtime/PixelScanner.cs
  - Runtime/RuntimeDependencies.cs
  - Input/NativeMethods.cs
  - Infrastructure/WowProcessLocator.cs
source_symbols:
  - PixelScanner.ScanScreenData
  - PixelScanner.ScanTopRow
  - PixelScanner.ScanLeftMarkerRow
  - PixelScanner.ScanHealAbsorbGrid
  - ScreenScanResult
verified_at: 2026-08-10
---

# Shigure 像素扫描与协议解码

> [!abstract] AI 快速摘要
> `PixelScanner` 通过 `WowProcessLocator` 选择 `wow_process.txt` 候选进程中 Z 顺序最靠前的可见窗口，并截取其客户区可见像素。顶行用 `(R,G,B)` 精确编码 510 个步骤和值；左边缘锚点定位计数行及其后六行治疗吸收。没有颜色容差、没有 `PrintWindow`，因此遮挡、最小化、缩放或着色都可能破坏读取。

## 图谱位置

- 上级：[[30-Shigure/00-Shigure-MOC]]
- 生产/消费权威契约：[[40-跨项目/01-Shigure-像素生产消费契约]]
- 下游状态构建：[[30-Shigure/03-Shigure-配置合并与GameState构建]]
- Fuyutsui 生产者入口：[[20-Fuyutsui/03-Fuyutsui-状态块与编码入口]]；吸收细节见 [[20-Fuyutsui/07-Fuyutsui-队伍与治疗吸收]]

## 范围与非范围

本页只描述屏幕定位、截图和 RGB 解码。不定义步骤 4 以后各业务字段的名字；那些名字由 ClassBlocks 生成的配置解释。也不描述模块规则。

## 姓名板扫描

扫描器在客户区中寻找 `(1,0,1)` 姓名板标记，读取第二格的 `B` 通道作为数据行数，再按 20 个等宽槽位读取后续行。每个槽位的 `R=1` 表示单位存在，`B` 是对应行的原始值；结果固定包含 `nameplate1` 至 `nameplate20`，缺失槽位为 `Present=false`。

## 输入与输出

| 输入 | 输出字段 | 语义 |
|---|---|---|
| `wow_process.txt` 候选进程的首个可见窗口 | `RowData: Dictionary<int,int>?` | 顶行步骤号到 0..255 值 |
| 左边缘红锚点后的计数段 | `BarData: Dictionary<int,int>` | 动作条/计数条索引到 `G-1` |
| 锚点后的六行吸收网格 | `HealAbsorbData: Dictionary<int,int>` | 组员 1..30 到 `G-1` |
| 定位/截图/解析异常 | `FailureReason` | 面向运行时和 UI 的诊断文本 |

`RowData == null` 表示顶行协议不可用；计数条没找到时仍可返回有效顶行数据，并附带失败/警告原因。字典遇到重复步骤时以后扫描到的值覆盖先前值。

## 顶行编码

精确匹配规则是：

```text
G 必须在 1..255
R == 0  => step = G         # 1..255
R == 1  => step = 255 + G   # 256..510
value = B
```

扫描器先在客户区顶行前 `min(510, width)` 个横坐标内寻找步骤 1，然后从该位置继续扫描整行，直到读到步骤 510。它不要求每个步骤都存在，但后续状态构建会对缺失值使用默认值。步骤 1 是锚点；通用配置规定步骤 2/3 为职业/专精。

## 动作条与治疗吸收编码

1. 在客户区 `x=0` 从上到下寻找精确红色 `(1,0,0)` 作为计数区锚点。
2. 锚点行由白色或红/红绿分段开始，以灰色 `(200,200,200)` 终止；每段第一个非白像素的 `G-1` 是值。
3. 紧随其后的 6 行构成吸收网格；每个白色 run 后的首个非白像素以 `B=1..30` 指定单位，以 `G-1` 指定治疗吸收。
4. 协议值由颜色通道限制：顶行值最大 255，计数和吸收的 `G-1` 最大 254。

生产端刷新时序见 [[20-Fuyutsui/02-Fuyutsui-事件与刷新调度]]；吸收值含义见 [[20-Fuyutsui/07-Fuyutsui-队伍与治疗吸收]]。

## 截图和窗口边界

- `WowProcessLocator` 每次重新读取 `wow_process.txt`，按名称取得候选 PID，再沿 Windows Z 顺序选择首个候选可见顶层窗口。
- 最小化窗口会被拒绝。
- 客户坐标经 `ClientToScreen` 转换后，用 `Graphics.CopyFromScreen` 截取；这是可见桌面像素，不是离屏窗口内容。
- `SetProcessDPIAware` 是 best effort；失败不会中止构造。
- 颜色比较逐通道完全相等，没有抗锯齿/亮度/色彩配置容差。

因此多个候选实例或过宽的进程名列表可能选中非预期窗口；目标被其他窗口覆盖、游戏滤镜改变颜色、Windows 缩放处理异常或插件纹理偏移，也可能造成错误或空扫描。

## 失败模式与排障

| 症状 | 优先检查 |
|---|---|
| 一直“等待像素” | `wow_process.txt`、候选窗口是否可见/最小化、步骤 1 是否在顶行前 510 像素、Fuyutsui 是否加载 |
| 部分状态总是 0 | 生产端是否绘制该步骤、配置步骤号是否与 ClassBlocks 顺序一致、是否超过 510 |
| 动作条值为空但状态正常 | 左边缘红锚点、终止灰色、动作条布局 |
| 吸收值错位 | 锚点之后是否恰好六行、单位 `B` 编号是否 1..30 |
| 偶发扫描失败 | 遮挡、桌面切换、DPI/分辨率变化、屏幕捕获异常 |

一个重要语义：配置里声明了字段但本次 `RowData` 没有该步骤时，`StateBuilder` 会把布尔设为 false、字符串设为空、数字设为 0。因此“缺失像素”不总等价于条件系统中的“未知值”；详见 [[30-Shigure/03-Shigure-配置合并与GameState构建]]。

## 修改影响

- 改步骤编码、锚点颜色、510 上限或吸收网格，必须同步 Fuyutsui 生产端和 [[40-跨项目/01-Shigure-像素生产消费契约]]。
- 改截图方式会影响遮挡、权限和 DPI 行为；需重新验证窗口坐标和性能。
- 新增业务字段通常不应改扫描器，而应改 Fuyutsui ClassBlocks、转换器和 config 映射。
- 增加步骤容量前要同时检查 G/R 编码范围、插件纹理宽度、转换器以及 `MaxStep`。

## 源码索引

- `Runtime/PixelScanner.cs:7-18`：结果结构、510 上限、255 分段、6×30 吸收常量。
- `Runtime/PixelScanner.cs:21-31`：DPI 感知。
- `Runtime/PixelScanner.cs:34-96`：窗口检查、客户区坐标、三类扫描结果。
- `Infrastructure/WowProcessLocator.cs`：候选进程和 Z 顺序窗口定位。
- `Runtime/PixelScanner.cs:99-135`：步骤 1 搜索和顶行扫描。
- `Runtime/PixelScanner.cs:137-203`：计数条锚点与分段。
- `Runtime/PixelScanner.cs:206-251`：六行治疗吸收。
- `Runtime/PixelScanner.cs:315-372`：屏幕截图、精确颜色与步骤解码。

## 知识图谱链接

- 上游 Fuyutsui：[[20-Fuyutsui/03-Fuyutsui-状态块与编码入口]]
- 契约中心：[[40-跨项目/00-Shigure-跨项目契约-MOC]]
- 下游：[[30-Shigure/03-Shigure-配置合并与GameState构建]]、[[30-Shigure/04-Shigure-运行循环触发模式与快照]]
- 相关原始说明：`README.md`
