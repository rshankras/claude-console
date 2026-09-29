namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;

    /// <summary>
    /// The interpreted, key-facing view of the desktop app: what the keys render.
    /// Produced from a <see cref="DesktopSnapshot"/> by a pure mapping (testable without AX).
    /// </summary>
    internal sealed class DesktopState
    {
        public DesktopActivity Activity { get; init; }
        public ApprovalRisk Risk { get; init; }
        public String CardText { get; init; } = "";
        public String Mode { get; init; } = "";
        public Boolean Attention { get; init; }
        public Boolean CanSend { get; init; }
        public Boolean CanCopyAnswer { get; init; }
        public String CopyAnswerError { get; init; } = "";
        public DesktopVoiceState VoiceChat { get; init; }
        public DesktopControl AvailableControls { get; init; }

        /// <summary>Sidebar conversations, in app order, with visible task activity applied only
        /// when the app identifies exactly one selected conversation.</summary>
        public IReadOnlyList<DesktopConversation> Conversations { get; init; } = Array.Empty<DesktopConversation>();

        /// <summary>What the three home-page conversation keys render: stable slots (null = empty), assigned
        /// by <see cref="DesktopSlotMap"/> so a key never moves under the user's fingers.</summary>
        public IReadOnlyList<DesktopConversation> Slots { get; init; } = new DesktopConversation[DesktopSlotMap.SlotCount];

        /// <summary>
        /// The conversation the visible approval card belongs to, when that is KNOWABLE: the
        /// app's own selection marker if it ever reports one, else the single conversation in
        /// Awaiting state while a card shows (card visible => the open conversation has the
        /// pending approval). Empty when ambiguous — the Approve face falls back to "Approve"
        /// rather than guessing which of several waiting conversations is open.
        /// </summary>
        public String ActiveTitle { get; init; } = "";

        /// <summary>Why Unavailable, when Unavailable: hidden / no-permission / not-running / no-signal.</summary>
        public String Reason { get; init; } = "";

        public Boolean Available => this.Activity != DesktopActivity.Unavailable;

        public static DesktopState Unavailable => new DesktopState { Activity = DesktopActivity.Unavailable, Reason = "no-signal" };
    }

    internal enum DesktopActivity
    {
        /// <summary>We cannot see the app (screen locked, window hidden, helper failed).</summary>
        Unavailable = 0,

        /// <summary>Nothing running, nothing pending.</summary>
        Ready,

        /// <summary>A task is running (the Stop control is present).</summary>
        Working,

        /// <summary>An approval card is up — the app is waiting on the user.</summary>
        WaitingApproval,
    }

    /// <summary>
    /// The desktop product's heartbeat: one bounded `status` reading per tick on a one-shot
    /// timer that re-arms only when the previous tick finishes — the StartPolling idiom, kept
    /// because it is what makes tick pile-up (the 1.3.1 thread-leak crash) structurally
    /// impossible. Cadence adapts to foreground activity and slow native reads; background
    /// scans run at most once per 15 seconds and yield to keypad commands.
    ///
    /// The mapping rule that must survive every refactor: an UNAVAILABLE surface is its own
    /// state. When the screen locks, every control vanishes at once — that must grey the keys,
    /// never read as "the approval resolved itself".
    /// </summary>
    internal sealed class DesktopMonitor : IDisposable
    {
        private const Int32 PollMs = 1000;

        private readonly IDesktopAutomation _automation;
        private readonly DesktopSlotMap _slotMap = new DesktopSlotMap();
        private Timer _timer;
        private readonly Object _lifecycle = new();
        private Int64 _generation, _backgroundReadDue;
        private Boolean _started;
        private Int32 _polling, _slowReads;
        internal Func<Boolean> IsCommandBusy { get; set; } = () => false;
        internal Func<Int64> Clock { get; set; } = () => Environment.TickCount64;
        internal Int32 NextPollDelayMs { get; private set; } = PollMs;

        /// <summary>Fires on every material change (activity, risk, attention, mode, card text).</summary>
        public event Action<DesktopState> OnChanged;

        public DesktopState Current { get; private set; } = DesktopState.Unavailable;

        public DesktopMonitor(IDesktopAutomation automation) => _automation = automation;

        public void Start()
        {
            lock (_lifecycle)
            {
                if (_started) return;
                _started = true;
                var generation = ++_generation;
                _backgroundReadDue = 0;
                _timer = new Timer(_ => Poll(generation), null, 0, Timeout.Infinite);
            }
        }

        public void Stop()
        {
            lock (_lifecycle)
            {
                _started = false; _generation++;
                _timer?.Dispose(); _timer = null;
            }
        }

        public void Dispose() => Stop();
        internal void PollOnce() => Poll(null);

        private Boolean Valid(Int64? generation)
        {
            lock (_lifecycle) return generation == null || (_started && generation == _generation);
        }

        private void Arm(Int64? generation, Int32 delay)
        {
            if (generation == null) return;
            lock (_lifecycle)
                if (_started && generation == _generation) _timer?.Change(delay, Timeout.Infinite);
        }

        private void Poll(Int64? generation)
        {
            if (!Valid(generation)) return;
            // A stopped timer can already have a callback in flight. Even a Stop/Start race
            // must not overlap native scans or allow the old callback to re-arm a new timer.
            if (Interlocked.Exchange(ref _polling, 1) != 0) { Arm(generation, 250); return; }
            var delay = 5000;
            try
            {
                if (!Valid(generation)) return;
                if (IsCommandBusy()) { delay = 500; return; }
                var foreground = _automation.IsAppFrontmost() == true;
                if (!foreground && Clock() < _backgroundReadDue) return;
                var start = Clock();
                var snapshot = _automation.Status();
                var elapsed = Math.Max(0, Clock() - start);
                _slowReads = elapsed >= 1000 ? Math.Min(4, _slowReads + 1) : 0;
                if (!Valid(generation)) return;
                var next = Map(snapshot, _slotMap);
                this.Apply(next);
                delay = PollDelay(next.Activity, next.VoiceChat == DesktopVoiceState.Active, _slowReads);
                _backgroundReadDue = Clock() + Math.Max(15000, delay);
                if (!foreground) delay = 5000; // Cheap foreground probe; full background reads are limited.
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "DesktopMonitor: poll failed");
                if (Valid(generation)) this.Apply(DesktopState.Unavailable);
                delay = 10000;
            }
            finally
            {
                NextPollDelayMs = delay;
                Volatile.Write(ref _polling, 0);
                Arm(generation, delay);
            }
        }

        internal static Int32 PollDelay(DesktopActivity activity, Boolean voiceActive, Int32 slowReads) =>
            slowReads > 0 ? Math.Max(NextDelayMs(activity), Math.Min(30000, 3000 << Math.Min(3, slowReads - 1)))
                : voiceActive ? PollMs : NextDelayMs(activity);

        private void Apply(DesktopState next)
        {
            if (!Differs(this.Current, next))
            {
                return;
            }

            this.Current = next;
            try { this.OnChanged?.Invoke(next); }
            catch (Exception ex) { PluginLog.Warning(ex, "DesktopMonitor: OnChanged handler failed"); }
        }

        /// <summary>
        /// Hot while something is happening, relaxed while nothing is: 1s when Working/Waiting,
        /// 3s at Ready, 5s when the surface is gone. Any state change re-enters through a tick,
        /// so the next tick after a wake is at the new state's cadence.
        /// </summary>
        internal static Int32 NextDelayMs(DesktopActivity activity) => activity switch
        {
            DesktopActivity.Working or DesktopActivity.WaitingApproval => PollMs,
            DesktopActivity.Ready => 3000,
            _ => 5000,
        };

        /// <summary>Pure snapshot→state mapping. Approval outranks Working: a card means the task is parked on you.</summary>
        internal static DesktopState Map(DesktopSnapshot snap, DesktopSlotMap slotMap = null)
        {
            if (snap == null || !snap.SurfaceAvailable)
            {
                // The slot map is deliberately NOT cleared: a locked screen comes back with the
                // same sidebar, and the keys must come back in the same places.
                return new DesktopState { Activity = DesktopActivity.Unavailable, Reason = snap?.UnavailableReason ?? "no-signal" };
            }

            var activity = snap.ApprovalPresent ? DesktopActivity.WaitingApproval
                : snap.StopPresent ? DesktopActivity.Working
                : DesktopActivity.Ready;

            // The card exposes whatever text the app shows — sometimes a command, often a
            // natural-language description. Grade what is actually visible: NL text lands on
            // Normal (amber), and red is reserved for text the classifier can genuinely flag.
            var risk = activity == DesktopActivity.WaitingApproval
                ? Max(RiskClassifier.Classify("Bash", snap.CardText), ApprovalRisk.Normal)
                : ApprovalRisk.None;

            var conversations = snap.Conversations ?? Array.Empty<DesktopConversation>();

            // The current task's Stop/approval controls are stronger than a missing sidebar
            // badge, but only the app's unique selection can tell us which key owns them.
            // Never infer this from recency, the last key pressed, or a single visible row.
            var selected = conversations.Where(c => c.Selected).Take(2).ToArray();
            var active = selected.Length == 1 && !String.IsNullOrEmpty(selected[0].Title)
                && conversations.Count(c => String.Equals(c.Title, selected[0].Title, StringComparison.Ordinal)) == 1
                ? selected[0].Title : null;
            if (active != null && (snap.ApprovalPresent || snap.StopPresent))
            {
                conversations = conversations.Select(c => c.Selected ? new DesktopConversation
                {
                    Title = c.Title,
                    Selected = true,
                    State = snap.ApprovalPresent || c.State == ConversationState.Awaiting
                        ? ConversationState.Awaiting : ConversationState.Running,
                } : c).ToArray();
            }
            if (active == null && selected.Length == 0 && snap.ApprovalPresent)
            {
                var awaiting = conversations.Where(c => c.State == ConversationState.Awaiting).Take(2).ToList();
                active = awaiting.Count == 1 ? awaiting[0].Title : null;
            }

            return new DesktopState
            {
                Activity = activity,
                Risk = risk,
                CardText = snap.CardText ?? "",
                Mode = snap.Mode ?? "",
                Attention = snap.Attention,
                CanSend = snap.CanSend && activity == DesktopActivity.Ready,
                CanCopyAnswer = snap.CanCopyAnswer && activity == DesktopActivity.Ready,
                CopyAnswerError = snap.CopyAnswerError,
                VoiceChat = snap.VoiceChat,
                AvailableControls = snap.AvailableControls,
                Conversations = conversations,
                Slots = slotMap != null ? slotMap.Apply(conversations) : conversations.Take(DesktopSlotMap.SlotCount).ToArray(),
                ActiveTitle = active ?? "",
            };
        }

        private static ApprovalRisk Max(ApprovalRisk a, ApprovalRisk b) => a >= b ? a : b;

        private static Boolean Differs(DesktopState a, DesktopState b) =>
            a.Activity != b.Activity
            || a.Risk != b.Risk
            || a.Attention != b.Attention
            || a.CanSend != b.CanSend
            || a.CanCopyAnswer != b.CanCopyAnswer
            || a.CopyAnswerError != b.CopyAnswerError
            || a.VoiceChat != b.VoiceChat
            || a.AvailableControls != b.AvailableControls
            || !String.Equals(a.Mode, b.Mode, StringComparison.Ordinal)
            || !String.Equals(a.CardText, b.CardText, StringComparison.Ordinal)
            || !String.Equals(a.ActiveTitle, b.ActiveTitle, StringComparison.Ordinal)
            || !String.Equals(a.Reason, b.Reason, StringComparison.Ordinal)
            || !SameConversations(a.Conversations, b.Conversations)
            || !SameSlots(a.Slots, b.Slots);

        private static Boolean SameSlots(IReadOnlyList<DesktopConversation> a, IReadOnlyList<DesktopConversation> b) =>
            a.Count == b.Count
            && a.Zip(b).All(p =>
                (p.First == null) == (p.Second == null)
                && (p.First == null || (p.First.State == p.Second.State
                    && String.Equals(p.First.Title, p.Second.Title, StringComparison.Ordinal))));

        private static Boolean SameConversations(
            IReadOnlyList<DesktopConversation> a, IReadOnlyList<DesktopConversation> b) =>
            a.Count == b.Count
            && a.Zip(b).All(p =>
                p.First.State == p.Second.State
                && String.Equals(p.First.Title, p.Second.Title, StringComparison.Ordinal));
    }
}
