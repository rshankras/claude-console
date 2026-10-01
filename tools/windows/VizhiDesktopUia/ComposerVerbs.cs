// The composer: draft targets, writes, appends, send and attachments.
//
// A draft target names the composer a caller may write to later: this window, this mode, this
// editor, this open conversation. Every write, append, send and attachment that carries
// --expect-target is refused when any of those has changed since the target was issued, so a
// prompt or a transcript never lands in an editor the user has since navigated away from.
//
// On Windows the composer is written through the value pattern, which replaces the whole
// value; the macOS helper inserts at the caret instead. The guards are the same: a write goes
// only into an empty composer, an append proves the original text survived by fingerprint, and
// both read the result back before reporting success.

using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;

namespace VizhiDesktopUia;

[SupportedOSPlatform("windows")]
internal static partial class Program
{
    private const Int32 MaxDraftLength = 50000;
    private const Int32 MaxAttachments = 8;
    private const Int64 MaxAttachmentSize = 50L * 1024 * 1024;
    private const Int64 MaxAttachmentsTotal = 100L * 1024 * 1024;

    private sealed record Composer(UiaNode Node, IUIAutomationElement Element, IUIAutomationValuePattern Value);

    private static String? Origin(Target target, Dictionary<String, List<String>> options)
    {
        // The window is identified by title, so the title must be unique among the app's.
        if (AppWindows(_appPids).Count(w => w.Title == target.Title) != 1)
        {
            return null;
        }
        var identity = String.Join("\n", target.Pid.ToString(), target.Title, target.Hwnd.ToInt64().ToString(),
            Value(options, "--mode-prefix") ?? "", Value(options, "--expect-mode") ?? "");
        return UiaMatching.Fingerprint(identity);
    }

    private static String? UniqueComposerError(IReadOnlyList<UiaNode> nodes, out Composer? composer)
    {
        composer = null;
        var composers = UiaMatching.Composers(nodes);
        if (composers.Count == 0) return "no-composer";
        if (composers.Count > 1) return "ambiguous-composer";
        if (composers[0].Handle is not IUIAutomationElement element
            || element.GetCurrentPattern(UiaIds.ValuePattern) is not IUIAutomationValuePattern value)
        {
            return "no-composer";
        }
        composer = new Composer(composers[0], element, value);
        return null;
    }

    private static Int32 FailComposer(String error) =>
        Fail(error, error is "ambiguous-composer" or "composer-target-changed" or "mode-changed" or "mode-unavailable"
            ? ExitChanged : ExitNoMatch);

    /// <summary>The target token for this scan, or the reason there is none.</summary>
    private static String? DraftTargetToken(Target target, IReadOnlyList<UiaNode> nodes,
        Dictionary<String, List<String>> options, out String? error)
    {
        error = UiaMatching.ModeError(UiaMatching.ReportedModes(nodes, Value(options, "--mode-prefix") ?? ""),
            Value(options, "--expect-mode") ?? "", pinned: false);
        if (error != null) return null;
        error = UniqueComposerError(nodes, out var composer);
        if (error != null) return null;
        var origin = Origin(target, options);
        var conversation = UiaMatching.SelectedConversation(nodes, Value(options, "--conv-marker") ?? "");
        if (origin == null || conversation == null)
        {
            error = "composer-target-changed";
            return null;
        }
        return UiaMatching.Fingerprint(origin + ":" + composer!.Node.RuntimeId + ":" + conversation);
    }

    /// <summary>Null when the scan still matches the target the caller prepared, else why not.</summary>
    private static String? PreparedDraftError(Target target, IReadOnlyList<UiaNode> nodes,
        Dictionary<String, List<String>> options)
    {
        var expected = Value(options, "--expect-target");
        if (expected == null)
        {
            return null;
        }
        var token = DraftTargetToken(target, nodes, options, out var error);
        return token == null ? error : token == expected ? null : "composer-target-changed";
    }

