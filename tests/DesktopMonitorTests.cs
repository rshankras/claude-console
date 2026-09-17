namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    using Xunit;

    /// <summary>
    /// The monitor's snapshot→state mapping, driven through a fake automation (the
    /// PlatformSeamTests pattern: performs nothing, records everything). The one rule that must
    /// survive every refactor is the mass-removal rule: when the surface disappears mid-approval
    /// (screen lock takes the whole web AX tree at once), the state is UNAVAILABLE — reading it
    /// as "the card resolved" would report an approval nobody gave.
    /// </summary>
    public class DesktopMonitorTests
    {
        private sealed class FakeAutomation : IDesktopAutomation
        {
            public DesktopSnapshot Next = DesktopSnapshot.Unavailable;

            public DesktopSnapshot Status() => this.Next;
            public Boolean Press(String[] labels, out String matched) { matched = null; return false; }
            public Boolean PressGuarded(String[] labels, String expectCard, out String matched, out String error) { matched = null; error = null; return false; }
            public Boolean WriteComposer(String text, Boolean send, out String error) { error = null; return true; }
            public Boolean SwitchMode(String modeName) => true;
            public Boolean FocusApp() => true;
        }

        private static DesktopSnapshot Snap(Boolean approval = false, Boolean stop = false,
            String card = "", String mode = "Codex", Boolean attention = false) => new DesktopSnapshot
        {
            SurfaceAvailable = true,
            ApprovalPresent = approval,
            DenyPresent = approval,
            StopPresent = stop,
            CardText = card,
            Mode = mode,
            Attention = attention,
        };

        [Fact]
        public void Mode_and_draft_changes_refresh_faces_even_with_identical_titles()
        {
            var fake = new FakeAutomation();
            using var monitor = new DesktopMonitor(fake);
            var states = new List<DesktopState>();
            monitor.OnChanged += states.Add;
            var conversations = new[] { new DesktopConversation { Title = "Plan" } };
            fake.Next = new DesktopSnapshot { SurfaceAvailable = true, Mode = "ChatGPT",
                AvailableControls = DesktopControl.Search, Conversations = conversations };
            monitor.PollOnce();
            fake.Next = new DesktopSnapshot { SurfaceAvailable = true, Mode = "Codex",
                AvailableControls = DesktopControl.Changes, Conversations = conversations };
            monitor.PollOnce();
            fake.Next = new DesktopSnapshot { SurfaceAvailable = true, Mode = "Codex", CanSend = true,
                AvailableControls = DesktopControl.Changes, Conversations = conversations };
            monitor.PollOnce();
            Assert.Equal(3, states.Count);
            Assert.Equal("Codex", states[1].Mode);
            Assert.Equal(DesktopControl.Changes, states[1].AvailableControls);
            Assert.True(states[2].CanSend);
            Assert.Equal("Plan", states[2].Slots[0].Title);
        }

        [Fact]
        public void Control_presence_maps_to_the_three_activities()
        {
            Assert.Equal(DesktopActivity.Ready, DesktopMonitor.Map(Snap()).Activity);
            Assert.Equal(DesktopActivity.Working, DesktopMonitor.Map(Snap(stop: true)).Activity);
            Assert.Equal(DesktopActivity.WaitingApproval, DesktopMonitor.Map(Snap(approval: true)).Activity);
        }

        [Fact]
        public void An_approval_outranks_a_running_task()
        {
            // A card parks the task on the user even while Stop is still rendered.
            var state = DesktopMonitor.Map(Snap(approval: true, stop: true));

            Assert.Equal(DesktopActivity.WaitingApproval, state.Activity);
        }

        [Fact]
        public void Card_text_the_classifier_cannot_flag_grades_amber_never_none()
        {
            // The card usually shows natural language, not a shell command. Waiting-with-a-card
            // is at least Normal by definition — None would unlight the approval keys.
            var state = DesktopMonitor.Map(Snap(approval: true, card: "Create a text file on your Desktop."));

            Assert.Equal(ApprovalRisk.Normal, state.Risk);
        }

        [Fact]
        public void Card_text_with_a_flaggable_command_grades_red()
        {
            var state = DesktopMonitor.Map(Snap(approval: true, card: "rm -rf node_modules && npm install"));

            Assert.Equal(ApprovalRisk.High, state.Risk);
        }

        [Fact]
        public void No_approval_means_no_risk_at_all()
        {
            Assert.Equal(ApprovalRisk.None, DesktopMonitor.Map(Snap(stop: true)).Risk);
        }

        [Fact]
        public void Losing_the_surface_mid_approval_reads_as_unavailable_not_resolved()
        {
            var fake = new FakeAutomation();
            using var monitor = new DesktopMonitor(fake);
            var seen = new List<DesktopActivity>();
            monitor.OnChanged += s => seen.Add(s.Activity);

            fake.Next = Snap(approval: true, card: "Run npm install");
            monitor.PollOnce();
            fake.Next = DesktopSnapshot.Unavailable;   // the screen locked
            monitor.PollOnce();

            Assert.Equal(DesktopActivity.Unavailable, monitor.Current.Activity);
            Assert.Equal(
                new[] { DesktopActivity.WaitingApproval, DesktopActivity.Unavailable }, seen);
            Assert.DoesNotContain(DesktopActivity.Ready, seen);   // never "it resolved itself"
        }

        [Fact]
        public void Unchanged_state_fires_no_event()
        {
            var fake = new FakeAutomation();
            using var monitor = new DesktopMonitor(fake);
            var fired = 0;
            monitor.OnChanged += _ => fired++;

            fake.Next = Snap(stop: true);
            monitor.PollOnce();
            monitor.PollOnce();
            monitor.PollOnce();

            Assert.Equal(1, fired);
        }

        [Fact]
        public void The_cadence_is_hot_only_while_something_is_happening()
        {
            Assert.Equal(1000, DesktopMonitor.NextDelayMs(DesktopActivity.Working));
            Assert.Equal(1000, DesktopMonitor.NextDelayMs(DesktopActivity.WaitingApproval));
            Assert.Equal(3000, DesktopMonitor.NextDelayMs(DesktopActivity.Ready));
            Assert.Equal(5000, DesktopMonitor.NextDelayMs(DesktopActivity.Unavailable));
        }

        [Fact]
        public void The_approve_face_learns_its_target_only_when_one_conversation_awaits()
        {
            var one = new DesktopSnapshot
            {
                SurfaceAvailable = true,
                ApprovalPresent = true,
                CardText = "Run npm install",
                Conversations = new[]
                {
                    new DesktopConversation { Title = "backend", State = ConversationState.Awaiting },
                    new DesktopConversation { Title = "frontend", State = ConversationState.Running },
                },
            };

            Assert.Equal("backend", DesktopMonitor.Map(one).ActiveTitle);

            var two = new DesktopSnapshot
            {
                SurfaceAvailable = true,
                ApprovalPresent = true,
                Conversations = new[]
                {
                    new DesktopConversation { Title = "backend", State = ConversationState.Awaiting },
                    new DesktopConversation { Title = "frontend", State = ConversationState.Awaiting },
                },
            };

            // Two waiting: which one is open is not knowable — empty, never a guess.
            Assert.Equal("", DesktopMonitor.Map(two).ActiveTitle);
        }

        [Fact]
        public void The_unavailable_reason_reaches_the_state()
        {
            var state = DesktopMonitor.Map(new DesktopSnapshot { UnavailableReason = "hidden" });

            Assert.Equal(DesktopActivity.Unavailable, state.Activity);
            Assert.Equal("hidden", state.Reason);
        }

        [Fact]
        public void Slots_stay_put_across_polls_while_the_sidebar_reorders()
        {
            var fake = new FakeAutomation();
            using var monitor = new DesktopMonitor(fake);

            DesktopSnapshot With(params String[] titles) => new DesktopSnapshot
            {
                SurfaceAvailable = true,
                Conversations = titles.Select(x => new DesktopConversation { Title = x }).ToList(),
            };

            fake.Next = With("A", "B", "C");
            monitor.PollOnce();
            fake.Next = With("C", "B", "A");   // full reorder
            monitor.PollOnce();

            Assert.Equal(new[] { "A", "B", "C" },
                monitor.Current.Slots.Take(3).Select(s => s.Title));
        }

        [Fact]
        public void A_throwing_automation_degrades_to_unavailable_instead_of_crashing_the_tick()
        {
            var monitor = new DesktopMonitor(new ThrowingAutomation());

            monitor.PollOnce();   // must not throw — a tick can never take the service down

            Assert.Equal(DesktopActivity.Unavailable, monitor.Current.Activity);
        }

        private sealed class ThrowingAutomation : IDesktopAutomation
        {
            public DesktopSnapshot Status() => throw new InvalidOperationException("boom");
            public Boolean Press(String[] labels, out String matched) => throw new InvalidOperationException();
            public Boolean PressGuarded(String[] labels, String expectCard, out String matched, out String error) => throw new InvalidOperationException();
            public Boolean WriteComposer(String text, Boolean send, out String error) => throw new InvalidOperationException();
            public Boolean SwitchMode(String modeName) => throw new InvalidOperationException();
            public Boolean FocusApp() => throw new InvalidOperationException();
        }
    }
}
