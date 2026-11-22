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
        try { MoveData(); } catch (Exception) { }
        if (args.Contains("--move-data-only")) return;   // (testing)
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

    // ------------------------------------------------------------------ v2.0 Beta 1: "Cranez" -> "Protobot"

    // Unity keeps an app's data (custom parts library, autosaves, replays, logs) in AppData\LocalLow\<company>\<product>
    // and its settings in HKCU\Software\<company>\<product>. The company was "Cranez" (the developer's name) until v2.0
    // Beta 1 and is "Protobot" now, so the first start of the new version moves both before the player starts: nothing
    // is lost. Both already there (an old version ran after the move): the old one's files and settings fill in only
    // what's missing and it's left as it is.
    const string OldCompany = "Cranez", NewCompany = "Protobot", ProductName = "ProtobotRebuiltV2";

    static void MoveData() {
        if (Process.GetProcessesByName(ProductName).Length > 0) return;   // Protobot is running: next time
        MoveSettings();
        MoveFolder();
    }

    static void MoveSettings() {
        var from = $@"Software\{OldCompany}\{ProductName}";
        var to = $@"Software\{NewCompany}\{ProductName}";
        using var old = Registry.CurrentUser.OpenSubKey(from);
        if (old == null) return;
        using var existing = Registry.CurrentUser.OpenSubKey(to);
        var fresh = existing == null || existing.ValueCount == 0;
        using (var target = Registry.CurrentUser.CreateSubKey(to)) CopyKey(old, target, overwrite: false);
        if (!fresh) return;
        old.Close();
        Registry.CurrentUser.DeleteSubKeyTree(from, false);
        using var company = Registry.CurrentUser.OpenSubKey($@"Software\{OldCompany}");
        if (company != null && company.SubKeyCount == 0 && company.ValueCount == 0) {
            company.Close();
            Registry.CurrentUser.DeleteSubKey($@"Software\{OldCompany}", false);
        }
    }

    static void CopyKey(RegistryKey from, RegistryKey to, bool overwrite) {
        foreach (var name in from.GetValueNames()) {
            if (!overwrite && to.GetValue(name) != null) continue;
            to.SetValue(name, from.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), from.GetValueKind(name));
        }
        foreach (var sub in from.GetSubKeyNames()) {
            using var a = from.OpenSubKey(sub);
            using var b = to.CreateSubKey(sub);
            if (a != null) CopyKey(a, b, overwrite);
        }
    }

    static void MoveFolder() {
        var low = LocalLow();
        if (string.IsNullOrEmpty(low)) return;
        var from = Path.Combine(low, OldCompany, ProductName);
        var to = Path.Combine(low, NewCompany, ProductName);
        if (!Directory.Exists(from)) return;
        if (!Directory.Exists(to)) {
            Directory.CreateDirectory(Path.Combine(low, NewCompany));
            Directory.Move(from, to);
            try { if (!Directory.EnumerateFileSystemEntries(Path.Combine(low, OldCompany)).Any()) Directory.Delete(Path.Combine(low, OldCompany)); }
            catch (Exception) { }
            return;
        }
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)) {
            var target = Path.Combine(to, file.Substring(from.Length).TrimStart('\\', '/'));
            if (File.Exists(target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            try { File.Copy(file, target); } catch (Exception) { }
        }
    }

    // AppData\LocalLow (no Environment.SpecialFolder for it).
    static string LocalLow() {
        var id = new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16");
        if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var path) == 0) {
            var result = Marshal.PtrToStringUni(path);
            Marshal.FreeCoTaskMem(path);
            return result;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");
    }

    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
}
