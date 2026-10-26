using System.Globalization;
using System.Text.RegularExpressions;

namespace Shigure;

public static class FormulaEvaluator
{
    internal static IEnumerable<string> ReferencedFields(string? expression)
    {
        var text = NormalizeExpression(expression);
        foreach (Match match in Regex.Matches(text, @"[_$\p{L}][_$\p{L}\p{N}.]*"))
        {
            var end = match.Index + match.Length;
            while (end < text.Length && char.IsWhiteSpace(text[end])) end++;
            if (end == text.Length || text[end] != '(') yield return match.Value;
        }
    }

    public static bool TryEvaluateInt(string? expression, GameState state, out int value, out string? error)
    {
        value = 0;
        error = null;

        var normalized = NormalizeExpression(expression);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            error = "公式为空。";
            return false;
        }

        try
        {
            var parser = new Parser(normalized, state);
            var result = parser.Parse();
            if (double.IsNaN(result) || double.IsInfinity(result))
            {
                error = "公式结果不是有效数字。";
                return false;
            }

            value = (int)result;
            return true;
        }
        catch (FormulaParseException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string NormalizeExpression(string? expression)
    {
        var text = StripComment(expression).Trim();
        var equals = text.IndexOf('=');
        if (equals > 0 && equals < text.Length - 1)
        {
            text = text[(equals + 1)..].Trim();
        }

        return text;
    }

    public static bool TrySplitAssignment(string? expression, out string field, out string formula)
    {
        field = string.Empty;
        formula = NormalizeExpression(expression);
        var text = StripComment(expression).Trim();
        var equals = text.IndexOf('=');
        if (equals <= 0 || equals >= text.Length - 1)
        {
            return false;
        }

        field = text[..equals].Trim();
        return field.Length > 0 && formula.Length > 0;
    }

    private static string StripComment(string? expression)
    {
        var text = expression ?? string.Empty;
        var comment = text.IndexOf('#');
        return comment < 0 ? text : text[..comment];
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly GameState _state;
        private int _position;

        public Parser(string text, GameState state)
        {
            _text = text;
            _state = state;
        }

        public double Parse()
        {
            var value = ParseExpression();
            SkipWhiteSpace();
            if (!IsEnd)
            {
                throw Error($"无法识别“{Current}”。");
            }

            return value;
        }

        private double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhiteSpace();
                if (Match('+'))
                {
                    value += ParseTerm();
                }
                else if (Match('-'))
                {
                    value -= ParseTerm();
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParseTerm()
        {
            var value = ParseFactor();
            while (true)
            {
                SkipWhiteSpace();
                if (Match('*'))
                {
                    value *= ParseFactor();
                }
                else if (Match('/'))
                {
                    var divisor = ParseFactor();
                    if (Math.Abs(divisor) < double.Epsilon)
                    {
                        throw Error("公式中出现除以 0。");
                    }

                    value /= divisor;
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParseFactor()
        {
            SkipWhiteSpace();
            if (Match('+'))
            {
                return ParseFactor();
            }

            if (Match('-'))
            {
                return -ParseFactor();
            }

            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            SkipWhiteSpace();
            if (Match('('))
            {
                var value = ParseExpression();
                Require(')');
                return value;
            }

            if (char.IsDigit(Current) || Current == '.')
            {
                return ParseNumber();
            }

            if (IsIdentifierStart(Current))
            {
                var name = ParseIdentifier();
                SkipWhiteSpace();
                return Match('(') ? ParseFunction(name) : ResolveField(name);
            }

            throw Error("公式不完整。");
        }

        private double ParseFunction(string name)
        {
            var args = new List<double>();
            SkipWhiteSpace();
            if (!Match(')'))
            {
                while (true)
                {
                    args.Add(ParseExpression());
                    SkipWhiteSpace();
                    if (Match(')'))
                    {
                        break;
                    }

                    Require(',');
                }
            }

            return name.ToLowerInvariant() switch
            {
                "int" when args.Count == 1 => (int)args[0],
                "round" when args.Count == 1 => Math.Round(args[0]),
                "floor" when args.Count == 1 => Math.Floor(args[0]),
                "ceil" when args.Count == 1 => Math.Ceiling(args[0]),
                "min" when args.Count > 0 => args.Min(),
                "max" when args.Count > 0 => args.Max(),
                _ => throw Error($"不支持函数“{name}”。")
            };
        }

        private double ParseNumber()
        {
            var start = _position;
            while (char.IsDigit(Current) || Current == '.')
            {
                _position++;
            }

            var text = _text[start.._position];
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out number))
            {
                return number;
            }

            throw Error($"数字“{text}”无效。");
        }

        private string ParseIdentifier()
        {
            var start = _position;
            while (IsIdentifierPart(Current))
            {
                _position++;
            }

            return _text[start.._position].Trim();
        }

        private double ResolveField(string name)
        {
            if (ModuleConditionEvaluator.TryResolveDouble(_state, name, out var value))
            {
                return value;
            }

            throw Error($"无法读取数值“{name}”。");
        }

        private void Require(char expected)
        {
            SkipWhiteSpace();
            if (!Match(expected))
            {
                throw Error($"缺少“{expected}”。");
            }
        }

        private bool Match(char expected)
        {
            if (Current != expected)
            {
                return false;
            }

            _position++;
            return true;
        }

        private void SkipWhiteSpace()
        {
            while (char.IsWhiteSpace(Current))
            {
                _position++;
            }
        }

        private bool IsEnd => _position >= _text.Length;

        private char Current => IsEnd ? '\0' : _text[_position];

        private FormulaParseException Error(string message)
            => new($"{message} 位置: {_position + 1}");

        private static bool IsIdentifierStart(char value)
            => value == '_' || value == '$' || char.IsLetter(value) || IsCjk(value);

        private static bool IsIdentifierPart(char value)
            => IsIdentifierStart(value) || char.IsDigit(value) || value == '.';

        private static bool IsCjk(char value)
            => value is >= '\u3400' and <= '\u9fff';
    }

    private sealed class FormulaParseException : Exception
    {
        public FormulaParseException(string message) : base(message)
        {
        }
    }
}
