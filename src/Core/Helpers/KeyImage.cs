namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

    /// <summary>
    /// Renders key faces. Current style: a clean dark background with a large, centred, COLORED
    /// icon (the colour is baked into the embedded PNG) and NO label. Live-display keys with no
    /// icon fall back to centred text. Centralises ALL BitmapBuilder use.
    ///
    /// Style is one-line switchable here:
    ///   • colored tiles  → change Clear(Background) to Clear(<the passed colour>) and draw a label
    ///   • white icons    → regenerate icons white in tools/generate-icons.swift
    /// </summary>
    internal static class KeyImage
    {
        // Palette. The state colours are the DESIGN's, sampled from the 2026-08-26 Logitech
        // frames rather than picked here — their set is deliberately muted against the Tailwind-ish
        // values this file used to carry, and mixing the two reads as two palettes on one pad.
        // Coral is Claude's identity colour, Blue is Codex's; identity and state must stay on
        // different cues, so a session's agent must never be signalled by Amber/Red/Green.
        // ("Always allow", #275DA3 in the same frames, gets a constant when a key actually binds it.)
        public static readonly BitmapColor Green  = new BitmapColor(0x4F, 0xA9, 0x75);   // Allow
        public static readonly BitmapColor Red    = new BitmapColor(0xDA, 0x3D, 0x29);   // Deny / risk
        public static readonly BitmapColor Amber  = new BitmapColor(0xE2, 0x9D, 0x37);   // waiting on approval
        public static readonly BitmapColor Coral  = new BitmapColor(0xCC, 0x7C, 0x5E);   // Claude identity
        public static readonly BitmapColor Blue   = new BitmapColor(0x60, 0xA5, 0xFA);
        public static readonly BitmapColor Purple = new BitmapColor(0xA7, 0x8B, 0xFA);
        public static readonly BitmapColor Slate  = new BitmapColor(0x94, 0xA3, 0xB8);
        // A muted neutral grey for the session state-word bar's quiet states (Ready/Thinking/
        // Waiting) — Slate reads as bright BLUE on the OLED and clashed with the blue selection
        // brackets; amber is reserved for "your approval is wanted".
        public static readonly BitmapColor Gray   = new BitmapColor(0x5A, 0x5A, 0x60);
        public static readonly BitmapColor Dark   = new BitmapColor(0x0D, 0x11, 0x17);

        // Pure black: matches the profile's stored icon tiles, the Options+ editor background,
        // and the hardware bezel as closely as a backlit LCD allows. The previous #0D1117
        // (GitHub-dark) read as a mismatched box next to those pure-black surfaces.
        private static readonly BitmapColor Background = new BitmapColor(0x00, 0x00, 0x00);
        private static readonly BitmapColor White = new BitmapColor(0xFF, 0xFF, 0xFF);

        /// <summary>Corner-bracket colour marking the session key the typing keys are aimed at.</summary>
        private static readonly BitmapColor Selection = new BitmapColor(0x60, 0xA5, 0xFA);

        // Approval badge: amber for a routine request, red when the command is destructive.
        // These two are the only palette entries that paint pixels today — Render ignores its
        // accent argument whenever an icon is present, because the colour lives in the PNG.
        private static readonly BitmapColor BadgeWaiting = Amber;
        private static readonly BitmapColor BadgeRisk = Red;

        /// <summary>
        /// Draw a key face. With an <paramref name="icon"/> (resource basename), the colored PNG is
        /// drawn large and centred with no label. Without one, the label is centred (live displays).
        /// The <paramref name="accent"/> colour is currently unused (kept for easy style switching).
        /// </summary>
        public static BitmapImage Render(PluginImageSize imageSize, String label, BitmapColor accent, String icon = null)
        {
            using (var bitmap = new BitmapBuilder(imageSize))
            {
                bitmap.Clear(Background);

                if (!String.IsNullOrEmpty(icon))
                {
                    try
                    {
                        // Qualify with the "icons." folder segment: the SDK's resource finder matches
                        // by name SUFFIX, so a bare "up.png" also matches "scroll_up.png" (and "tab.png"
                        // matches "new_tab.png"), and it returns the first alphabetically — the wrong one.
                        // "icons.up.png" pins the lookup to exactly one embedded resource.
                        var img = PluginResources.ReadImage("icons." + icon + ".png");
                        var w = bitmap.Width;
                        var h = bitmap.Height;
                        var s = (Int32)(Math.Min(w, h) * 0.82);
                        bitmap.DrawImage(img, (w - s) / 2, (h - s) / 2, s, s);
                        return bitmap.ToImage();
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Verbose(ex, $"KeyImage: icon '{icon}' failed to load — falling back to text");
                    }
                }

                bitmap.DrawText(label ?? "");
                return bitmap.ToImage();
            }
        }

        /// <summary>
        /// A session-grid key face. Hardware taught the layout (photos, 3 iterations):
        /// the bitmap covers only the UPPER SQUARE of the key — the service always reserves the
        /// bottom strip for the label, and that strip's single-line font is the largest, crispest
        /// text a key can carry (two-line labels get shrunk; in-bitmap text at comparable size
        /// clips). So identity goes where the platform is strongest: the PROJECT NAME is the
        /// service label (SessionSlotCommand.GetCommandDisplayName), matching every other key's
        /// design language — and the bitmap carries the state icon with the small slate context %
        /// under it. An empty slot is a plain dark face. <paramref name="selected"/> adds corner
        /// brackets so you can see which session the typing keys are pointed at.
        /// </summary>
        /// <summary>
        /// A session-grid key face, redrawn to the 2026-08 design: the session NAME on top and a
        /// colour-filled STATE-WORD bar below it (Thinking / Allow? / Waiting / Ready). Amber is
        /// reserved for "your approval is wanted", the one state that needs the user.
        ///
        /// The bar lives inside the BITMAP, not the service's own label strip below the key, because
        /// that strip is a fixed dark single-line label the plugin cannot colour — and the design's
        /// point is a coloured state bar. So the whole face is custom; the service strip is left
        /// blank. An empty slot is a plain dark face. The bar's COLOUR is the selection cue: the
        /// caller passes amber for the routed session, grey for the rest (no corner brackets).
        /// </summary>
        public static BitmapImage RenderSessionSlot(
            PluginImageSize imageSize, String name, String stateWord,
            BitmapColor barColor, Boolean darkText)
        {
            using (var bitmap = new BitmapBuilder(imageSize))
            {
                bitmap.Clear(Background);
                var w = bitmap.Width;
                var h = bitmap.Height;
                var scale = Math.Min(w, h) / 96f;
                var pad = (Int32)(4 * scale);

                // Empty slot: a plain dark face (no live session in this slot).
                if (String.IsNullOrEmpty(stateWord))
                {
                    return bitmap.ToImage();
                }

                // NAME — the design's title, over the top ~60%. Smaller than the service's own label
                // font, but the design puts the name ABOVE the state and the service strip is pinned
                // to the very bottom, so the name has to live in the bitmap.
                if (!String.IsNullOrWhiteSpace(name))
                {
                    // Wrap into up to two lines — DrawText centres a single line and CLIPS the
                    // overflow (a long "claude-console" lost both ends on the device) instead of
                    // wrapping, so the split is done here.
                    var lines = WrapTwo(name, 11);
                    var lineH = (Int32)(17 * scale);
                    var top = pad + Math.Max(0, ((Int32)(h * 0.62) - (lines.Length * lineH)) / 2);
                    for (var i = 0; i < lines.Length; i++)
                    {
                        bitmap.DrawText(lines[i], pad, top + (i * lineH), w - (2 * pad), lineH, White, fontSize: (Int32)(14 * scale));
                    }
                }

                // STATE-WORD BAR — a filled band with the word centred, flush to the bottom and both
                // side edges (zero padding) so it reads as a solid strip. Colour = selection (amber
                // for the routed session, grey otherwise); dark text on amber, white on grey.
                var barH = (Int32)(h * 0.30);
                var barY = h - barH;
                bitmap.FillRectangle(0, barY, w, barH, barColor);
                bitmap.DrawText(stateWord, 0, barY, w, barH, darkText ? Dark : White, fontSize: (Int32)(14 * scale));

                return bitmap.ToImage();
            }
        }

        /// <summary>
        /// A normal key face plus an approval badge — used by Yes / No so you can see that an answer
        /// is wanted, and whether it's routine, without looking at the screen.
        /// </summary>
        public static BitmapImage RenderWithApprovalBadge(
            PluginImageSize imageSize, String label, BitmapColor accent, String icon, ApprovalRisk risk)
        {
            if (risk == ApprovalRisk.None)
            {
                return Render(imageSize, label, accent, icon);   // nothing pending: the usual face
            }

            using (var bitmap = new BitmapBuilder(imageSize))
            {
                bitmap.Clear(Background);

                if (!String.IsNullOrEmpty(icon))
                {
                    try
                    {
                        var img = PluginResources.ReadImage("icons." + icon + ".png");
                        var s = (Int32)(Math.Min(bitmap.Width, bitmap.Height) * 0.82);
                        bitmap.DrawImage(img, (bitmap.Width - s) / 2, (bitmap.Height - s) / 2, s, s);
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Verbose(ex, $"KeyImage: icon '{icon}' failed to load — falling back to text");
                        bitmap.DrawText(label ?? "");
                    }
                }
                else
                {
                    bitmap.DrawText(label ?? "");
                }

                DrawApprovalBadge(bitmap, risk);
                return bitmap.ToImage();
            }
        }

        // A filled dot in the top-right corner. Amber = something wants an answer; red = that
        // something is destructive. Drawn in code so no new icon art has to ship (and so the two
        // states can never drift apart visually). A thin background-colour halo ring gives the dot
        // a clean silhouette over whatever sits behind it (an icon corner on Yes/No, a bracket on
        // a session key) — the standard notification-dot treatment.
        private static void DrawApprovalBadge(BitmapBuilder bitmap, ApprovalRisk risk)
        {
            if (risk == ApprovalRisk.None)
            {
                return;
            }

            var scale = Math.Min(bitmap.Width, bitmap.Height) / 96f;
            var radius = Math.Max(2, (Int32)(9 * scale));
            var halo = Math.Max(1, (Int32)(2 * scale));
            var inset = (Int32)(3 * scale);
            var cx = bitmap.Width - inset - radius;
            var cy = inset + radius;

            bitmap.FillCircle(cx, cy, radius + halo, Background);
            bitmap.FillCircle(cx, cy, radius, risk == ApprovalRisk.High ? BadgeRisk : BadgeWaiting);
        }

        // Top corner brackets marking the pinned session. Everything scales off the key's short
        // side so the marker looks the same on every PluginImageSize the SDK asks for. 3px arms on
        // purpose: selection must read from an arm's length without focusing, and 2px didn't.
        // Split a session name into at most two lines, breaking at a hyphen/space/underscore near
        // the middle when there is one (so "claude-console" -> "claude-" / "console"), otherwise at
        // the midpoint. The second line is ellipsised if it would still overflow.
        private static String[] WrapTwo(String s, Int32 maxLen)
        {
            if (s.Length <= maxLen)
            {
                return new[] { s };
            }

            var mid = s.Length / 2;
            var best = -1;
            for (var i = 1; i < s.Length - 1; i++)
            {
                if ((s[i] == '-' || s[i] == ' ' || s[i] == '_') &&
                    (best < 0 || Math.Abs(i - mid) < Math.Abs(best - mid)))
                {
                    best = i;
                }
            }

            var cut = best > 0 ? best + 1 : mid;   // keep a hyphen on the first line
            var a = s.Substring(0, cut);
            var b = s.Substring(cut);
            if (b.Length > maxLen)
            {
                b = b.Substring(0, maxLen - 1) + "…";
            }

            return new[] { a, b };
        }

        private static void DrawSelectionCorners(BitmapBuilder bitmap, Boolean badgePresent)
        {
            var scale = Math.Min(bitmap.Width, bitmap.Height) / 96f;
            var inset = (Int32)(4 * scale);
            var arm = (Int32)(18 * scale);
            var thickness = Math.Max(2, (Int32)(3 * scale));

            // top-left
            bitmap.FillRectangle(inset, inset, arm, thickness, Selection);
            bitmap.FillRectangle(inset, inset, thickness, arm, Selection);

            // The badge owns the top-right corner while an approval is pending — pinned-and-
            // waiting is the single most important state this key has, and dot-over-bracket
            // turned it to mush (a shortened bracket stub reads as a rendering glitch, so the
            // whole bracket yields; the left one alone still marks the pin).
            if (badgePresent)
            {
                return;
            }

            // top-right
            bitmap.FillRectangle(bitmap.Width - inset - arm, inset, arm, thickness, Selection);
            bitmap.FillRectangle(bitmap.Width - inset - thickness, inset, thickness, arm, Selection);
        }
    }
}
