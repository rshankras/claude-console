using System.Reflection;
using System.Text.Json;
using Loupedeck;

// Render the built DLL's actual drawing functions with synthetic data. Do not instantiate the
// plugin, declare DesktopServices, start monitoring, or read any app/clipboard/microphone state.
if (args.Length != 2) throw new ArgumentException("Usage: preview <built-plugin.dll> <output-directory>");
var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
var resources = assembly.GetType("Loupedeck.ClaudeConsolePlugin.PluginResources")!;
resources.GetMethod("Init", flags)!.Invoke(null, new object[] { assembly });
var key = assembly.GetType("Loupedeck.ClaudeConsolePlugin.KeyImage")!;
var conversationRenderer = assembly.GetType("Loupedeck.ClaudeConsolePlugin.Desktop.DesktopConversationRenderer");
key.GetMethod("UseIdentityIconFolder", flags)!.Invoke(null, new object[] { "desktop_icons" });
var pages = new Dictionary<string, List<string>>();

void Save(string page, string id, string method, params object[] parameters)
{
    var draw = method == "RenderConversationSlot" && conversationRenderer != null
        ? conversationRenderer.GetMethod("Render", flags)! : key.GetMethod(method, flags)!;
    using var image = (BitmapImage)draw.Invoke(null, parameters)!;
    var name = id + ".png";
    image.SaveToFile(Path.Combine(output, name));
    if (!pages.ContainsKey(page)) pages[page] = new();
    pages[page].Add(name);
}
void Control(string page, string id, string label, string icon, bool enabled = true, string status = null) =>
    Save(page, id, "RenderControlTile", PluginImageSize.Width90, label, icon, enabled, status);
void Intent(string page, string id, string label, string icon, string intent) =>
    Save(page, id, "RenderIntentTile", PluginImageSize.Width90, label, icon, intent);

foreach (var mode in new[] { "ChatGPT", "Codex" })
{
    var home = mode + " Home";
    for (var i = 0; i < 3; i++)
        Save(home, mode + "-home-" + i, "RenderConversationSlot", PluginImageSize.Width90,
            new[] { "Project update", "Research notes", "Next work plan" }[i], "Ready", new BitmapColor(0x5A,0x5A,0x60), false);
    Save(home, mode + "-all", "Render", PluginImageSize.Width90, "All Chats", new BitmapColor(0x60,0xA5,0xFA), "all_chats");
    Control(home, mode + "-new", mode == "Codex" ? "New Task" : "New Chat", "new_chat");
    Intent(home, mode + "-screenshot", "Screenshot", "screenshot", "ADD TO CHAT");
    Intent(home, mode + "-voice", "Dictate", "voice_draft", "DRAFT");
    Control(home, mode + "-send", "Send", "send", true, "REVIEW FIRST");
    Control(home, mode + "-native-home", "Voice Chat", "voice_chat", true, "TOGGLE");
    var page = mode + " Tools";
    Control(page, mode + "-mode", mode, "switch_mode", true, mode == "ChatGPT" ? "TO CODEX" : "TO CHATGPT");
    if (mode == "ChatGPT") {
        Save(page, mode + "-unused-approve", "Render", PluginImageSize.Width90, "", new BitmapColor(0x60,0xA5,0xFA), null);
        Save(page, mode + "-unused", "Render", PluginImageSize.Width90, "", new BitmapColor(0x60,0xA5,0xFA), null);
    } else {
        var risk = assembly.GetType("Loupedeck.ClaudeConsolePlugin.ApprovalRisk")!;
        Save(page, "codex-approve", "RenderApprovalTile", PluginImageSize.Width90, "Approve", "yes", "REVIEW REQUEST", Enum.Parse(risk,"Normal"));
        Save(page, "codex-deny", "RenderApprovalTile", PluginImageSize.Width90, "Deny", "no", "REVIEW REQUEST", Enum.Parse(risk,"Normal"));
    }
    Control(page, mode + "-attach", "Attach Files", "attach");
    Intent(page, mode + "-clipboard", "Paste into Chat", "copy", "CLIPBOARD");
    if (mode == "ChatGPT") Control(page, mode + "-navigation", "Find Chat", "search");
    else Save(page, mode + "-navigation", "Render", PluginImageSize.Width90, "", new BitmapColor(0x60,0xA5,0xFA), null);
    Control(page, mode + "-copy", "Copy Reply", "copy", true, "LATEST ANSWER");
    Save(page, mode + "-saved", "Render", PluginImageSize.Width90, mode == "ChatGPT" ? "Prompts" : "Tasks", new BitmapColor(0x60,0xA5,0xFA), "writing");
    Save(page, mode + "-more", "Render", PluginImageSize.Width90, "More", new BitmapColor(0x60,0xA5,0xFA), "more");
}
// Render the task menu from the built action order and real workflow definitions.
var workflowType = assembly.GetType("Loupedeck.ClaudeConsolePlugin.DesktopActions.DesktopWorkflowCommand")!;
var taskFolderType = assembly.GetType("Loupedeck.ClaudeConsolePlugin.DesktopActions.DesktopSavedPromptsDynamicFolder")!;
var defaults = (Array)workflowType.GetField("CodexDefaults", flags)!.GetValue(null)!;
var taskActions = (string[])taskFolderType.GetMethod("Actions", flags)!.Invoke(null, new object[] { "VizhiDesktop", "Codex", defaults })!;
foreach (var action in taskActions)
{
    var parameter = ActionString.FromString(action).ActionParameter;
    if (parameter == "show_diff") { Control("Codex Tasks", "tasks-view", "View Changes", "diff"); continue; }
    var definition = defaults.GetValue(int.Parse(parameter[5..]) - 1)!;
    var face = ((string Label, string Icon, string Footer))workflowType.GetMethod("FaceFor", flags)!.Invoke(null, new object[] { definition, "Codex", null })!;
    Intent("Codex Tasks", "tasks-" + parameter, face.Label, face.Icon, face.Footer);
}

