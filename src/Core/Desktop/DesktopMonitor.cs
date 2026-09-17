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
        public DesktopControl AvailableControls { get; init; }

        /// <summary>Sidebar conversations, raw, in the app's own order (recency first).</summary>
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
    /// impossible. 1s cadence; a reading costs ~130ms of helper wall-clock.
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

        /// <summary>Fires on every material change (activity, risk, attention, mode, card text).</summary>
        public event Action<DesktopState> OnChanged;

        public DesktopState Current { get; private set; } = DesktopState.Unavailable;

        public DesktopMonitor(IDesktopAutomation automation) => _automation = automation;

        public void Start()
        {
            if (_timer != null)
            {
                return;
            }

            _timer = new Timer(_ => this.PollOnce(), null, 0, Timeout.Infinite);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        public void Dispose() => this.Stop();

        // Internal so tests drive ticks synchronously; with no timer armed, the re-arm is a no-op.
        internal void PollOnce()
        {
            try
            {
                this.Apply(Map(_automation.Status(), _slotMap));
            }
            catch (Exception ex)
            {
                // A monitor tick must never take the service down; unavailable is the honest fallback.
                PluginLog.Warning(ex, "DesktopMonitor: poll failed");
                this.Apply(DesktopState.Unavailable);
            }
            finally
            {
                // Re-arm only after this tick fully finished — ticks can never overlap or pile
                // up — at a cadence matched to what's happening (review round: adaptive polling).
                try { _timer?.Change(NextDelayMs(this.Current.Activity), Timeout.Infinite); } catch (ObjectDisposedException) { }
            }
        }

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

            // Approval identity, from verified signals only (the app exposes no selection today).
            var active = conversations.FirstOrDefault(c => c.Selected)?.Title;
            if (active == null && snap.ApprovalPresent)
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
