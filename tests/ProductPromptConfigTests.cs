namespace Loupedeck.ClaudeConsolePlugin.Tests;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Loupedeck.ClaudeConsolePlugin.Actions;
using Xunit;

public sealed class ProductPromptConfigTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "prompt-migration-" + Guid.NewGuid().ToString("N"));
    private string Source => ProductPromptConfig.PathFor(home, "claude-console");
    private string Destination => ProductPromptConfig.PathFor(home, "codex-console");
    private const string Custom = "[\n {\"id\":\"second\",\"label\":\"Custom\",\"prompt\":\"é draft\",\"icon\":\"review\",\"submit\":false}, {\"id\":\"first\",\"prompt\":\"hello\"}\n]";
    public ProductPromptConfigTests() => Directory.CreateDirectory(home);
    public void Dispose() => Directory.Delete(home, true);
    private void Seed(string text) { Directory.CreateDirectory(Path.GetDirectoryName(Source)); File.WriteAllText(Source, text); }
    [Fact] public void Copy_preserves_bytes_order_and_draft_then_products_are_independent()
    {
        Seed(Custom);
        Assert.Equal(Destination, ProductPromptConfig.Resolve(home, "codex-console", null));
        Assert.Equal(File.ReadAllBytes(Source), File.ReadAllBytes(Destination));
        var prompts = PromptCommand.LoadPrompts(Destination).ToArray();
        Assert.Equal(new[] { "second", "first" }, prompts.Select(p => p.Id));
        Assert.False(prompts[0].Submits);
        File.WriteAllText(Destination, "[]");
        Assert.Equal(Destination, ProductPromptConfig.Resolve(home, "codex-console", null));
        Assert.Empty(PromptCommand.LoadPrompts(Destination));
        Assert.Equal(Custom, File.ReadAllText(Source));
        Assert.Equal(Source, ProductPromptConfig.Resolve(home, "claude-console", null));
    }
    [Fact] public void Migration_preserves_source_encoding_and_bom()
    {
        Seed(Custom);
        File.WriteAllText(Source, Custom, System.Text.Encoding.Unicode);
        ProductPromptConfig.Resolve(home, "codex-console", null);
        Assert.Equal(File.ReadAllBytes(Source), File.ReadAllBytes(Destination));
        Assert.Equal("é draft", PromptCommand.LoadPrompts(Destination).First().Prompt);
    }
    [Theory] [InlineData("broken")] [InlineData("null")] [InlineData("[null]")]
    [InlineData("[{\"id\":\"same\"},{\"id\":\"same\"}]")] [InlineData("[{}]")]
    public void Invalid_source_is_untouched_and_retryable(string raw)
    {
        Seed(raw); string warning = null;
        Assert.Null(ProductPromptConfig.Resolve(home, "codex-console", s => warning = s));
        Assert.NotNull(warning); Assert.False(File.Exists(Destination)); Assert.Equal(raw, File.ReadAllText(Source));
        Seed(Custom);
        Assert.Equal(Destination, ProductPromptConfig.Resolve(home, "codex-console", null));
    }
    [Fact] public void Existing_destination_wins_even_if_source_is_invalid()
    {
        Seed("broken"); ProductPromptConfig.Publish(Destination, "[]", false);
        Assert.Equal(Destination, ProductPromptConfig.Resolve(home, "codex-console", _ => Assert.Fail("no migration expected")));
        Assert.Equal("[]", File.ReadAllText(Destination));
    }
    [Fact] public void Missing_source_seeds_only_codex()
    {
        var path = ProductPromptConfig.Resolve(home, "codex-console", null);
        Assert.NotEmpty(PromptCommand.LoadPrompts(path));
        Assert.True(File.Exists(Destination)); Assert.False(File.Exists(Source));
    }
    [Fact] public void Blocked_destination_leaves_source_and_uses_fallback()
    {
        Seed(Custom); File.WriteAllText(Path.Combine(home, ".codex"), "blocking file");
        Assert.Null(ProductPromptConfig.Resolve(home, "codex-console", null));
        Assert.Equal(Custom, File.ReadAllText(Source));
    }
    [Fact] public void Concurrent_create_never_overwrites_winner_or_leaves_temporary_files()
    {
        Parallel.For(0, 20, n => ProductPromptConfig.Publish(Destination, n.ToString(), false));
        var winner = File.ReadAllText(Destination);
        ProductPromptConfig.Publish(Destination, "replacement", false);
        Assert.Equal(winner, File.ReadAllText(Destination));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Destination)));
    }
    [Fact] public void Interrupted_temporary_copy_does_not_prevent_retry()
    {
        Seed(Custom); Directory.CreateDirectory(Path.GetDirectoryName(Destination));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Destination), ".prompts-old.tmp"), "partial");
        Assert.Equal(Destination, ProductPromptConfig.Resolve(home, "codex-console", null));
        Assert.Equal(Custom, File.ReadAllText(Destination));
    }
}
