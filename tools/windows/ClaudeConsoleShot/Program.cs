// claude-console-shot — the Windows body of `screencapture -i`: an interactive region capture
// that ends as a PNG at a caller-chosen path. Windows' own picker (the ms-screenclip: overlay)
// can only deliver to the clipboard, so this helper launches it, waits for the clipboard to
// receive the snip, and saves that image to the requested file. The clipboard's previous owner
// is disturbed only if the user actually snips — a cancelled overlay changes nothing.
//
// A separate short-lived process for the same reason as the other helpers: clipboard reads want
// an STA thread and WinForms, neither of which belongs inside the Logi service. Same runtime
// posture as claude-console-focus: the executable bundles the Windows Desktop Runtime.
//
// Exit codes (the contract with WindowsPlatformBridge.CaptureScreenshotInteractive):
//   0 image captured and written to the output path
//   1 cancelled (overlay dismissed) or nothing arrived before the deadline
//   2 usage / cannot even start the overlay

using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

internal static class Program
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

    [STAThread]
    private static Int32 Main(String[] args)
    {
        if (args.Length < 1 || String.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("usage: claude-console-shot <output.png>");
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
                var image = TryGetClipboardImage();
                if (image != null)
                {
                    return Save(image, outputPath) ? ExitOk : ExitNoCapture;
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

    private static Image? TryGetClipboardImage()
    {
        try
        {
            return Clipboard.ContainsImage() ? Clipboard.GetImage() : null;
        }
        catch (Exception ex)
        {
            // Another process holds the clipboard open — the next poll retries.
            Console.Error.WriteLine($"clipboard read failed (will retry): {ex.Message}");
            return null;
        }
    }

    private static Boolean Save(Image image, String outputPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!String.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (image)
            {
                image.Save(outputPath, ImageFormat.Png);
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"cannot write {outputPath}: {ex.Message}");
            return false;
        }
    }

    // The cheap "did anything happen" signal: bumps on every clipboard update, readable without
    // opening the clipboard at all.
    [DllImport("user32.dll")]
    private static extern UInt32 GetClipboardSequenceNumber();
}
