---
title: "Fuyutsui core/classmacros.lua：AI 宏规则参考"
summary: "说明 ClassMacros 的职业级动态、静态、特殊宏声明，以及 CreateMacro 的 1170 槽展开与覆盖绑定顺序。"
language: "zh-CN"
primary_file: "core/classmacros.lua"
related:
  - "[[20-Fuyutsui/00-Fuyutsui-MOC|Fuyutsui MOC]]"
  - "[[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约|ClassMacros 到 keymap 与按键契约]]"
  - "[[30-Shigure/09-Shigure-Fuyutsui配置宏编辑与同步|Shigure Fuyutsui 配置宏编辑与同步]]"
purpose: "供 AI 理解、审查、新增职业宏时作为单一事实来源；说明 ClassMacros 三表规则、职业级 dynamicSpells、MacroBodies 查表与 CreateMacro 展开逻辑"
tags:
  - "project/fuyutsui"
  - "doc/reference"
  - "area/macro"
project: "Fuyutsui"
doc_type: "implementation-reference"
status: "current"
authority: "source-derived"
up:
  - "[[50-参考资料/00-参考资料-MOC|参考资料 MOC]]"
source_files:
  - "Fuyutsui/core/classmacros.lua"
  - "Fuyutsui/core/macro.lua"
  - "Fuyutsui/main.lua"
source_symbols:
  - "ClassMacros"
  - "Fuyutsui:ClearMacros"
  - "Fuyutsui:CreateMacro"
verified_at: "2026-08-09"
---

# Fuyutsui `core/classmacros.lua`：AI 宏规则参考

> 本文描述如何在 `ClassMacros` 里声明职业宏，以及 `CreateMacro` 如何把它们展开成 SecureActionButton 覆盖绑定。  
> **改宏只改 `core/classmacros.lua`**，不要改 `class/*.lua`，也不要在别处硬编码职业宏列表。

## 0. 一句话定义

`classmacros.lua` 提供：

1. `Fuyutsui.MacroBodies`：命名宏体查表（药水等）。
2. `Fuyutsui.ClassMacros[classFile]`：各职业的三份列表（`dynamicSpells` / `staticSpells` / `specialSpells`）。

运行时由 `main.lua:LoadPlayerMacros()` 按 `UnitClassBase("player")` 取出职业级列表，再调用：

```lua
Fuyutsui:CreateMacro(dynamicSpells, m.staticSpells, m.specialSpells)
```

`CreateMacro` 开头会先 `ClearMacros()`（清覆盖绑定 + 清空已有按钮 `macrotext`），再按新列表从头占键。真正创建按钮、拼宏文本、绑定热键的逻辑在 `core/macro.lua`。

**创建顺序（依次占键）**：`dynamicSpells`（每组 30 键）→ `staticSpells` → `specialSpells`。

## 1. AI 必须遵守的规则

1. **键名必须是** `UnitClassBase` 返回值：`WARRIOR` / `PALADIN` / `HUNTER` / `ROGUE` / `PRIEST` / `DEATHKNIGHT` / `SHAMAN` / `MAGE` / `WARLOCK` / `MONK` / `DRUID` / `DEMONHUNTER` / `EVOKER`。
2. 每个职业表**必须有**三个字段：`dynamicSpells`、`staticSpells`、`specialSpells`（可为空表 `{}`）。字段顺序建议与文件一致：dynamic → static → special。
3. `dynamicSpells`、`staticSpells`、`specialSpells` 一律用**职业级数组**（例如 `{"牺牲祝福", "代祷", "圣盾术"}`），**不要**再写 `[n] = "..."` 稀疏键值表。
4. **需要点名队友/团队成员**的治疗、驱散、护盾等 → 放 `dynamicSpells`（顺序敏感；牧师和圣骑士的专精宏已按顺序合并到职业数组）。
5. **普通单体施法**（目标默认、或条件写在法术名里）→ 放 `staticSpells`，只写法术/条件字符串，**不要**自己加 `/cast `。
6. **完整宏文本**（`/castsequence`、`/stopcasting`、`/cancelaura` 等）→ 可直接写在 `staticSpells`（以 `/` 开头）或追加到 `specialSpells`；以 `/` 开头的字符串**不会**再加 `/cast `。
7. 药水等复用宏体 → 在列表里写**名称**（如 `"银月城生命药水"`），并在 `Fuyutsui.MacroBodies` 中登记；创建时用表内值。
8. 需要跳过某个槽位但不平移后续键 → 在数组对应位置写 `""`（占位，不创建按钮）。
9. 战斗中 `InCombatLockdown()` 时无法创建/修改安全按钮；改宏后需在脱战后 `/reload` 或等下次非战斗加载生效。专精切换重建宏同样受此限制。
10. 保持 Lua 5.1 / WoW 兼容；法术名用**本地化中文名**（与客户端一致），与现有表风格一致。
11. 不要格式化整个文件；只改目标职业段落。

