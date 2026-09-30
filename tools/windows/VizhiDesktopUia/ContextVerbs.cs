// Context in and out of the chat: the clipboard, the app you were just in, a screenshot, and
// Copy Reply. The macOS helper's context-* and copy-reply verbs, on Win32 and UIA.
//
// These verbs run with --app @frontmost on macOS: they act on whatever is in front, and the
// chat app is only the process they must NOT capture from (--process names it here).

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VizhiDesktopUia;

[SupportedOSPlatform("windows")]
internal static partial class Program
{
    private const Int32 MaxCaptureText = 50000;
    private const Int64 MaxClipboardImage = 50L * 1024 * 1024;

    private static UiaMatching.ReplyRules ReplyRules(Dictionary<String, List<String>> options) => new(
        Values(options, "--assistant-heading"), Values(options, "--user-heading"),
        Values(options, "--copy-response"), Values(options, "--copy-button"), Values(options, "--copy-completed"),
        Values(options, "--response-action"), Values(options, "--stop"), Values(options, "--voice-end"),
        Values(options, "--approve"), Value(options, "--conv-marker") ?? "",
        Values(options, "--state-awaiting"), Values(options, "--state-running"));

    /// <summary>
    /// Copy only an identifiable, completed assistant answer: press its own Copy control and
    /// take what the app put on the clipboard, once the app acknowledged the copy.
    /// </summary>
    private static Int32 CopyReply(Target target, Dictionary<String, List<String>> options)
    {
        if (!AppIsFrontmost(target)) return Fail("app-not-frontmost", ExitNoMatch);
        var initial = WaitForSurface(target, 2000);
        if (initial == null || AppWindows(_appPids).Count != 1) return Fail("surface-unavailable", ExitNoMatch);
        var rules = ReplyRules(options);
        var (candidate, error) = UiaMatching.ReplyTarget(initial.Nodes, rules);
        if (candidate == null) return Fail(error, ExitNoMatch);
        var conversation = UiaMatching.SelectedConversation(initial.Nodes, rules.ConversationMarker);

        Boolean Confirmed(Boolean afterCopy)
        {
            if (!AppIsFrontmost(target) || Win32.WindowTitle(target.Hwnd) != target.Title) return false;
            var fresh = ScanTarget(target);
            if (!fresh.Surface || UiaMatching.SelectedConversation(fresh.Nodes, rules.ConversationMarker) != conversation) return false;
            var current = UiaMatching.ReplyTarget(fresh.Nodes, rules, afterCopy ? candidate : null).Node;
            return current != null && SameElement(current, candidate);
        }

        var before = Clipboard.Sequence;
        if (!Confirmed(false)) return Fail("answer-changed", ExitNoMatch);
        if (!Invoke(candidate)) return Fail("copy-unconfirmed", ExitError);
        var deadline = Stopwatch.StartNew();
        var acknowledged = false;
        while ((Clipboard.Sequence == before || !acknowledged) && deadline.ElapsedMilliseconds < 1200)
        {
            Thread.Sleep(40);
            // The pressed button relabels to Copied for a moment: the app's own receipt.
            acknowledged = rules.CopyCompleted.Count > 0 && candidate.Handle is IUIAutomationElement element
                && element.GetCurrentPropertyValue(UiaIds.Name) is String name
                && rules.CopyCompleted.Any(c => UiaMatching.NormalizeLabel(c) == UiaMatching.NormalizeLabel(name));
        }
        if (!Confirmed(true)) return Fail("answer-changed", ExitNoMatch);
        var revision = Clipboard.Sequence;
        var text = Clipboard.Text();
        if (revision == before || String.IsNullOrWhiteSpace(text) || text.Length > MaxCaptureText || Clipboard.Sequence != revision)
        {
            return Fail("copy-unconfirmed", ExitError);
        }
        // Leave the app's own clipboard payload intact. Only the plain text is retained.
        return Emit(new Dictionary<String, Object?> { ["text"] = text });
    }

    // ---- context-* -----------------------------------------------------------