Intent("Spoken workflow", "workflow-start", "Draft Reply", "writing", "SPEAK");
Intent("Spoken workflow", "workflow-listen", "Listening", "voice", "TAP TO FINISH");
Intent("Spoken workflow", "workflow-wait", "Preparing", "voice_draft", "WAIT");
Intent("Spoken workflow", "workflow-review", "Send Draft", "send", "REVIEW FIRST");
Intent("Spoken workflow", "workflow-sent", "Sent", "writing", "DONE");

var conversations = new[] { "Q3 report", "Release notes", "Plan next sprint", "Research notes", "Design review", "Fix build error", "Review proposal", "Weekly recap" };
for (var i = 0; i < conversations.Length; i++)
    Save("All Chats entries", "chat-" + i, "RenderConversationSlot", PluginImageSize.Width90, conversations[i], "Ready", new BitmapColor(0x5A, 0x5A, 0x60), false);

// Include real visible titles from the owner's photo and a synthetic completion of the first
// truncated title. These catch the extra SDK wrap that shorter sample names did not expose.
var longTitles = new[] { "Organize recent ChatGPT discussions", "Make plate with duration", "Find planned next work" };
var shortLabels = new[] { "Organize chats", "Plate duration", "Next work plan" };
for (var i = 0; i < longTitles.Length; i++)
{
    Save("Long title regression", "long-title-" + i, "RenderConversationSlot", PluginImageSize.Width90, longTitles[i], "Ready", new BitmapColor(0x5A, 0x5A, 0x60), false);
    Save("Short display labels", "short-label-" + i, "RenderConversationSlot", PluginImageSize.Width90, shortLabels[i], "Ready", new BitmapColor(0x5A, 0x5A, 0x60), false);
}
Save("Conversation states", "state-thinking", "RenderConversationSlot", PluginImageSize.Width90, "Review proposal", "Thinking", new BitmapColor(0x5A, 0x5A, 0x60), false);
Save("Conversation states", "state-approval", "RenderConversationSlot", PluginImageSize.Width90, "Review proposal", "Allow?", new BitmapColor(0xE2, 0x9D, 0x37), true);
Save("Conversation states", "state-complete", "RenderConversationSlot", PluginImageSize.Width90, "Review proposal", "Complete", new BitmapColor(0x4F, 0xA9, 0x75), false);