## 2. 加载与消费链路

```text
classmacros.lua
        │  MacroBodies（命名宏体）
        │  ClassMacros[classFile]
        ▼
main.lua:LoadPlayerMacros()
        │  classFile = UnitClassBase("player")
        │  dynamic = m.dynamicSpells（职业级数组）
        │  MacrosList = { dynamicSpells=dynamic, staticSpells, specialSpells }
        ▼
macro.lua:CreateMacro(dynamic, static, special)
        │  0. ClearMacros()：ClearOverrideBindings + 清空 macrotext
        │  1. dynamic：每组 × 30 键
        │  2. static：ipairs 依次占键（resolveMacroBody）
        │  3. special：接在 static 后依次占键（resolveMacroBody）
        │  SetOverrideBindingClick + macrotext → 按钮 s1..sN
        ▼
玩家按覆盖热键 → SecureActionButton 执行宏
```

触发时机：

- `core/player.lua:GetCharacterSpecInfo()`（角色/专精信息初次加载）
- `core/player.lua:UpdatePlayerSpecInfo()`（切换专精后重建 blocks **并**重建宏）

切换专精时会删除旧覆盖绑定后按当前职业数组重新创建；同一职业的宏槽位不随专精切换改变。

## 3. 热键池（`macroKind`）

`core/macro.lua` 用修饰键 × 基础键生成有序列表 `macroKind[1..N]`。覆盖绑定挂在独立 `bindingOwner` 上，便于 `ClearMacros` 只清本插件绑定。

| 修饰键顺序 | 基础键 |
|---|---|
| `CTRL` → `ALT` → `SHIFT` → `ALT-CTRL` → `ALT-SHIFT` → `CTRL-SHIFT` → `ALT-CTRL-SHIFT` | 小键盘 0–9 / 小数点 / + / − / × / ÷；`F1–F3,F5–F12`（**无 F4**）；`, . / ; ' [ ] \`；`7 8 9 0 =`；末尾 `-`、`INSERT` `DELETE` `HOME` `END` `PAGEUP` `PAGEDOWN`、`UP` `DOWN` `LEFT` `RIGHT`。不加反引号、`NUMPADENTER`。 |

- 总槽位数 = `26 × 45 = 1170`。
- 按钮名：`s1`、`s2`、…（与 `macroKind` 下标一致）。
- 当前键池定义直接见 `Fuyutsui/core/macro.lua`（ID 1 = `CTRL-NUMPAD1`，以此类推）。

**AI 改宏时**：关心的是解析后的 `dynamic` 占用多少槽、以及 static/special 数组下标对应的全局热键；不必手算，除非要核对外部程序按键映射。

## 4. 槽位分配算法（核心）

`CreateMacro` **按顺序推进槽位下标 `i`**（从 1 起），不再用「special 与 static 同序号互斥覆盖」：

```text
ClearMacros()   -- 先清绑定与旧 macrotext
i = 1

-- 1. dynamicSpells（已是解析后的纯数组）
for each spell in dynamicSpells:
    for raidIdx = 1..30:
        按 §5 生成 macroBody（spell 为空则不建按钮）
        占用 macroKind[i]，i = i + 1

