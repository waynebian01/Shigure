using System.Globalization;

namespace Shigure;

internal readonly record struct SpellDatabaseRange(int SourceIndex, int Count);

internal sealed class SpellDatabaseResultSet
{
    private readonly IReadOnlyList<SpellSuggestion> _source;
    private readonly SpellDatabaseRange[]? _ranges;
    private readonly int[]? _indices;

    private SpellDatabaseResultSet(
        IReadOnlyList<SpellSuggestion> source,
        SpellDatabaseRange[]? ranges,
        int[]? indices,
        int count)
    {
        _source = source;
        _ranges = ranges;
        _indices = indices;
        Count = count;
    }

    public static SpellDatabaseResultSet Empty { get; } = new(
        Array.Empty<SpellSuggestion>(),
        Array.Empty<SpellDatabaseRange>(),
        null,
        0);

    public int Count { get; }

    public SpellSuggestion this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (_indices is not null)
            {
                return _source[_indices[index]];
            }

            var remaining = index;
            foreach (var range in _ranges!)
            {
                if (remaining < range.Count)
                {
                    return _source[range.SourceIndex + remaining];
                }

                remaining -= range.Count;
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    public static SpellDatabaseResultSet FromAll(IReadOnlyList<SpellSuggestion> source)
        => source.Count == 0
            ? Empty
            : new SpellDatabaseResultSet(
                source,
                [new SpellDatabaseRange(0, source.Count)],
                null,
                source.Count);

    public static SpellDatabaseResultSet FromRanges(
        IReadOnlyList<SpellSuggestion> source,
        SpellDatabaseRange[] ranges)
    {
        var count = ranges.Sum(range => range.Count);
        return count == 0
            ? Empty
            : new SpellDatabaseResultSet(source, ranges, null, count);
    }

    public static SpellDatabaseResultSet FromIndices(
        IReadOnlyList<SpellSuggestion> source,
        int[] indices)
        => indices.Length == 0
            ? Empty
            : new SpellDatabaseResultSet(source, null, indices, indices.Length);
}

internal static class SpellDatabaseQuery
{
    public static SpellDatabaseResultSet Filter(
        IReadOnlyList<SpellSuggestion> source,
        IReadOnlyDictionary<long, string> registeredNames,
        string query,
        CancellationToken cancellationToken)
    {
        var numeric = query.All(character => character is >= '0' and <= '9');
        if (numeric)
        {
            return FilterByIdPrefix(source, query);
        }

        var indices = new List<int>();
        for (var index = 0; index < source.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var suggestion = source[index];
            var name = string.IsNullOrWhiteSpace(suggestion.Name)
                ? registeredNames.GetValueOrDefault(suggestion.SpellId) ?? string.Empty
                : suggestion.Name;
            if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                indices.Add(index);
            }
        }

        return SpellDatabaseResultSet.FromIndices(source, indices.ToArray());
    }

    private static SpellDatabaseResultSet FilterByIdPrefix(
        IReadOnlyList<SpellSuggestion> source,
        string query)
    {
        if (source.Count == 0
            || query.Length > 19
            || query[0] == '0'
            || !long.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out var prefix)
            || prefix <= 0)
        {
            return SpellDatabaseResultSet.Empty;
        }

        var ranges = new List<SpellDatabaseRange>(19);
        long scale = 1;
        while (prefix <= long.MaxValue / scale)
        {
            var startSpellId = prefix * scale;
            var intervalLength = scale - 1;
            var endSpellId = intervalLength > long.MaxValue - startSpellId
                ? long.MaxValue
                : startSpellId + intervalLength;
            var startIndex = LowerBound(source, startSpellId);
            var endIndex = UpperBound(source, endSpellId);
            if (endIndex > startIndex)
            {
                ranges.Add(new SpellDatabaseRange(startIndex, endIndex - startIndex));
            }

            if (scale > long.MaxValue / 10)
            {
                break;
            }

            scale *= 10;
        }

        return SpellDatabaseResultSet.FromRanges(source, ranges.ToArray());
    }

    private static int LowerBound(IReadOnlyList<SpellSuggestion> source, long spellId)
    {
        var low = 0;
        var high = source.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (source[middle].SpellId < spellId)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static int UpperBound(IReadOnlyList<SpellSuggestion> source, long spellId)
    {
        var low = 0;
        var high = source.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (source[middle].SpellId <= spellId)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