Intent("Voice Draft recovery", "draft-retry", "Insert Draft", "voice_draft", "HOLD TO DISCARD");
Intent("Voice Draft recovery", "draft-occupied", "Insert Draft", "voice_draft", "HOLD TO DISCARD");
Intent("Voice Draft recovery", "draft-discarded", "Discarded", "voice_draft", "VOICE DRAFT");
Intent("Voice Draft recovery", "draft-ready", "Draft Ready", "voice_draft", "SEND DRAFT");
Control("Updated symbols", "new-chat", "New Chat", "new_chat");
Control("Updated symbols", "search", "Search", "search");
Intent("Find Chat", "find-speak", "Speak Query", "voice", "SEARCH");
Intent("Speak Query setup", "voice-checking", "Preparing Voice", "voice", "Checking");
Intent("Speak Query setup", "voice-downloading", "Preparing Voice", "voice", "Download 100%");
Intent("Speak Query setup", "voice-verifying", "Preparing Voice", "voice", "Verifying");
Intent("Speak Query setup", "voice-retry", "Voice Setup", "voice", "Tap to retry");
Control("Find Chat", "find-type", "Type Now", "writing", true, "Tap to Focus");
Save("Find Chat", "find-query", "RenderConversationSlot", PluginImageSize.Width90, "plate duration", "2 found", new BitmapColor(0x5A, 0x5A, 0x60), false);
Save("Find Chat", "find-result-1", "RenderConversationSlot", PluginImageSize.Width90, "Plate duration", "Open chat", new BitmapColor(0x5A, 0x5A, 0x60), false);
Save("Find Chat", "find-result-2", "RenderConversationSlot", PluginImageSize.Width90, "Plate design", "Open chat", new BitmapColor(0x5A, 0x5A, 0x60), false);
Control("Find Chat", "find-unavailable", "Speak Query", "voice", false, "Use App");
Control("Find Chat recovery", "find-not-ready", "Speak Query", "voice", false, "Not ready");
Control("Find Chat recovery", "find-retry", "Open Search", "search", true, "Retry");
Save("Find Chat recovery", "find-no-field", "RenderConversationSlot", PluginImageSize.Width90, "Search not ready", "Open in app", new BitmapColor(0x5A, 0x5A, 0x60), false);
Save("Find Chat recovery", "find-no-layout", "RenderConversationSlot", PluginImageSize.Width90, "Search layout", "Unsupported", new BitmapColor(0x5A, 0x5A, 0x60), false);
Save("Find Chat recovery", "find-no-mode", "RenderConversationSlot", PluginImageSize.Width90, "Mode unreadable", "Retry", new BitmapColor(0x5A, 0x5A, 0x60), false);
Intent("Updated symbols", "rewrite", "Rewrite", "rewrite", "SPEAK");
Intent("Updated symbols", "plan", "Plan", "plan", "SEND");
Intent("Updated symbols", "continue", "Continue", "continue", "SEND");
Control("Updated symbols", "send-ready", "Send Draft", "send");
Control("Send and Stop", "send-prompt-ready", "Send", "send", true, "REVIEW FIRST");
Control("Send and Stop", "send-prompt-empty", "Send", "send", false, "No draft");
Control("Send and Stop", "send-prompt-stop", "Stop", "stop", true, "RESPONSE");
Control("Copy Reply", "copy-ready", "Copy Reply", "copy", true, "LATEST ANSWER");
Control("Copy Reply", "copy-wait", "Copy Reply", "copy", false, "Wait");
Control("Copy Reply", "copy-empty", "Copy Reply", "copy", false, "No answer");
Control("Copy Reply", "copy-done", "Copied", "copy", true, "READY TO PASTE");
Control("Copy Reply", "copy-unrecognized", "Copy Reply", "copy", false, "Check Chat");
Control("Copy Reply", "copy-use-app", "Copy Reply", "copy", false, "Use App");
Control("Voice Chat states", "voice-chat-ready", "Voice Chat", "voice_chat", true, "TALK");
Control("Voice Chat states", "voice-chat-active", "End Voice", "voice_chat", true, "ACTIVE");
Control("Voice Chat states", "voice-chat-unavailable", "No Voice", "voice_chat", true, "CHECK APP");
// This is the inset image only; the SDK adds the folder opener's caption and draws Back itself.
Save("SDK folder icon (caption excluded)", "all-chats", "Render", PluginImageSize.Width90, "All Chats", new BitmapColor(0x60, 0xA5, 0xFA), "all_chats");
foreach (var item in new[] {
    ("selection", "Use Selection", "document", "ADD CONTEXT"),
    ("clipboard", "Use Clipboard", "copy", "1 SOURCE"),
    ("screenshot", "Screenshot", "screenshot", "ADD CONTEXT"),
    ("reply", "Draft Reply", "writing", "SPEAK"),
    ("voice", "Voice Draft", "voice_draft", "DRAFT"),
    ("send", "Send Draft", "send", "REVIEW FIRST"),
    ("copy", "Copy Reply", "copy", "LATEST ANSWER"),
    ("return", "Return to App", "quick_chat", "Mail"),
    ("paste", "Paste Reply", "writing", "EMPTY REPLY BOX") })
    Intent("Context", "context-" + item.Item1, item.Item2, item.Item3, item.Item4);
Intent("Context feedback", "context-added", "Text Added", "document", "1 SOURCE");
Intent("Context feedback", "context-area", "Select Area", "screenshot", "ESC TO CANCEL");
Intent("Context feedback", "context-check-image", "Check Image", "screenshot", "TAP TO RETRY");
Control("View Changes availability", "changes-available", "View Changes", "diff", true);
Control("View Changes availability", "changes-unavailable", "View Changes", "diff", false, "Not available");
Control("View Changes availability", "changes-failed", "View Changes", "diff", true, "Couldn't open");

File.WriteAllText(Path.Combine(output, "pages.json"), JsonSerializer.Serialize(pages, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Rendered {pages.Values.Sum(p => p.Count)} real plugin faces from {assembly.GetName().Name} {assembly.GetName().Version} to {output}");