-- 2. staticSpells
for each entry in staticSpells:
    macroBody = resolveMacroBody(entry)   -- "" → nil，不建按钮但仍占位
    占用 macroKind[i]，i = i + 1

-- 3. specialSpells
for each entry in specialSpells:
    macroBody = resolveMacroBody(entry)
    占用 macroKind[i]，i = i + 1
```

因此：

| 解析后 `#dynamicSpells` | 动态占用槽 | `staticSpells[1]` 对应全局 `i` |
|---:|---:|---:|
| 0 | 0 | 1 |
| 1 | 30 | 31 |
| 2 | 60 | 61 |
| 4 | 120 | 121 |
| 5 | 150 | 151 |
| 6 | 180 | 181 |
| 7 | 210 | 211 |

**新增或删除 `dynamicSpells` 条目（含专精切换导致解析长度变化）会平移其后所有 static/special 的实际热键。**  
**在 `staticSpells` 中间插入/删除条目（含 `""`）同样会平移其后键位。**

### 4.1 `resolveMacroBody(spell)`

| 情况 | 结果 |
|---|---|
| `spell` 为 `nil` 或 `""` | 不创建宏（槽位仍前进） |
| `Fuyutsui.MacroBodies[spell]` 存在，且值以 `/` 开头 | 原样使用表内值 |
| `MacroBodies[spell]` 存在，值不以 `/` 开头 | `"/cast " .. 表内值` |
| `spell` 本身以 `/` 开头 | 原样使用（完整宏文本） |
| 其他 | `"/cast " .. spell` |

### 4.2 `ClearMacros()`

- `ClearOverrideBindings(bindingOwner)`：清除本插件登记的覆盖绑定。
- 遍历已创建按钮：`SetAttribute("macrotext", nil)`。
- 按钮 Frame 复用；下次 `CreateMacro` 会重新 `SetOverrideBindingClick` 并写入新 `macrotext`。

## 5. `dynamicSpells` 规则

### 5.1 数据结构（职业级数组）

**旧格式（纯数组，仍兼容）**：

```lua
dynamicSpells = { "法术A", "法术B", "法术C" }  -- 数组，1-based，顺序 = 组号
```

**当前格式**：

```lua
dynamicSpells = { "纯净术", "快速治疗", "真言术：盾", "苦修" },
```

旧版 `common` / `[specIndex]` 结构仅作为导入迁移输入，运行时不再解析：

| 表内容 | 解析结果 |
|---|---|
| 纯字符串数组 | 原样使用该职业数组 |
| 仍存在专精表 | 转换器报错，模块导入按兼容规则迁移 |

牧师和圣骑士的历史专精数组已按 `common → 专精 1 → 专精 2` 顺序合并，并仅对完全相同的宏名称去重；唤魔师等其他职业的历史专精宏会被丢弃。

- 用**纯法术名**（中文），不要写 `/cast`，不要写 `@raid`（展开逻辑会加）。
- 每组固定占 **30** 个连续热键，对应 `raid1` … `raid30`。
- 职业数组长度决定该职业后续 static/special 宏的偏移；专精切换不会改变宏表。

### 5.2 单组内 30 键展开

设组内相对位 `raidIdx = 1..30`，法术名为 `spell`：

| raidIdx | 生成的 `macrotext` |
|---:|---|
| 1 | `/cast [group:raid,@raid1]spell;[group:party,@player]spell;[nogroup,@player]spell` |
| 2..5 | `/cast [group:raid,@raidN]spell;[group:party,@party(N-1)]spell` |
| 6..30 | `/cast [group:raid,@raidN]spell` |

含义：

- 团队：始终 `@raid1` … `@raid30`。
- 小队：仅前 5 键有意义 → `@player`、`@party1`…`@party4`。
- 单人：仅第 1 键落到 `@player`。

### 5.3 何时放入 dynamic

适合：治疗术、驱散、护盾、急救类等**必须点名不同队友**的技能。

不适合：

