using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Velopack;

// V2: Protobot's launcher. Velopack runs its hooks (install, update, uninstall) through this small exe, which handles
// them and exits at once; a normal start launches the Unity player beside it with the same arguments.
static class Program {
    [STAThread]
    static void Main(string[] args) {
        try { VelopackApp.Build().SetAutoApplyOnStartup(false).Run(); }
        catch (Exception) { }
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        var game = Path.Combine(dir, "ProtobotRebuiltV2.exe");
        var start = new ProcessStartInfo(game, string.Join(" ", args.Select(Quote))) { UseShellExecute = false, WorkingDirectory = dir };
        Process.Start(start);
    }

    static string Quote(string arg) => arg.Length == 0 || arg.Any(char.IsWhiteSpace) || arg.Contains('"') ? "\"" + arg.Replace("\"", "\\\"") + "\"" : arg;
}
