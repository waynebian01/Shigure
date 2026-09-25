# Boss 战斗时间轴 API 与像素输出

> 适用范围：正式服 12.0+ 的 `EncounterTimeline` / `EncounterEvents` API。本文按 2026-09-25 的接口资料整理，并结合当前 Fuyutsui 的顶部像素协议说明可行性。接口可能随补丁调整，上线前应在目标客户端中用 `/api` 再确认一次。

## 结论

可以把指定 Boss 技能设置为固定颜色，也可以把时间轴事件显示成像素，但要区分下面三件事：

| 目标 | 是否可行 | 说明 |
| --- | --- | --- |
| 把某个技能在暴雪时间轴中固定为某种颜色 | 可以 | 战斗前用 `C_EncounterEvents` 按 `spellID` 找到事件记录，再为 `TimelineEvent` 和 `TimelineEventHighlight` 设置相同颜色。 |
| 在战斗中直接判断 `eventInfo.spellID == 指定技能ID` | 不可以 | Boss 时间轴事件的 `spellID`、`spellName`、图标等是秘密值，插件代码不能比较、计算或拿它做表键。 |
| 把所有活动时间轴事件分配到像素槽，并把事件颜色原样画出来 | 可以 | `eventInfo.id` 永不保密，可作为实例键；`GetEventColor()` 得到的颜色即使是秘密值，也可以不经判断和计算，直接传给允许接收秘密值的纹理接口。 |
| 只给指定技能分配一个固定的游戏内像素，其余技能完全不占槽 | 不能直接做到 | 这需要在战斗中先识别技能，而技能身份正是秘密值。可改为“所有事件占动态槽，外部扫描器按固定颜色识别目标技能”。 |
| 输出事件剩余时间 | 可以 | 用公开的事件实例 ID 调 `GetEventTimeRemaining()`；建议用另一个像素编码倒计时，不要与身份颜色共用同一像素。 |

推荐方案是：**登录后给目标技能配置唯一颜色；战斗中不识别技能，只把每个时间轴实例的颜色透传到动态像素槽；Shigure 在截图侧识别颜色。**

## 两套相关 API

### `C_EncounterEvents`：静态事件目录与用户配置

这一套 API 面向“事件记录”，适合在登录、重载界面或打开设置时使用：

- `GetEventList()`：取得全部 `encounterEventID`。
- `GetEventInfo(encounterEventID)`：取得事件的 `spellID`、图标、严重程度、辅助图标等静态信息。
- `SetEventColor(encounterEventID, trigger, color)`：设置某条事件记录的自定义颜色。
- `GetEventColor(encounterEventID, trigger)`：读取颜色配置。
- `SetEventSound` / `GetEventSound`：设置或读取事件声音。

`EncounterEventInfo.spellID` 可以用于配置阶段的查找。同一个 `spellID` 可能对应多条 `encounterEventID`，因此应遍历并配置所有匹配项，不能找到第一条后就停止。

颜色触发器：

| 枚举 | 值 | 用途 |
| --- | ---: | --- |
| `TextWarning` | 0 | 文字警告颜色 |
| `TimelineEvent` | 1 | 普通时间轴事件颜色 |
| `TimelineEventHighlight` | 2 | 事件进入临近高亮阶段后的颜色 |

### `C_EncounterTimeline`：当前战斗中的动态实例

这一套 API 面向“当前时间轴实例”。同一技能在一次战斗中多次出现时，会产生不同的时间轴 `eventID`。

常用查询：

| API | 用途 |
| --- | --- |
| `IsFeatureAvailable()` | 客户端是否支持时间轴。 |
| `IsFeatureEnabled()` | 玩家是否启用了时间轴功能。 |
| `GetEventList()` | 当前所有事件实例 ID，列表未排序。 |
| `GetSortedEventList(...)` | 按剩余时间从短到长取事件。 |
| `GetEventInfo(eventID)` | 取得实例的静态信息。Boss 事件的部分字段为秘密值。 |
| `GetEventState(eventID)` | `Active`、`Paused`、`Finished`、`Canceled`。 |
| `GetEventTimeRemaining(eventID)` | 剩余时间。 |
| `GetEventTimeElapsed(eventID)` | 已经过的时间。 |
| `GetEventTimer(eventID)` | 返回自动处理暂停状态的 Duration 对象，适合直接绑定 UI。 |
| `GetEventTrack(eventID)` | 当前所在轨道及排序位置。 |
| `GetEventColor(eventID[, trigger])` | 取得当前渲染颜色；Boss 事件返回的颜色可能是秘密值。 |
| `IsEventBlocked(eventID)` | 当前事件是否因战斗条件未满足而被阻塞。 |

