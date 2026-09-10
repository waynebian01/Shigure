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

const string fourFields = "auras = { { name = '光环甲', spellId = 589 }, { name = '光环乙', spellId = 34914 } }";
const string fiveFieldGroup = "group = { state = { 'healthPercent', 'role', 'dispel' }, aura = { { spellId = 194384 }, { spellIds = { 17, 1253593 } } } },";
var (spec, warnings) = Compile(fourFields, fiveFieldGroup);
var config = spec["nameplates"]!.AsObject();
Check(warnings.Count == 0, "Unexpected config warning");
Check(config["start"]!.GetValue<int>() == 155, "Nameplates must follow all 30 group slots including the final offset");
Check(config["num"]!.GetValue<int>() == 4 && config["auraStart"]!.GetValue<int>() == 3, "Two fixed fields plus two auras must use exactly 80 pixels without gaps");
Check(config["healthPercent"]!.GetValue<int>() == 1 && config["range"]!.GetValue<int>() == 2, "生命值/距离 must sit at the hardcoded offsets");

var pixels = new Dictionary<int, int> { [154] = 99, [235] = 88 };
for (var slot = 1; slot <= 20; slot++)
{
    var first = 155 + (slot - 1) * 4;
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
pixels[155] = 0;
Check((bool)Read(config, pixels)["1"]["存在"]!, "Zero health must not imply absence");
// 第 2 个单位整段置黑：start + (2 - 1) * num .. + num - 1。
for (var index = 159; index <= 162; index++) pixels.Remove(index);
var absent = Read(config, pixels)["2"];
Check(!(bool)absent["存在"]! && (int)absent["光环1"]! == 0, "Removed unit retains data");
Check(pixels[154] == 99 && pixels[235] == 88, "Adjacent allocations were changed");
object?[] black = [Color.Black, 0, 0];
Check(!(bool)decode.Invoke(null, black)!, "Cleared nameplate must have no readable index");

// 生命值/距离写死为第 1、2 格，配置里残留的 state 与旧偏移都不再影响布局。
foreach (var (fields, count) in new[]
{
    ("", 2),
    ("auras = { { spellId = 589 } }", 3),
    ("auras = { { spellId = 589 }, { spellId = 34914 } }", 4),
    ("state = {}, auras = { { spellId = 589 } }", 3),
    ("state = { 'range' }", 2),
    ("state = { 'range', 'range', 'unknown', 'healthPercent' }", 2),
    ("healthPercent = 0, range = 4, auras = { { spellId = 589 } }", 3)
})
{
    var (single, _) = Compile(fields);
    var layout = single["nameplates"]!.AsObject();
    Check(layout["start"]!.GetValue<int>() == 4 && layout["num"]!.GetValue<int>() == count, "Fixed fields plus auras reserve the wrong pixel count");
    Check(layout["healthPercent"]!.GetValue<int>() == 1 && layout["range"]!.GetValue<int>() == 2, "Fixed field offsets must never move");
    Check(layout["auraStart"]!.GetValue<int>() == 3, "Auras must always start at the third pixel");
}
// 15 个队伍字段占到 455 格，姓名板再要 80 格就越过 510 格上限。
var oversizedGroup = "group = { healthPercent = 1, aura = { "
    + string.Join(",", Enumerable.Range(1, 14).Select(id => "{ spellId = " + id + " }")) + " } },";
var (overflow, overflowWarnings) = Compile(fourFields, oversizedGroup);
Check(overflow["nameplates"] is null && overflowWarnings.Count > 0, "Overflow must be reported instead of partially transmitting units");
Console.WriteLine("PASS: converter layout, 20-unit main-row round-trip (including index 255/256), zero health, removal, hardcoded fields and overflow.");

var groupLayout = spec["group"]!.AsObject();
Check(!groupLayout.ContainsKey("num"), "Generated group config must not carry a manual num");
Check(groupLayout["auras.194384.value"]!["step"]!.GetValue<int>() == 4
    && groupLayout["auras.17.value"]!["step"]!.GetValue<int>() == 5, "Plain aura list overlaps health/role/dispel");
var readGroup = typeof(StateBuilder).GetMethod("BuildGroup", BindingFlags.NonPublic | BindingFlags.Static)!;
var groupPixels = new Dictionary<int, int> { [5] = 80, [6] = 4, [7] = 1, [8] = 12, [9] = 25,
    [10] = 45, [11] = 2, [12] = 0, [13] = 8, [14] = 20 };
var groups = (Dictionary<string, IReadOnlyDictionary<string, object?>>)readGroup.Invoke(null,
    [groupLayout, groupPixels, new Dictionary<int, int>(), new Dictionary<int, int>()])!;
Check((int)groups["1"]["生命值"]! == 80 && (int)groups["2"]["生命值"]! == 45, "Derived group stride misreads adjacent members");
Check((int)groups["2"]["auras.17.value"]! == 20, "Group aura is read from the wrong pixel");
var legacy = Compile(fourFields, "group = { num = 99, role = 8, aura = { [10] = { spellId = 17 }, [4] = { spellId = 194384 } } },").Item1;
Check(!legacy["group"]!.AsObject().ContainsKey("num")
    && legacy["group"]!["职责"]!["step"]!.GetValue<int>() == 1
    && legacy["group"]!["auras.194384.value"]!["step"]!.GetValue<int>() == 2
    && legacy["nameplates"]!["start"]!.GetValue<int>() == 95, "Legacy num/offsets must not control allocation");
var emptyGroup = Compile(fourFields, "group = { num = 99 },").Item1;
Check(emptyGroup["group"] is null && emptyGroup["nameplates"]!["start"]!.GetValue<int>() == 4, "Empty group reserves pixels");
Console.WriteLine("PASS: automatic group stride, plain aura lists, adjacent members, legacy offsets and following nameplate allocation.");

var store = assembly.GetType("Shigure.ClassBlocksStore")!;
var tempLua = Path.GetTempFileName();
try
{
    foreach (var auraSource in new[]
    {
        "{ { spellId = 194384 }, { spellIds = { 17, 1253593 } } }",
        "{ [8] = { spellIds = { 17, 1253593 } }, [4] = { spellId = 194384 } }"
    })
    {
        File.WriteAllText(tempLua, "Fuyutsui.ClassBlocks = { [1] = { group = { num = 99, healthPercent = 1, role = 2, dispel = 3, aura = " + auraSource + " } } }");
        var document = store.GetMethod("Load")!.Invoke(null, [tempLua])!;
        var saved = (string)store.GetMethod("SerializeClassBlocks")!.Invoke(null,
            [document.GetType().GetProperty("Specs")!.GetValue(document)])!;
        Check(!saved.Contains("num =") && !saved.Contains("[4] =") && !saved.Contains("[8] ="), "Editor serialization reintroduces manual counts/offsets");
        Check(saved.Contains("state = { \"healthPercent\", \"role\", \"dispel\",")
            && !saved.Contains("healthPercent =") && !saved.Contains("role =") && !saved.Contains("dispel ="), "Legacy fields must save as a state list");
        Check(saved.IndexOf("194384", StringComparison.Ordinal) < saved.IndexOf("1253593", StringComparison.Ordinal), "Legacy aura ordering was lost");
        Check(saved.Contains("17, 1253593"), "Multiple spell IDs did not survive editor round-trip");
    }
}
finally
{
    File.Delete(tempLua);
}
Console.WriteLine("PASS: editor load/save retains both new and legacy group aura lists without num or explicit offsets.");

var ordered = Compile(fourFields, "group = { state = { 'dispel', 'role', 'healthPercent' }, healthPercent = 99, aura = { { spellId = 194384 } } },").Item1;
Check(ordered["group"]!["驱散"]!["step"]!.GetValue<int>() == 1
    && ordered["group"]!["职责"]!["step"]!.GetValue<int>() == 2
    && ordered["group"]!["生命值"]!["step"]!.GetValue<int>() == 3
    && ordered["group"]!["auras.194384.value"]!["step"]!.GetValue<int>() == 4, "state order must determine offsets");
var explicitEmpty = Compile(fourFields, "group = { state = {}, healthPercent = 1, role = 2 },").Item1;
Check(explicitEmpty["group"] is null, "Explicit empty state must suppress legacy fields");
var deduped = Compile(fourFields, "group = { state = { 'role', 'role', 'unknown', 'healthPercent' } },").Item1;
Check(deduped["group"]!.AsObject().ContainsKey("职责")
    && deduped["group"]!.AsObject().ContainsKey("生命值")
    && !deduped["group"]!.AsObject().ContainsKey("unknown"), "Unknown and duplicate fields must not reserve pixels");
var reorderedLua = Path.GetTempFileName();
try
{
    File.WriteAllText(reorderedLua, "Fuyutsui.ClassBlocks = { [1] = { group = { state = { 'dispel', 'role', 'healthPercent' } } } }");
    var document = store.GetMethod("Load")!.Invoke(null, [reorderedLua])!;
    var saved = (string)store.GetMethod("SerializeClassBlocks")!.Invoke(null,
        [document.GetType().GetProperty("Specs")!.GetValue(document)])!;
    Check(saved.Contains("state = { \"dispel\", \"role\", \"healthPercent\",")
        && !saved.Contains("healthPercent ="), "Editor must preserve reordered state list on save");
}
finally { File.Delete(reorderedLua); }
Console.WriteLine("PASS: state order/precedence, explicit empty list, duplicate filtering and ordered editor round-trip.");

var nameplateLua = Path.GetTempFileName();
try
{
    foreach (var source in new[]
    {
        "nameplates = { healthPercent = 7, range = 9, auras = { { spellId = 589 } } }",
        "nameplates = { state = { 'range', 'healthPercent' }, auras = { { spellId = 589 } } }",
        "nameplates = { auras = { { spellId = 589 } } }"
    })
    {
        File.WriteAllText(nameplateLua, "Fuyutsui.ClassBlocks = { [1] = { " + source + " } }");
        var document = store.GetMethod("Load")!.Invoke(null, [nameplateLua])!;
        var saved = (string)store.GetMethod("SerializeClassBlocks")!.Invoke(null,
            [document.GetType().GetProperty("Specs")!.GetValue(document)])!;
        Check(saved.Contains("nameplates = {") && saved.Contains("spellId = 589"), "Nameplate aura list was lost on save");
        Check(!saved.Contains("state = {") && !saved.Contains("healthPercent =") && !saved.Contains("range ="),
            "Editor serialization reintroduces nameplate state or manual offsets");
    }
}
finally { File.Delete(nameplateLua); }
Console.WriteLine("PASS: nameplate editor round-trip drops the state list and legacy offsets, keeping only auras.");
