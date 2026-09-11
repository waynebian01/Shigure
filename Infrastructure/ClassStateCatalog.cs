namespace Shigure;

/// <summary>
/// ClassBlocks states 可选字段目录。界面使用单级分类；
/// 保存时按 Lua 原格式写入同名分类表，并保持固定分类顺序。
/// </summary>
internal static class ClassStateCatalog
{
    public const string CategoryState = "状态";
    public const string CategoryPlayerDisplay = "玩家";
    public const string CategorySpecial = "特殊";
    public const string CategoryConfig = "配置开关";
    public const string CategoryItem = "物品";
    public const string CategoryResource = "能量";
    public const string CategoryTarget = "目标";
    public const string CategoryFocus = "焦点";
    public const string CategoryMouseover = "鼠标";
    public const string CategoryPet = "宠物";
    public const string CategoryBoss1 = "首领1";
    public const string CategoryBoss2 = "首领2";
    public const string CategoryBoss3 = "首领3";
    public const string CategoryBoss4 = "首领4";
    public const string CategoryBoss5 = "首领5";

    private static readonly IReadOnlyDictionary<string, long> LegacyItemIds =
        new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["治疗药水"] = 241304,
            ["魔法药水"] = 241301,
            ["治疗石"] = 5512,
            ["鲁莽药水"] = 241288,
            ["圣光潜力"] = 241308
        };

    public static readonly string[] TopCategories =
    [
        CategoryState,
        CategorySpecial,
        CategoryResource,
        CategoryConfig,
        CategoryTarget,
        CategoryFocus,
        CategoryMouseover,
        CategoryPet,
        CategoryBoss1,
        CategoryBoss2,
        CategoryBoss3,
        CategoryBoss4,
        CategoryBoss5
    ];

    private static readonly (string Category, string[] Names)[] Categories =
    [
        (CategoryState,
        [
            "职业", "专精", "有效性", "战斗时间", "移动",
            "生命值", "一键辅助", "插入法术", "插入物品", "队伍类型", "队伍人数",
            "首领战", "难度", "英雄天赋", "施法目标", "施法技能",
            "敌人数量", "敌人数-无仇恨", "敌人数-有仇恨",
            "施法(正计时)", "施法(倒计时)", "引导", "蓄力", "蓄力层数",
            "上个技能", "公共冷却",
        ]),
        (CategorySpecial,
        [
            "计时器", "循环计时器", "战斗计时(秒)", "战斗计时(分)",
            "酒池", "符文", "姿态", "神圣军备", "自律", "天启骑士数量",
            "英勇打击", "吸血鬼打击", "收割者战刃", "沸点",
            "风暴涌流图腾", "风暴涌流图腾数量", "治疗之泉图腾", "治疗之泉图腾数量",
        ]),
        (CategoryConfig,
        [
            "爆发开关", "AOE开关", "输出模式", "爆发药水开关", "延迟"
        ]),
        (CategoryResource,
        [
            "法力值", "怒气值", "集中值", "能量值", "符文", "符文能量",
            "星界能量", "漩涡值", "狂乱值", "奥术充能", "恶魔之怒", "痛苦值",
            "连击点", "神圣能量", "精华能量", "灵魂碎片", "真气", "增压层数"
        ]),
        (CategoryTarget,
        [
            "类型", "驱散类型", "生命值", "距离", "施法(倒计时)", "施法(正计时)", "施法可打断", "引导", "引导可打断"
        ]),
        (CategoryFocus,
        [
            "类型", "驱散类型", "生命值", "距离", "施法(倒计时)", "施法(正计时)", "施法可打断", "引导", "引导可打断"
        ]),
        (CategoryMouseover,
        [
            "类型", "驱散类型", "生命值", "距离", "施法(倒计时)", "施法(正计时)", "施法可打断", "引导", "引导可打断"
        ]),
        (CategoryPet,
        [
            "存在", "生命值"
        ]),
        (CategoryBoss1,
        [
            "类型", "驱散类型", "生命值", "距离", "施法(倒计时)", "施法(正计时)", "施法可打断", "引导", "引导可打断"
        ]),
        (CategoryBoss2,
        [
            "类型", "驱散类型", "生命值", "距离", "施法(倒计时)", "施法(正计时)", "施法可打断", "引导", "引导可打断"
        ]),
        (CategoryBoss3,
        [
            "类型", "驱散类型", "生命值", "距离", "施法(倒计时)", "施法(正计时)", "施法可打断", "引导", "引导可打断"
        ]),
        (CategoryBoss4,
        [
            "类型", "驱散类型", "生命值", "距离", "施法(倒计时)", "施法(正计时)", "施法可打断", "引导", "引导可打断"
        ]),
        (CategoryBoss5,
        [
            "类型", "驱散类型", "生命值", "距离", "施法(倒计时)", "施法(正计时)", "施法可打断", "引导", "引导可打断"
        ])
    ];

    public static IReadOnlyList<StateOption> GetOptions(string category)
    {
        var definition = Categories.FirstOrDefault(item =>
            string.Equals(item.Category, category, StringComparison.Ordinal));
        return definition.Names is null
            ? []
            : definition.Names.Select(name => new StateOption(category, name)).ToList();
    }

    public static IReadOnlyList<StateOption> GetAllOptions(string category)
        => GetOptions(category);

    public static string GetCategoryDisplayName(string category)
        => string.Equals(category, CategoryState, StringComparison.Ordinal)
            ? CategoryPlayerDisplay
            : category;

    public static string GetStorageCategoryFromDisplay(string displayName)
        => string.Equals(displayName, CategoryPlayerDisplay, StringComparison.Ordinal)
            ? CategoryState
            : displayName;

    public static bool IsKnown(string category, string name)
        => !string.IsNullOrWhiteSpace(name)
           && GetNames(category).Contains(name, StringComparer.Ordinal);

    public static string? FindCategory(string name)
    {
        foreach (var (category, names) in Categories)
        {
            if (names.Contains(name, StringComparer.Ordinal))
            {
                return category;
            }
        }

        return null;
    }

    public static string ClassifyField(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return CategoryState;
        }

        foreach (var category in TopCategories)
        {
            if (IsUnitPrefixCategory(category) && name.StartsWith(category, StringComparison.Ordinal))
            {
                return category;
            }
        }

        return FindCategory(name) ?? CategoryState;
    }

    public static bool IsUnitPrefixCategory(string category)
        => category is CategoryTarget
            or CategoryFocus
            or CategoryMouseover
            or CategoryPet
            or CategoryBoss1
            or CategoryBoss2
            or CategoryBoss3
            or CategoryBoss4
            or CategoryBoss5;

    public static bool IsInCategory(string name, string category)
    {
        if (IsKnown(category, name))
        {
            return true;
        }

        // Lua 中已有但目录尚未收录的状态仍放在“状态”中，保证可见、可编辑。
        return string.Equals(category, CategoryState, StringComparison.Ordinal)
               && FindCategory(name) is null;
    }

    public static string GetStorageCategory(string category)
        => category;

    public static bool TryGetLegacyItemId(string name, out long itemId)
        => LegacyItemIds.TryGetValue(name, out itemId);

    private static string[] GetNames(string category)
        => Categories.FirstOrDefault(item =>
            string.Equals(item.Category, category, StringComparison.Ordinal)).Names ?? [];

    public sealed record StateOption(string Category, string Name)
    {
        public string Display => string.Equals(Category, "未识别", StringComparison.Ordinal)
            ? $"未识别 · {Name}"
            : Name;

        public override string ToString() => Display;
    }
}
