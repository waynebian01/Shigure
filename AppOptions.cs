namespace Shigure;

public enum SendMode
{
    Switch,
    Click,
    Hold
}

public sealed record AppOptions(
    string WindowTitle,
    string ToggleKey,
    SendMode Mode,
    TimeSpan LogicInterval,
    TimeSpan RenderInterval)
{
    public static AppOptions FromArgs(string[] args)
    {
        var windowTitle = "魔兽世界";
        var toggleKey = "XBUTTON2";
        var mode = SendMode.Switch;
        var logicInterval = TimeSpan.FromMilliseconds(100);
        var renderInterval = TimeSpan.FromMilliseconds(100);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var value = i + 1 < args.Length ? args[i + 1] : null;
            switch (arg)
            {
                case "--window" when value is not null:
                    windowTitle = value;
                    i++;
                    break;
                case "--toggle" when value is not null:
                    toggleKey = value;
                    i++;
                    break;
                case "--mode" when value is not null:
                    mode = ParseMode(value);
                    i++;
                    break;
                case "--logic-ms" when value is not null && int.TryParse(value, out var logicMs):
                    logicInterval = TimeSpan.FromMilliseconds(Math.Max(50, logicMs));
                    i++;
                    break;
                case "--render-ms" when value is not null && int.TryParse(value, out var renderMs):
                    renderInterval = TimeSpan.FromMilliseconds(Math.Max(100, renderMs));
                    i++;
                    break;
            }
        }

        return new AppOptions(windowTitle, toggleKey, mode, logicInterval, renderInterval);
    }

    private static SendMode ParseMode(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "click" => SendMode.Click,
            "hold" => SendMode.Hold,
            _ => SendMode.Switch
        };
    }
}

