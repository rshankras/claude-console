namespace Loupedeck.ClaudeConsolePlugin.VizhiDesktop.Registration
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    // 0.17.18: Home takes the family layout (Deny/Approve where Claude Console and Vizhi for Codex
    // put No/Yes), All Chats joins Find Chat on Tools, Voice Chat moves to Tools, and no Tools key
    // is blank in either mode. Supersedes DesktopTasksMenuMigration, which turned Find Chat / View
    // Changes into the ChatGPT-only search key; running both would flip that key on every load.
    //
    // Nearly every key moves, so this works on whole pages: Home and Tools are rewritten together,
    // and only when BOTH still match a known stock layout. A customized key on either page leaves
    // both alone — rewriting one page could drop the only route to Chats or Voice Chat.
    internal static class DesktopHomeToolsLayoutMigration
    {
        internal const String Profile = "A8B982E4103C4F99A4C75070AF60A6E4";
        private const String Ns = "$VizhiDesktop___Loupedeck.ClaudeConsolePlugin.DesktopActions.";
        private static String Act(String type, String parameter = null) => Ns + type + (parameter == null ? "" : "___" + parameter);
        private static String Folder(String type) => "$VizhiDesktop___#DynamicFolder___DynamicFolder#Loupedeck.ClaudeConsolePlugin.DesktopActions." + type;

        private static readonly String[] Conversations =
            { Act("DesktopConversationCommand", "1"), Act("DesktopConversationCommand", "2"), Act("DesktopConversationCommand", "3") };

        // 0.17.14 – 0.17.17 (after DesktopHomeScreenshotMigration has run).
        internal static readonly String[] OldHome = Conversations.Concat(new[]
        {
            Folder("AllChatsDynamicFolder"), Act("DesktopControlCommand", "new_chat"), Act("DesktopCaptureCommand", "screenshot"),
            Act("DesktopVoiceDraftCommand"), Act("DesktopComposerCommand", "send_stop"), Act("DesktopVoiceChatCommand"),
        }).ToArray();

        private static String[] OldTools(String approve, String deny, String navigation) => new[]
        {
            Act("DesktopControlCommand", "mode"), Act("DesktopToolsCommand", approve), Act("DesktopToolsCommand", deny),
            Folder("DesktopFilesDynamicFolder"), Act("DesktopCaptureCommand", "clipboard"), Act("DesktopNavigateCommand", navigation),
            Act("DesktopCaptureCommand", "copy"), Folder("DesktopSavedPromptsDynamicFolder"), Folder("DesktopMoreDynamicFolder"),
        };

        internal static readonly String[][] StockTools =
        {
            OldTools("approve", "deny", "find"),                // 0.17.14 – 0.17.15
            OldTools("approve", "deny", "search"),              // 0.17.16 – 0.17.17
            OldTools("clear_approve", "return_deny", "find"),   // an unreleased 0.17.18 build
        };

        internal static readonly String[] NewHome = Conversations.Concat(new[]
        {
            Act("DesktopControlCommand", "new_chat"), Act("DesktopApprovalCommand", "deny"), Act("DesktopApprovalCommand", "approve"),
            Act("DesktopCaptureCommand", "screenshot"), Act("DesktopVoiceDraftCommand"), Act("DesktopComposerCommand", "send_stop"),
        }).ToArray();

        internal static readonly String[] NewTools =
        {
            Act("DesktopControlCommand", "mode"), Folder("AllChatsDynamicFolder"), Act("DesktopNavigateCommand", "find"),
            Folder("DesktopFilesDynamicFolder"), Act("DesktopCaptureCommand", "clipboard"), Act("DesktopCaptureCommand", "copy"),
            Act("DesktopVoiceChatCommand"), Folder("DesktopSavedPromptsDynamicFolder"), Folder("DesktopMoreDynamicFolder"),
        };

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
                JsonObject[] Keys(String pageName)
                {
                    var page = pages?.OfType<JsonObject>().SingleOrDefault(p => (String)p["displayName"] == pageName);
                    var controls = (page?["controls"] as JsonArray)?.OfType<JsonObject>().ToArray();
                    if (controls == null || controls.Length != 9) return null;
                    var ordered = Enumerable.Range(0, 9).Select(id => controls.SingleOrDefault(c => (Int32?)c["controlId"] == id)).ToArray();
                    return ordered.Any(c => c == null) ? null : ordered;
                }
                var home = Keys("Home");
                var tools = Keys("Tools");
                if (home == null || tools == null) return false;
                var homeBindings = home.Select(c => (String)c["pressAction"]).ToArray();
                var toolsBindings = tools.Select(c => (String)c["pressAction"]).ToArray();
                if (!homeBindings.SequenceEqual(OldHome) || !StockTools.Any(stock => toolsBindings.SequenceEqual(stock))) return false;

                for (var i = 0; i < 9; i++)
                {
                    home[i]["pressAction"] = NewHome[i];
                    tools[i]["pressAction"] = NewTools[i];
                }
                var backup = file + ".before-0.17.18";
                if (!File.Exists(backup)) File.Copy(file, backup);
                temp = file + ".layout-" + Guid.NewGuid().ToString("N");
                File.WriteAllText(temp, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                if (File.ReadAllText(file) != original) return false;
                File.Move(temp, file, true);
                return true;
            }
            catch { PluginLog.Warning("Desktop Home/Tools layout migration skipped"); return false; }
            finally { if (temp != null && File.Exists(temp)) File.Delete(temp); }
        }
    }
}
