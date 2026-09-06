using System.Runtime.InteropServices;
using System.Text;

namespace Shigure;

internal static class NativeMethods
{
    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    public const uint ProcessQueryLimitedInformation = 0x1000;

    public const uint WmKeyDown = 0x0100;
    public const uint WmKeyUp = 0x0101;
    public const uint WmNcLButtonDown = 0x00A1;
    public const nint HtClient = 1;
    public const nint HtCaption = 2;
    public const nint HtLeft = 10;
    public const nint HtRight = 11;
    public const nint HtTop = 12;
    public const nint HtTopLeft = 13;
    public const nint HtTopRight = 14;
    public const nint HtBottom = 15;
    public const nint HtBottomLeft = 16;
    public const nint HtBottomRight = 17;
    public const nint HwndNotTopmost = -2;
    public const uint SwpNomove = 0x0002;
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoActivate = 0x0010;
    public const ushort XInputGamepadDPadUp = 0x0001;
    public const ushort XInputGamepadDPadDown = 0x0002;
    public const ushort XInputGamepadDPadLeft = 0x0004;
    public const ushort XInputGamepadDPadRight = 0x0008;
    public const ushort XInputGamepadStart = 0x0010;
    public const ushort XInputGamepadBack = 0x0020;
    public const ushort XInputGamepadLeftThumb = 0x0040;
    public const ushort XInputGamepadRightThumb = 0x0080;
    public const ushort XInputGamepadLeftShoulder = 0x0100;
    public const ushort XInputGamepadRightShoulder = 0x0200;
    public const ushort XInputGamepadA = 0x1000;
    public const ushort XInputGamepadB = 0x2000;
    public const ushort XInputGamepadX = 0x4000;
    public const ushort XInputGamepadY = 0x8000;
    public const byte XInputGamepadTriggerThreshold = 30;

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageName(nint hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(nint hWnd, ref Point lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(nint hWnd, out Rect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    public static extern uint XInputGetState(uint userIndex, out XInputState state);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    public static extern nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern short VkKeyScanW(char ch);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetProcessDPIAware();

    public static bool IsKeyDown(int vk)
    {
        return (GetAsyncKeyState(vk) & unchecked((short)0x8000)) != 0;
    }
}
