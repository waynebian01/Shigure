using static Shigure.LuaLiteParser;

namespace Shigure;

/// <summary>替换技的固定目标槽位与 Lua 读取规则，供编辑器、转换器和模块导入共用。</summary>
internal static class OverrideSpellLayout
{
    public const int TargetCount = 5;
    public const string Category = "替换技";
    public const string Prefix = "override.";

    public static string FieldKey(long originalId) => Prefix + originalId;

    public static List<ClassBlocksStore.OverrideEntry> Read(TableValue? table)
    {
        var result = new List<ClassBlocksStore.OverrideEntry>();
        if (table is null) return result;
        foreach (var (key, value) in table.Entries)
        {
            if (key is not long originalId || value is not TableValue entry)
                throw new InvalidDataException("替换技必须以原始 ID 为键，值为配置表。");
            var targets = entry.GetTable("overrideSpellID");
            var item = new ClassBlocksStore.OverrideEntry
            {
                OriginalId = originalId,
                Name = entry.GetString("name")?.Trim() ?? string.Empty
            };
            if (targets is not null)
            {
                foreach (var (slot, target) in targets.Entries)
                {
                    if (slot is not long position || position is < 1 or > TargetCount
                        || target is not NumberValue number || number.Value <= 0
                        || number.Value >= long.MaxValue || number.Value != Math.Truncate(number.Value))
                        throw new InvalidDataException($"替换技 {originalId} 的目标 ID 或槽位无效。");
                    item.TargetIds[position - 1] = (long)number.Value;
                }
            }
            result.Add(item);
        }
        Validate(result);
        return result.OrderBy(entry => entry.OriginalId).ToList();
    }

    public static void Validate(IEnumerable<ClassBlocksStore.OverrideEntry> entries)
    {
        var ids = new HashSet<long>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var row = 0;
        foreach (var entry in entries)
        {
            row++;
            var label = $"替换技第 {row} 行";
            if (entry.OriginalId <= 0 || !ids.Add(entry.OriginalId))
                throw new InvalidDataException($"{label}的原始 ID 必须是正整数且不能重复。");
            if (string.IsNullOrWhiteSpace(entry.Name))
                throw new InvalidDataException($"{label}的名称不能为空。");
            if (!names.Add(entry.Name.Trim()))
                throw new InvalidDataException($"{label}的名称“{entry.Name.Trim()}”重复，替换技名称不能重复。");
            if (entry.TargetIds.Length != TargetCount || !entry.TargetIds.Any(id => id.HasValue))
                throw new InvalidDataException($"{label}必须填写至少一个目标 ID，并保留五个固定槽位。");
            var targets = new HashSet<long>();
            foreach (var target in entry.TargetIds)
                if (target is { } id && (id <= 0 || id == entry.OriginalId || !targets.Add(id)))
                    throw new InvalidDataException($"{label}的目标 ID 必须为正整数，不能重复或等于原始 ID。");
        }
    }
}
