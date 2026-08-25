namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Stable slot assignment for conversation keys — the fix for the product's worst UX defect
    /// (external review, 2026-08-25): the sidebar orders by recency, so mirroring it re-mapped
    /// physical keys under the user's fingers, sometimes between the glance and the press.
    /// *Live information is good; live remapping of physical controls is not.*
    ///
    /// The model is the terminal grid's (SessionRegistry): a conversation CLAIMS a slot on first
    /// sight and keeps it — through reorders, state changes, and sidebar churn — until it leaves
    /// the sidebar entirely. New conversations fill the lowest empty slot. The app's own pinning
    /// rides along free: a pinned chat never leaves the sidebar, so it never moves.
    ///
    /// Identity is the TITLE — the only identity the AX tree offers. Two honest consequences,
    /// accepted and documented: a retitled conversation reads as departed-plus-new (it moves),
    /// and duplicate titles collapse to the first. Both beat the alternative, which was every
    /// key moving every time anything happened.
    /// </summary>
    internal sealed class DesktopSlotMap
    {
        public const Int32 SlotCount = 6;

        private readonly String[] _slots = new String[SlotCount];

        /// <summary>
        /// Reconcile the sidebar reading into stable slots. Returns exactly
        /// <see cref="SlotCount"/> entries; null = empty slot.
        /// </summary>
        public DesktopConversation[] Apply(IReadOnlyList<DesktopConversation> sidebar)
        {
            // Membership = the sidebar's top-N distinct titles (recency, the app's own order).
            var membership = new Dictionary<String, DesktopConversation>(StringComparer.Ordinal);
            foreach (var c in sidebar ?? Array.Empty<DesktopConversation>())
            {
                if (membership.Count == SlotCount)
                {
                    break;
                }
                if (!String.IsNullOrEmpty(c.Title) && !membership.ContainsKey(c.Title))
                {
                    membership[c.Title] = c;
                }
            }

            // Evict what fell out of the top-N (or left the sidebar). This is the ONLY way a
            // slot frees, and it is what makes room for the newcomer without moving survivors.
            for (var i = 0; i < SlotCount; i++)
            {
                if (_slots[i] != null && !membership.ContainsKey(_slots[i]))
                {
                    _slots[i] = null;
                }
            }

            // Fill: new members take the lowest empty slot, in sidebar order — a fresh map
            // matches the sidebar top-to-bottom and only diverges later, which is the point.
            var assigned = new HashSet<String>(_slots.Where(t => t != null), StringComparer.Ordinal);
            foreach (var c in membership.Values)
            {
                if (assigned.Contains(c.Title))
                {
                    continue;
                }

                var empty = Array.IndexOf(_slots, null);
                if (empty < 0)
                {
                    break;
                }

                _slots[empty] = c.Title;
                assigned.Add(c.Title);
            }

            return _slots.Select(t => t != null && membership.TryGetValue(t, out var c) ? c : null).ToArray();
        }

        /// <summary>Forget everything — used when the surface goes away entirely.</summary>
        public void Clear() => Array.Clear(_slots, 0, SlotCount);
    }
}
