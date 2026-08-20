namespace Shigure;

public enum SendMode
{
    Switch,
    Click,
    Hold
}

public sealed record AppOptions(
    string ToggleKey,
    SendMode Mode,
    string? ModuleId,
    TimeSpan LogicInterval,
    TimeSpan RenderInterval,
    bool BurstAutoOff = true)
{
    public static AppOptions FromArgs(string[] args)
    {
        var toggleKey = "XBUTTON2";
        var mode = SendMode.Switch;
        string? moduleId = null;
        var logicInterval = TimeSpan.FromMilliseconds(100);
        var renderInterval = TimeSpan.FromMilliseconds(100);
        var burstAutoOff = true;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var value = i + 1 < args.Length ? args[i + 1] : null;
            switch (arg)
            {
                case "--toggle" when value is not null:
                    toggleKey = value;
                    i++;
                    break;
                case "--mode" when value is not null:
                    mode = ParseMode(value);
                    i++;
                    break;
                case "--module" when value is not null:
                    moduleId = value;
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
                case "--burst-auto-off" when value is not null:
                    burstAutoOff = ParseBool(value, defaultValue: true);
                    i++;
                    break;
            }
        }

        return new AppOptions(toggleKey, mode, moduleId, logicInterval, renderInterval, burstAutoOff);
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

    private static bool ParseBool(string value, bool defaultValue)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" or "是" => true,
            "0" or "false" or "no" or "off" or "否" => false,
            _ => defaultValue
        };
    }
}
