using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Velopack;

// V2: Protobot's launcher. Velopack runs its hooks (install, update, uninstall) through this small exe, which handles
// them and exits at once; a normal start launches the Unity player beside it with the same arguments.
// It also makes Protobot the program for its build (.pbb) and replay (.pbr) files, for this user only, so a file
// someone sends opens with a double-click (the player opens a file given on its command line: Builds/OpenWith).
static class Program {
    [STAThread]
    static void Main(string[] args) {
        try {
            VelopackApp.Build().SetAutoApplyOnStartup(false)
                .OnAfterInstallFastCallback(_ => RegisterFiles())
                .OnAfterUpdateFastCallback(_ => RegisterFiles())
                .OnBeforeUninstallFastCallback(_ => UnregisterFiles())
                .Run();
        }
        catch (Exception) { }
        try { RegisterFiles(); } catch (Exception) { }   // installs from before 1.9.67 had no hook for it
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        var game = Path.Combine(dir, "ProtobotRebuiltV2.exe");
        var start = new ProcessStartInfo(game, string.Join(" ", args.Select(Quote))) { UseShellExecute = false, WorkingDirectory = dir };
        Process.Start(start);
    }

    static string Quote(string arg) => arg.Length == 0 || arg.Any(char.IsWhiteSpace) || arg.Contains('"') ? "\"" + arg.Replace("\"", "\\\"") + "\"" : arg;

    static readonly (string ext, string id, string name)[] Types = {
        (".pbb", "ProtobotRebuiltV2.Build", "Protobot build"),
        (".pbr", "ProtobotRebuiltV2.Replay", "Protobot replay"),
    };

    // The exe that stays put across updates: Velopack's stub one folder up (current\ is replaced on each update).
    static string Exe() {
        var dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        var stub = Path.Combine(Path.GetDirectoryName(dir) ?? dir, "ProtobotLauncher.exe");
        return File.Exists(stub) ? stub : Path.Combine(dir, "ProtobotLauncher.exe");
    }

    static void RegisterFiles() {
        var exe = Exe();
        var command = "\"" + exe + "\" \"%1\"";
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
        var changed = false;
        foreach (var (ext, id, name) in Types) {
            using (var type = classes.CreateSubKey(id)) {
                if ((type.GetValue("") as string) != name) { type.SetValue("", name); changed = true; }
                using (var icon = type.CreateSubKey("DefaultIcon")) icon.SetValue("", "\"" + exe + "\",0");
                using var open = type.CreateSubKey(@"shell\open\command");
                if ((open.GetValue("") as string) != command) { open.SetValue("", command); changed = true; }
            }
            using var key = classes.CreateSubKey(ext);
            if ((key.GetValue("") as string) != id) { key.SetValue("", id); changed = true; }
        }
        if (changed) SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);   // SHCNE_ASSOCCHANGED: Explorer shows the icons
    }

    static void UnregisterFiles() {
        using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", true);
        if (classes == null) return;
        foreach (var (ext, id, _) in Types) {
            classes.DeleteSubKeyTree(id, false);
            using (var key = classes.OpenSubKey(ext)) {
                if (key == null || (key.GetValue("") as string) != id) continue;
            }
            classes.DeleteSubKeyTree(ext, false);
        }
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
