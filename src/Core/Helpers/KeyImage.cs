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
        // Quiet conversation states. Slate reads blue on the keypad OLED; this stays neutral so
        // amber and green remain unmistakable attention/completion signals.
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
            => RenderFromResource(imageSize, label, icon, "icons.");

        /// <summary>
        /// Render a Vizhi Desktop action with the dedicated monochrome Codex/ChatGPT icon set.
        /// Keeping these resources separate prevents the desktop palette from recolouring the
        /// Claude Console and Codex CLI products, which share this renderer.
        /// </summary>
        public static BitmapImage RenderDesktop(PluginImageSize imageSize, String label, String icon = null)
            => RenderFromResource(imageSize, label, icon, "desktop_icons.");

        private static BitmapImage RenderFromResource(
            PluginImageSize imageSize, String label, String icon, String resourcePrefix)
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
                        var img = PluginResources.ReadImage(resourcePrefix + icon + ".png");
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
        public static BitmapImage RenderSessionSlot(
            PluginImageSize imageSize, String icon, Int32? ctxPercent,
            Boolean selected, ApprovalRisk risk = ApprovalRisk.None)
        {
            using (var bitmap = new BitmapBuilder(imageSize))
            {
                bitmap.Clear(Background);
                var w = bitmap.Width;
                var h = bitmap.Height;
                var scale = Math.Min(w, h) / 96f;
                var pad = (Int32)(2 * scale);

                if (!String.IsNullOrEmpty(icon))
                {
                    try
                    {
                        var img = PluginResources.ReadImage("icons." + icon + ".png");
                        var s = (Int32)(Math.Min(w, h) * 0.44);
                        bitmap.DrawImage(img, (w - s) / 2, (Int32)(h * 0.06), s, s);
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Verbose(ex, $"KeyImage: session icon '{icon}' failed to load");
                    }
                }

                if (ctxPercent.HasValue)
                {
                    // White and readable — slate at 13 was fine print on the real key. Colour
                    // carries meaning, matching the Context gauge key's thresholds: amber when the
                    // window is filling (75%+), red when it's nearly full (90%+) — so a session
                    // that needs /compact flags itself from across the room.
                    // Drawn TWICE, 1px apart: DrawText has no weight parameter, and the double
                    // strike is a renderer-proof bold.
                    var pct = ctxPercent.Value;
                    var color = pct >= 90 ? Red : pct >= 75 ? Amber : White;
                    var text = $"{pct}%";
                    var y = (Int32)(h * 0.54);
                    var th = (Int32)(h * 0.40);
                    var size = (Int32)(17 * scale);
                    var embolden = Math.Max(1, (Int32)(1 * scale));
                    bitmap.DrawText(text, pad, y, w - (2 * pad), th, color, fontSize: size);
                    bitmap.DrawText(text, pad + embolden, y, w - (2 * pad), th, color, fontSize: size);
                }

                if (selected)
                {
                    DrawSelectionCorners(bitmap, badgePresent: risk != ApprovalRisk.None);
                }

                DrawApprovalBadge(bitmap, risk);
                return bitmap.ToImage();
            }
        }

        /// <summary>
        /// A full-surface conversation card: the conversation title occupies the upper 75% and
        /// the live state is written inside a flush colour bar across the bottom 25%.
        ///
        /// This deliberately bypasses Options+' inset icon canvas and static label strip. A
        /// conversation is live information, not an icon: its identity and state must remain one
        /// glanceable unit and update together when the desktop sidebar changes.
        /// </summary>
        public static BitmapImage RenderConversationSlot(
            PluginImageSize imageSize, String title, String stateWord,
            BitmapColor barColor, Boolean darkText)
        {
            using (var bitmap = ButtonCanvas(imageSize))
            {
                bitmap.Clear(Background);

                if (String.IsNullOrWhiteSpace(title) || String.IsNullOrWhiteSpace(stateWord))
                {
                    return bitmap.ToImage();
                }

                var w = bitmap.Width;
                var h = bitmap.Height;
                var scale = Math.Min(w, h) / 96f;
                var pad = Math.Max(2, (Int32)(4 * scale));
                var titleH = (Int32)(h * 0.75f);

                // Use the whole title region. One/two-line names get larger type; long titles can
                // take three balanced lines instead of leaving black space while ellipsising early.
                var lines = WrapConversationTitle(title, 12, 3);
                var fontSize = (Int32)((lines.Length switch { 1 => 18, 2 => 16, _ => 15 }) * scale);
                var lineH = (Int32)((lines.Length switch { 1 => 22, 2 => 21, _ => 18 }) * scale);
                var top = Math.Max(0, (titleH - (lines.Length * lineH)) / 2);
                for (var i = 0; i < lines.Length; i++)
                {
                    bitmap.DrawText(
                        lines[i], pad, top + (i * lineH), w - (2 * pad), lineH,
                        White, fontSize: fontSize);
                }

                var barY = titleH;
                var barH = h - barY;
                bitmap.FillRectangle(0, barY, w, barH, barColor);
                bitmap.DrawText(
                    stateWord, 0, barY, w, barH,
                    darkText ? Dark : White,
                    fontSize: (Int32)(14 * scale));

                return bitmap.ToImage();
            }
        }

        /// <summary>
        /// A normal key face plus an approval badge — used by Yes / No so you can see that an answer
        /// is wanted, and whether it's routine, without looking at the screen.
        /// </summary>
        public static BitmapImage RenderWithApprovalBadge(
            PluginImageSize imageSize, String label, BitmapColor accent, String icon, ApprovalRisk risk)
            => RenderWithApprovalBadgeFromResource(imageSize, label, accent, icon, risk, "icons.");

        /// <summary>Desktop counterpart using the unified Codex/ChatGPT icon resources.</summary>
        public static BitmapImage RenderDesktopWithApprovalBadge(
            PluginImageSize imageSize, String label, BitmapColor accent, String icon, ApprovalRisk risk)
            => RenderWithApprovalBadgeFromResource(
                imageSize, label, accent, icon, risk, "desktop_icons.");

        private static BitmapImage RenderWithApprovalBadgeFromResource(
            PluginImageSize imageSize, String label, BitmapColor accent, String icon,
            ApprovalRisk risk, String resourcePrefix)
        {
            if (risk == ApprovalRisk.None)
            {
                return RenderFromResource(imageSize, label, icon, resourcePrefix);
            }

            using (var bitmap = new BitmapBuilder(imageSize))
            {
                bitmap.Clear(Background);

                if (!String.IsNullOrEmpty(icon))
                {
                    try
                    {
                        var img = PluginResources.ReadImage(resourcePrefix + icon + ".png");
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

        private static BitmapBuilder ButtonCanvas(PluginImageSize imageSize)
        {
            var width = imageSize.GetButtonWidth();
            var height = imageSize.GetButtonHeight();
            return width > 0 && height > 0
                ? new BitmapBuilder(width, height)
                : new BitmapBuilder(imageSize);
        }

        internal static String[] WrapConversationTitle(String value, Int32 maxLength, Int32 maxLines)
        {
            if (String.IsNullOrWhiteSpace(value) || maxLength < 2 || maxLines < 1)
            {
                return Array.Empty<String>();
            }

            var remaining = String.Join(" ", value.Trim().Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries));
            var lines = new System.Collections.Generic.List<String>();

            while (!String.IsNullOrEmpty(remaining) && lines.Count < maxLines)
            {
                if (remaining.Length <= maxLength)
                {
                    lines.Add(remaining);
                    break;
                }

                if (lines.Count == maxLines - 1)
                {
                    lines.Add(remaining.Substring(0, maxLength - 1).TrimEnd() + "…");
                    break;
                }

                // Include the boundary character in the search: "Find planned" is exactly 12
                // characters and the following space is the ideal cut, not the earlier one.
                var window = remaining.Substring(0, Math.Min(remaining.Length, maxLength + 1));
                var breakAt = window.LastIndexOf(' ');
                if (breakAt <= 0 || breakAt > maxLength)
                {
                    breakAt = maxLength;
                }

                lines.Add(remaining.Substring(0, breakAt).TrimEnd());
                remaining = remaining.Substring(breakAt).TrimStart();
            }

            return lines.ToArray();
        }

        // Top corner brackets marking the pinned session. Everything scales off the key's short
        // side so the marker looks the same on every PluginImageSize the SDK asks for. 3px arms on
        // purpose: selection must read from an arm's length without focusing, and 2px didn't.
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