- 只打当前目标 / 自身 / 鼠标指向 / 焦点 → 用 `staticSpells` + 条件前缀。
- 需要 sequence / stopcasting / cancelaura → 在 `staticSpells` 写以 `/` 开头的完整文本，或放入 `specialSpells`。

专精差异：需要保留的宏直接按约定顺序合并进职业数组；宏系统不再写入 `[specIndex]`。

### 5.4 现有职业占用（便于估算偏移）

下表「动态长度」指职业级 `dynamicSpells` 数组长度：

| 职业键 | 格式 | 解析后长度（示例） | 动态槽 | static 第 1 项全局 `i` |
|---|---|---:|---:|---:|
| WARRIOR / HUNTER / ROGUE / DEATHKNIGHT / WARLOCK / DEMONHUNTER | 纯数组 `{}` | 0 | 0 | 1 |
| MAGE | 纯数组 | 1 | 30 | 31 |
| DRUID | 纯数组 | 5 | 150 | 151 |
| PALADIN / SHAMAN / MONK | 纯数组 | 6 | 180 | 181 |
| PRIEST | `common` + `[spec]` | 戒律/神圣 4；暗影 2 | 120 / 60 | 121 / 61 |
| EVOKER | 纯数组 | 7 | 210 | 211 |

## 6. `staticSpells` 规则

### 6.1 数据结构

```lua
staticSpells = {
    "英勇投掷",
    "[@mouseover]保护祝福",
    "[spec:2]圣洁鸣钟;[spec:3]灰烬觉醒",
    "银月城生命药水",                          -- 走 MacroBodies
    "/castsequence reset=0.3 真言术：耀,x",     -- 以 / 开头：完整宏
    "",                                        -- 空串：占位跳过，不建按钮
}
```

- **数组顺序 = 占键顺序**（接在 dynamic 区之后）。
- 普通条目是**拼在 `/cast ` 后面的字符串**，不要自带前导 `/cast `。
- 需要完整命令时，直接写以 `/` 开头的字符串。
- `""` 保留槽位但不创建按钮（例如法师历史上空出的位置）。
- `staticSpells` 本身**不按专精分支**；专精条件写在条目内（如 `[spec:N]`），或依赖 dynamic 长度变化带来的键位偏移。

### 6.2 常见写法（直接抄现有风格）

| 意图 | `staticSpells` 值示例 | 最终宏 |
|---|---|---|
| 默认目标施法 | `"审判"` | `/cast 审判` |
| 自身 | `"[@player]荣耀圣令"` | `/cast [@player]荣耀圣令` |
| 鼠标指向 | `"[@mouseover]破咒祝福"` | `/cast [@mouseover]破咒祝福` |
| 光标地面 | `"[@cursor]乱射"` | `/cast [@cursor]乱射` |
| 焦点优先 | `"[target=focus,exists] 窒息;窒息"` | `/cast [target=focus,exists] 窒息;窒息` |
| 专精分支 | `"[spec:2]圣洁鸣钟;[spec:3]灰烬觉醒"` | `/cast [spec:2]...` |
| 天赋已知 | `"[known:116844,@cursor]平心之环;[known:198898]赤精之歌"` | `/cast [known:...]...` |
| 姿态/形态 | `"[nostance:1]暗影形态"` | `/cast [nostance:1]暗影形态` |
| 引导分流 | `"[channeling]火焰吐息"` / `"[nochanneling]火焰吐息"` | `/cast [channeling]...` |
| 命名药水 | `"银月城生命药水"` | `/cast item:241304` + 第二行 `/cast item:241305`（见 MacroBodies） |
| 完整宏 | `"/stopcasting"` | `/stopcasting`（不加 `/cast`） |
| 占位跳过 | `""` | 不创建 |

## 7. `Fuyutsui.MacroBodies`（命名宏体）

### 7.1 作用

列表里写**可读名称**，实际宏体集中维护，避免多职业重复粘贴物品 ID。

```lua
Fuyutsui.MacroBodies = {
    ["鲁莽药水"] = "item:241288\n/cast item:241289",
    ["银月城生命药水"] = "item:241304\n/cast item:241305",
}
```

