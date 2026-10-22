using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ReforgerLauncher;

// A dedicated server running on this PC, from its command line.
public class ServerProc
{
    public int Id;
    public DateTime Started;
    public string Config = "", Scenario = "", Profile = "", Addons = "", AddonsDir = "";
    public string Mode => Config.Length > 0 ? "public" : Scenario.Length > 0 ? "local" : "other";
}

// What the running server's log says (see Launch.Info).
public class ServerInfo
{
    public string Name = "", Registered = "", JoinCode = "";
    public int Port, ConfigPort;
    public bool Ready;
    public long LogLength = -1;
}

// Where each mod is on disk for a local game.
public class LocalPlan
{
    public List<string> Dirs = new();
    public List<string> Missing = new();
    public string Addons = "";
}

// Running servers and the game: finding, starting, stopping, and their command lines.
public class Launch
{
    readonly App app;
    readonly Dictionary<int, ServerInfo> info = new();
    public Launch(App app) { this.app = app; }

    static string Arg(string cl, string name)
    {
        var m = Regex.Match(cl, $@"\s-{name}\s+(?:""([^""]+)""|(\S+))");
        return m.Success ? m.Groups[1].Value + m.Groups[2].Value : "";
    }

    public List<ServerProc> Servers()
    {
        var list = new List<ServerProc>();
        foreach (var (p, cl) in Native.ProcessesNamed("ArmaReforgerServer"))
        {
            var s = new ServerProc { Id = p.Id, Config = Arg(cl, "config"), Scenario = Arg(cl, "server"), Profile = Arg(cl, "profile"), AddonsDir = Arg(cl, "addonsDir") };
            var a = Regex.Match(cl, @"\s-addons\s+(\S+)");
            if (a.Success) s.Addons = a.Groups[1].Value;
            try { s.Started = p.StartTime; } catch { s.Started = DateTime.Now; }
            list.Add(s);
        }
        return list;
    }

    public FileInfo ServerLog(ServerProc proc)
    {
        var profile = proc != null && proc.Profile.Length > 0 ? proc.Profile : Path.Combine(app.Root, "profile");
        var logs = new DirectoryInfo(Path.Combine(profile, "logs"));
        if (!logs.Exists) return null;
        var dir = logs.GetDirectories().OrderByDescending(d => d.CreationTime).FirstOrDefault();
        if (dir == null) return null;
        var file = new FileInfo(Path.Combine(dir.FullName, "console.log"));
        return file.Exists ? file : null;
    }

    public bool IsReady(ServerProc proc) => Info(proc).Ready;