    private static Boolean PreparedDraftMatches(Target target, IReadOnlyList<UiaNode> nodes,
        Dictionary<String, List<String>> options) => PreparedDraftError(target, nodes, options) == null;

    private static List<String> BlockingLabels(Dictionary<String, List<String>> options) =>
        Values(options, "--stop").Concat(Values(options, "--approve")).Concat(Values(options, "--voice-end")).ToList();

    private static Boolean SendEnabled(IReadOnlyList<UiaNode> nodes, Dictionary<String, List<String>> options) =>
        UiaMatching.ExactButtons(nodes, Values(options, "--composer-send-label")).Any(n => n.Enabled);

    private static String ComposerDraft(Composer composer, IReadOnlyList<UiaNode> nodes, Dictionary<String, List<String>> options)
    {
        var raw = composer.Value.CurrentValue ?? "";
        return UiaMatching.IsPlaceholderDraft(raw, Values(options, "--draft-placeholder"), SendEnabled(nodes, options)) ? "" : raw;
    }

    /// <summary>The scan, the target check and the readiness check every composer verb starts with.</summary>
    private static (Scan? Scan, Composer? Composer, String? Error, Int32 Code) ComposerScan(Target target,
        Dictionary<String, List<String>> options, Boolean checkReady)
    {
        var scan = WaitForSurface(target, 2000);
        if (scan == null) return (null, null, "surface-unavailable", ExitError);
        var error = PreparedDraftError(target, scan.Nodes, options);
        if (error != null) return (null, null, error, FailCode(error));
        if (checkReady && UiaMatching.ComposerBlocked(scan.Nodes, BlockingLabels(options)))
        {
            return (null, null, "composer-unavailable", ExitNoMatch);
        }
        error = UniqueComposerError(scan.Nodes, out var composer);
        return error != null ? (null, null, error, FailCode(error)) : (scan, composer, null, 0);
    }

    private static Int32 FailCode(String error) =>
        error is "ambiguous-composer" or "composer-target-changed" or "mode-changed" or "mode-unavailable" ? ExitChanged : ExitNoMatch;

    private static Int32 DraftTarget(Target target, Dictionary<String, List<String>> options)
    {
        var scan = WaitForSurface(target, 2000);
        if (scan == null)
        {
            return Fail("surface-unavailable", ExitError);
        }
        var token = DraftTargetToken(target, scan.Nodes, options, out var error);
        if (token == null)
        {
            return FailComposer(error!);
        }
        UniqueComposerError(scan.Nodes, out var composer);
        var draft = ComposerDraft(composer!, scan.Nodes, options);
        if (!options.ContainsKey("--allow-existing") && UiaMatching.ComparableDraft(draft).Length > 0)
        {
            return Fail("draft-exists", ExitNoMatch);
        }
        return Emit(new Dictionary<String, Object?> { ["target"] = token });
    }

    /// <summary>The target plus a fingerprint of what the composer holds now, without the text itself.</summary>
    private static Int32 AppendTarget(Target target, Dictionary<String, List<String>> options)
    {
        var (scan, composer, error, code) = ComposerScan(target, options, checkReady: true);
        if (scan == null) return Fail(error!, code);
        var token = DraftTargetToken(target, scan.Nodes, options, out error);
        if (token == null) return FailComposer(error!);
        var value = ComposerDraft(composer!, scan.Nodes, options);
        if (value.Length > MaxDraftLength) return Fail("text-too-long", ExitNoMatch);
        return Emit(new Dictionary<String, Object?>
        {
            ["target"] = token,
            ["fingerprint"] = UiaMatching.Fingerprint(value),
            ["hasContent"] = UiaMatching.ComparableDraft(value).Length > 0 || SendEnabled(scan.Nodes, options),
        });
    }

