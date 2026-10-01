if UnitClassBase("player") ~= "ROGUE" then return end
local addon, ns = ...

-- 单一职业配置；[1] 仅为像素协议的内部槽位，不代表专精。
Shingen.ClassBlocks = {
    [1] = {
        states = {
            ["状态"] = { "锚点", "职业", "专精", "有效性", "移动", "生命值", "队伍人数", "插入法术", "插入物品" },
            ["能量"] = { "能量值", "连击点" },
            ["目标"] = { "施法技能", "类型", "生命值", "距离" },
            ["焦点"] = { "施法技能", "生命值" },
        },
        auras = {},
        spells = {
            { spellId = 1776, name = "凿击" },
            { spellId = 5277, name = "闪避" },
            { spellId = 2983, name = "疾跑" },
            { spellId = 1766, name = "脚踢" },
            { spellId = 1856, name = "消失" },
            { spellId = 408, name = "肾击" },
            { spellId = 2094, name = "致盲" },
            { spellId = 14177, name = "冷血" },
            { spellId = 13877, name = "剑刃乱舞" },
            { spellId = 13750, name = "冲动" },
            { spellId = 14185, name = "伺机待发" },
            { spellId = 16511, name = "出血" },
            { spellId = 14278, name = "鬼魅攻击" },
        },
        items = {},
        group = {},
    },
}

Shingen.spellsList = {
    [2098] = { index = 1, name = "刺骨" },
    [1752] = { index = 2, name = "影袭" },
    [1784] = { index = 3, name = "潜行" },
    [53] = { index = 4, name = "背刺" },
    [921] = { index = 5, name = "搜索" },
    [1776] = { index = 6, name = "凿击" },
    [5277] = { index = 7, name = "闪避" },
    [6770] = { index = 8, name = "闷棍" },
    [5171] = { index = 9, name = "切割" },
    [2983] = { index = 10, name = "疾跑" },
    [1766] = { index = 11, name = "脚踢" },
    [8647] = { index = 12, name = "破甲" },
    [703] = { index = 13, name = "锁喉" },
    [1966] = { index = 14, name = "佯攻" },
    [8676] = { index = 15, name = "伏击" },
    [1943] = { index = 16, name = "割裂" },
    [1725] = { index = 17, name = "扰乱" },
    [1856] = { index = 18, name = "消失" },
    [1833] = { index = 19, name = "偷袭" },
    [408] = { index = 20, name = "肾击" },
    [2094] = { index = 21, name = "致盲" },
    [14177] = { index = 22, name = "冷血" },
    [13877] = { index = 23, name = "剑刃乱舞" },
    [13750] = { index = 24, name = "冲动" },
    [14185] = { index = 25, name = "伺机待发" },
    [16511] = { index = 26, name = "出血" },
    [14278] = { index = 27, name = "鬼魅攻击" },
}
Shingen.itemsList = {}
