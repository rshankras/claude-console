namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;

    internal sealed record DesktopFilePickerState
    {
        public String Session { get; init; } = "";
        public String Mode { get; init; }
        public String Target { get; init; }
        public DesktopFile[] Files { get; init; } = Array.Empty<DesktopFile>();
        public String[] Selected { get; init; } = Array.Empty<String>();
        public String Feedback { get; init; }
        public Boolean Busy { get; init; }
        public Boolean Uncertain { get; init; }
    }

    /// <summary>Explicit, bounded Downloads reads. No watcher, indexing, or clipboard polling.</summary>
    internal sealed class DesktopFilePicker
    {
        private readonly IDesktopAutomation _automation;
        private readonly DesktopContextCapture _context;
        private readonly String _directory;
        private Int64 _generation;
        private volatile DesktopFilePickerState _state = new();
        internal DesktopFilePickerState Current => _state;
        internal event Action Changed;
        internal DesktopFilePicker(IDesktopAutomation automation, DesktopContextCapture context, String directory)
        { _automation = automation; _context = context; _directory = directory; }
        private void Publish(DesktopFilePickerState value, Int64 generation)
        { if (generation == Interlocked.Read(ref _generation)) { _state = value; Changed?.Invoke(); } }
        internal void End()
        { Interlocked.Increment(ref _generation); _state = new(); Changed?.Invoke(); }

        internal void Begin()
        {
            var generation = Interlocked.Increment(ref _generation);
            var state = new DesktopFilePickerState { Session = Guid.NewGuid().ToString("N"), Busy = true, Feedback = "Loading" };
            Publish(state, generation);
            var app = _automation.Status();
            String why = "surface-unavailable";
            var target = app.SurfaceAvailable && app.Mode is "ChatGPT" or "Codex" ? _automation.PrepareAppend(app.Mode, out why) : null;
            if (target == null) { Publish(state with { Busy = false, Feedback = DesktopContextCapture.Problem(why) }, generation); return; }
            var files = ReadDownloads(out var error);
            Publish(state with { Mode = app.Mode, Target = target.Target, Files = files, Busy = false, Feedback = error }, generation);
        }

        private DesktopFile[] ReadDownloads(out String error)
        {
            error = null;
            try
            {
                if (!Directory.Exists(_directory)) return Array.Empty<DesktopFile>();
                return new DirectoryInfo(_directory).EnumerateFiles().Take(2048)
                    .Where(f => !f.Name.StartsWith(".", StringComparison.Ordinal))
                    .Select(f => DesktopFile.Read(f.FullName)).Where(f => f != null)
                    .OrderByDescending(f => f.Modified).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Take(18).ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { error = "Downloads Access"; return Array.Empty<DesktopFile>(); }
        }

        internal String Parameter(DesktopFile file) => "file:" + _state.Session + ":" + file.Id;
        internal DesktopFile Resolve(String parameter)
        {
            var state = _state;
            return state.Files.FirstOrDefault(f => parameter == "file:" + state.Session + ":" + f.Id);
        }

        // Called only by the shared action runner. Deactivation invalidates delayed results.
        internal Boolean Execute(String parameter, IDesktopAppAdapter app, VoiceCaptureState voice)
        {
            var generation = Interlocked.Read(ref _generation); var state = _state;
            if (state.Session.Length == 0 || state.Busy) return false;
            if (voice.Phase != VoicePhase.Idle || _context.DraftPending())
            { Publish(state with { Feedback = voice.Phase != VoicePhase.Idle ? "Finish Speaking" : "Insert Draft First" }, generation); return false; }
            if (parameter == "refresh")
            {
                if (state.Target == null) { Begin(); return false; }
                var files = ReadDownloads(out var error);
                Publish(state with { Files = files, Selected = Array.Empty<String>(), Feedback = error }, generation); return false;
            }
            if (parameter == "browse")
            {
                var target = _automation.PrepareAppend(state.Mode, out _);
                var opened = target != null && target.Target == state.Target
                    && _automation.PressInMode(app.ControlLabels(DesktopControl.AttachFiles), state.Mode, out _);
                Publish(state with { Feedback = opened ? "Use File Picker" : "Check Chat" }, generation); return opened && generation == Interlocked.Read(ref _generation);
            }
            var file = Resolve(parameter);
            if (file != null)
            {
                var selected = state.Selected.ToHashSet(StringComparer.Ordinal);
                if (!selected.Remove(file.Id))
                {
                    if (selected.Count >= DesktopFile.MaximumCount) { Publish(state with { Feedback = "8 Files Max" }, generation); return false; }
                    selected.Add(file.Id);
                }
                Publish(state with { Selected = selected.ToArray(), Feedback = null }, generation); return false;
            }
            if (parameter != "attach") return false;
            if (state.Uncertain) { Publish(state with { Feedback = "Check Files" }, generation); return false; }
            var chosen = state.Files.Where(f => state.Selected.Contains(f.Id)).ToArray();
            if (chosen.Length == 0) { Publish(state with { Feedback = "Select Files" }, generation); return false; }
            Publish(state with { Busy = true, Feedback = "Attaching" }, generation);
            var result = _context.AttachFiles(chosen, state.Mode, state.Target);
            Publish(state with { Busy = false, Feedback = result, Uncertain = result == "Check Files",
                Selected = result == "Attached" ? Array.Empty<String>() : state.Selected }, generation);
            return result == "Attached" && generation == Interlocked.Read(ref _generation);
        }
    }
}
