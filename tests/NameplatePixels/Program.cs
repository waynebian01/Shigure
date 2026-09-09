using System.Drawing;
using System.Reflection;
using System.Text.Json.Nodes;
using Shigure;

var assembly = typeof(StateBuilder).Assembly;
var parser = assembly.GetType("Shigure.LuaLiteParser")!;
var converter = assembly.GetType("Shigure.FuyutsuiConfigConverter")!;
var compile = converter.GetMethod("CompileSpec", BindingFlags.NonPublic | BindingFlags.Static)!;
var build = typeof(StateBuilder).GetMethod("BuildNameplates", BindingFlags.NonPublic | BindingFlags.Static)!;
var decode = typeof(PixelScanner).GetMethod("TryDecodeTopRowBlock", BindingFlags.NonPublic | BindingFlags.Static)!;

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

(JsonObject, List<string>) Compile(string fields, string group = "")
{
    var source = "fixture = { states = { '锚点', '职业', '专精' }, " + group + " nameplates = { " + fields + " } }";
    var table = parser.GetMethod("ExtractAssignedTable")!.Invoke(null, [source, "fixture"]);
    return ((JsonObject, List<string>))compile.Invoke(null, [table, "test"])!;
}

Dictionary<string, IReadOnlyDictionary<string, object?>> Read(JsonObject config, Dictionary<int, int> pixels)
    => (Dictionary<string, IReadOnlyDictionary<string, object?>>)build.Invoke(null, [config, pixels])!;

const string fourFields = "healthPercent = 7, range = 9, auras = { { name = '光环甲', spellId = 589 }, { name = '光环乙', spellId = 34914 } }";
var (spec, warnings) = Compile(fourFields, "group = { num = 5, healthPercent = 1, role = 2 },");
var config = spec["nameplates"]!.AsObject();
Check(warnings.Count == 0, "Unexpected config warning");
Check(config["start"]!.GetValue<int>() == 205, "Nameplates must follow all 40 group slots including the final offset");
Check(config["num"]!.GetValue<int>() == 4 && config["auraStart"]!.GetValue<int>() == 3, "Four configured fields must use exactly 80 pixels without gaps");
Check(config["healthPercent"]!.GetValue<int>() == 1 && config["range"]!.GetValue<int>() == 2, "Legacy row offsets must be compacted");

var pixels = new Dictionary<int, int> { [204] = 99, [285] = 88 };
for (var slot = 1; slot <= 20; slot++)
{
    var first = 205 + (slot - 1) * 4;
    foreach (var (offset, value) in new[] { (0, slot), (1, slot + 20), (2, slot + 40), (3, slot + 60) })
    {
        var index = first + offset;
        var color = Color.FromArgb(index > 255 ? 1 : 0, index > 255 ? index - 255 : index, value);
        object?[] decodeArgs = [color, 0, 0];
        Check((bool)decode.Invoke(null, decodeArgs)!, "Main-row pixel must decode");
        pixels[(int)decodeArgs[1]!] = (int)decodeArgs[2]!;
    }
}
var plates = Read(config, pixels);
for (var slot = 1; slot <= 20; slot++)
{
    var plate = plates[slot.ToString()];
    Check((bool)plate["存在"]!, "Present unit lost");
    Check((int)plate["生命值"]! == slot && (int)plate["距离"]! == slot + 20, "Unit fields overlap");
    Check((int)plate["光环1"]! == slot + 40 && (int)plate["光环乙"]! == slot + 60, "Aura offset or alias mismatch");
}
pixels[205] = 0;
Check((bool)Read(config, pixels)["1"]["存在"]!, "Zero health must not imply absence");
for (var index = 209; index <= 212; index++) pixels.Remove(index);
var absent = Read(config, pixels)["2"];
Check(!(bool)absent["存在"]! && (int)absent["光环1"]! == 0, "Removed unit retains data");
Check(pixels[204] == 99 && pixels[285] == 88, "Adjacent allocations were changed");
object?[] black = [Color.Black, 0, 0];
Check(!(bool)decode.Invoke(null, black)!, "Cleared nameplate must have no readable index");

foreach (var (fields, count, health, range, auraStart) in new[]
{
    ("healthPercent = 2", 1, 1, 0, 2),
    ("range = 9", 1, 0, 1, 2),
    ("auras = { { spellId = 589 } }", 1, 0, 0, 1),
    ("healthPercent = 0, range = 4, auras = { { spellId = 589 } }", 2, 0, 1, 2)
})
{
    var (single, _) = Compile(fields);
    var layout = single["nameplates"]!.AsObject();
    Check(layout["start"]!.GetValue<int>() == 4 && layout["num"]!.GetValue<int>() == count, "Optional fields reserve empty pixels");
    Check((layout["healthPercent"]?.GetValue<int>() ?? 0) == health && (layout["range"]?.GetValue<int>() ?? 0) == range, "Optional field offsets differ");
    Check(layout["auraStart"]!.GetValue<int>() == auraStart, "Aura-only layout differs");
}
Check(Compile("").Item1["nameplates"] is null, "Empty config must allocate nothing");
var (overflow, overflowWarnings) = Compile(fourFields, "group = { num = 11 },");
Check(overflow["nameplates"] is null && overflowWarnings.Count > 0, "Overflow must be reported instead of partially transmitting units");
Console.WriteLine("PASS: converter layout, 20-unit main-row round-trip (including index 255/256), zero health, removal, optional fields, empty config and overflow.");