在 `staticSpells` / `specialSpells` 中写 `"银月城生命药水"` 即可；`resolveMacroBody` 会查表并（对非 `/` 开头的值）自动加 `/cast `。

### 7.2 新增命名宏体

1. 在 `MacroBodies` 增加 `["名称"] = "宏体片段或完整宏"`。
2. 各职业数组中写 `"名称"`，不要再内联物品 ID。
3. 若宏体已是完整命令（以 `/` 开头），查表后原样使用；否则前缀 `/cast `。

## 8. `specialSpells` 规则

### 8.1 数据结构

```lua
specialSpells = {
    "/castsequence reset=0.5 死亡之握,x", -- 死亡之握
    "/stopcasting", -- 停止施法
}
```

- 同样是**数组**，接在该职业**全部** `staticSpells` 之后依次占键。
- 也走 `resolveMacroBody`：可用完整 `/...` 文本，或 MacroBodies 名称，或普通法术名（会加 `/cast`）。
- Fuyutsui 只使用字符串宏体；Shigure 把同行 `--` 注释作为必须手工维护的技能名，不分析特殊宏正文，并固定生成无目标、无宏条件的 keymap 条目。
- 当前仓库多数职业 `specialSpells = {}`；历史上与 static 同槽的特殊宏（sequence / stopcasting 等）已直接写在 `staticSpells` 对应位置（以 `/` 开头），以保持键位。

### 8.2 何时用 special 而不是写进 static

| 选择 | 适用 |
|---|---|
| 写进 `staticSpells`（`/` 开头或 MacroBodies 名） | 需要固定在 static 区某一相对位置 |
| 追加到 `specialSpells` | 明确接在 static **末尾之后**的额外宏 |

### 8.3 典型完整宏

| 模式 | 示例（文件中已有） |
|---|---|
| castsequence + 哑元 | `"/castsequence reset=0.5 死亡之握,x"` |
| 停施法 | `"/stopcasting"` |
| 取消光环再施法 | `"/cancelaura [spec:4]猎豹形态\n/cast 万灵之召"` |

`castsequence ...,x` 中的 `x` 是占位，用于快速连按重置序列；改此类宏时保持现有 reset 秒数风格，除非有明确需求。

## 9. 决策树：新技能放哪

```text
需要按队友/团队槽位点名？
  ├─ 是 → dynamicSpells
  │         ├─ 全专精都要 → common（或纯数组）
  │         └─ 仅某专精 → [specIndex] 追加表
  └─ 否 → 是否已有 MacroBodies 名称？
            ├─ 是 → staticSpells 写名称
            └─ 否 → 能否写成「/cast + 一段条件/法术名」？
                      ├─ 是 → staticSpells 写条件/法术名
                      └─ 否 → staticSpells 写以 / 开头的完整宏
                                （或追加到 specialSpells 末尾）
```

需要跳过某键：在数组该位置写 `""`。

## 10. 修改检查清单（AI 改完自检）

1. 职业键是否为正确的 `UnitClassBase` 字符串？
2. 三个字段是否都在（空也要 `{}`）？`static`/`special` 是否为数组？`dynamic` 是否为纯数组或合法的 `common`+`[spec]`？
3. 若改了 `dynamicSpells` 解析后长度（含专精表）：是否意识到后续热键整体平移？
4. 若在 `staticSpells` 中间插入/删除（含 `""`）：是否评估了后续键位？
5. 普通 static 值是否**没有**多余的前导 `/cast `？
6. 完整宏是否以 `/` 开头（或走 MacroBodies）？
7. 药水等是否用 MacroBodies 名称，而不是内联 `item:...`？
8. 条件语法是否与现有条目一致（`[@unit]`、`[spec:N]`、`[known:id]`、`[group:...]`）？
9. 法术名是否与游戏客户端中文一致？
10. 新格式 `dynamicSpells`：`common` / `[n]` 的值是否都是**表**（数组），未把字符串直接写在 `[1]` 上覆盖 `common` 数组位？
11. 提醒：战斗中不会更新安全按钮；需脱战 `/reload` 或脱战后切换专精验证。

