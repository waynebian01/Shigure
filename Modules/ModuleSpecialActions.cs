using System.Globalization;

namespace Shigure;

internal static class ModuleSpecialActions
{
    public const string PauseSpell = "暂停";
    public const string KeyPress = "按键";

    public static bool IsKeyPress(string? spell) => spell?.Trim() == KeyPress;
    public const string InsertSpellState = "插入法术";
    public const string InsertItemState = "插入物品";
    public const string FailedSpell = "自动插入法术";
    public const string FailedItem = "自动插入物品";
    public const string OneKeySpell = "一键法术";
    public const string OneKeyItem = "一键物品";

    public static bool IsPauseSpell(string? spell)
    {
        return string.Equals(spell?.Trim(), PauseSpell, StringComparison.Ordinal);
    }

    public static bool IsFailedSpell(string? spell)
    {
        return string.Equals(spell?.Trim(), FailedSpell, StringComparison.Ordinal);
    }

    public static bool IsFailedItem(string? spell)
    {
        return string.Equals(spell?.Trim(), FailedItem, StringComparison.Ordinal);
    }

    /// <summary>旧模块动作中的“插入法术”迁移为“自动插入法术”；“插入物品”迁移为“自动插入物品”。条件字段不在此处改写。</summary>
    public static string NormalizeSpellAction(string? spell)
    {
        var normalized = spell?.Trim() ?? string.Empty;
        if (string.Equals(normalized, InsertSpellState, StringComparison.Ordinal))
        {
            return FailedSpell;
        }

        return string.Equals(normalized, InsertItemState, StringComparison.Ordinal)
            ? FailedItem
            : normalized;
    }

    public static bool IsOneKeySpell(string? spell)
    {
        return string.Equals(spell?.Trim(), OneKeySpell, StringComparison.Ordinal);
    }

    public static long? GetFailedSpell(GameState state, IReadOnlyDictionary<int, long>? failedSpellMap)
    {
        var failedSpellId = state.GetInt(InsertSpellState);
        if (failedSpellMap is null || !failedSpellMap.TryGetValue(failedSpellId, out var spellId))
        {
            return null;
        }

        return state.Spells.TryGetValue($"{spellId}.{SpellFieldKey.SpellCooldown}", out var cooldown)
            && IsZero(cooldown)
                ? spellId
                : null;
    }

    public static long? GetFailedItem(
        GameState state,
        IReadOnlyDictionary<int, long>? insertItemMap)
    {
        var insertItemIndex = state.GetInt(InsertItemState);
        if (insertItemMap is null || !insertItemMap.TryGetValue(insertItemIndex, out var itemId))
        {
            return null;
        }

        foreach (var (name, trackedItemId) in state.ItemIds)
        {
            if (trackedItemId != itemId)
            {
                continue;
            }

            return IsZero(state.GetValue(name)) ? itemId : null;
        }

        return itemId;
    }

    public static long? GetOneKeySpell(GameState state, IReadOnlyDictionary<int, long>? oneKeySpellMap)
    {
        var oneKeyAssist = state.GetInt("一键辅助");
        return oneKeySpellMap is not null && oneKeySpellMap.TryGetValue(oneKeyAssist, out var spellId)
            ? spellId
            : null;
    }

    private static bool IsZero(object? value)
    {
        return value switch
        {
            int i => i == 0,
            long l => l == 0,
            float f => Math.Abs(f) < float.Epsilon,
            double d => Math.Abs(d) < double.Epsilon,
            decimal m => m == 0,
            bool b => !b,
            string s => TryParseZero(s, out var isZero) && isZero,
            _ => false
        };
    }

    private static bool TryParseZero(string text, out bool isZero)
    {
        isZero = false;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
        {
            return false;
        }

        isZero = Math.Abs(parsed) < double.Epsilon;
        return true;
    }
}
