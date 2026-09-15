namespace Loupedeck.ClaudeConsolePlugin.Tests;

using System;
using System.Text.Json.Nodes;
using Xunit;

public class ReviewRegressionTests
{
    [Fact]
    public void An_unchanged_inline_document_is_byte_identical()
    {
        using var home = new TempHome();
        const string input = "{\n  // foreign comment\n  \"permissions\": { \"allow\": [\"Read\", \"Glob\"] },\n\n  \"model\": \"\\u006fpus\",\n}\n";
        home.WriteSettings(input);
        Assert.True(BridgeManager.RewriteSettings(_ => true, out _));
        Assert.Equal(input, home.ReadSettings());
    }

    [Theory]
    [InlineData("\n", "  ", true)]
    [InlineData("\r\n", "    ", false)]
    [InlineData("\n", "\t", true)]
    public void Removing_owned_hooks_keeps_nested_foreign_source(string newline, string indent, bool trailing)
    {
        using var home = new TempHome();
        var foreign = "{ \"type\": \"command\", \"command\": \"echo café & \\\"hello\\\"\" }";
        var input = "{" + newline + indent + "\"permissions\": { \"allow\": [\"Read\", \"Glob\"] }," + newline +
            indent + "\"hooks\": { \"Stop\": [{ \"hooks\": [{\"command\":\"ours\"}, " + foreign + "] }] }" + newline + "}" + (trailing ? newline : "");
        home.WriteSettings(input);
        Assert.True(BridgeManager.RewriteSettings(root =>
        {
            ((JsonArray)root["hooks"]["Stop"][0]["hooks"]).RemoveAt(0);
            root["statusLine"] = new JsonObject { ["command"] = "ours" };
            return true;
        }, out _));
        var result = home.ReadSettings();
        Assert.Contains(indent + "\"permissions\": { \"allow\": [\"Read\", \"Glob\"] },", result);
        Assert.Contains(foreign, result);
        Assert.Equal(trailing, result.EndsWith(newline));
        Assert.Single((JsonArray)JsonNode.Parse(result)["hooks"]["Stop"][0]["hooks"]);
    }

    [Fact]
    public void Removing_a_group_and_editing_the_next_keeps_that_groups_foreign_source()
    {
        using var home = new TempHome();
        const string foreign = "{ \"command\": \"echo foreign\", \"timeout\": 17 }";
        home.WriteSettings("{\"hooks\":{\"Stop\":[{\"hooks\":[{\"command\":\"owned\"}]},{ \"hooks\": [{\"command\":\"owned\"}, " + foreign + "] }]}}");
        Assert.True(BridgeManager.RewriteSettings(root =>
        {
            var groups = (JsonArray)root["hooks"]["Stop"];
            groups.RemoveAt(0);
            ((JsonArray)groups[0]["hooks"]).RemoveAt(0);
            return true;
        }, out _));
        Assert.Contains(foreign, home.ReadSettings());
        Assert.DoesNotContain("owned", home.ReadSettings());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\n}")]
    [InlineData("{\"a\":1,\"b\":[1,2,3]}")]
    [InlineData("{\n // leading comment\n \"a\":1, /* comma , in comment */ \"b\":[1,2,3],\n}")]
    public void Structural_edits_produce_the_requested_values(string input)
    {
        var options = new System.Text.Json.JsonDocumentOptions
        { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var value = JsonNode.Parse(input, documentOptions: options).AsObject();
        value.Remove("a");
        value["b"] = new JsonArray(2, 3, 4);
        value["z"] = new JsonObject { ["nested"] = true };
        var output = SettingsText.Rewrite(input, value, SettingsLayout.Default);
        Assert.True(JsonNode.DeepEquals(value, JsonNode.Parse(output, documentOptions: options)));
        var empty = SettingsText.Rewrite(input, new JsonObject(), SettingsLayout.Default);
        Assert.Empty(JsonNode.Parse(empty, documentOptions: options).AsObject());
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(1, true, true)]
    [InlineData(2, false, false)]
    [InlineData(2, true, true)]
    public void Focus_requires_console_identity_even_for_unique_labels(int count, bool identified, bool expected)
    {
        Assert.Equal(expected, ClaudeConsoleFocus.TabSelection.TrySelect(count, () => identified));
    }

    [Fact]
    public void Cold_start_shows_starting_until_microphone_ready()
    {
        var state = new VoiceCaptureState();
        var now = DateTime.UtcNow;
        Assert.Equal(VoiceAction.Start, state.Press(VoiceIntent.Draft, now, awaitReadiness: true).Action);
        Assert.Equal("Starting", state.StartupLabel(VoiceIntent.Draft));
        Assert.False(state.IsRecording(VoiceIntent.Draft));
        Assert.Null(state.StartupLabel(VoiceIntent.Send));
        Assert.True(state.MarkReady(now.AddSeconds(6)));
        Assert.True(state.IsRecording(VoiceIntent.Draft));
        Assert.Null(state.StartupLabel(VoiceIntent.Draft));
        Assert.Equal(VoiceAction.Stop, state.Press(VoiceIntent.Send, now.AddSeconds(7)).Action);
        Assert.Equal(VoiceIntent.Draft, state.Intent);
    }

    [Fact]
    public void Cancel_during_start_cannot_restart_or_be_revived_by_a_late_acknowledgement()
    {
        var state = new VoiceCaptureState();
        var now = DateTime.UtcNow;
        state.Press(VoiceIntent.Send, now, awaitReadiness: true);
        Assert.Equal(VoiceAction.Cancel, state.Press(VoiceIntent.Project, now.AddSeconds(1)).Action);
        Assert.Equal("Cancelling", state.StartupLabel(VoiceIntent.Send));
        Assert.False(state.MarkReady(now.AddSeconds(6)));
        Assert.Equal(VoiceAction.Refuse, state.Press(VoiceIntent.Project, now.AddMinutes(2)).Action);
        state.Finish(); // Only the worker that has reaped the old helper can release ownership.
        Assert.Equal(VoiceAction.Start, state.Press(VoiceIntent.Project, now.AddMinutes(2), true).Action);
    }
}