    // What a running server's log says about it: ready, the address it registered with the server browser and its direct
    // join code (public servers), the port it listens on. Read again only while the log grows and something is missing.
    public ServerInfo Info(ServerProc proc)
    {
        if (!info.TryGetValue(proc.Id, out var si)) info[proc.Id] = si = new ServerInfo();
        if (si.Name.Length == 0)
        {
            if (proc.Mode == "public")
            {
                try { var cfg = Json.Parse(File.ReadAllText(proc.Config)); si.Name = Json.Str(cfg, "game.name"); si.ConfigPort = (int)Json.Long(cfg, "bindPort"); if (si.ConfigPort == 0) si.ConfigPort = (int)Json.Long(cfg, "publicPort"); } catch { }
                if (si.Name.Length == 0) si.Name = Path.GetFileName(proc.Config);
            }
            else if (proc.Mode == "local") si.Name = app.FindScenario(proc.Scenario)?.Name ?? Regex.Replace(proc.Scenario, "^.*/", "");
            else si.Name = "Dedicated server";
        }
        bool done = si.Ready && si.Port > 0 && (proc.Mode != "public" || si.Registered.Length > 0);
        if (!done)
        {
            var log = ServerLog(proc);
            if (log != null && log.CreationTime >= proc.Started.AddMinutes(-1) && log.Length != si.LogLength)
            {
                try
                {
                    using var fs = new FileStream(log.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    // Only what was added since the last look (a little before it, for a line cut in half).
                    if (si.LogLength > 0 && si.LogLength <= fs.Length) fs.Seek(Math.Max(0, si.LogLength - 300), SeekOrigin.Begin);
                    using var sr = new StreamReader(fs);
                    var text = sr.ReadToEnd();
                    si.LogLength = fs.Length;
                    if (text.Contains("Entered online game state")) si.Ready = true;
                    var m = Regex.Match(text, @"Server registered with address:\s*(\S+)");
                    if (m.Success) si.Registered = m.Groups[1].Value;
                    m = Regex.Match(text, @"Direct Join Code:\s*(\S+)");
                    if (m.Success) si.JoinCode = m.Groups[1].Value;
                    m = Regex.Match(text, @"listening on address \S+:(\d+)");
                    if (m.Success) si.Port = int.Parse(m.Groups[1].Value);
                }
                catch { }
            }
        }
        return si;
    }

    // The address others use: the one the server registered (public), else this PC and its port.
    public string AddressOf(ServerProc proc)
    {
        var si = Info(proc);
        if (si.Registered.Length > 0) return si.Registered;
        int port = si.Port > 0 ? si.Port : si.ConfigPort > 0 ? si.ConfigPort : 2001;
        return $"127.0.0.1:{port}";
    }

    // This PC's address on the network its default route uses (e.g. 192.168.1.20), or "".
    public static string LanAddress()
    {
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .OrderBy(n => n.GetIPProperties().GetIPv4Properties()?.Index ?? 999))
            {
                var props = ni.GetIPProperties();
                if (!props.GatewayAddresses.Any(gw => gw.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !gw.Address.Equals(System.Net.IPAddress.Any))) continue;
                var ip = props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (ip != null) return ip.Address.ToString();
            }
        }
        catch { }
        return "";
    }

    // Stops every dedicated server, including start-server.bat's restart loop (else it restarts the server 15 s later).
    public void StopServers()
    {
        foreach (var (p, cl) in Native.ProcessesNamed("cmd"))
            if (cl.Contains("start-server.bat", StringComparison.OrdinalIgnoreCase))
            {
                try { Process.Start(new ProcessStartInfo("taskkill.exe", $"/PID {p.Id} /T /F") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(5000); } catch { }
            }
        foreach (var s in Servers())
            try { Process.GetProcessById(s.Id).Kill(); } catch { }
    }

    // Where each mod is on disk for a local game: unpublished project, the game's downloads, or the server's addons\ (in
    // that order of preference).
    public LocalPlan Plan(ModList holder)
    {
        var plan = new LocalPlan();
        foreach (var g in holder.Mods.Keys)
        {
            string dir = null;
            if (app.ProjectMods.TryGetValue(g, out var proj)) dir = Path.GetDirectoryName(proj.Folder);
            else if (app.Installed.ContainsKey(g)) dir = app.AddonsDir;
            else if (app.ServerMods.ContainsKey(g)) dir = Path.Combine(app.Root, "addons");
            if (dir == null) plan.Missing.Add(g);
            else if (!plan.Dirs.Contains(dir)) plan.Dirs.Add(dir);
        }
        // The game always searches its own downloads folder last, whatever -addonsDir says, and the first folder holding a
        // mod wins. The server must search in that same order, or a mod in two folders loads a different copy on each side
        // and the server refuses the game ("script source code checksum does not match").
        if (plan.Dirs.Remove(app.AddonsDir)) plan.Dirs.Add(app.AddonsDir);
        plan.Addons = string.Join(",", holder.Mods.Keys);
        return plan;
    }

    public List<string> LocalServerArgs(string scenarioId, LocalPlan plan, string adminPassword, bool loadSave)
    {
        var a = new List<string> { "-server", scenarioId, "-addonsDir", string.Join(",", new[] { Path.Combine(app.ServerDir, "addons") }.Concat(plan.Dirs)) };
        if (plan.Addons.Length > 0) a.AddRange(new[] { "-addons", plan.Addons });
        a.AddRange(new[] { "-profile", Path.Combine(app.Root, "profile-test"), "-maxFPS", "60", "-logStats", "60000", "-nothrow" });
        if (!string.IsNullOrEmpty(adminPassword)) a.AddRange(new[] { "-adminPassword", adminPassword });
        if (loadSave) a.Add("-loadSessionSave");
        return a;
    }

    public void StartLocalServer(List<string> args) =>
        Process.Start(new ProcessStartInfo(Path.Combine(app.ServerDir, "ArmaReforgerServer.exe"), Native.JoinArgs(args))
        { WorkingDirectory = app.ServerDir, UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized });

    public static List<string> ModArgs(IEnumerable<string> dirs, string addons)
    {
        var a = new List<string>();
        if (string.IsNullOrEmpty(addons)) return a;
        var d = dirs?.ToList() ?? new List<string>();
        if (d.Count > 0) a.AddRange(new[] { "-addonsDir", string.Join(",", d) });
        a.AddRange(new[] { "-addons", addons });
        return a;
    }

    // The game joining the dedicated server on this PC with exactly that server's mods.
    public List<string> ClientArgsFor(ServerProc proc)
    {
        var serverAddons = Path.Combine(app.ServerDir ?? "", "addons").TrimEnd('\\');
        var dirs = proc.AddonsDir.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(d => !d.TrimEnd('\\').Equals(serverAddons, StringComparison.OrdinalIgnoreCase));
        var a = ModArgs(dirs, proc.Addons);
        a.AddRange(new[] { "-client", "-rpl-reconnect" });
        return a;
    }

    // Test hook: when set, StartGame calls it instead of starting the game.
    public Action<List<string>> StartGameHook;
    public Action AfterGameStart;

    // Starts the game with the arguments plus the launch options from ADVANCED.
    public void StartGame(IEnumerable<string> arguments = null)
    {
        var all = (arguments ?? Enumerable.Empty<string>()).Concat(app.Data.AdvancedArgs()).Where(a => !string.IsNullOrEmpty(a)).ToList();
        if (StartGameHook != null) { StartGameHook(all); return; }
        Process.Start(new ProcessStartInfo(Path.Combine(app.GameDir, "ArmaReforgerSteam.exe"), Native.JoinArgs(all)) { WorkingDirectory = app.GameDir, UseShellExecute = false });
        AfterGameStart?.Invoke();
    }

    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception ex) { Ui.ShowError(ex); }
    }
}
