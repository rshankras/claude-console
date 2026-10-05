using System.Text.Json;

// A real child process with controllable pipe/exit failures, on Windows and macOS.
var mode = args[0];
if (args[1] != "serve")
{
    if (args[1] == "sleep")
    {
        if (args.Length > 3) File.WriteAllText(args[3], Environment.ProcessId.ToString());
        Thread.Sleep(Int32.Parse(args[2]));
    }
    Console.WriteLine(JsonSerializer.Serialize(args.Skip(1)));
    return;
}
if (mode == "old") { Console.WriteLine("{\"ok\":false,\"error\":\"unknown-verb serve\"}"); return; }
if (mode == "cold") Thread.Sleep(500);
Console.WriteLine("{\"ready\":1}");
Console.Out.Flush();
if (mode == "no-read") { Thread.Sleep(30000); return; }
String line;
while ((line = Console.ReadLine()) != null)
{
    var request = JsonSerializer.Deserialize<String[]>(line);
    if (request[0] == "die") return;
    if (request[0] == "corrupt") { File.AppendAllText(request[1], "acted\n"); Console.WriteLine("bad reply"); Console.Out.Flush(); continue; }
    if (request[0] == "sleep")
    {
        if (request.Length > 2) File.WriteAllText(request[2], Environment.ProcessId.ToString());
        Thread.Sleep(Int32.Parse(request[1]));
    }
    var output = request[0] == "pid" ? Environment.ProcessId.ToString() : JsonSerializer.Serialize(request);
    Console.WriteLine(JsonSerializer.Serialize(new { exit = 0, @out = output }));
    Console.Out.Flush();
}