事件生命周期：

| 事件 | 处理建议 |
| --- | --- |
| `ENCOUNTER_TIMELINE_EVENT_ADDED` | 为 `eventInfo.id` 分配像素槽，保存实例 ID，立即刷新颜色和计时。 |
| `ENCOUNTER_TIMELINE_EVENT_COLOR_CHANGED` | 重新读取颜色并刷新对应像素。 |
| `ENCOUNTER_TIMELINE_EVENT_STATE_CHANGED` | 更新活动/暂停/结束/取消状态。 |
| `ENCOUNTER_TIMELINE_EVENT_TRACK_CHANGED` | 如需按长、中、短时间筛选则刷新槽位。 |
| `ENCOUNTER_TIMELINE_EVENT_BLOCK_STATE_CHANGED` | 更新阻塞标记。 |
| `ENCOUNTER_TIMELINE_EVENT_HIGHLIGHT` | 表示事件进入临近高亮阶段，通常约为剩余 5 秒。 |
| `ENCOUNTER_TIMELINE_EVENT_REMOVED` | 释放槽位并清零像素。此时该实例可能已经无法再查询。 |
| `ENCOUNTER_TIMELINE_VIEW_DEACTIVATED` | 清空全部缓存和像素槽。 |

`ENCOUNTER_START` / `ENCOUNTER_END` 仍适合管理整场 Boss 战的总生命周期；时间轴事件用于描述该场战斗内的技能预告。

## 秘密值限制

`EncounterTimelineEventInfo` 的字段并非都能由插件读取：

| 字段 | Boss 时间轴事件中是否可安全处理 | 说明 |
| --- | --- | --- |
| `id` | 可以 | `NeverSecret`，当前时间轴实例 ID。 |
| `source` | 可以 | `NeverSecret`，来源为 Encounter / Script / EditMode。 |
| `duration` | 可以 | `NeverSecret`，进入时间轴时的基础时长。 |
| `maxQueueDuration` | 可以 | `NeverSecret`，到点后可在队列停留的时长。 |
| `spellID` | 不可检查 | Boss 事件中为秘密值。 |
| `spellName` | 不可检查 | Boss 事件中为秘密值。 |
| `iconFileID` | 不可检查 | Boss 事件中为秘密值。 |
| `icons`、`severity`、`isApproximate` | 不可检查 | Boss 事件中为秘密值。 |

插件代码可以保存秘密值，或把它直接传给被允许接收秘密值的界面 API；不能进行以下操作：

- 与常量比较，例如 `eventInfo.spellID == 123456`；
- 算术运算，例如 `eventInfo.spellID / 255`；
- 作为表键，例如 `events[eventInfo.spellID] = true`；
- 对秘密布尔值做 `if` 判断；
- 先读出秘密颜色，再根据 RGB 分支。

违规操作会直接产生 Lua 错误，而不是简单返回 `false`。`issecretvalue(value)` 只能用来判断“它是不是秘密值”，不能打开或转换秘密值。

错误示例：

```lua
function Fuyutsui:ENCOUNTER_TIMELINE_EVENT_ADDED(_, eventInfo)
    -- Boss 事件中 spellID 是秘密值；比较会报错。
    if eventInfo.spellID == 123456 then
        self:CreateTexture(100, 1)
    end
end
```

## 将指定技能固定为某种颜色

技能与事件记录的匹配应在配置阶段完成，不要等到 Boss 时间轴事件触发后再匹配。

```lua
local TARGET_SPELL_ID = 123456
local TARGET_COLOR = CreateColor(1, 0, 1, 1) -- 品红色

local function ConfigureTargetEncounterColor()
    if not C_EncounterEvents or not C_EncounterEvents.GetEventList then
        return
    end

    for _, encounterEventID in ipairs(C_EncounterEvents.GetEventList()) do
        local info = C_EncounterEvents.GetEventInfo(encounterEventID)
        if info and info.spellID == TARGET_SPELL_ID then
            C_EncounterEvents.SetEventColor(
                encounterEventID,
                Enum.EncounterEventColorTrigger.TimelineEvent,
                TARGET_COLOR)
            C_EncounterEvents.SetEventColor(
                encounterEventID,
                Enum.EncounterEventColorTrigger.TimelineEventHighlight,
                TARGET_COLOR)
        end
    end
end
```

