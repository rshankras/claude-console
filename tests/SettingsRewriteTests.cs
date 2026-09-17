namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Text.Json.Nodes;

    using Xunit;

    /// <summary>
    /// settings.json is rewritten through one door, BridgeManager.RewriteSettings, and these are the
    /// door's rules: a mutate that changes nothing costs no write and no backup; a write lands only
    /// over the exact bytes that were read (Claude Code writes this file too), retrying once from the
    /// fresh contents; a file that will not hold still is left alone; the temp file is unique per
    /// write and never left behind; a symlink is refused before anything is read.
    /// </summary>
    public class SettingsRewriteTests
    {
        [Fact]
        public void A_rewrite_that_changes_nothing_writes_nothing_and_takes_no_backup()
        {
            using var home = new TempHome();
            home.WriteSettings("{\n  \"model\": \"opus\"\n}");
            var before = File.GetLastWriteTimeUtc(home.Settings);

            var ok = BridgeManager.RewriteSettings(_ => false, out var changed);

            Assert.True(ok);
            Assert.False(changed);
            Assert.Equal("{\n  \"model\": \"opus\"\n}", home.ReadSettings());
            Assert.Equal(before, File.GetLastWriteTimeUtc(home.Settings));
            Assert.False(File.Exists(home.Backup), "a no-op must not take a backup — the backup is the state one CHANGE ago");
        }

        [Fact]
        public void A_write_lands_takes_a_rolling_backup_and_leaves_no_temp_behind()
        {
            using var home = new TempHome();
            home.WriteSettings("{\"model\":\"opus\"}");

            var ok = BridgeManager.RewriteSettings(root => { root["x"] = "y"; return true; }, out var changed);

            Assert.True(ok);
            Assert.True(changed);
            var after = JsonNode.Parse(home.ReadSettings()).AsObject();
            Assert.Equal("opus", after["model"].GetValue<String>());
            Assert.Equal("y", after["x"].GetValue<String>());
            Assert.Equal("{\"model\":\"opus\"}", File.ReadAllText(home.Backup));
            Assert.Empty(home.LeftoverTemps());
        }

        [Fact]
        public void A_missing_file_is_created_from_an_empty_document()
        {
            using var home = new TempHome();

            var ok = BridgeManager.RewriteSettings(root => { root["x"] = "y"; return true; }, out var changed);

            Assert.True(ok);
            Assert.True(changed);
            Assert.Equal("y", JsonNode.Parse(home.ReadSettings())["x"].GetValue<String>());
            Assert.False(File.Exists(home.Backup), "there was nothing to back up");
        }

        [Fact]
        public void A_file_edited_between_read_and_write_is_re_read_and_retried_once()
        {
            using var home = new TempHome();
            home.WriteSettings("{\"model\":\"opus\"}");
            var calls = 0;

            var ok = BridgeManager.RewriteSettings(root =>
            {
                calls++;
                if (calls == 1)
                {
                    // Someone else (Claude Code, an editor) saves the file while we hold our copy.
                    home.WriteSettings("{\"model\":\"opus\",\"theirs\":true}");
                }
                root["ours"] = true;
                return true;
            }, out var changed);

            Assert.True(ok);
            Assert.True(changed);
            Assert.Equal(2, calls);
            var after = JsonNode.Parse(home.ReadSettings()).AsObject();
            Assert.True(after["theirs"].GetValue<Boolean>(), "the external edit must survive — we re-read before writing");
            Assert.True(after["ours"].GetValue<Boolean>());
            Assert.Empty(home.LeftoverTemps());
        }

        [Fact]
        public void A_file_that_keeps_changing_is_left_alone_after_the_retry()
        {
            using var home = new TempHome();
            home.WriteSettings("{\"n\":0}");
            var calls = 0;

            var ok = BridgeManager.RewriteSettings(root =>
            {
                calls++;
                home.WriteSettings("{\"n\":" + calls + "}");   // moves under us every time
                root["ours"] = true;
                return true;
            }, out var changed);

            Assert.False(ok);
            Assert.False(changed);
            Assert.Equal(2, calls);
            Assert.Equal("{\"n\":2}", home.ReadSettings());   // the last external write, untouched by us
            Assert.Empty(home.LeftoverTemps());
        }

        [Fact]
        public void A_symlinked_settings_file_is_refused_before_it_is_read()
        {
            if (OperatingSystem.IsWindows())
            {
                return; // symlinks need a privilege there; the guard itself is platform-neutral
            }

            using var home = new TempHome();
            Directory.CreateDirectory(home.ClaudeDir);
            var real = Path.Combine(home.Dir, "elsewhere.json");
            File.WriteAllText(real, "{\"model\":\"opus\"}");
            File.CreateSymbolicLink(home.Settings, real);
            var calls = 0;

            var ok = BridgeManager.RewriteSettings(_ => { calls++; return true; }, out var changed);

            Assert.False(ok);
            Assert.False(changed);
            Assert.Equal(0, calls);
            Assert.Equal("{\"model\":\"opus\"}", File.ReadAllText(real));
        }

        [Fact]
        public void Invalid_json_is_refused_and_left_untouched()
        {
            using var home = new TempHome();
            home.WriteSettings("{ not json");

            var ok = BridgeManager.RewriteSettings(_ => true, out var changed);

            Assert.False(ok);
            Assert.False(changed);
            Assert.Equal("{ not json", home.ReadSettings());
            Assert.False(File.Exists(home.Backup));
        }
    }
}
