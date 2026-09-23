namespace Loupedeck.ClaudeConsolePlugin.VizhiDesktop.Registration
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    internal static class DesktopHomeNavigationMigration
    {
        internal const String OldBinding = "$VizhiDesktop___#DynamicFolder___DynamicFolder#Loupedeck.ClaudeConsolePlugin.DesktopActions.FindChatDynamicFolder";
        internal const String NewBinding = "$VizhiDesktop___Loupedeck.ClaudeConsolePlugin.DesktopActions.DesktopNavigateCommand___find";
        internal static readonly String[] Profiles = { "A8B982E4103C4F99A4C75070AF60A6E4", "390FE86F17D84EC6B4920C7A5C3F37FA" };

        // Only the stock Home position in known Vizhi-owned profiles. Custom keys, page
        // order, names, icons, selected profile and every other setting stay untouched.
        internal static Boolean Upgrade(String applicationDirectory)
        {
            var changed = false;
            foreach (var id in Profiles)
            {
                var file = Path.Combine(applicationDirectory, "Profiles", id, "ProfileInfo.json");
                if (!File.Exists(file)) continue;
                String temp = null;
                try
                {
                    var original = File.ReadAllText(file);
                    var doc = JsonNode.Parse(original);
                    if ((String)doc?["name"] != id || (String)doc?["nativePluginName"] != "VizhiDesktop") continue;
                    var modes = doc?["layout"]?["layoutModes"] as JsonArray;
                    var main = modes?.OfType<JsonObject>().SingleOrDefault(m => (String)m["modeName"] == "main");
                    var page = main?["workspaces"]?[0]?["pressPages"]?[0];
                    var controls = page?["controls"] as JsonArray;
                    var key = controls?.OfType<JsonObject>().SingleOrDefault(c => (Int32?)c["controlId"] == 5);
                    if ((String)key?["pressAction"] != OldBinding) continue;
                    key["pressAction"] = NewBinding;
                    var backup = file + ".before-0.17.10";
                    if (!File.Exists(backup)) File.Copy(file, backup);
                    temp = file + ".navigation-" + Guid.NewGuid().ToString("N");
                    File.WriteAllText(temp, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    if (File.ReadAllText(file) != original) continue;
                    File.Move(temp, file, true);
                    changed = true;
                }
                catch { PluginLog.Warning("Desktop Home navigation migration skipped"); }
                finally { if (temp != null && File.Exists(temp)) File.Delete(temp); }
            }
            return changed;
        }
    }
}
