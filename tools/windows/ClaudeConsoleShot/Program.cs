// claude-console-shot — the Windows body of `screencapture -i`: an interactive region capture
// that ends as a PNG at a caller-chosen path. Windows' own picker (the ms-screenclip: overlay)
// can only deliver to the clipboard, so this helper launches it, waits for the clipboard to
// receive the snip, and saves that image to the requested file. The clipboard's previous owner
// is disturbed only if the user actually snips — a cancelled overlay changes nothing.
//
// A separate short-lived process for the same reason as the other helpers: a two-minute wait on
// an overlay does not belong inside the Logi service. A plain console helper like inject and hook:
// the clipboard is read through Win32, never WinForms, so no Desktop Runtime rides along. It once
// did — the WinForms Clipboard class — which made this exe either framework-dependent and broken on
// clean machines (#83) or self-contained and 68 MB.
//
//   claude-console-shot <output.png>               launch the overlay, wait for the snip, save it
//   claude-console-shot --clipboard <output.png>   save the image already on the clipboard (a QA
//                                                  verb: proves the read-and-save path on a machine
//                                                  without anyone drawing a region)
//
// Exit codes (the contract with WindowsPlatformBridge.CaptureScreenshotInteractive):
//   0 image captured and written to the output path
//   1 cancelled (overlay dismissed) or nothing arrived before the deadline
//   2 usage / cannot even start the overlay

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
internal static class ShotProgram
{
    private const Int32 ExitOk = 0;
    private const Int32 ExitNoCapture = 1;
    private const Int32 ExitUsage = 2;

    // The Mac side gives screencapture two minutes; the overlay gets the same patience.
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(120);

    // The overlay lives in one of these processes depending on the Windows build. Watching them
    // is what turns "user pressed Esc" into a fast exit instead of a two-minute hang.
    private static readonly String[] OverlayProcessNames =
        { "ScreenClippingHost", "SnippingTool", "PickerHost" };

    internal static Int32 Main(String[] args)
    {
        if (args.Length >= 2 && args[0] == "--clipboard")
        {
            var now = ReadClipboardPng();
            if (now == null)
            {
                Console.Error.WriteLine("no image on the clipboard");
                return ExitNoCapture;
            }
            return Save(now, args[1]) ? ExitOk : ExitNoCapture;
        }

        if (args.Length < 1 || String.IsNullOrWhiteSpace(args[0]) || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("usage: claude-console-shot <output.png> | --clipboard <output.png>");
            return ExitUsage;
        }

        var outputPath = args[0];
        var baseline = GetClipboardSequenceNumber();

        try
        {
            // UseShellExecute: ms-screenclip: is a protocol, not an executable.
            Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"cannot launch the capture overlay: {ex.Message}");
            return ExitUsage;
        }

        var watch = Stopwatch.StartNew();
        var overlaySeen = false;
        var overlayGoneAt = TimeSpan.Zero;

        while (watch.Elapsed < Deadline)
        {
            Thread.Sleep(200);

            if (GetClipboardSequenceNumber() != baseline)
            {
                // The clipboard moved — but only an IMAGE is a capture. A change without one
                // (the user copied text elsewhere mid-snip) just resets what "changed" means.
                var png = ReadClipboardPng();
                if (png != null)
                {
                    return Save(png, outputPath) ? ExitOk : ExitNoCapture;
                }

                baseline = GetClipboardSequenceNumber();
            }

            // Esc detection: once the overlay process has been seen and is gone again with the
            // clipboard untouched, the user dismissed it — exit now, not at the deadline.
            var overlayUp = OverlayProcessNames.Any(n => Process.GetProcessesByName(n).Length > 0);
            if (overlayUp)
            {
                overlaySeen = true;
                overlayGoneAt = TimeSpan.Zero;
            }
            else if (overlaySeen)
            {
                if (overlayGoneAt == TimeSpan.Zero)
                {
                    overlayGoneAt = watch.Elapsed;
                }
                else if (watch.Elapsed - overlayGoneAt > TimeSpan.FromSeconds(2))
                {
                    Console.Error.WriteLine("capture overlay dismissed without a snip");
                    return ExitNoCapture;
                }
            }
        }

