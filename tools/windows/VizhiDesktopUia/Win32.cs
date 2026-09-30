// Win32 for the helper: windows, foreground, input and the clipboard. Declared once, here;
// the verbs read like the macOS helper's and never see a handle type they do not need.

using System.Runtime.InteropServices;
using System.Text;

namespace VizhiDesktopUia;

internal static class Win32
{
    public const Int32 SW_RESTORE = 9;
    public const UInt32 GW_OWNER = 4;
    public const UInt32 GW_HWNDNEXT = 2;
    public const Int32 GWL_EXSTYLE = -20;
    public const Int64 WS_EX_TOOLWINDOW = 0x80;
    public const Int32 DWMWA_CLOAKED = 14;
    public const UInt32 PW_RENDERFULLCONTENT = 2;

    public delegate Boolean EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public Int32 Left, Top, Right, Bottom; }

    public static String WindowTitle(IntPtr hwnd)
    {
        var buffer = new StringBuilder(512);
        var length = GetWindowTextW(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : "";
    }

    public static String WindowClass(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        var length = GetClassNameW(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : "";
    }

    public static Boolean IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(Int32)) == 0 && cloaked != 0;

    public static Int32 PidOf(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return (Int32)pid;
    }

    public static Int32 ForegroundPid() => PidOf(GetForegroundWindow());

    /// <summary>A top-level window a user could switch to: visible, unowned, uncloaked, titled, not a tool window.</summary>
    public static Boolean IsAppWindow(IntPtr hwnd) =>
        IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == IntPtr.Zero
        && (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) == 0 && !IsCloaked(hwnd)
        && WindowTitle(hwnd).Length > 0;

    /// <summary>Every top-level window, front to back.</summary>
    public static List<IntPtr> TopLevelWindows()
    {
        var windows = new List<IntPtr>();
        EnumWindows((hwnd, _) => { windows.Add(hwnd); return true; }, IntPtr.Zero);
        return windows;
    }

    // ---- keyboard ------------------------------------------------------------

    private const UInt32 INPUT_KEYBOARD = 1;
    private const UInt32 KEYEVENTF_KEYUP = 2;
    public const UInt16 VK_CONTROL = 0x11;
    public const UInt16 VK_END = 0x23;
    public const UInt16 VK_C = 0x43;
    public const UInt16 VK_V = 0x56;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public UInt16 VirtualKey;
        public UInt16 ScanCode;
        public UInt32 Flags;
        public UInt32 Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT
    {
        [FieldOffset(0)] public UInt32 Type;
        [FieldOffset(8)] public KEYBDINPUT Keyboard;
    }

    /// <summary>
    /// One chord — a modifier held around one key — as a single SendInput call, so the four
    /// events cannot interleave with anything else. The caller has verified where the focus
    /// is; this is the keystroke, nothing more.
    /// </summary>
    public static Boolean Chord(UInt16 modifier, UInt16 key)
    {
        INPUT Key(UInt16 vk, Boolean up) => new INPUT
        {
            Type = INPUT_KEYBOARD,
            Keyboard = new KEYBDINPUT { VirtualKey = vk, Flags = up ? KEYEVENTF_KEYUP : 0 },
        };
        var inputs = new[] { Key(modifier, false), Key(key, false), Key(key, true), Key(modifier, true) };
        return SendInput((UInt32)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    public static Boolean Tap(UInt16 key)
    {
        var inputs = new[]
        {
            new INPUT { Type = INPUT_KEYBOARD, Keyboard = new KEYBDINPUT { VirtualKey = key } },
            new INPUT { Type = INPUT_KEYBOARD, Keyboard = new KEYBDINPUT { VirtualKey = key, Flags = KEYEVENTF_KEYUP } },
        };
        return SendInput((UInt32)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    // ---- user32 / dwmapi / gdi -----------------------------------------------

    [DllImport("user32.dll")] public static extern Boolean EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern UInt32 GetWindowThreadProcessId(IntPtr hwnd, out UInt32 processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern Boolean IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern Boolean IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, UInt32 command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, Int32 index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern Int32 GetWindowTextW(IntPtr hwnd, StringBuilder text, Int32 max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern Int32 GetClassNameW(IntPtr hwnd, StringBuilder text, Int32 max);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern Boolean SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern Boolean ShowWindow(IntPtr hwnd, Int32 command);
    [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr hwnd, Boolean altTab);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern Boolean IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern Boolean GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern Boolean PrintWindow(IntPtr hwnd, IntPtr hdc, UInt32 flags);
    [DllImport("user32.dll")] private static extern UInt32 SendInput(UInt32 count, INPUT[] inputs, Int32 size);
    [DllImport("dwmapi.dll")] private static extern Int32 DwmGetWindowAttribute(IntPtr hwnd, Int32 attribute, out Int32 value, Int32 size);

    // ---- clipboard -------------------------------------------------------------

    public const UInt32 CF_TEXT = 1;
    public const UInt32 CF_BITMAP = 2;
    public const UInt32 CF_METAFILEPICT = 3;
    public const UInt32 CF_OEMTEXT = 7;
    public const UInt32 CF_DIB = 8;
    public const UInt32 CF_PALETTE = 9;
    public const UInt32 CF_UNICODETEXT = 13;
    public const UInt32 CF_ENHMETAFILE = 14;
    public const UInt32 CF_HDROP = 15;
    public const UInt32 CF_LOCALE = 16;
    public const UInt32 CF_DIBV5 = 17;
    public const UInt32 GMEM_MOVEABLE = 2;

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern Boolean OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern Boolean CloseClipboard();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern Boolean EmptyClipboard();
    [DllImport("user32.dll")] public static extern UInt32 EnumClipboardFormats(UInt32 format);
    [DllImport("user32.dll")] public static extern IntPtr GetClipboardData(UInt32 format);
    [DllImport("user32.dll")] public static extern IntPtr SetClipboardData(UInt32 format, IntPtr handle);
    [DllImport("user32.dll")] public static extern UInt32 GetClipboardSequenceNumber();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern UInt32 RegisterClipboardFormatW(String name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern Int32 GetClipboardFormatNameW(UInt32 format, StringBuilder name, Int32 max);
    [DllImport("kernel32.dll")] public static extern IntPtr GlobalAlloc(UInt32 flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] public static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern Boolean GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")] public static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll")] public static extern IntPtr GlobalFree(IntPtr handle);
}