这里同时设置普通颜色和高亮颜色，否则事件进入高亮阶段后会换色，像素识别就不再稳定。若要恢复默认颜色，把第三个参数传为 `nil`。建议每次登录或 `/reload` 后重新应用配置，并记录找到的匹配数量；匹配数为 0 时说明该技能没有暴露为 Encounter Event，或当前客户端数据中没有对应记录。

这段逻辑改变的是该技能的时间轴显示颜色。它不会让本来不在官方 Boss 时间轴中的技能凭空出现。

## 将时间轴事件输出为像素

### 推荐布局

预留固定数量的动态槽，例如 16 个；每个槽至少使用两个像素：

| 像素 | 内容 |
| --- | --- |
| 身份像素 | 原样输出 `C_EncounterTimeline.GetEventColor(eventID)` 的 RGB。目标技能已提前设置为唯一固定颜色。 |
| 时间像素 | 编码 `GetEventTimeRemaining(eventID)`，例如钳制到 0–255 秒。 |

可选增加状态像素，编码 Active / Paused / Finished / Canceled、Blocked、Highlight 等公开状态。实例 ID 只用于插件内部管理，不应直接当技能 ID。

战斗中的核心代码只能做“槽位管理”和“秘密颜色透传”：

```lua
local eventSlotByID = {}
local timelineTextures = {} -- 预先创建并固定位置，不在战斗中改变布局

local function PaintEventColor(slot, eventID)
    local color = C_EncounterTimeline.GetEventColor(eventID)
    local r, g, b = color:GetRGB()

    -- 不比较、不计算 r/g/b，直接传给允许接收秘密颜色的纹理接口。
    timelineTextures[slot]:SetColorTexture(r, g, b, 1)
end

function Fuyutsui:ENCOUNTER_TIMELINE_EVENT_ADDED(_, eventInfo)
    local eventID = eventInfo.id -- NeverSecret
    local slot = self:AcquireTimelinePixelSlot(eventID)
    if not slot then return end

    eventSlotByID[eventID] = slot
    PaintEventColor(slot, eventID)
end

function Fuyutsui:ENCOUNTER_TIMELINE_EVENT_COLOR_CHANGED(_, eventID)
    local slot = eventSlotByID[eventID]
    if slot then
        PaintEventColor(slot, eventID)
    end
end

function Fuyutsui:ENCOUNTER_TIMELINE_EVENT_REMOVED(_, eventID)
    local slot = eventSlotByID[eventID]
    if not slot then return end

    timelineTextures[slot]:SetColorTexture(0, 0, 0, 1)
    eventSlotByID[eventID] = nil
    self:ReleaseTimelinePixelSlot(slot)
end
```

上例是结构示意，`AcquireTimelinePixelSlot`、纹理创建、计时更新和清理仍需实现。纹理应在非战斗阶段预先创建并固定锚点，战斗中只更新允许变化的颜色值。

### 与当前 Fuyutsui 像素协议的关系

当前 `Fuyutsui:CreateTexture(i, b)` 的含义是：

- R/G 编码状态索引；
- B 编码该状态的数值；
- Shigure 依靠 R/G 找到字段索引。

因此，不能把 `GetEventColor()` 的完整 RGB 直接交给现有 `CreateTexture(i, b)`，否则会破坏索引协议。可以选择以下方案：

1. **单独时间轴色带（推荐）**：新增一行固定位置的事件槽，像素完整保存 RGB；Shigure 按屏幕位置读槽，不再用 R/G 解析字段索引。
2. **只透传颜色的 B 通道**：为目标技能选择唯一 B 值，再把秘密 `b` 直接传入现有 `CreateTexture(index, b)`。改动小，但只有 8 位编码空间，存在颜色碰撞，且无法保留完整颜色。
3. **所有事件各占固定主像素字段**：预留 `时间轴1`～`时间轴N`，每格只保存一个颜色分量或编码值。需要多个像素才能可靠保存完整身份颜色。

如果最终目标是让模块规则读取“目标 Boss 技能将在 N 秒后发生”，推荐数据流为：

