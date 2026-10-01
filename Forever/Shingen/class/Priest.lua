if UnitClassBase("player") ~= "PRIEST" then return end
local addon, ns = ...

-- 单一职业配置；[1] 仅为像素协议的内部槽位，不代表专精。
Shingen.ClassBlocks = {
    [1] = {
        states = {
            ["状态"] = { "锚点", "职业", "专精", "有效性", "移动", "生命值", "队伍人数", "插入法术", "插入物品" },
            ["能量"] = { "法力值" },
            ["目标"] = { "施法技能", "类型", "生命值", "距离" },
            ["焦点"] = { "施法技能", "生命值" },
        },
        auras = {},
        spells = {
            { spellId = 17, name = "真言术：盾" },
            { spellId = 586, name = "渐隐术" },
            { spellId = 8092, name = "心灵震爆" },
            { spellId = 8122, name = "心灵尖啸" },
            { spellId = 10060, name = "能量灌注" },
            { spellId = 14752, name = "神圣之灵" },
            { spellId = 15237, name = "神圣新星" },
            { spellId = 15286, name = "吸血鬼的拥抱" },
            { spellId = 15473, name = "暗影形态" },
            { spellId = 15487, name = "沉默" },
        },
        items = {},
        group = {},
    },
}

Shingen.spellsList = {
    [2050] = { index = 1, name = "次级治疗术" },
    [1243] = { index = 2, name = "真言术：韧" },
    [585] = { index = 3, name = "惩击" },
    [589] = { index = 4, name = "暗言术：痛" },
    [17] = { index = 5, name = "真言术：盾" },
    [586] = { index = 6, name = "渐隐术" },
    [139] = { index = 7, name = "恢复" },
    [8092] = { index = 8, name = "心灵震爆" },
    [2006] = { index = 9, name = "复活术" },
    [588] = { index = 10, name = "心灵之火" },
    [528] = { index = 11, name = "祛病术" },
    [8122] = { index = 12, name = "心灵尖啸" },
    [2054] = { index = 13, name = "治疗术" },
    [527] = { index = 14, name = "驱散魔法" },
    [2061] = { index = 15, name = "快速治疗" },
    [14914] = { index = 16, name = "神圣之火" },
    [453] = { index = 17, name = "安抚心灵" },
    [9484] = { index = 18, name = "束缚亡灵" },
    [2096] = { index = 19, name = "心灵视界" },
    [8129] = { index = 20, name = "法力燃烧" },
    [605] = { index = 21, name = "精神控制" },
    [596] = { index = 22, name = "治疗祷言" },
    [976] = { index = 23, name = "防护暗影" },
    [552] = { index = 24, name = "驱除疾病" },
    [1706] = { index = 25, name = "漂浮术" },
    [2060] = { index = 26, name = "强效治疗术" },
    [21562] = { index = 27, name = "坚韧祷言" },
    [27683] = { index = 28, name = "暗影防护祷言" },
    [27681] = { index = 29, name = "精神祷言" },
    [10060] = { index = 30, name = "能量灌注" },
    [14752] = { index = 31, name = "神圣之灵" },
    [15237] = { index = 32, name = "神圣新星" },
    [15286] = { index = 33, name = "吸血鬼的拥抱" },
    [15473] = { index = 34, name = "暗影形态" },
    [15487] = { index = 35, name = "沉默" },
}
Shingen.itemsList = {}
