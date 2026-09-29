namespace Loupedeck.ClaudeConsolePlugin.VizhiDesktop.Registration
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    internal static class DesktopHomeScreenshotMigration
    {
        internal const String Profile = "A8B982E4103C4F99A4C75070AF60A6E4";
        internal const String Screenshot = "$VizhiDesktop___Loupedeck.ClaudeConsolePlugin.DesktopActions.DesktopCaptureCommand___screenshot";
        internal const String Navigation = DesktopHomeNavigationMigration.NewBinding;

        // Swap the two stock positions together. If either was customized, preserve both;
        // otherwise moving only one could remove the user's route to capture or navigation.
        internal static Boolean Upgrade(String applicationDirectory)
        {
            var file = Path.Combine(applicationDirectory, "Profiles", Profile, "ProfileInfo.json");
            if (!File.Exists(file)) return false;
            String temp = null;
            try
            {
                var original = File.ReadAllText(file);
                var doc = JsonNode.Parse(original);
                if ((String)doc?["name"] != Profile || (String)doc?["nativePluginName"] != "VizhiDesktop") return false;
                var modes = doc?["layout"]?["layoutModes"] as JsonArray;
                var main = modes?.OfType<JsonObject>().SingleOrDefault(m => (String)m["modeName"] == "main");
                var pages = main?["workspaces"]?[0]?["pressPages"] as JsonArray;
                JsonNode Key(String pageName)
                {
                    var page = pages?.OfType<JsonObject>().SingleOrDefault(p => (String)p["displayName"] == pageName);
                    return (page?["controls"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(c => (Int32?)c["controlId"] == 5);
                }
                var home = Key("Home");
                var tools = Key("Tools");
                var homeBinding = (String)home?["pressAction"];
                if (homeBinding != Navigation && homeBinding != DesktopHomeNavigationMigration.OldBinding) return false;
                if ((String)tools?["pressAction"] != Screenshot) return false;
                home["pressAction"] = Screenshot;
                tools["pressAction"] = Navigation;
                var backup = file + ".before-0.17.14";
                if (!File.Exists(backup)) File.Copy(file, backup);
                temp = file + ".screenshot-" + Guid.NewGuid().ToString("N");
                File.WriteAllText(temp, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                if (File.ReadAllText(file) != original) return false;
                File.Move(temp, file, true);
                return true;
            }
            catch { PluginLog.Warning("Desktop Home screenshot migration skipped"); return false; }
            finally { if (temp != null && File.Exists(temp)) File.Delete(temp); }
        }
    }
}
