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

        // User icon overrides: ~/.claude/claude-console/icons/<name>.png is checked before the
        // embedded resources, so custom prompts (prompts.json) can carry their own icons — and an
        // embedded icon can be overridden — without rebuilding the plugin.
        private static readonly String UserIconsDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "claude-console", "icons");

        private static readonly BitmapColor Background = new BitmapColor(0x0D, 0x11, 0x17);
        private static readonly BitmapColor White = new BitmapColor(0xFF, 0xFF, 0xFF);

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
                        // User file first (see UserIconsDir), then embedded. Embedded lookups are
                        // qualified with the "icons." folder segment: the SDK's resource finder matches
                        // by name SUFFIX, so a bare "up.png" also matches "scroll_up.png" (and "tab.png"
                        // matches "new_tab.png"), and it returns the first alphabetically — the wrong one.
                        // "icons.up.png" pins the lookup to exactly one embedded resource.
                        var img = LoadUserIcon(icon) ?? PluginResources.ReadImage("icons." + icon + ".png");
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

        // Load an icon PNG from the user icons dir, or null if absent/unreadable. Only simple
        // basenames are looked up (no path separators), so prompts.json can't reach outside the dir.
        private static BitmapImage LoadUserIcon(String icon)
        {
            try
            {
                if (icon.IndexOfAny(new[] { '/', '\\' }) >= 0)
                {
                    return null;
                }
                var file = System.IO.Path.Combine(UserIconsDir, icon + ".png");
                if (!System.IO.File.Exists(file))
                {
                    return null;
                }
                return BitmapImage.TryCreateFromFile(file, out var img) ? img : null;
            }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, $"KeyImage: user icon '{icon}' failed to load");
                return null;
            }
        }
    }
}
