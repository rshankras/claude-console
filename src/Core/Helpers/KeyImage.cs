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
        // Palette kept for callers / future use (e.g. restoring colored tiles).
        public static readonly BitmapColor Green  = new BitmapColor(0x22, 0xC5, 0x5E);
        public static readonly BitmapColor Red    = new BitmapColor(0xEF, 0x44, 0x44);
        public static readonly BitmapColor Orange = new BitmapColor(0xF5, 0x9E, 0x0B);
        public static readonly BitmapColor Blue   = new BitmapColor(0x60, 0xA5, 0xFA);
        public static readonly BitmapColor Purple = new BitmapColor(0xA7, 0x8B, 0xFA);
        public static readonly BitmapColor Slate  = new BitmapColor(0x94, 0xA3, 0xB8);
        public static readonly BitmapColor Dark   = new BitmapColor(0x0D, 0x11, 0x17);

        // Pure black: matches the profile's stored icon tiles, the Options+ editor background,
        // and the hardware bezel as closely as a backlit LCD allows. The previous #0D1117
        // (GitHub-dark) read as a mismatched box next to those pure-black surfaces.
        private static readonly BitmapColor Background = new BitmapColor(0x00, 0x00, 0x00);
        private static readonly BitmapColor White = new BitmapColor(0xFF, 0xFF, 0xFF);

        /// <summary>Corner-bracket colour marking the session key the typing keys are aimed at.</summary>
        private static readonly BitmapColor Selection = new BitmapColor(0x60, 0xA5, 0xFA);

        // Approval badge: amber for a routine request, red when the command is destructive.
        private static readonly BitmapColor BadgeWaiting = new BitmapColor(0xF5, 0xB9, 0x42);
        private static readonly BitmapColor BadgeRisk = new BitmapColor(0xFB, 0x71, 0x85);

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
                    var color = pct >= 90 ? Red : pct >= 75 ? Orange : White;
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
