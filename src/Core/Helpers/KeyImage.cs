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
        private static String _identityIconFolder = "icons";

        /// <summary>
        /// Select the product's neutral icon set. Each product compiles Core into its own assembly,
        /// so this setting remains isolated even when Claude Console and Vizhi run side by side.
        /// State-semantic artwork (Yes/No, warnings, model tiers and listening) remains shared.
        /// </summary>
        internal static void UseIdentityIconFolder(String folder) =>
            _identityIconFolder = String.IsNullOrWhiteSpace(folder) ? "icons" : folder;

        private static String IconResource(String icon)
        {
            var stateSemantic = icon == "allow" || icon == "deny" ||
                icon == "brain_haiku" || icon == "brain_sonnet" || icon == "brain_opus" ||
                icon == "gauge_warn" || icon == "gauge_crit" ||
                icon == "wave0" || icon == "wave1" || icon == "wave2" || icon == "wave3";
            var folder = stateSemantic ? "icons" : _identityIconFolder;
            return folder + "." + icon + ".png";
        }

        // Palette. The state colours are the DESIGN's, sampled from the 2026-08-26 Logitech
        // frames rather than picked here — their set is deliberately muted against the Tailwind-ish
        // values this file used to carry, and mixing the two reads as two palettes on one pad.
        // Coral is Claude's identity colour, Blue is Codex's; identity and state must stay on
        // different cues, so a session's agent must never be signalled by Amber/Red/Green.
        // ("Always allow", #275DA3 in the same frames, gets a constant when a key actually binds it.)
        public static readonly BitmapColor Green  = new BitmapColor(0x4F, 0xA9, 0x75);   // Allow
        public static readonly BitmapColor Red    = new BitmapColor(0xDA, 0x3D, 0x29);   // Deny / risk
        public static readonly BitmapColor Amber  = new BitmapColor(0xE2, 0x9D, 0x37);   // approval badge
        // Hardware-calibrated from the mockup's #E5A943: the keypad OLED pushes that source colour
        // yellow, so reduce green/blue while preserving a brighter orange than Claude's coral.
        public static readonly BitmapColor SelectionOrange = new BitmapColor(0xE2, 0x8A, 0x32); // active session bar
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
        private static BitmapColor Selection = new BitmapColor(0x60, 0xA5, 0xFA);

        private static BitmapColor _sessionBar = SelectionOrange;

        /// <summary>
        /// The bar behind the routed session's state word. Identity, not state: the word itself
        /// still says what the session is doing, and an inactive session is grey on every product.
        /// </summary>
        internal static BitmapColor SessionBar => _sessionBar;

        /// <summary>
        /// Declare the product's two identity colours — the corner bracket and the routed-session
        /// bar. Defaults are Claude Console's, so a product that never calls this keeps the faces
        /// QA signed off. Called from the plugin CONSTRUCTOR beside UseIdentityIconFolder, before
        /// the SDK builds any action. Each product compiles Core into its own assembly, so the
        /// setting stays isolated when both plugins run side by side.
        ///
        /// The engine owns state colour (Amber/Red/Green and the grey quiet bar) and must never
        /// learn an agent's name to pick one; identity belongs to whoever is shipping the keypad.
        /// </summary>
        internal static void UseIdentityColors(BitmapColor selection, BitmapColor sessionBar)
        {
            Selection = selection;
            _sessionBar = sessionBar;
        }

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
                        var img = PluginResources.ReadImage(IconResource(icon));
                        var w = bitmap.Width;
                        var h = bitmap.Height;
                        // The three utility-row glyphs use denser/taller artwork than the mic and
                        // other open line icons. Keep their render box smaller so Clear, Esc and Tab
                        // read as one balanced group on the physical keypad.
                        var compactUtilityIcon = icon == "clear" || icon == "esc" || icon == "tab";
                        var iconShare = compactUtilityIcon ? 0.70 : 0.82;
                        var s = (Int32)(Math.Min(w, h) * iconShare);
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
        /// The session face is split in two: the TITLE region is the top 75% of the key, and the
        /// bottom 25% is the state bar.
        /// </summary>
        private const Single SessionTitleShare = 0.75f;

        /// <summary>
        /// A session-grid key face divided 75/25. The session's project (directory) name is centred
        /// within the black title region; the bottom region contains its state. Orange marks the
        /// active/routed session and grey marks inactive sessions — the state word is independent.
        ///
        /// The name lives in the bitmap rather than the service's own label strip because that
        /// strip is pinned to the bottom edge; centring needs the face. Long names wrap to two
        /// lines (DrawText clips overflow instead of wrapping). An empty slot is the bare black face.
        /// </summary>
        public static BitmapImage RenderSessionSlot(
            PluginImageSize imageSize, String name, String stateWord,
            BitmapColor barColor, Boolean darkText)
        {
            // Width90 normally gives a plugin an inset 80x80 image inside the physical 90x90 key;
            // Options+ reserves the remaining strip for its command label. Session keys draw their
            // own title and state, and suppress that label, so render against the full button canvas
            // instead. This is what lets the state bar reach all three physical edges with padding 0.
            var canvasWidth = imageSize.GetButtonWidth();
            var canvasHeight = imageSize.GetButtonHeight();
            using (var bitmap = canvasWidth > 0 && canvasHeight > 0
                ? new BitmapBuilder(canvasWidth, canvasHeight)
                : new BitmapBuilder(imageSize))
            {
                bitmap.Clear(Background);

                if (String.IsNullOrWhiteSpace(name) || String.IsNullOrWhiteSpace(stateWord))
                {
                    return bitmap.ToImage();   // empty slot: bare black face
                }

                var w = bitmap.Width;
                var h = bitmap.Height;
                var scale = Math.Min(w, h) / 96f;
                var pad = (Int32)(4 * scale);
                var titleH = (Int32)(h * SessionTitleShare);

                // Wrap into up to two lines — DrawText centres a single line and CLIPS the overflow
                // (a long "claude-console" lost both ends on the device) — then stack the block
                // vertically centred within the TITLE region (top 75%), not the whole face.
                var lines = WrapTwo(name, 11);
                var lineH = (Int32)(17 * scale);
                var top = Math.Max(0, (titleH - (lines.Length * lineH)) / 2);
                for (var i = 0; i < lines.Length; i++)
                {
                    bitmap.DrawText(lines[i], pad, top + (i * lineH), w - (2 * pad), lineH, White, fontSize: (Int32)(14 * scale));
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
        /// A full-key Yes/No face matching the supplied Logitech design: solid state colour,
        /// white circled decision glyph, and a white label. It remains a live widget, so approval
        /// badges can still update without Options+ creating a static icon override.
        ///
        /// The glyph is the designer-pipeline PNG (icons/allow.png, icons/deny.png — a ring and a
        /// mark sharing one 2.7-unit stroke, drawn in the pack's 43-unit language), not SDK
        /// primitives: BitmapBuilder.DrawCircle has no stroke width, so a hand-drawn ring could
        /// only ever be 1 px against a 4 px mark, and on the device the check broke out through
        /// the ring. The PNG's circle spans ~74% of its box; drawn at half the tile's width it
        /// lands at the frame's ~37%-of-tile circle, centred a little above the middle.
        /// </summary>
        public static BitmapImage RenderDecisionTile(
            PluginImageSize imageSize, String label, BitmapColor color,
            Boolean approve, ApprovalRisk risk, String targetLabel = null)
        {
            using (var bitmap = ButtonCanvas(imageSize))
            {
                bitmap.Clear(color);

                var w = bitmap.Width;
                var h = bitmap.Height;
                var scale = Math.Min(w, h) / 96f;

                var glyph = approve ? "allow" : "deny";
                var size = (Int32)(Math.Min(w, h) * 0.50);
                var cy = h * 0.42f;
                try
                {
                    var img = PluginResources.ReadImage(IconResource(glyph));
                    bitmap.DrawImage(img, (w - size) / 2, (Int32)(cy - (size / 2f)), size, size);
                }
                catch (Exception ex)
                {
                    PluginLog.Verbose(ex, $"KeyImage: decision glyph '{glyph}' failed to load — tile keeps colour + label");
                }

                var labelY = (Int32)(h * (targetLabel == null ? 0.66f : 0.62f));
                var labelHeight = targetLabel == null ? h - labelY : (Int32)(h * 0.20f);
                bitmap.DrawText(label, 0, labelY, w, labelHeight, White, fontSize: (Int32)(15 * scale));
                if (targetLabel != null)
                {
                    var caption = targetLabel.Length <= 16 ? targetLabel : targetLabel.Substring(0, 15) + "…";
                    bitmap.DrawText(caption, (Int32)(3 * scale), (Int32)(h * 0.82f),
                        w - (Int32)(6 * scale), (Int32)(h * 0.17f), White, fontSize: (Int32)(10 * scale));
                }
                DrawApprovalBadge(bitmap, risk);
                return bitmap.ToImage();
            }
        }

        /// <summary>A full-canvas black action face used by the other Answer-command widgets.</summary>
        public static BitmapImage RenderWidgetAction(PluginImageSize imageSize, String label, String icon)
        {
            using (var bitmap = ButtonCanvas(imageSize))
            {
                bitmap.Clear(Background);
                var w = bitmap.Width;
                var h = bitmap.Height;
                var scale = Math.Min(w, h) / 96f;

                try
                {
                    var img = PluginResources.ReadImage(IconResource(icon));
                    var size = (Int32)(Math.Min(w, h) * 0.58);
                    bitmap.DrawImage(img, (w - size) / 2, (Int32)(h * 0.08), size, size);
                }
                catch (Exception ex)
                {
                    PluginLog.Verbose(ex, $"KeyImage: widget icon '{icon}' failed to load");
                }

                var labelY = (Int32)(h * 0.68f);
                bitmap.DrawText(label, 0, labelY, w, h - labelY, White, fontSize: (Int32)(14 * scale));
                return bitmap.ToImage();
            }
        }

        private static BitmapBuilder ButtonCanvas(PluginImageSize imageSize)
        {
            var width = imageSize.GetButtonWidth();
            var height = imageSize.GetButtonHeight();
            return width > 0 && height > 0
                ? new BitmapBuilder(width, height)
                : new BitmapBuilder(imageSize);
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
