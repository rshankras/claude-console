using System.Runtime.Versioning;

internal static class ToolsProgram
{
    // Also inspected by the package verifier: rejecting an old/incomplete toolkit is preferable
    // to discovering a missing verb on the first voice or screenshot press.
    internal const String Contract = "claude-console-tools/v1 inject focus voice shot";

    [SupportedOSPlatform("windows")]
    private static Int32 Main(String[] args)
    {
        if (args.Length == 1 && args[0] == "manifest")
        {
            Console.WriteLine(Contract);
            return 0;
        }
        var rest = args.Skip(1).ToArray();
        return (args.FirstOrDefault() ?? "") switch
        {
            "inject" => InjectProgram.Main(rest),
            "focus" => FocusProgram.Main(rest),
            "voice" => VoiceProgram.Main(rest),
            "shot" => ShotProgram.Main(rest),
            _ => Usage(),
        };
    }

    private static Int32 Usage()
    {
        Console.Error.WriteLine("usage: claude-console-tools <inject|focus|voice|shot> [arguments]");
        return 2;
    }
}
