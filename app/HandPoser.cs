// HandPoser.cs -- start-up, settings, and the link to the game folder (the two files under reframework\data\hand_poser\).
//
//   HandPoser.exe                      the window
//   HandPoser.exe --game <folder>      use this game folder instead of looking for a running game
//   HandPoser.exe --selftest <folder>  no window: exercise the model and the game link in <folder>, print PASS/FAIL
//   HandPoser.exe --demo               with the window: once connected, add the left fingers and turn one, for the test
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

static class Program {
    public static readonly string AppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HandPoserVR");

    [STAThread]
    static int Main(string[] args) {
        string gameArg = null;
        bool demo = false;
        for (int i = 0; i < args.Length; i++) {
            if (args[i] == "--game" && i + 1 < args.Length) gameArg = args[++i];
            else if (args[i] == "--selftest" && i + 1 < args.Length) return SelfTest.Run(args[++i]) ? 0 : 1;
            else if (args[i] == "--demo") demo = true;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new PoserUi(gameArg, demo));
        return 0;
    }
}

// settings.txt: one `key=value` per line, in %APPDATA%\HandPoserVR.
class Settings {
    readonly string file = Path.Combine(Program.AppData, "settings.txt");
    readonly Dictionary<string, string> values = new Dictionary<string, string>();

    public Settings() {
        try {
            if (File.Exists(file))
                foreach (var line in File.ReadAllLines(file)) {
                    int eq = line.IndexOf('=');
                    if (eq > 0) values[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
        } catch (IOException) { }
    }

    public string Get(string key, string dflt) { string v; return values.TryGetValue(key, out v) ? v : dflt; }

    public void Set(string key, string value) {
        values[key] = value;
        try {
            Directory.CreateDirectory(Program.AppData);
            File.WriteAllLines(file, values.Select(kv => kv.Key + "=" + kv.Value).ToArray());
        } catch (IOException) { }
    }
}

// The game folder and the two files. `Folder` is the game's install folder (the one with reframework\ in it).
class GameLink {
    public const string SCRIPT_NAME = "hand_poser.lua";
    public const double STALE_SEC = 3.0;        // state.json older than this = the script is not running

    public string Folder { get; private set; }
    public string DataDir { get { return Folder == null ? null : Path.Combine(Folder, "reframework", "data", "hand_poser"); } }
    public string StateFile { get { return Folder == null ? null : Path.Combine(DataDir, "state.json"); } }
    public string CommandFile { get { return Folder == null ? null : Path.Combine(DataDir, "command.json"); } }
    public string ScriptTarget { get { return Folder == null ? null : Path.Combine(Folder, "reframework", "autorun", SCRIPT_NAME); } }
    public string ScriptSource { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SCRIPT_NAME); } }

    public void Use(string folder) {
        Folder = folder;
        if (folder != null) Directory.CreateDirectory(DataDir);
    }

    public static bool LooksLikeGame(string folder) {
        return folder != null && Directory.Exists(Path.Combine(folder, "reframework"));
    }

    // A running game with REFramework beside its exe. Games we cannot look into (access denied) are skipped.
    public static string FindRunningGame() {
        foreach (var p in Process.GetProcesses()) {
            string exe = null;
            try { exe = p.MainModule.FileName; } catch (Exception) { continue; }
            string dir = Path.GetDirectoryName(exe);
            if (LooksLikeGame(dir)) return dir;
        }
        return null;
    }

    // true when state.json was written recently enough to count as live
    public bool ScriptAlive() {
        try { return File.Exists(StateFile) && (DateTime.Now - File.GetLastWriteTime(StateFile)).TotalSeconds < STALE_SEC; }
        catch (IOException) { return false; }
    }

    public string ReadState() {
        try { return File.Exists(StateFile) ? File.ReadAllText(StateFile) : null; }
        catch (IOException) { return null; }
    }

    // Written to a side file and swapped in, so the script never sees half a file.
    public void WriteCommand(string json) {
        if (Folder == null) return;
        string tmp = CommandFile + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(CommandFile)) File.Replace(tmp, CommandFile, null); else File.Move(tmp, CommandFile);
    }

    public bool ScriptInstalled() { return Folder != null && File.Exists(ScriptTarget); }

    public bool ScriptUpToDate() {
        return ScriptInstalled() && File.Exists(ScriptSource) && File.ReadAllText(ScriptSource) == File.ReadAllText(ScriptTarget);
    }

    public void InstallScript() {
        if (!File.Exists(ScriptSource)) throw new FileNotFoundException("hand_poser.lua is not next to HandPoser.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(ScriptTarget));
        File.Copy(ScriptSource, ScriptTarget, true);
    }
}