```text
C_EncounterEvents 预设目标技能颜色
        ↓
EncounterTimeline 实例分配到动态像素槽
        ↓ 颜色原样透传；剩余时间单独编码
Shigure 扫描所有槽并在外部匹配固定 RGB
        ↓
构造“目标事件存在 / 剩余秒数”等普通 GameState 字段
        ↓
模块规则使用普通数值条件
```

这样，Lua 端没有读取或判断秘密技能身份；技能颜色的识别和槽位聚合发生在截图读取端。

## 可靠性与边界

- 时间轴表示官方提供的战斗预告，不等于已经观察到一次真实施法。事件可能近似、暂停、阻塞、取消或提前结束。
- 并非所有 Boss、难度和技能都有时间轴事件；先检查 `IsFeatureAvailable()`，并对 0 匹配、0 事件、槽位耗尽做降级处理。
- 同一技能可能有多个事件记录，同一个战斗中也可能同时存在多个实例。
- `eventID` 是当前时间轴实例 ID，不是 `spellID`，不能跨战斗持久化。
- `AddScriptEvent()` 创建的是插件自己的 Script 事件，不能借此读取或改写 Boss 的 Encounter 事件；Pause / Resume / Cancel / Finish 也只应作用于 Script 来源事件。
- 12.0.7 已从 `EncounterTimelineEventInfo` 和 `EncounterEventInfo` 移除 `color` 字段；颜色必须通过 `GetEventColor()` / `SetEventColor()` 处理。
- 若只希望玩家看见固定颜色，做到 `SetEventColor()` 即可；若要供 Shigure 读取，还需要实现时间轴像素槽和 C# 侧解码。

## 对当前仓库的落地建议

当前仓库已经注册并留出了以下处理函数，但函数体为空：

- `Fuyutsui/core/events.lua` 中的 `ENCOUNTER_TIMELINE_EVENT_ADDED`；
- `ENCOUNTER_TIMELINE_EVENT_REMOVED`；
- `ENCOUNTER_TIMELINE_EVENT_STATE_CHANGED`。

建议后续实现拆成四层：

1. `core/encountertimeline.lua`：事件记录颜色配置、时间轴实例缓存、槽位分配与释放。
2. `core/block.lua`：新增独立时间轴色带，预创建固定数量纹理。
3. `Runtime/PixelScanDecoder.cs`：读取时间轴色带并保留 RGB 与槽位号。
4. `Runtime/StateBuilder.cs`：把匹配到的目标颜色聚合为模块可用字段，例如“Boss技能存在”“Boss技能剩余时间”。

第一版建议只做“目标颜色存在 + 剩余秒数”，不要一开始就尝试导出全部时间轴元数据。

## 参考资料

- [Warcraft Wiki：EncounterTimeline API 系统](https://warcraft.wiki.gg/wiki/Category:API_systems/EncounterTimeline)
- [暴雪生成的 EncounterTimelineDocumentation.lua 镜像](https://github.com/Gethe/wow-ui-source/blob/live/Interface/AddOns/Blizzard_APIDocumentationGenerated/EncounterTimelineDocumentation.lua)
- [Warcraft Wiki：C_EncounterTimeline.GetEventInfo](https://warcraft.wiki.gg/wiki/API:C_EncounterTimeline.GetEventInfo)
- [Warcraft Wiki：ENCOUNTER_TIMELINE_EVENT_ADDED](https://warcraft.wiki.gg/wiki/Event:ENCOUNTER_TIMELINE_EVENT_ADDED)
- [Warcraft Wiki：EncounterEvents API 系统](https://warcraft.wiki.gg/wiki/Category:API_systems/EncounterEvents)
- [Warcraft Wiki：C_EncounterEvents.SetEventColor](https://warcraft.wiki.gg/wiki/API:C_EncounterEvents.SetEventColor)
- [Warcraft Wiki：秘密值与 Secret Aspects](https://warcraft.wiki.gg/wiki/Secret_aspects)
- [暴雪默认时间轴 DataProvider 实现](https://github.com/Gethe/wow-ui-source/blob/live/Interface/AddOns/Blizzard_EncounterTimeline/EncounterTimelineDataProvider.lua)
- [暴雪默认时间轴事件渲染实现](https://github.com/Gethe/wow-ui-source/blob/live/Interface/AddOns/Blizzard_EncounterTimeline/EncounterTimelineTimerEvent.lua)
