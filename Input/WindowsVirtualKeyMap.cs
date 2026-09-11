namespace Shigure;

internal static class WindowsVirtualKeyMap
{
    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SHIFT"] = 0x10,
        ["LSHIFT"] = 0xA0,
        ["RSHIFT"] = 0xA1,
        ["CONTROL"] = 0x11,
        ["CTRL"] = 0x11,
        ["LCTRL"] = 0xA2,
        ["RCTRL"] = 0xA3,
        ["MENU"] = 0x12,
        ["ALT"] = 0x12,
        ["LALT"] = 0xA4,
        ["RALT"] = 0xA5,
        ["XBUTTON1"] = 0x05,
        ["X1"] = 0x05,
        ["MOUSE4"] = 0x05,
        ["XBUTTON2"] = 0x06,
        ["X2"] = 0x06,
        ["MOUSE5"] = 0x06,
        ["F1"] = 0x70,
        ["F2"] = 0x71,
        ["F3"] = 0x72,
        ["F4"] = 0x73,
        ["F5"] = 0x74,
        ["F6"] = 0x75,
        ["F7"] = 0x76,
        ["F8"] = 0x77,
        ["F9"] = 0x78,
        ["F10"] = 0x79,
        ["F11"] = 0x7A,
        ["F12"] = 0x7B,
        ["NUMPAD0"] = 0x60,
        ["NUMPAD1"] = 0x61,
        ["NUMPAD2"] = 0x62,
        ["NUMPAD3"] = 0x63,
        ["NUMPAD4"] = 0x64,
        ["NUMPAD5"] = 0x65,
        ["NUMPAD6"] = 0x66,
        ["NUMPAD7"] = 0x67,
        ["NUMPAD8"] = 0x68,
        ["NUMPAD9"] = 0x69,
        ["NUMPADDECIMAL"] = 0x6E,
        ["NUMPADPLUS"] = 0x6B,
        ["NUMPADMINUS"] = 0x6D,
        ["NUMPADMULTIPLY"] = 0x6A,
        ["NUMPADDIVIDE"] = 0x6F,
        ["INSERT"] = 0x2D,
        ["DELETE"] = 0x2E,
        ["HOME"] = 0x24,
        ["END"] = 0x23,
        ["PAGEUP"] = 0x21,
        ["PAGEDOWN"] = 0x22,
        ["LEFT"] = 0x25,
        ["UP"] = 0x26,
        ["RIGHT"] = 0x27,
        ["DOWN"] = 0x28
    };

    private static readonly Dictionary<string, int> CharacterKeys = new()
    {
        [","] = 0xBC,
        ["."] = 0xBE,
        ["/"] = 0xBF,
        [";"] = 0xBA,
        ["'"] = 0xDE,
        ["["] = 0xDB,
        ["]"] = 0xDD,
        ["="] = 0xBB,
        ["-"] = 0xBD,
        ["`"] = 0xC0,
        ["\\"] = 0xDC
    };

    public static int? Resolve(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return null;
        }

        var key = keyName.Trim();
        if (NamedKeys.TryGetValue(key, out var mapped))
        {
            return mapped;
        }

        if (key.Length != 1)
        {
            return null;
        }

        if (CharacterKeys.TryGetValue(key, out var characterMapped))
        {
            return characterMapped;
        }

        var scan = NativeMethods.VkKeyScanW(key[0]);
        return scan == -1 ? null : scan & 0xFF;
    }
}