    private sealed record SourceWindow(Int32 Pid, Int64 Hwnd, String Title)
    {
        public String Encode()
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("pid", this.Pid.ToString());
                writer.WriteString("hwnd", this.Hwnd.ToString());
                writer.WriteString("title", this.Title);
                writer.WriteEndObject();
            }
            return Convert.ToBase64String(stream.ToArray());
        }

        public static SourceWindow? Decode(String? encoded)
        {
            try
            {
                using var document = JsonDocument.Parse(Convert.FromBase64String(encoded ?? ""));
                var root = document.RootElement;
                return new SourceWindow(Int32.Parse(root.GetProperty("pid").GetString()!),
                    Int64.Parse(root.GetProperty("hwnd").GetString()!), root.GetProperty("title").GetString() ?? "");
            }
            catch (Exception ex) when (ex is FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return null;
            }
        }

        public Boolean Alive() => Win32.IsWindow(new IntPtr(this.Hwnd)) && Win32.PidOf(new IntPtr(this.Hwnd)) == this.Pid
            && Win32.WindowTitle(new IntPtr(this.Hwnd)) == this.Title;
    }

    private static String AppName(Int32 pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var description = process.MainModule?.FileVersionInfo.FileDescription;
            return String.IsNullOrWhiteSpace(description) ? process.ProcessName : description;
        }
        catch { return "Source app"; }
    }

    private static String CaptureDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var directory = Path.Combine(home, ".claude", "claude-console", "desktop-captures");
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// Keypad-only capture, no picker: the window that matters is the one just behind the chat
    /// app, the app you were in. The window list is front to back; PrintWindow renders that
    /// window's own contents, even where the chat app covers it.
    /// </summary>
    private static SourceWindow? WindowBehind()
    {
        var self = Environment.ProcessId;
        foreach (var hwnd in Win32.TopLevelWindows())
        {
            var pid = Win32.PidOf(hwnd);
            if (pid == self || _appPids.Contains(pid) || !Win32.IsAppWindow(hwnd) || Win32.IsIconic(hwnd)) continue;
            Win32.GetWindowRect(hwnd, out var rect);
            if (rect.Right - rect.Left < 80 || rect.Bottom - rect.Top < 80) continue;
            return new SourceWindow(pid, hwnd.ToInt64(), Win32.WindowTitle(hwnd));
        }
        return null;
    }

    private static String? CaptureWindow(SourceWindow source)
    {
        var hwnd = new IntPtr(source.Hwnd);
        if (!Win32.GetWindowRect(hwnd, out var rect)) return null;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return null;
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var hdc = graphics.GetHdc();
            var ok = Win32.PrintWindow(hwnd, hdc, Win32.PW_RENDERFULLCONTENT);
            graphics.ReleaseHdc(hdc);
            if (!ok) return null;
        }
        var path = Path.Combine(CaptureDirectory(), "Vizhi-" + Guid.NewGuid().ToString("N") + ".png");
        bitmap.Save(path, ImageFormat.Png);
        return new FileInfo(path).Length > 0 ? path : null;
    }

    /// <summary>The system snip, through the shared toolkit that already owns that dance for the terminal products.</summary>
    private static String? CaptureRegion()
    {
        var toolkit = Path.Combine(AppContext.BaseDirectory, "claude-console-tools.exe");
        if (!File.Exists(toolkit)) return null;
        var path = Path.Combine(CaptureDirectory(), "Vizhi-" + Guid.NewGuid().ToString("N") + ".png");
        using var process = Process.Start(new ProcessStartInfo(toolkit) { ArgumentList = { "shot", path }, UseShellExecute = false, CreateNoWindow = true });
        if (process == null) return null;
        if (!process.WaitForExit(120000))
        {
            try { process.Kill(true); } catch { }
            return null;
        }
        return process.ExitCode == 0 && File.Exists(path) && new FileInfo(path).Length > 0 ? path : null;
    }

    private static String? SavePng(Byte[]? png, Byte[]? dib)
    {
        Byte[] data;
        if (png != null)
        {
            data = png;
        }
        else if (dib != null && dib.Length > 40)
        {
            // A DIB is a BMP without its file header; add one and let GDI+ re-encode it.
            var headerSize = BitConverter.ToInt32(dib, 0);
            var bitCount = BitConverter.ToInt16(dib, 14);
            var compression = BitConverter.ToInt32(dib, 16);
            var colorsUsed = BitConverter.ToInt32(dib, 32);
            var palette = colorsUsed != 0 ? colorsUsed : bitCount <= 8 ? 1 << bitCount : 0;
            var masks = compression == 3 && headerSize == 40 ? 12 : 0;
            var offset = 14 + headerSize + masks + palette * 4;
            var bmp = new Byte[14 + dib.Length];
            bmp[0] = (Byte)'B'; bmp[1] = (Byte)'M';
            BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
            BitConverter.GetBytes(offset).CopyTo(bmp, 10);
            dib.CopyTo(bmp, 14);
            using var stream = new MemoryStream(bmp);
            using var image = new Bitmap(stream);
            if (image.Width <= 0 || image.Height <= 0 || image.Width > 16384 || image.Height > 16384 || (Int64)image.Width * image.Height > 40_000_000) return null;
            using var output = new MemoryStream();
            image.Save(output, ImageFormat.Png);
            data = output.ToArray();
        }
        else
        {
            return null;
        }
        if (data.Length == 0 || data.Length > MaxClipboardImage) return null;
        var path = Path.Combine(CaptureDirectory(), "Clipboard-" + Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant() + ".png");
        File.WriteAllBytes(path, data);
        return path;
    }

    private static Int32 Context(String action, Dictionary<String, List<String>> options)
    {
        switch (action)
        {
            case "return": return ContextReturn(options);
            case "paste": return ContextPaste(options);
            case "window": return ContextWindow();
            case "screenshot": return ContextScreenshot();
            case "clipboard": return ContextClipboard();
            case "selection": return ContextSelection();
            default: return Fail("unsupported", ExitNoMatch);
        }
    }

    private static Int32 ContextReturn(Dictionary<String, List<String>> options)
    {
        var source = SourceWindow.Decode(Value(options, "--source"));
        if (source == null || !source.Alive()) return Fail("source-closed", ExitNoMatch);
        return Raise(new IntPtr(source.Hwnd)) && Win32.GetForegroundWindow() == new IntPtr(source.Hwnd)
            ? Emit(new Dictionary<String, Object?>()) : Fail("source-changed", ExitNoMatch);
    }

    /// <summary>
    /// Paste the reply into the empty field the user has selected in the original app. The
    /// value pattern when the field offers one; else the clipboard and one Ctrl+V, given back
    /// afterwards. Either way the result is read back before it is reported.
    /// </summary>
    private static Int32 ContextPaste(Dictionary<String, List<String>> options)
    {
        var source = SourceWindow.Decode(Value(options, "--source"));
        var text = Value(options, "--text") ?? "";
        if (source == null || !source.Alive()) return Fail("source-closed", ExitNoMatch);
        if (Win32.ForegroundPid() != source.Pid || Win32.GetForegroundWindow() != new IntPtr(source.Hwnd)) return Fail("app-not-frontmost", ExitNoMatch);
        if (text.Length == 0) return Fail("choose-reply-field", ExitNoMatch);
        if (text.Length > MaxCaptureText) return Fail("text-too-long", ExitNoMatch);

        var editor = Uia.GetFocusedElement();
        if (editor == null || editor.GetCurrentPropertyValue(UiaIds.ProcessId) is not Int32 pid || pid != source.Pid) return Fail("choose-reply-field", ExitNoMatch);
        var role = UiaIds.Role(editor.GetCurrentPropertyValue(UiaIds.ControlType) is Int32 type ? type : 0);
        if (role is not ("Edit" or "Document") || editor.GetCurrentPropertyValue(UiaIds.IsPassword) is true) return Fail("choose-reply-field", ExitNoMatch);
        var value = editor.GetCurrentPattern(UiaIds.ValuePattern) as IUIAutomationValuePattern;
        var current = value?.CurrentValue ?? "";
        if (UiaMatching.ComparableDraft(current).Length > 0) return Fail("draft-exists", ExitNoMatch);

        Boolean StillHere() => Win32.ForegroundPid() == source.Pid && Win32.GetForegroundWindow() == new IntPtr(source.Hwnd)
            && Uia.GetFocusedElement() is IUIAutomationElement now && editor.GetCurrentPropertyValue(UiaIds.HasKeyboardFocus) is true
            && now.GetCurrentPropertyValue(UiaIds.ProcessId) is Int32 p && p == source.Pid;
        Boolean Confirmed() => value != null && UiaMatching.ComparableDraft(value.CurrentValue) == UiaMatching.ComparableDraft(text);

        if (value != null && value.CurrentIsReadOnly == 0)
        {
            try { value.SetValue(text); } catch (System.Runtime.InteropServices.COMException) { }
            for (var attempt = 0; attempt < 5 && !Confirmed(); attempt++) Thread.Sleep(80);
            if (Confirmed()) return Emit(new Dictionary<String, Object?>());
            if (!StillHere()) return Fail("source-changed", ExitNoMatch);
        }

        var saved = Clipboard.Save();
        if (saved == null) return Fail("clipboard-changed", ExitNoMatch);
        if (!StillHere() || !Clipboard.WriteText(text)) return Fail("source-changed", ExitNoMatch);
        var written = Clipboard.Sequence;
        var posted = StillHere() && Win32.Chord(Win32.VK_CONTROL, Win32.VK_V);
        var deadline = Stopwatch.StartNew();
        while (posted && value != null && !Confirmed() && deadline.ElapsedMilliseconds < 1000) Thread.Sleep(30);
        Clipboard.Restore(saved, written);
        // A field with no readable value cannot confirm the paste; the keystroke was posted
        // while it had the focus, which is all this helper can establish.
        return posted && (value == null || Confirmed()) ? Emit(new Dictionary<String, Object?>()) : Fail("write-not-applied", ExitError);
    }

    private static Int32 ContextWindow()
    {
        var source = WindowBehind();
        if (source == null) return Fail("choose-source-app", ExitNoMatch);
        var image = CaptureWindow(source);
        if (image == null) return Fail("capture-failed", ExitNoMatch);
        return Emit(new Dictionary<String, Object?> { ["appName"] = AppName(source.Pid), ["source"] = source.Encode(), ["image"] = image });
    }

    private static Int32 ContextScreenshot()
    {
        // The window behind the chat app is where the region will be picked from: the app you
        // were in before you switched to the chat. Windows' picker freezes the screen as it is
        // and closes the moment another window is activated (switching apps under it cancels
        // it — unlike the macOS picker), so bring that window forward BEFORE the picker opens.
        // Remembered either way, so Return to App can lead back there.
        var front = Win32.GetForegroundWindow();
        var frontPid = Win32.PidOf(front);
        var source = _appPids.Contains(frontPid) ? WindowBehind()
            : Win32.IsAppWindow(front) ? new SourceWindow(frontPid, front.ToInt64(), Win32.WindowTitle(front)) : null;
        if (source != null && _appPids.Contains(frontPid))
        {
            // A fresh, UIA-free process is allowed to move the foreground (see restore-front).
            Raise(new IntPtr(source.Hwnd));
        }
        var image = CaptureRegion();
        if (image == null) return Fail("cancelled", ExitNoMatch);
        var result = new Dictionary<String, Object?> { ["image"] = image, ["appName"] = source == null ? "Source app" : AppName(source.Pid) };
        if (source != null) result["source"] = source.Encode();
        return Emit(result);
    }

    private static Dictionary<String, Object?> SourceResult()
    {
        var front = Win32.GetForegroundWindow();
        var pid = Win32.PidOf(front);
        var result = new Dictionary<String, Object?> { ["appName"] = AppName(pid) };
        if (!_appPids.Contains(pid) && Win32.IsAppWindow(front))
        {
            result["source"] = new SourceWindow(pid, front.ToInt64(), Win32.WindowTitle(front)).Encode();
        }
        return result;
    }

    private static Int32 ContextClipboard()
    {
        if (Clipboard.IsEmpty()) return Fail("clipboard-empty", ExitNoMatch);
        var result = SourceResult();
        var sequence = Clipboard.Sequence;
        var files = Clipboard.Files();
        if (files is { Count: > 0 })
        {
            if (files.Count > MaxAttachments || files.Any(f => !Path.IsPathRooted(f)) || Clipboard.Sequence != sequence) return Fail("clipboard-changed", ExitNoMatch);
            result["files"] = files;
            return Emit(result);
        }
        // Image data takes precedence over an application's textual description of it.
        var (png, dib) = Clipboard.Image();
        if (png != null || dib != null)
        {
            var image = SavePng(png, dib);
            if (image == null || Clipboard.Sequence != sequence) return Fail("clipboard-image-invalid", ExitNoMatch);
            result["image"] = image;
            return Emit(result);
        }
        var text = Clipboard.Text();
        if (text == null) return Fail("clipboard-not-text", ExitNoMatch);
        if (String.IsNullOrWhiteSpace(text)) return Fail("no-selection", ExitNoMatch);
        if (text.Length > MaxCaptureText) return Fail("text-too-long", ExitNoMatch);
        result["text"] = text;
        return Emit(result);
    }

    /// <summary>
    /// The selected text in the front app: read from its focused element when it exposes a
    /// text pattern, else copied with the app's own Ctrl+C, whose clipboard change is the
    /// proof. Stale clipboard data is never used, and the clipboard is put back.
    /// </summary>
    private static Int32 ContextSelection()
    {
        var front = Win32.GetForegroundWindow();
        var pid = Win32.PidOf(front);
        if (_appPids.Contains(pid)) return Fail("choose-source-app", ExitNoMatch);
        var result = SourceResult();
        String? selected = null;
        var focused = Uia.GetFocusedElement();
        if (focused != null && focused.GetCurrentPropertyValue(UiaIds.ProcessId) is Int32 focusedPid && focusedPid == pid)
        {
            if (focused.GetCurrentPropertyValue(UiaIds.IsPassword) is true) return Fail("no-selection", ExitNoMatch);
            if (focused.GetCurrentPattern(UiaIds.TextPattern) is IUIAutomationTextPattern pattern)
            {
                var ranges = pattern.GetSelection();
                var parts = new List<String>();
                for (var i = 0; ranges != null && i < ranges.Length; i++)
                {
                    parts.Add(ranges.GetElement(i).GetText(MaxCaptureText) ?? "");
                }
                selected = String.Join("", parts);
            }
        }
        if (String.IsNullOrWhiteSpace(selected))
        {
            var before = Clipboard.Sequence;
            var saved = Clipboard.Save();
            if (saved == null || Clipboard.Sequence != before || Win32.GetForegroundWindow() != front || !Win32.Chord(Win32.VK_CONTROL, Win32.VK_C))
            {
                return Fail("no-selection", ExitNoMatch);
            }
            var deadline = Stopwatch.StartNew();
            while (Clipboard.Sequence == before && deadline.ElapsedMilliseconds < 600) Thread.Sleep(30);
            var after = Clipboard.Sequence;
            if (after != before)
            {
                selected = Clipboard.Text();
                Clipboard.Restore(saved, after);
            }
        }
        if (Win32.GetForegroundWindow() != front) return Fail("source-changed", ExitNoMatch);
        if (String.IsNullOrWhiteSpace(selected)) return Fail("no-selection", ExitNoMatch);
        if (selected.Length > MaxCaptureText) return Fail("text-too-long", ExitNoMatch);
        result["text"] = selected;
        return Emit(result);
    }
}
