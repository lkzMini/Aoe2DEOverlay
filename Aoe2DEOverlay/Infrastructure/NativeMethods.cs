using System.Runtime.InteropServices;

namespace Aoe2DEOverlay;

internal static class NativeMethods
{
    internal const int GwlExStyle = -20;
    internal const nint WsExTransparent = 0x00000020;
    internal const nint WsExToolWindow = 0x00000080;
    internal const nint WsExNoActivate = 0x08000000;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;
    internal const int WmHotkey = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint windowHandle, int id);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern nint SetWindowLongPtr(nint windowHandle, int index, nint newLong);
}
