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

    // A server's console.log is in the newest log folder of its profile - but only once it has made one: until then the
    // newest is the previous server's (whose "online" would read as this one being up). Its folder is made a moment after
    // the process starts; the seconds allow for the start time being taken a little late.
    public const int LogTolerance = 10;

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
            if (log != null && log.CreationTime >= proc.Started.AddSeconds(-LogTolerance) && log.Length != si.LogLength)
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

    // The port a server plays on: from its log, else its config, else the default 2001.
    public int PortOf(ServerProc proc)
    {
        var si = Info(proc);
        return si.Port > 0 ? si.Port : si.ConfigPort > 0 ? si.ConfigPort : 2001;
    }

    public static int PortOf(System.Text.Json.Nodes.JsonObject config)
    {
        long p = Json.Long(config, "bindPort");
        if (p == 0) p = Json.Long(config, "publicPort");
        return p > 0 ? (int)p : 2001;
    }

    // Where a server's RCON listens and its password, from its config ("rcon" section); null when it has none.
    public (string Host, int Port, string Password)? RconOf(ServerProc proc)
    {
        if (proc == null || proc.Config.Length == 0) return null;
        try
        {
            var cfg = Json.Parse(File.ReadAllText(proc.Config));
            var password = Json.Str(cfg, "rcon.password");
            if (cfg is not System.Text.Json.Nodes.JsonObject o || !Json.Has(o, "rcon") || password.Length == 0) return null;
            var host = Json.Str(cfg, "rcon.address");
            if (host.Length == 0 || host == "0.0.0.0") host = "127.0.0.1";
            long port = Json.Long(cfg, "rcon.port");
            return (host, port > 0 ? (int)port : 19999, password);
        }
        catch { return null; }
    }

    // Test hook: when set, StartServer calls it instead of starting the server.
    public Func<ProcessStartInfo, Process> StartServerHook;

    // The dedicated server with a config and no window of its own: the launcher's Console tab shows its log and talks to
    // it over RCON. The same options as start-server.bat; the launcher does the restarting.
    public Process StartServer(string configPath)
    {
        var psi = new ProcessStartInfo(Path.Combine(app.ServerDir, "ArmaReforgerServer.exe")) { WorkingDirectory = app.ServerDir, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-config", configPath, "-profile", Path.Combine(app.Root, "profile"), "-addonDownloadDir", app.Root, "-maxFPS", "60", "-logStats", "60000", "-backendlog", "-nothrow" })
            psi.ArgumentList.Add(a);
        return StartServerHook != null ? StartServerHook(psi) : Process.Start(psi);
    }

    // Stops one server the way its console would: RCON #shutdown when connected, else Ctrl+C. Either way it saves its
    // persistence first and exits within seconds; only one that doesn't answer is killed. A server started by
    // start-server.bat has the batch file's restart loop ended first (its cmd only - the server must exit by itself).
    public async Task<bool> StopServerAsync(ServerProc proc, RconClient rcon, int waitMs = 60000)
    {
        Process p;
        try { p = Process.GetProcessById(proc.Id); } catch { return true; }
        int parent = Native.ParentId(proc.Id);
        if (parent > 0)
            foreach (var (cmd, cl) in Native.ProcessesNamed("cmd"))
                if (cmd.Id == parent && cl.Contains("start-server.bat", StringComparison.OrdinalIgnoreCase)) { try { cmd.Kill(); } catch { } }
        // RCON first (a "monitor" login may not shut it down: then Ctrl+C after 20 s), killed only when neither works.
        if (rcon is { LoggedIn: true, Closed: false })
        {
            try { await rcon.SendAsync("#shutdown", 3000, 0); if (await Task.Run(() => p.WaitForExit(20000))) return true; } catch { }
        }
        if (Native.SendCtrlC(proc.Id) && await Task.Run(() => p.WaitForExit(waitMs))) return true;
        try { p.Kill(); } catch { }
        return await Task.Run(() => p.WaitForExit(5000));
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

    // A PLAY test server: no window either; the Console tab shows its log.
    public void StartLocalServer(List<string> args) =>
        Process.Start(new ProcessStartInfo(Path.Combine(app.ServerDir, "ArmaReforgerServer.exe"), Native.JoinArgs(args))
        { WorkingDirectory = app.ServerDir, UseShellExecute = false, CreateNoWindow = true });

    // Where the game hosts a scenario it runs itself (PLAY single-player / PLAY SOLO): it listens on 0.0.0.0:2001 unless
    // given -addr, and can't start at all ("Unable to start replication") when a server already has that port. -bindPort
    // is ignored. This PC only, on a free port away from the servers' 2001/2002.
    public static string GameServerAddress()
    {
        var used = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Select(e => e.Port).ToHashSet();
        int port = 2301;
        while (used.Contains(port)) port++;
        return $"127.0.0.1:{port}";
    }

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
    // Test hooks for EnsureSteam: Steam's state, and starting it; how long to wait and how long a login must hold.
    public Func<(bool Running, bool LoggedIn)> SteamStateHook;
    public Action<string> StartSteamHook;
    public TimeSpan SteamTimeout = TimeSpan.FromMinutes(2), SteamSettle = TimeSpan.FromSeconds(15), SteamSettleAfterLogin = TimeSpan.FromSeconds(3);

    // The game needs the Steam client running and logged in; started without it, it stops at "SteamAPI_Init failed. Is
    // Steam running?" / "Unable to initialize the game". Steam's ActiveProcess key names its process and logged-in user
    // while it runs. A clean exit sets both to 0; a crash leaves them behind - so the process has to really be there too.
    public static (bool Running, bool LoggedIn) SteamState()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
        int pid = key?.GetValue("pid") is int p ? p : 0;
        int user = key?.GetValue("ActiveUser") is int u ? u : 0;
        if (pid <= 0) return (false, false);
        try
        {
            using var proc = Process.GetProcessById(pid);
            bool running = !proc.HasExited && proc.ProcessName.Equals("steam", StringComparison.OrdinalIgnoreCase);
            return (running, running && user != 0);
        }
        catch { return (false, false); }
    }

    public static bool SteamReady() => SteamState().LoggedIn;

    // Starts Steam (to the tray) when it isn't running and waits until it's logged in - up to 2 minutes, keeping the window
    // responsive. Measured 2026-10-23: a Steam starting after a clean exit writes its process at once and the user ~4 s
    // later, when logged in; after a crash the old user is still there, so a just-started Steam looks logged in for a few
    // seconds until it clears it to log in. So once Steam was seen logged out, a login must hold 3 s; if it never was, 15 s.
    // False (after saying why) when it doesn't come up.
    public bool EnsureSteam(Action<string> status = null)
    {
        if (StartGameHook != null && SteamStateHook == null) return true;   // tests that don't start anything
        var state = SteamStateHook ?? SteamState;
        if (state().LoggedIn) return true;
        var exe = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamExe") as string;
        if (StartSteamHook == null && (string.IsNullOrEmpty(exe) || !File.Exists(exe)))
        {
            Ui.Message("Steam isn't running, and steam.exe wasn't found to start it. Start Steam, then try again.", MessageBoxIcon.Warning);
            return false;
        }
        status?.Invoke("Steam isn't running - starting it (the game needs it)...");
        if (StartSteamHook != null) StartSteamHook(exe);
        else Process.Start(new ProcessStartInfo(exe, "-silent") { UseShellExecute = true });
        var until = DateTime.Now + SteamTimeout;
        bool sawLoggedOut = false;
        DateTime? loggedInSince = null;
        while (DateTime.Now < until)
        {
            var s = state();
            if (!s.LoggedIn)
            {
                loggedInSince = null;
                if (s.Running) sawLoggedOut = true;
            }
            else
            {
                loggedInSince ??= DateTime.Now;
                if (DateTime.Now - loggedInSince >= (sawLoggedOut ? SteamSettleAfterLogin : SteamSettle))
                {
                    status?.Invoke("Steam is up.");
                    return true;
                }
            }
            Pause(250);
        }
        Ui.Message("Steam didn't finish starting within 2 minutes (it may be updating, or waiting for you to log in). Once it's up, try again.", MessageBoxIcon.Warning);
        return false;
    }
    static void Pause(int ms)
    {
        var end = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(30); }
    }

    // Starts the game with the arguments plus the launch options from ADVANCED, starting Steam first if it isn't running.
    // False when it didn't start.
    public bool StartGame(IEnumerable<string> arguments = null, Action<string> status = null)
    {
        var all = (arguments ?? Enumerable.Empty<string>()).Concat(app.Data.AdvancedArgs()).Where(a => !string.IsNullOrEmpty(a)).ToList();
        if (StartGameHook != null) { StartGameHook(all); return true; }
        if (!EnsureSteam(status)) return false;
        Process.Start(new ProcessStartInfo(Path.Combine(app.GameDir, "ArmaReforgerSteam.exe"), Native.JoinArgs(all)) { WorkingDirectory = app.GameDir, UseShellExecute = false });
        AfterGameStart?.Invoke();
        return true;
    }

    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception ex) { Ui.ShowError(ex); }
    }
}
