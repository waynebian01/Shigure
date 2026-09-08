namespace Shigure;

internal sealed class WindowsTriggerKeyState : ITriggerKeyState
{
    private const int GamepadRightTrigger = -1;
    private const int GamepadRightShoulder = -2;
    private const int GamepadLeftTrigger = -3;
    private const int GamepadLeftShoulder = -4;
    private const int GamepadA = -5;
    private const int GamepadB = -6;
    private const int GamepadX = -7;
    private const int GamepadY = -8;
    private const int GamepadDPadUp = -9;
    private const int GamepadDPadDown = -10;
    private const int GamepadDPadLeft = -11;
    private const int GamepadDPadRight = -12;
    private const int GamepadStart = -13;
    private const int GamepadBack = -14;
    private const int GamepadLeftStick = -15;
    private const int GamepadRightStick = -16;

    private static readonly IReadOnlyDictionary<string, int> GamepadKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["GAMEPAD_RT"] = GamepadRightTrigger,
        ["GAMEPAD_RB"] = GamepadRightShoulder,
        ["GAMEPAD_LT"] = GamepadLeftTrigger,
        ["GAMEPAD_LB"] = GamepadLeftShoulder,
        ["GAMEPAD_A"] = GamepadA,
        ["GAMEPAD_B"] = GamepadB,
        ["GAMEPAD_X"] = GamepadX,
        ["GAMEPAD_Y"] = GamepadY,
        ["GAMEPAD_DPAD_UP"] = GamepadDPadUp,
        ["GAMEPAD_DPAD_DOWN"] = GamepadDPadDown,
        ["GAMEPAD_DPAD_LEFT"] = GamepadDPadLeft,
        ["GAMEPAD_DPAD_RIGHT"] = GamepadDPadRight,
        ["GAMEPAD_START"] = GamepadStart,
        ["GAMEPAD_BACK"] = GamepadBack,
        ["GAMEPAD_LS"] = GamepadLeftStick,
        ["GAMEPAD_RS"] = GamepadRightStick
    };

    private static readonly IReadOnlyDictionary<int, ushort> GamepadButtonMasks = new Dictionary<int, ushort>
    {
        [GamepadRightShoulder] = NativeMethods.XInputGamepadRightShoulder,
        [GamepadLeftShoulder] = NativeMethods.XInputGamepadLeftShoulder,
        [GamepadA] = NativeMethods.XInputGamepadA,
        [GamepadB] = NativeMethods.XInputGamepadB,
        [GamepadX] = NativeMethods.XInputGamepadX,
        [GamepadY] = NativeMethods.XInputGamepadY,
        [GamepadDPadUp] = NativeMethods.XInputGamepadDPadUp,
        [GamepadDPadDown] = NativeMethods.XInputGamepadDPadDown,
        [GamepadDPadLeft] = NativeMethods.XInputGamepadDPadLeft,
        [GamepadDPadRight] = NativeMethods.XInputGamepadDPadRight,
        [GamepadStart] = NativeMethods.XInputGamepadStart,
        [GamepadBack] = NativeMethods.XInputGamepadBack,
        [GamepadLeftStick] = NativeMethods.XInputGamepadLeftThumb,
        [GamepadRightStick] = NativeMethods.XInputGamepadRightThumb
    };

    internal static IReadOnlyList<string> GamepadKeyNames { get; } = GamepadKeys.Keys.ToList();

    public int? ResolveVirtualKey(string keyName)
    {
        if (GamepadKeys.TryGetValue(keyName, out var gamepadKey))
        {
            return gamepadKey;
        }

        return WindowsVirtualKeyMap.Resolve(keyName);
    }

    public TriggerKeySample Read(int virtualKey)
    {
        if (virtualKey < 0)
        {
            return ReadGamepad(virtualKey);
        }

        var state = NativeMethods.GetAsyncKeyState(virtualKey);
        return new(
            IsDown: (state & unchecked((short)0x8000)) != 0,
            WasPressed: (state & 0x0001) != 0);
    }

    private static TriggerKeySample ReadGamepad(int triggerKey)
    {
        if (NativeMethods.XInputGetState(0, out var state) != 0)
        {
            return new(false, false);
        }

        var isDown = triggerKey switch
        {
            GamepadRightTrigger => state.Gamepad.RightTrigger > NativeMethods.XInputGamepadTriggerThreshold,
            GamepadLeftTrigger => state.Gamepad.LeftTrigger > NativeMethods.XInputGamepadTriggerThreshold,
            _ => GamepadButtonMasks.TryGetValue(triggerKey, out var buttonMask)
                && (state.Gamepad.Buttons & buttonMask) != 0
        };
        return new(isDown, false);
    }
}
