namespace Shigure;

public static class ClassNames
{
    private static readonly Dictionary<int, string> Classes = new()
    {
        [1] = "战士",
        [2] = "圣骑士",
        [3] = "猎人",
        [4] = "盗贼",
        [5] = "牧师",
        [6] = "死亡骑士",
        [7] = "萨满",
        [8] = "法师",
        [9] = "术士",
        [10] = "武僧",
        [11] = "德鲁伊",
        [12] = "恶魔猎手",
        [13] = "唤魔师"
    };

    private static readonly Dictionary<(int ClassId, int SpecId), string> Specs = new()
    {
        [(1, 1)] = "武器",
        [(1, 2)] = "狂怒",
        [(1, 3)] = "防护",
        [(2, 1)] = "神圣",
        [(2, 2)] = "防护",
        [(2, 3)] = "惩戒",
        [(3, 1)] = "兽王",
        [(3, 2)] = "射击",
        [(3, 3)] = "生存",
        [(4, 1)] = "刺杀",
        [(4, 2)] = "狂徒",
        [(4, 3)] = "敏锐",
        [(5, 1)] = "戒律",
        [(5, 2)] = "神牧",
        [(5, 3)] = "暗影",
        [(6, 1)] = "鲜血",
        [(6, 2)] = "冰霜",
        [(6, 3)] = "邪恶",
        [(7, 1)] = "元素",
        [(7, 2)] = "增强",
        [(7, 3)] = "奶萨",
        [(8, 1)] = "奥术",
        [(8, 2)] = "火焰",
        [(8, 3)] = "冰霜",
        [(9, 1)] = "痛苦",
        [(9, 2)] = "恶魔",
        [(9, 3)] = "毁灭",
        [(10, 1)] = "酒仙",
        [(10, 2)] = "织雾",
        [(10, 3)] = "踏风",
        [(11, 1)] = "平衡",
        [(11, 2)] = "野性",
        [(11, 3)] = "守护",
        [(11, 4)] = "奶德",
        [(12, 1)] = "浩劫",
        [(12, 2)] = "复仇",
        [(12, 3)] = "噬灭",
        [(13, 1)] = "湮灭",
        [(13, 2)] = "恩护",
        [(13, 3)] = "增辉"
    };

    public static (string? ClassName, string? SpecName) GetClassAndSpecName(int? classId, int? specId)
    {
        var className = classId is null or 0 ? null : Classes.GetValueOrDefault(classId.Value, $"职业{classId}");
        var specName = classId is null or 0 || specId is null or 0
            ? null
            : Specs.GetValueOrDefault((classId.Value, specId.Value), $"专精{specId}");
        return (className, specName);
    }
}

