local addon, ns = ...

-- 由当前职业 class/*.lua 覆盖填充
Shingen.spellsList = {}

-- 地下城和团队副本分别编号；未配置的实例输出 0。
-- 按推荐等级排序；完整列表和来源见 Forever/副本列表.md。
Shingen.dungeonIndex = {
    [389] = 1, -- 怒焰裂谷（13–18级）
    [3065] = 2, -- 领主大厅（13–18级）
    [2999] = 3, -- 洛丹伦废墟（15–20级）
    [43] = 4, -- 哀嚎洞穴（15–25级）
    [36] = 5, -- 死亡矿井（18–23级）
    [33] = 6, -- 影牙城堡（22–30级）
    [34] = 7, -- 暴风城监狱（22–30级）
    [2998] = 8, -- 挖掘场：湿地（24–29级）
    [48] = 9, -- 黑暗深渊（24–32级）
    [2959] = 10, -- 达拉然城（28–33级）
    [189] = 11, -- 血色修道院（28–45级）
    [90] = 12, -- 诺莫瑞根（29–38级）
    [47] = 13, -- 剃刀沼泽（30–40级）
    -- 预留编号 14：The Drowned City（35–40级），游戏副本 ID 待核实
    -- 预留编号 15：Krol'dok Stronghold（40–45级），游戏副本 ID 待核实
    [129] = 16, -- 剃刀高地（40–50级）
    [70] = 17, -- 奥达曼（42–52级）
    [209] = 18, -- 祖尔法拉克（44–54级）
    [349] = 19, -- 玛拉顿（45–57级）
    [2994] = 20, -- 奥卡兹监狱（48–53级）
    [109] = 21, -- 阿塔哈卡神庙（50–60级）
    [230] = 22, -- 黑石深渊（52–60级）
    -- 预留编号 23：Blackmaw Hold（55–60级），游戏副本 ID 待核实
    [229] = 24, -- 黑石塔（55–60级）
    [429] = 25, -- 厄运之槌（58–60级）
    [289] = 26, -- 通灵学院（58–60级）
    [3001] = 27, -- 塑造者之台（58–60级）
    [329] = 28, -- 斯坦索姆（58–60级）
}

Shingen.raidIndex = {
    [531] = 1, -- 安其拉神殿（60级）
    [3052] = 2, -- Barrow Deeps（60级）
    [469] = 3, -- 黑翼之巢（60级）
    [2981] = 4, -- Hyjal Summit（60级）
    [409] = 5, -- 熔火之心（60级）
    [533] = 6, -- 纳克萨玛斯（60级）
    [249] = 7, -- 奥妮克希亚的巢穴（60级）
    [509] = 8, -- 安其拉废墟（60级）
    [309] = 9, -- 祖尔格拉布（60级）
}

-- 难度文本
Shingen.difficultyToText = {
    [1] = "5人本普通", -- Normal (Dungeon)
    [2] = "5人本英雄", -- Heroic (Dungeon)
    [14] = "团本普通", -- Normal (Raid)
    [15] = "团本英雄", -- Heroic (Raid)
    [16] = "团本史诗", -- Mythic (Raid)
    [17] = "团本随机", -- Looking (Raid)
    [23] = "5人本史诗", -- Mythic (Dungeon)
}
-- 首领战直接输出事件的 Encounter ID，不再维护序号映射。
-- 能量类型
Shingen.EnumPowerType = {
    ["MANA"] = 0,
    ["RAGE"] = 1,
    ["FOCUS"] = 2,
    ["ENERGY"] = 3,
    ["COMBO_POINTS"] = 4,
    ["RUNES"] = 5,
    ["RUNIC_POWER"] = 6,
    ["SOUL_SHARDS"] = 7,
    ["LUNAR_POWER"] = 8,
    ["HOLY_POWER"] = 9,
    ["MAELSTROM"] = 11,
    ["CHI"] = 12,
    ["INSANITY"] = 13,
    ["BURNING_EMBERS"] = 14,
    ["DEMONIC_FURY"] = 15,
    ["ARCANE_CHARGES"] = 16,
    ["FURY"] = 17,
    ["PAIN"] = 18,
    ["ESSENCE"] = 19,
    ["SHADOW_ORBS"] = 28,
}
-- 动作条
Shingen.actionBars = {
    { startSlot = 1,   endSlot = 12,  bindingPrefix = "ACTIONBUTTON" },
    { startSlot = 13,  endSlot = 24,  bindingPrefix = "ACTIONBUTTON" },
    { startSlot = 25,  endSlot = 36,  bindingPrefix = "MULTIACTIONBAR3BUTTON" },
    { startSlot = 37,  endSlot = 48,  bindingPrefix = "MULTIACTIONBAR4BUTTON" },
    { startSlot = 49,  endSlot = 60,  bindingPrefix = "MULTIACTIONBAR2BUTTON" },
    { startSlot = 61,  endSlot = 72,  bindingPrefix = "MULTIACTIONBAR1BUTTON" },
    { startSlot = 73,  endSlot = 84,  bindingPrefix = "ACTIONBUTTON" }, -- 战斗姿态, 猫形态, 潜行, 暗影
    { startSlot = 85,  endSlot = 96,  bindingPrefix = "ACTIONBUTTON" }, -- 防御姿态,
    { startSlot = 97,  endSlot = 108, bindingPrefix = "ACTIONBUTTON" }, -- 狂暴姿态, 熊形态
    { startSlot = 109, endSlot = 120, bindingPrefix = "ACTIONBUTTON" }, -- 枭兽形态
    { startSlot = 121, endSlot = 143, bindingPrefix = "ACTIONBUTTON" },
    { startSlot = 145, endSlot = 156, bindingPrefix = "MULTIACTIONBAR5BUTTON" },
    { startSlot = 157, endSlot = 168, bindingPrefix = "MULTIACTIONBAR6BUTTON" },
    { startSlot = 169, endSlot = 180, bindingPrefix = "MULTIACTIONBAR7BUTTON" }
}
-- 按键映射
Shingen.keymap = {
    ["1"] = 49,
    ["2"] = 50,
    ["3"] = 51,
    ["4"] = 52,
    ["5"] = 53,
    ["6"] = 54,
    ["7"] = 55,
    ["8"] = 56,
    ["9"] = 57,
    ["0"] = 48,

    ["F1"] = 112,
    ["F2"] = 113,
    ["F3"] = 114,
    ["F4"] = 115,
    ["F5"] = 116,
    ["F6"] = 117,
    ["F7"] = 118,
    ["F8"] = 119,
    ["F9"] = 120,
    ["F10"] = 121,
    ["F11"] = 122,
    ["F12"] = 123,

    ["Q"] = 81,
    ["W"] = 87,
    ["E"] = 69,
    ["R"] = 82,
    ["T"] = 84,
    ["Y"] = 89,
    ["U"] = 85,
    ["I"] = 73,
    ["O"] = 79,
    ["P"] = 80,
    ["A"] = 65,
    ["S"] = 83,
    ["D"] = 68,
    ["F"] = 70,
    ["G"] = 71,
    ["H"] = 72,
    ["J"] = 74,
    ["K"] = 75,
    ["L"] = 76,
    ["Z"] = 90,
    ["X"] = 88,
    ["C"] = 67,
    ["V"] = 86,
    ["B"] = 66,
    ["N"] = 78,
    ["M"] = 77,

    ["NUMPAD0"] = 96,
    ["NUMPAD1"] = 97,
    ["NUMPAD2"] = 98,
    ["NUMPAD3"] = 99,
    ["NUMPAD4"] = 100,
    ["NUMPAD5"] = 101,
    ["NUMPAD6"] = 102,
    ["NUMPAD7"] = 103,
    ["NUMPAD8"] = 104,
    ["NUMPAD9"] = 105,
    ["NUMPADMULTIPLY"] = 106,
    ["NUMPADPLUS"] = 107,
    ["NUMPADMINUS"] = 109,
    ["NUMPADDECIMAL"] = 110,
    ["NUMPADDIVIDE"] = 111,

    ["N0"] = 96,  -- 0x60
    ["N1"] = 97,  -- 0x61
    ["N2"] = 98,  -- 0x62
    ["N3"] = 99,  -- 0x63
    ["N4"] = 100, -- 0x64
    ["N5"] = 101, -- 0x65
    ["N6"] = 102, -- 0x66
    ["N7"] = 103, -- 0x67
    ["N8"] = 104, -- 0x68
    ["N9"] = 105, -- 0x69
    ["N*"] = 106, -- 0x6A
    ["N+"] = 107, -- 0x6B
    ["N-"] = 109, -- 0x6D
    ["N."] = 110, -- 0x6E
    ["N/"] = 111, -- 0x6F

    ["SPACE"] = 32,
    ["="] = 187,
    ["EQUALS"] = 187, -- WoW可能返回EQUALS而不是=
    ["-"] = 189,
    ["MINUS"] = 189,  -- WoW可能返回MINUS而不是-
    ["["] = 219,
    ["]"] = 221,
    ["\\"] = 220,
    [";"] = 186,
    ["SEMICOLON"] = 186, -- WoW可能返回SEMICOLON而不是;
    ["'"] = 222,
    [","] = 188,
    ["COMMA"] = 188,  -- WoW可能返回COMMA而不是,
    ["."] = 190,
    ["PERIOD"] = 190, -- WoW可能返回PERIOD而不是.
    ["/"] = 191,
    ["INSERT"] = 45,
    ["DELETE"] = 46,
    ["HOME"] = 36,
    ["END"] = 35,
    ["PAGEUP"] = 33,
    ["PAGEDOWN"] = 34,
    ["LEFT"] = 37,
    ["UP"] = 38,
    ["RIGHT"] = 39,
    ["DOWN"] = 40,
}
-- 角色类型映射
Shingen.roleMap = {
    ["TANK"] = 1,
    ["HEALER"] = 2,
    ["DAMAGER"] = 3,
    ["NONE"] = 0,
}