## 11. 最小示例

### 11.1 无动态（近战输出职业常见）

```lua
WARRIOR = {
    dynamicSpells = {},
    staticSpells = {
        "英勇投掷",
        -- ...
        "拳击",
        "[@focus]拳击",
    },
    specialSpells = {},
}
```

- 第 1 项 → 全局 `i=1` → `/cast 英勇投掷`
- 最后一项 → `/cast [@focus]拳击`

### 11.2 按专精 dynamic + MacroBodies + special

```lua
PRIEST = {
    dynamicSpells = {
        common = { "纯净术", "快速治疗" },
        [1] = { "真言术：盾", "苦修" },           -- 戒律 → 共 4 组，占 120 槽
        [2] = { "愈合祷言", "圣言术：静" },       -- 神圣 → 共 4 组，占 120 槽
        -- 暗影：仅 common → 2 组，占 60 槽；static 起始 i 变为 61
    },
    staticSpells = {
        "心灵震爆",
        -- ...
        "银月城生命药水",
        "渐隐术",
    },
    specialSpells = {
        "/castsequence reset=0.4 真言术：耀,x",
        "/stopcasting",
    },
}
```

### 11.3 旧式纯数组 dynamic（仍有效）

```lua
PALADIN = {
    dynamicSpells = { "神圣震击", "圣光闪现", "圣光术", "荣耀圣令", "清毒术", "圣疗术" },
    staticSpells = { --[=[ ... ]=] },
    specialSpells = {},
}
```

### 11.4 空槽占位

```lua
-- 法师示例：数组中某处写 ""，该全局槽不建按钮，后续项仍按顺序占下一键
staticSpells = {
    "[@cursor]暴风雪",
    "",              -- 占位
    "奥术智慧",
}
```

## 12. 与本文相关、但不要在这里改的东西

| 文件 | 职责 | 改宏时 |
|---|---|---|
| `core/macro.lua` | 热键表、`ClearMacros`、顺序占键、`resolveMacroBody`、安全按钮 | 仅当要改分配规则/键池时才动；须与 C# `FuyutsuiKeymapConverter` 同序 |
| `main.lua` | `LoadPlayerMacros` 按职业读取宏列表 | 一般不动 |
| `core/player.lua` | 加载/切换专精时调用 `LoadPlayerMacros` | 一般不动 |
| `core/keybinds.lua` / `config.lua` keymap | 动作条扫描 → 像素协议 | **另一套**按键编码，与 ClassMacros 覆盖绑定无关 |
| `class/*.lua` | ClassBlocks 色块 | 不放宏 |

## 13. 常见错误

| 错误 | 后果 |
|---|---|
| 普通法术误加 `/cast ` 前缀 | 实际变成 `/cast /cast 火球术` |
| 完整宏未以 `/` 开头且未进 MacroBodies | 被加上 `/cast `，命令错误 |
| 把点名治疗放进 static | 无法按 raid/party 槽位点名 |
| 仍使用 `[36] = "..."` 稀疏表（static） | 与当前 `ipairs` 顺序占键不兼容，中间空洞行为不符合预期 |
| 新格式写成 `"纯净术", "快速治疗", [1]={...}` | Lua 中 `[1]` 覆盖数组第一项，`common` 丢失 |
| 药水内联 `item:...` 而不用 MacroBodies 名称 | 多职业重复、难统一改 ID |
| 在 `dynamicSpells` 或 `staticSpells` 中间插入却不评估偏移 | 后续所有按键语义错位 |
| 假设专精切换后 static 全局键位不变 | dynamic 长度变化会平移；当前设计不占位对齐 |
| 用错职业键（如 `DeathKnight`） | `LoadPlayerMacros` 取不到表，宏不创建 |
| 假设战斗中改表或切专精立即生效 | `InCombatLockdown` 直接 return，按钮不更新 |
| 误以为 special 与 static 仍按同序号覆盖 | 已改为顺序追加；同槽特殊宏应直接写在 static 对应位置 |