        Console.Error.WriteLine("no capture arrived before the deadline");
        return ExitNoCapture;
    }

    // ---- clipboard ---------------------------------------------------------

    private const UInt32 CF_BITMAP = 2;
    private static readonly UInt32 CF_PNG = RegisterClipboardFormatW("PNG");
    private static readonly Byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// The snip as PNG bytes: the overlay's own "PNG" format when it offers one (exact pixels,
    /// alpha kept), else the bitmap Windows synthesises from the DIB, encoded by GDI+. Null when
    /// neither is there — the clipboard moved for some other reason — or when another process
    /// holds the clipboard open, in which case the next poll retries.
    /// </summary>
    private static Byte[]? ReadClipboardPng()
    {
        if (!OpenClipboardWithRetry())
        {
            Console.Error.WriteLine("clipboard busy (will retry)");
            return null;
        }

        try
        {
            if (CF_PNG != 0 && IsClipboardFormatAvailable(CF_PNG))
            {
                var handle = GetClipboardData(CF_PNG);
                var locked = handle != IntPtr.Zero ? GlobalLock(handle) : IntPtr.Zero;
                if (locked != IntPtr.Zero)
                {
                    try
                    {
                        var bytes = new Byte[(Int32)GlobalSize(handle)];
                        Marshal.Copy(locked, bytes, 0, bytes.Length);
                        var trimmed = TrimToIend(bytes);
                        if (trimmed != null)
                        {
                            return trimmed;
                        }
                    }
                    finally
                    {
                        GlobalUnlock(handle);
                    }
                }
            }

            if (IsClipboardFormatAvailable(CF_BITMAP))
            {
                var hbitmap = GetClipboardData(CF_BITMAP);
                if (hbitmap != IntPtr.Zero)
                {
                    // GDI+ copies the pixels out; the handle stays the clipboard's to free.
                    using var bitmap = Image.FromHbitmap(hbitmap);
                    using var stream = new MemoryStream();
                    bitmap.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"clipboard read failed (will retry): {ex.Message}");
            return null;
        }
        finally
        {
            CloseClipboard();
        }
    }

    // A global allocation is often larger than the PNG inside it; cut at the IEND chunk's CRC so a
    // strict decoder never sees trailing garbage. Null when the bytes are not a PNG at all.
    private static Byte[]? TrimToIend(Byte[] bytes)
    {
        if (bytes.Length < PngSignature.Length || !bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            return null;
        }

        var iend = new Byte[] { (Byte)'I', (Byte)'E', (Byte)'N', (Byte)'D' };
        var at = bytes.AsSpan().LastIndexOf(iend);
        if (at < 0)
        {
            return null;
        }

        var end = Math.Min(bytes.Length, at + iend.Length + 4);   // the chunk's CRC follows its type
        return end == bytes.Length ? bytes : bytes[..end];
    }

    private static Boolean OpenClipboardWithRetry()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                return true;
            }
            Thread.Sleep(40);
        }
        return false;
    }

    private static Boolean Save(Byte[] png, String outputPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!String.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllBytes(outputPath, png);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"cannot write {outputPath}: {ex.Message}");
            return false;
        }
    }

    // ---- Win32 -------------------------------------------------------------

    // The cheap "did anything happen" signal: bumps on every clipboard update, readable without
    // opening the clipboard at all.
    [DllImport("user32.dll")]
    private static extern UInt32 GetClipboardSequenceNumber();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern Boolean OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern Boolean CloseClipboard();

    [DllImport("user32.dll")]
    private static extern Boolean IsClipboardFormatAvailable(UInt32 format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(UInt32 format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern UInt32 RegisterClipboardFormatW(String name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern Boolean GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr handle);
}
