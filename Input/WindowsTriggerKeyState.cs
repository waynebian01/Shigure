namespace Shigure;

internal sealed class WindowsTriggerKeyState : ITriggerKeyState
{
    public int? ResolveVirtualKey(string keyName) => WindowsVirtualKeyMap.Resolve(keyName);

    public TriggerKeySample Read(int virtualKey)
    {
        var state = NativeMethods.GetAsyncKeyState(virtualKey);
        return new(
            IsDown: (state & unchecked((short)0x8000)) != 0,
            WasPressed: (state & 0x0001) != 0);
    }
}
