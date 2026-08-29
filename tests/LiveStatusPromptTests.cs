namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// The dialog half of the first press (#31): where the product can ask a yes/no question on
    /// screen, "Turn on" enables, "Not now" and silence do not, and a key press that settles the
    /// question first closes the dialog and wins. The dialog runs on its own thread — never the
    /// SDK's key thread — so these wait for the gate to settle rather than assume it.
    /// </summary>
    public class LiveStatusPromptTests
    {
        private sealed class Rig : IDisposable
        {
            public TempHome Home { get; } = new TempHome();
            public BridgeManager Bridge { get; }
            public LiveStatusGate Gate { get; }
            public List<String> Asked { get; } = new();
            public Int32 Cancelled;

            public Rig(Func<Boolean?> answer, ManualResetEventSlim hold = null)
            {
                this.Bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge());
                this.Bridge.Notify = (status, message, url, title) => { };
                this.Bridge.Prompt = (title, text, yes, no, seconds, cancel) =>
                {
                    lock (this.Asked) { this.Asked.Add(text); }
                    if (hold != null)
                    {
                        // The dialog is "open" until released or cancelled — like a real one.
                        WaitHandle.WaitAny(new[] { hold.WaitHandle, cancel.WaitHandle });
                    }
                    if (cancel.IsCancellationRequested)
                    {
                        Interlocked.Increment(ref this.Cancelled);
                        return null;
                    }
                    return answer();
                };
                this.Home.WriteSettings("{\"model\":\"opus\"}");
                this.Bridge.RunLoadWiringForTests();
                this.Gate = new LiveStatusGate(this.Bridge, "Cost", () => { }, applies: true);
            }

            public Boolean WaitUntil(Func<Boolean> condition, Int32 ms = 3000)
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(ms);
                while (DateTime.UtcNow < deadline)
                {
                    if (condition()) { return true; }
                    Thread.Sleep(10);
                }
                return condition();
            }

            public void Dispose() => this.Home.Dispose();
        }

        [Fact]
        public void Turn_on_in_the_dialog_enables()
        {
            using var rig = new Rig(() => true);

            Assert.True(rig.Gate.Press());

            Assert.True(rig.WaitUntil(() => rig.Bridge.LiveStatus == LiveStatusState.JustEnabled), "the dialog's yes never enabled");
            Assert.False(rig.Gate.Armed);
            Assert.Single(rig.Asked);
            Assert.StartsWith("Turn on the live keys?", rig.Asked[0]);
            Assert.Contains("5 hooks and a status line", rig.Asked[0]);
        }

        [Fact]
        public void Not_now_in_the_dialog_changes_nothing_and_disarms()
        {
            using var rig = new Rig(() => false);

            Assert.True(rig.Gate.Press());

            Assert.True(rig.WaitUntil(() => !rig.Gate.Armed), "the dialog's no never disarmed the key");
            Thread.Sleep(50);
            Assert.Equal(LiveStatusState.NotEnabled, rig.Bridge.LiveStatus);
            Assert.Equal("{\"model\":\"opus\"}", rig.Home.ReadSettings());
            Assert.Equal("Set up", rig.Gate.Label);   // the flash came down with the answer
        }

        [Fact]
        public void No_answer_changes_nothing_and_disarms()
        {
            using var rig = new Rig(() => null);

            Assert.True(rig.Gate.Press());

            Assert.True(rig.WaitUntil(() => !rig.Gate.Armed));
            Thread.Sleep(50);
            Assert.Equal(LiveStatusState.NotEnabled, rig.Bridge.LiveStatus);
            Assert.Equal("{\"model\":\"opus\"}", rig.Home.ReadSettings());
        }

        [Fact]
        public void A_second_key_press_while_the_dialog_is_open_closes_it_and_enables_once()
        {
            using var hold = new ManualResetEventSlim(false);
            using var rig = new Rig(() => true, hold);   // would say yes, if ever released

            Assert.True(rig.Gate.Press());                 // opens the dialog
            Assert.True(rig.WaitUntil(() => { lock (rig.Asked) { return rig.Asked.Count == 1; } }));
            Assert.True(rig.Gate.Press());                 // the user presses the key instead

            Assert.Equal(LiveStatusState.JustEnabled, rig.Bridge.LiveStatus);
            Assert.True(rig.WaitUntil(() => rig.Cancelled == 1), "the open dialog was not cancelled by the key press");
            Thread.Sleep(50);
            hold.Set();
            Thread.Sleep(50);
            Assert.Equal(LiveStatusState.JustEnabled, rig.Bridge.LiveStatus);   // the late 'yes' did nothing more
            Assert.Single(rig.Asked);
        }

        [Fact]
        public void With_no_dialog_available_the_first_press_still_arms_and_notifies()
        {
            using var rig = new Rig(() => true);
            rig.Bridge.Prompt = null;
            var toasts = new List<String>();
            rig.Bridge.Toast = (title, text) => toasts.Add(title + ": " + text);

            Assert.True(rig.Gate.Press());

            Assert.True(rig.Gate.Armed);
            Assert.Empty(rig.Asked);
            var toast = Assert.Single(toasts);
            Assert.StartsWith("Turn on live status?: Press Cost again", toast);
        }
    }
}
