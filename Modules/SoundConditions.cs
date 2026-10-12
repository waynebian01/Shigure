using System.Text.RegularExpressions;

namespace Shigure;

internal static class SoundConditions
{
    private static readonly Regex Comparison = new(
        @"(?:^|&&|\|\|)\s*(?:state\.)?声音识别\s*==\s*(?<name>[^&|]+)", RegexOptions.Compiled);

    public static bool UsesSound(ModuleRule rule) => Expressions(rule)
        .Any(expression => Comparison.IsMatch(expression));

    public static IReadOnlyList<string> GetNames(IEnumerable<ModuleRule> rules) => rules
        .Where(rule => rule.Enabled).SelectMany(Expressions)
        .SelectMany(expression => Comparison.Matches(expression).Cast<Match>())
        .Select(match => match.Groups["name"].Value.Trim().Trim('"', '\''))
        .Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).ToArray();

    private static IEnumerable<string> Expressions(ModuleRule rule)
        => new[] { rule.Condition }.Concat(rule.SubConditions ?? []);
}