    /// <summary>
    /// write: put text into an EMPTY composer, or report that a draft already exists.
    /// append: add text beneath the draft whose fingerprint the caller saw, and prove by
    /// fingerprint that the original survived. Neither ever silently replaces what the user
    /// typed; the desktop injection law: the text lands in the composer the caller targeted,
    /// or nowhere, and the failure is named.
    /// </summary>
    private static Int32 Write(Target target, Dictionary<String, List<String>> options, Boolean appending)
    {
        var supplied = Value(options, "--text") ?? "";
        if (UiaMatching.ComparableDraft(supplied).Length == 0)
        {
            return Fail("empty-text", ExitNoMatch);
        }
        var requestedBefore = Value(options, "--expect-draft");
        var sendLabel = Value(options, "--send-label");
        if (appending && (requestedBefore == null || requestedBefore.Length != 64 || !requestedBefore.All(Uri.IsHexDigit)
            || Value(options, "--expect-target") == null || sendLabel != null))
        {
            return Fail("invalid-append", ExitNoMatch);
        }
        var text = appending ? UiaMatching.ComparableDraft(supplied) : supplied;
        var expectedText = UiaMatching.ComparableDraft(text);
        if (text.Length > MaxDraftLength)
        {
            return Fail("text-too-long", ExitNoMatch);
        }

        var (scan, composer, error, code) = ComposerScan(target, options, checkReady: true);
        if (scan == null || composer == null) return Fail(error ?? "no-composer", code);

        var original = ComposerDraft(composer, scan.Nodes, options);
        var before = appending ? requestedBefore! : UiaMatching.Fingerprint(original);
        if (options.ContainsKey("--accept-existing") && sendLabel == null)
        {
            var alreadyInserted = appending
                ? UiaMatching.AppendedDraftMatches(original, before, text)
                : UiaMatching.ComparableDraft(original) == expectedText;
            if (alreadyInserted)
            {
                return Emit(new Dictionary<String, Object?> { ["method"] = "existing", ["sent"] = false });
            }
        }
        if (!appending && UiaMatching.ComparableDraft(original).Length > 0)
        {
            return Fail("draft-exists", ExitNoMatch);
        }
        if (UiaMatching.Fingerprint(original) != before)
        {
            return Fail("draft-changed", ExitNoMatch);
        }
        if (original.Length + text.Length + 2 > MaxDraftLength)
        {
            return Fail("text-too-long", ExitNoMatch);
        }

        var insertion = UiaMatching.ComparableDraft(original).Length == 0 ? text : original.TrimEnd('\r', '\n') + "\n\n" + text;
        Boolean Confirmed(String? value) => appending
            ? UiaMatching.AppendedDraftMatches(value ?? "", before, text)
            : UiaMatching.ComparableDraft(value) == expectedText;

        // A last look before the only write: the same composer, still holding what we read.
        if (ComposerDraft(composer, scan.Nodes, options) != original)
        {
            return Fail("draft-changed", ExitNoMatch);
        }
        composer.Value.SetValue(insertion);
        var applied = false;
        for (var attempt = 0; attempt < 10 && !applied; attempt++)
        {
            Thread.Sleep(100);
            applied = Confirmed(composer.Value.CurrentValue);
        }
        if (!applied)
        {
            return Fail(appending ? "append-unconfirmed" : "write-not-applied", ExitError);
        }

        var sent = false;
        if (sendLabel != null)
        {
            var latest = ScanTarget(target);
            var send = latest.Surface && PreparedDraftMatches(target, latest.Nodes, options)
                && !UiaMatching.ComposerBlocked(latest.Nodes, BlockingLabels(options))
                && latest.Nodes.Any(n => n.Role == "Edit" && SameElement(n, composer.Node))
                && UiaMatching.ComparableDraft(composer.Value.CurrentValue) == expectedText
                ? UiaMatching.SendTarget(latest.Nodes, sendLabel, Values(options, "--stop"), Values(options, "--approve"))
                : null;
            if (send == null)
            {
                return Fail("no-sendable-draft", ExitNoMatch);
            }
            if (!Invoke(send))
            {
                return Fail("send-press-failed", ExitError);
            }
            sent = true;
        }

        return Emit(new Dictionary<String, Object?> { ["method"] = "value", ["sent"] = sent });
    }

