// The clipboard, the way the macOS helper treats the pasteboard: read what the user copied,
// borrow it for one paste, and put back exactly what was there — never over a newer copy.

using System.Runtime.InteropServices;
using System.Text;

namespace VizhiDesktopUia;

internal sealed class ClipboardSnapshot
{
    public UInt32 Sequence { get; init; }
    public List<(UInt32 Format, Byte[] Data)> Items { get; } = new();
}

internal static class Clipboard
{
    private const Int64 MaxSavedBytes = 16L * 1024 * 1024;
    private static readonly UInt32 PngFormat = Win32.RegisterClipboardFormatW("PNG");

    // Formats Windows synthesises from another, or that are not memory blocks. Saving the
    // source format restores them; copying their handles would not.
    private static readonly HashSet<UInt32> NotSaved = new()
    {
        Win32.CF_BITMAP, Win32.CF_METAFILEPICT, Win32.CF_PALETTE, Win32.CF_ENHMETAFILE, Win32.CF_LOCALE,
        Win32.CF_TEXT, Win32.CF_OEMTEXT, Win32.CF_DIBV5, 0x80, 0x81, 0x82, 0x83, 0x8E,
    };

    private static T? WithClipboard<T>(Func<T?> work) where T : class
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (Win32.OpenClipboard(IntPtr.Zero))
            {
                try { return work(); }
                finally { Win32.CloseClipboard(); }
            }
            Thread.Sleep(30);
        }
        return null;
    }

    public static UInt32 Sequence => Win32.GetClipboardSequenceNumber();

    public static Boolean IsEmpty() => WithClipboard(() => (Object)(Win32.EnumClipboardFormats(0) == 0)) is true;

    private static Byte[]? Bytes(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return null;
        var size = (Int64)Win32.GlobalSize(handle);
        var pointer = Win32.GlobalLock(handle);
        if (pointer == IntPtr.Zero) return null;
        try
        {
            var data = new Byte[size];
            Marshal.Copy(pointer, data, 0, (Int32)size);
            return data;
        }
        finally { Win32.GlobalUnlock(handle); }
    }

    public static String? Text() => WithClipboard(() =>
    {
        var data = Bytes(Win32.GetClipboardData(Win32.CF_UNICODETEXT));
        if (data == null) return null;
        var text = Encoding.Unicode.GetString(data);
        var end = text.IndexOf('\0');
        return end >= 0 ? text[..end] : text;
    });

    public static Boolean HasText() => WithClipboard(() => (Object)(Win32.GetClipboardData(Win32.CF_UNICODETEXT) != IntPtr.Zero)) is true;

    /// <summary>File paths from a CF_HDROP payload, or null when there is none.</summary>
    public static List<String>? Files() => WithClipboard(() =>
    {
        var data = Bytes(Win32.GetClipboardData(Win32.CF_HDROP));
        if (data == null || data.Length < 20) return null;
        var offset = BitConverter.ToInt32(data, 0);
        var wide = BitConverter.ToInt32(data, 16) != 0;
        var files = new List<String>();
        var text = wide ? Encoding.Unicode.GetString(data, offset, data.Length - offset)
            : Encoding.Default.GetString(data, offset, data.Length - offset);
        foreach (var path in text.Split('\0'))
        {
            if (path.Length == 0) break;
            files.Add(path);
        }
        return files;
    });

    /// <summary>PNG bytes when an app put a PNG on the clipboard, else a DIB to convert, else null.</summary>
    public static (Byte[]? Png, Byte[]? Dib) Image()
    {
        var result = WithClipboard(() =>
        {
            var png = Bytes(Win32.GetClipboardData(PngFormat));
            var dib = png == null ? Bytes(Win32.GetClipboardData(Win32.CF_DIB)) : null;
            return (Object)(png, dib);
        });
        return result is ValueTuple<Byte[]?, Byte[]?> pair ? pair : (null, null);
    }

    /// <summary>
    /// Every restorable format and its bytes. Null when something cannot be saved faithfully
    /// (too large, or a clipboard that could not be opened): a paste must not cost the user
    /// what they had copied.
    /// </summary>
    public static ClipboardSnapshot? Save() => WithClipboard(() =>
    {
        var snapshot = new ClipboardSnapshot { Sequence = Win32.GetClipboardSequenceNumber() };
        Int64 total = 0;
        var format = Win32.EnumClipboardFormats(0);
        while (format != 0)
        {
            if (!NotSaved.Contains(format))
            {
                var data = Bytes(Win32.GetClipboardData(format));
                if (data != null)
                {
                    total += data.Length;
                    if (total > MaxSavedBytes) return null;
                    snapshot.Items.Add((format, data));
                }
            }
            format = Win32.EnumClipboardFormats(format);
        }
        return snapshot;
    });

    private static Boolean Put(IEnumerable<(UInt32 Format, Byte[] Data)> items)
    {
        if (!Win32.EmptyClipboard()) return false;
        foreach (var (format, data) in items)
        {
            var handle = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE, (UIntPtr)data.Length);
            if (handle == IntPtr.Zero) return false;
            var pointer = Win32.GlobalLock(handle);
            Marshal.Copy(data, 0, pointer, data.Length);
            Win32.GlobalUnlock(handle);
            if (Win32.SetClipboardData(format, handle) == IntPtr.Zero)
            {
                Win32.GlobalFree(handle);
                return false;
            }
        }
        return true;
    }

    /// <summary>Put the saved contents back, unless someone copied something newer meanwhile.</summary>
    public static Boolean Restore(ClipboardSnapshot saved, UInt32 written)
    {
        if (Win32.GetClipboardSequenceNumber() != written) return false;
        return WithClipboard(() => (Object)Put(saved.Items)) is true;
    }

    public static Boolean WriteText(String text) =>
        WithClipboard(() => (Object)Put(new[] { (Win32.CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + "\0")) })) is true;

    /// <summary>A CF_HDROP payload: the same thing Explorer copies for a file.</summary>
    public static Boolean WriteFiles(IReadOnlyList<String> paths)
    {
        var list = new StringBuilder();
        foreach (var path in paths)
        {
            list.Append(path).Append('\0');
        }
        list.Append('\0');
        var names = Encoding.Unicode.GetBytes(list.ToString());
        var data = new Byte[20 + names.Length];
        BitConverter.GetBytes(20).CopyTo(data, 0);        // pFiles: the list follows the header
        BitConverter.GetBytes(1).CopyTo(data, 16);        // fWide
        names.CopyTo(data, 20);
        return WithClipboard(() => (Object)Put(new[] { (Win32.CF_HDROP, data) })) is true;
    }
}
