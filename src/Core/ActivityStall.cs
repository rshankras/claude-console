namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.IO;

    /// <summary>
    /// When is a session that claims to be "busy" actually finished? (#30)
    ///
    /// Activity comes from the agent's lifecycle hooks, and the only exit from busy is the agent
    /// saying so — `Stop` for Claude Code. **An interrupted turn fires no hook at all**: press Esc
    /// while it thinks and nothing further is written until you submit a new prompt, so the key
    /// keeps its hourglass over an idle session.
    ///
    /// The obvious repair — expire a busy state by age — is the one that must not be used alone.
    /// Sessions are legitimately busy for a long time, and a single slow tool call is indistinguish-
    /// able by age from a dead turn. The distinguishing signal is the transcript: the agent appends
    /// to it throughout a turn, so it grows while working and stops the instant the turn dies.
    /// Measured on the device: a genuinely busy session had written its transcript 1s earlier, the
    /// stuck one 113s earlier. Measured again here while writing this, a working session's transcript
    /// lagged by ~9s — which is why the quiet window is 90s and not 30s.
    ///
    /// This replaces TWO different thresholds that used to disagree about the same question:
    /// SessionRegistry expired a busy state after 45s (driving the session-slot keys) while
    /// BridgeManager used a bare 300s literal (driving the Status key's hourglass). QA measured the
    /// stuck session at 114s — past the first, far short of the second — so the session keys had
    /// already given up while the hourglass was still turning. That divergence is why the existing
    /// timeout looked like it "never fired": the key in the report was never governed by it.
    /// </summary>
    internal static class ActivityStall
    {
        /// <summary>
        /// A busy session whose transcript has not grown for this long has stopped working. The
        /// issue's recommended window is 60–120s; 90s sits clear of the ~9s lag a healthy session
        /// shows while still clearing the key well inside the two minutes QA waited.
        /// </summary>
        internal static readonly TimeSpan TranscriptQuietFor = TimeSpan.FromSeconds(90);

        /// <summary>
        /// Last resort, used ONLY when there is no transcript to consult — an agent that reports
        /// none, a path that has gone away, or a state file written before the field existed.
        ///
        /// Deliberately far longer than the 45s it replaces on that path. 45s was short enough to
        /// call a legitimately long tool call finished, and with the transcript rule now doing the
        /// real work this exists only so a session cannot stay busy forever when the better signal
        /// is unavailable. Erring long is the safe direction here: a key that clears too early
        /// reports a lie about a session that is still working.
        ///
        /// The value is not a new invention — it is exactly the 300s BridgeManager already used for
        /// this same fallback, so unifying the two thresholds keeps the more conservative of them
        /// rather than splitting the difference. A first attempt at 10 minutes was rejected by
        /// A_session_stuck_on_busy_settles_back_to_ready, which pins a 600s-old busy state settling
        /// to ready: that test's intent (a missed Stop hook must not strand the key on "Working"
        /// forever) survives this change untouched, and it should keep passing unmodified.
        /// </summary>
        internal static readonly TimeSpan NoTranscriptStallAfter = TimeSpan.FromSeconds(300);

        /// <summary>
        /// How long to wait after WE sent the interrupt. Short, because this is no longer an
        /// inference: the plugin pressed Escape into that session itself, so it is corroborating
        /// evidence it already had rather than 90 seconds of silence it has to sit through.
        ///
        /// Not zero, and the reason matters. Escape is not exclusively "interrupt" — it also exits a
        /// mode and dismisses a menu, and AnswerCommand sends it to REJECT a tool, after which the
        /// turn carries on. So the transcript still has to agree: if it grew after the interrupt, the
        /// agent kept working and this hint is discarded. The wait is what gives it time to say so.
        /// </summary>
        internal static readonly TimeSpan InterruptQuietFor = TimeSpan.FromSeconds(5);

        /// <summary>
        /// True when <paramref name="state"/> claims busy but the evidence says the turn is over.
        ///
        /// Pure, so the policy can be tested without a clock or a filesystem.
        /// <paramref name="transcriptMtimeUnix"/> is null when there is no transcript to consult.
        /// <paramref name="interruptedAtUnix"/> is when the plugin last sent Escape to this session,
        /// or null if it never did.
        /// </summary>
        internal static Boolean IsStalledBusy(
            String state,
            Int64 activityTsUnix,
            Int64? transcriptMtimeUnix,
            Int64 nowUnix,
            Int64? interruptedAtUnix = null)
        {
            if (!String.Equals(state, "busy", StringComparison.Ordinal))
            {
                return false;
            }

            // We pressed Escape, and nothing has been written since. Don't make the user watch an
            // hourglass for a minute and a half over a turn we ended ourselves.
            //
            // "Nothing since" is checked against BOTH files, and the activity file is the stronger
            // of the two. UserPromptSubmit writes busy at T0 and our Escape lands at T1 >= T0, so a
            // hint OLDER than the busy write cannot be about this turn: it is left over from a
            // previous session on a recycled tty (macOS reuses ttys000... as tabs close and open),
            // and honouring it would clear a brand-new session the instant it went busy. The same
            // test also catches an Escape that merely dismissed a menu or rejected a tool — the
            // turn carries on, PostToolUse rewrites busy with a newer stamp, and the hint expires.
            if (interruptedAtUnix.HasValue
                && interruptedAtUnix.Value >= activityTsUnix
                && (!transcriptMtimeUnix.HasValue || transcriptMtimeUnix.Value <= interruptedAtUnix.Value)
                && nowUnix - interruptedAtUnix.Value > InterruptQuietFor.TotalSeconds)
            {
                return true;
            }

            if (transcriptMtimeUnix.HasValue)
            {
                return nowUnix - transcriptMtimeUnix.Value > TranscriptQuietFor.TotalSeconds;
            }

            return nowUnix - activityTsUnix > NoTranscriptStallAfter.TotalSeconds;
        }

        /// <summary>
        /// Last-write time of <paramref name="transcriptPath"/> as a unix timestamp, or null when
        /// there is nothing to consult. A path the agent named but that does not exist counts as
        /// "no transcript" rather than "infinitely quiet": treating a missing file as evidence of a
        /// stall would clear the key for any agent whose transcript lives somewhere unreadable.
        /// </summary>
        internal static Int64? TranscriptMtime(String transcriptPath)
        {
            if (String.IsNullOrWhiteSpace(transcriptPath))
            {
                return null;
            }

            try
            {
                var info = new FileInfo(transcriptPath);
                return info.Exists ? new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds() : (Int64?)null;
            }
            catch (Exception)
            {
                // Unreadable for any reason — permissions, a path from another machine, a race with
                // the agent rotating it. Fall back to the age check rather than guessing.
                return null;
            }
        }
    }
}