    /// <summary>Submit the existing draft without replacing it; refuse an ambiguous target.</summary>
    private static Int32 Send(Target target, Dictionary<String, List<String>> options)
    {
        var (scan, composer, error, code) = ComposerScan(target, options, checkReady: false);
        if (scan == null) return Fail(error!, code);
        var sendLabel = Value(options, "--send-label") ?? "";
        var first = UiaMatching.SendTarget(scan.Nodes, sendLabel, Values(options, "--stop"), Values(options, "--approve"));
        if (first == null)
        {
            return Fail("no-sendable-draft", ExitNoMatch);
        }
        var draft = composer!.Value.CurrentValue ?? "";
        var expected = Value(options, "--expect-text");
        if (expected != null && (UiaMatching.ComparableDraft(expected).Length == 0
            || UiaMatching.ComparableDraft(draft) != UiaMatching.ComparableDraft(expected)))
        {
            return Fail("draft-changed", ExitChanged);
        }

        // A second look before the press: the same window, target, composer and draft, and
        // the same Send.
        var latest = ScanTarget(target);
        var confirmed = latest.Surface && PreparedDraftMatches(target, latest.Nodes, options)
            && latest.Nodes.Any(n => n.Role == "Edit" && SameElement(n, composer.Node))
            && (composer.Value.CurrentValue ?? "") == draft
            ? UiaMatching.SendTarget(latest.Nodes, sendLabel, Values(options, "--stop"), Values(options, "--approve"))
            : null;
        if (confirmed == null || !SameElement(confirmed, first))
        {
            return Fail("composer-target-changed", ExitChanged);
        }
        if (!Invoke(confirmed))
        {
            return Fail("send-press-failed", ExitError);
        }
        return Emit(new Dictionary<String, Object?> { ["sent"] = true });
    }

    private sealed record AttachedFile(String Path, Int64 Size, Int64 Modified)
    {
        public String Name => System.IO.Path.GetFileName(this.Path);

