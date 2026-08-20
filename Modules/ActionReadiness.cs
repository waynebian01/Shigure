using System.Globalization;

namespace Shigure;

/// <summary>
/// 对齐 SimulationCraft 的 action_ready()：APL 条件成立之后，还要确认这一招当前真的能放。
/// 冷却没好、充能为 0、没绑键，都视为不可用并评估下一行。
/// </summary>
internal static class ActionReadiness
{
    public static bool IsSpecialAction(string? spell)
    {
        return ModuleSpecialActions.IsPauseSpell(spell)
            || ModuleSpecialActions.IsFailedSpell(spell)
            || ModuleSpecialActions.IsOneKeySpell(spell);
    }

    /// <summary>
    /// 条件已命中后能否真正执行。暂停由调用方单独处理；其余技能必须有热键且就绪。
    /// </summary>
    public static bool CanExecute(GameState state, string? actionSpell, string? hotkey)
    {
        if (IsSpecialAction(actionSpell))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return false;
        }

        return IsSpellReady(state, actionSpell);
    }

    /// <summary>
    /// 技能当前是否就绪。状态里没有该技能像素时不过滤（无法判断则交给按键与游戏客户端）。
    /// 充能技能以「层数 ≥ 1」为准；同时有冷却像素时还要求冷却为 0。
    /// </summary>
    public static bool IsSpellReady(GameState state, string? spellName)
    {
        var name = spellName?.Trim();
        if (string.IsNullOrWhiteSpace(name) || IsSpecialAction(name))
        {
            return true;
        }

        var spells = state.Spells;
        var hasCharges = spells.TryGetValue(name + "层数", out var chargesValue);
        var hasCooldown = spells.TryGetValue(name, out var cooldownValue);

        if (!hasCharges && !hasCooldown)
        {
            return true;
        }

        if (hasCharges && ToInt(chargesValue) < 1)
        {
            return false;
        }

        if (hasCooldown && !IsZero(cooldownValue))
        {
            return false;
        }

        return true;
    }

    private static int ToInt(object? value)
    {
        return value switch
        {
            int i => i,
            long l => (int)l,
            float f => (int)f,
            double d => (int)d,
            decimal m => (int)m,
            bool b => b ? 1 : 0,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                || int.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed) => parsed,
            _ => 0
        };
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
            string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                || double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out n)
                    ? Math.Abs(n) < double.Epsilon
                    : false,
            _ => false
        };
    }
}