        public Boolean Unchanged()
        {
            try
            {
                var info = new FileInfo(this.Path);
                return info.Exists && (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0
                    && info.Length == this.Size
                    && new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds() == this.Modified;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Attach files by pasting a file list into the focused composer — the same route the
    /// macOS helper takes with file URLs — and confirm the attachment by its name appearing
    /// in the composer area. The clipboard is borrowed and given back; a copy the user makes
    /// meanwhile is never overwritten.
    /// </summary>
    private static Int32 Attach(Target target, Dictionary<String, List<String>> options, Boolean image)
    {
        var files = new List<AttachedFile>();
        if (image)
        {
            var path = Value(options, "--image");
            if (path == null || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)
                || new FileInfo(path).Length == 0)
            {
                return Fail("invalid-image", ExitNoMatch);
            }
            var info = new FileInfo(path);
            files.Add(new AttachedFile(info.FullName, info.Length, new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds()));
        }
        else
        {
            try
            {
                using var document = JsonDocument.Parse(Value(options, "--files") ?? "");
                if (document.RootElement.ValueKind != JsonValueKind.Array) return Fail("invalid-files", ExitNoMatch);
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    var path = item.TryGetProperty("Path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                    if (path == null || !Path.IsPathRooted(path)
                        || !item.TryGetProperty("Size", out var s) || !s.TryGetInt64(out var size) || size <= 0 || size > MaxAttachmentSize
                        || !item.TryGetProperty("Modified", out var m) || !m.TryGetInt64(out var modified))
                    {
                        return Fail("invalid-files", ExitNoMatch);
                    }
                    files.Add(new AttachedFile(path, size, modified));
                }
            }
            catch (JsonException)
            {
                return Fail("invalid-files", ExitNoMatch);
            }
            if (files.Count == 0 || files.Count > MaxAttachments) return Fail("invalid-files", ExitNoMatch);
            if (files.Sum(f => f.Size) > MaxAttachmentsTotal) return Fail("files-too-large", ExitNoMatch);
        }

        Boolean FilesUnchanged() => files.All(f => f.Unchanged());
        if (Value(options, "--expect-target") == null || !FilesUnchanged())
        {
            return Fail("files-changed", ExitNoMatch);
        }

        var (scan, composer, error, code) = ComposerScan(target, options, checkReady: true);
        if (scan == null) return Fail(error!, code);
        var names = files.Select(f => f.Name).ToList();
        if (names.Select(n => n.ToLowerInvariant()).Distinct().Count() != names.Count)
        {
            return Fail("attachment-name-exists", ExitNoMatch);
        }

        Boolean Visible(String name, IReadOnlyList<UiaNode> nodes) =>
            nodes.Any(n => n.Role is "Image" or "Text" or "Button" && n.Text == name);
        Boolean AllVisible(IReadOnlyList<UiaNode> nodes) => names.All(n => Visible(n, nodes));

        if (image && AllVisible(scan.Nodes)) return Emit(new Dictionary<String, Object?> { ["attached"] = true });
        if (!image && names.Any(n => Visible(n, scan.Nodes))) return Fail("attachment-name-exists", ExitNoMatch);
        if (!AppIsFrontmost(target)) return Fail("app-not-frontmost", ExitNoMatch);

        var value = composer!.Value.CurrentValue ?? "";
        String? Current()
        {
            // The same composer, unchanged, in the same target, with the app still in front.
            var fresh = ScanTarget(target);
            if (!fresh.Surface || !fresh.Nodes.Any(n => n.Role == "Edit" && SameElement(n, composer.Node))
                || !PreparedDraftMatches(target, fresh.Nodes, options)
                || UiaMatching.ComposerBlocked(fresh.Nodes, BlockingLabels(options)) || !AppIsFrontmost(target))
            {
                return null;
            }
            return composer.Value.CurrentValue ?? "";
        }

        // Focus the composer through UIA (no click, no key), and prove it took.
        composer.Element.SetFocus();
        var focused = false;
        for (var attempt = 0; attempt < 5 && !focused; attempt++)
        {
            Thread.Sleep(60);
            focused = composer.Element.GetCurrentPropertyValue(UiaIds.HasKeyboardFocus) is Boolean b && b;
        }
        if (!focused || Current() != value) return Fail("composer-target-changed", ExitNoMatch);

        var saved = Clipboard.Save();
        if (saved == null) return Fail("clipboard-busy", ExitNoMatch);
        if (!FilesUnchanged()) return Fail("files-changed", ExitNoMatch);
        if (!Clipboard.WriteFiles(files.Select(f => f.Path).ToList())) return Fail("clipboard-busy", ExitNoMatch);
        var written = Clipboard.Sequence;

        // The one keystroke, only while the composer provably has the focus.
        var stillFocused = composer.Element.GetCurrentPropertyValue(UiaIds.HasKeyboardFocus) is Boolean f2 && f2;
        if (!stillFocused || !AppIsFrontmost(target) || !Win32.Chord(Win32.VK_CONTROL, Win32.VK_V))
        {
            Clipboard.Restore(saved, written);
            return Fail("app-not-frontmost", ExitNoMatch);
        }

        // Once the paste was posted, a failure cannot establish that no attachment was created.
        var deadline = Stopwatch.StartNew();
        var attached = false;
        while (!attached && deadline.ElapsedMilliseconds < 2000)
        {
            Thread.Sleep(80);
            attached = AllVisible(ScanTarget(target).Nodes);
        }
        Clipboard.Restore(saved, written);
        if (!attached || !FilesUnchanged() || Current() != value)
        {
            return Fail("attachment-unconfirmed", ExitNoMatch);
        }
        return Emit(new Dictionary<String, Object?> { ["attached"] = true });
    }
}
