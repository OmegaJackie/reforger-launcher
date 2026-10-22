using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReforgerLauncher;

// HISTORY / FAVORITES data: every server the game joined (read from its logs, which it deletes after ~10 sessions,
// with the exact mods it loaded for each) plus favorites and servers added by address, the ADVANCED launch options and
// the Server Configs folders - all in launcher.json. A list of every public server isn't reachable from outside the
// game: its own browser needs its login, BattleMetrics' API a paid key, and Reforger servers don't register with Steam.
public class ServerRecord
{
    public string Addr { get; set; } = "";
    public string Name { get; set; } = "";
    public string Scenario { get; set; } = "";
    public string ServerId { get; set; } = "";
    public List<string> Mods { get; set; } = new();
    public Dictionary<string, string> ModNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ModsKnown { get; set; }
    public bool Fav { get; set; }
    public bool Added { get; set; }
    public bool Hidden { get; set; }
    public List<string> Times { get; set; } = new();
    public int QueryPort { get; set; }
    public bool Password { get; set; }

    public string LastJoined => Times.FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "";
    public string Title => string.IsNullOrEmpty(Name) ? Addr : Name;
}

public class AdvancedOptions
{
    public bool Window { get; set; }
    public int MaxFps { get; set; }
    public bool NoSound { get; set; }
    public string Extra { get; set; } = "";
    public bool Minimize { get; set; }
    // START SERVER runs the server inside the launcher (Console tab) rather than in start-server.bat's window.
    public bool ServerInApp { get; set; } = true;
    // The launcher starts its server again when it stops by itself (as start-server.bat does).
    public bool AutoRestart { get; set; } = true;
}

public class LauncherStore
{
    public Dictionary<string, ServerRecord> Servers { get; set; } = new();
    public Dictionary<string, long> Logs { get; set; } = new();
    public AdvancedOptions Advanced { get; set; } = new();
    // Server Configs folders ("Kunar/Tests" = Tests inside Kunar) and the folder of each config file. Only the launcher's
    // view: the files stay next to start-server.bat, which needs them there.
    public List<string> ConfigFolders { get; set; } = new();
    public Dictionary<string, string> ConfigFolderOf { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // Configs whose owner said no to turning RCON on (asked once each).
    public List<string> RconOffered { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore] public string File;

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static LauncherStore Read(string path)
    {
        LauncherStore data = null;
        try { if (System.IO.File.Exists(path)) data = JsonSerializer.Deserialize<LauncherStore>(System.IO.File.ReadAllText(path), Options); } catch { data = null; }
        data ??= new LauncherStore();
        data.Servers ??= new(); data.Logs ??= new(); data.Advanced ??= new(); data.RconOffered ??= new();
        data.ConfigFolders = (data.ConfigFolders ?? new()).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var folderOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in data.ConfigFolderOf ?? new()) if (!string.IsNullOrEmpty(k) && !string.IsNullOrEmpty(v)) folderOf[k] = v;
        data.ConfigFolderOf = folderOf;
        // Records without an address (an early build could write one) are dropped.
        foreach (var k in data.Servers.Keys.ToList())
        {
            if (string.IsNullOrEmpty(k) || data.Servers[k] == null) { data.Servers.Remove(k); continue; }
            var r = data.Servers[k];
            r.Addr = k; r.Mods ??= new(); r.Times ??= new();
            r.ModNames = new Dictionary<string, string>(r.ModNames ?? new(), StringComparer.OrdinalIgnoreCase);
        }
        data.File = path;
        return data;
    }

    public string Save()
    {
        try { System.IO.File.WriteAllText(File, JsonSerializer.Serialize(this, Options), new UTF8Encoding(false)); return null; }
        catch (Exception ex) { return $"Couldn't save {File}: {ex.Message}"; }
    }

    public ServerRecord Record(string addr, bool create = false)
    {
        if (Servers.TryGetValue(addr, out var rec) || !create) return rec;
        rec = new ServerRecord { Addr = addr };
        Servers[addr] = rec;
        return rec;
    }

    public List<string> AdvancedArgs()
    {
        var a = Advanced;
        var o = new List<string>();
        if (a.Window) o.Add("-window");
        if (a.MaxFps > 0) { o.Add("-maxFPS"); o.Add(a.MaxFps.ToString(CultureInfo.InvariantCulture)); }
        if (a.NoSound) o.Add("-nosound");
        if (!string.IsNullOrWhiteSpace(a.Extra))
            foreach (Match m in Regex.Matches(a.Extra, "\"[^\"]*\"|\\S+")) o.Add(m.Value.Trim('"'));
        return o;
    }

    // -------------------------------------------------------------------------------------------------------------
    // The game's logs

    public class Join { public string Addr = "", Name = "", Id = "", Time = ""; public bool Known; public List<string> Mods = new(); public Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase); }

    // "2026-10-20 22:04" from a log folder (logs_2026-10-20_21-58-20) and a line's time of day (past midnight = next day).
    static string LogTime(string folder, string line)
    {
        var m = Regex.Match(folder, @"logs_(\d{4}-\d{2}-\d{2})_(\d{2})-(\d{2})");
        if (!m.Success) return "";
        var day = DateTime.ParseExact(m.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var t = Regex.Match(line, @"^(\d{2}):(\d{2})");
        if (!t.Success) return day.ToString("yyyy-MM-dd");
        int minutes = int.Parse(t.Groups[1].Value) * 60 + int.Parse(t.Groups[2].Value);
        if (minutes < int.Parse(m.Groups[2].Value) * 60 + int.Parse(m.Groups[3].Value)) day = day.AddDays(1);
        return day.AddMinutes(minutes).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    static readonly Regex GprojRx = new(@"gproj: '([^']*)' guid: '([0-9A-Fa-f]{16})'");
    static readonly Regex AddrCountRx = new(@"number of addresses to try connecting to: (\d+)");
    static readonly Regex AddrLineRx = new(@"NETWORK\s+:\s+(\S+:\d+)\s*$");

    // The servers one game log connected to: the address ("number of addresses to try connecting to"), name and ID when
    // joined from the game's browser ("Server Join Data"), and the mods it had loaded for it (the last "Loaded addons:").
    public static List<Join> ReadLogJoins(string path)
    {
        var folder = Path.GetFileName(Path.GetDirectoryName(path));
        var joins = new List<Join>();
        (string Name, string Id)? join = null;
        List<string> mods = null; Dictionary<string, string> names = null;
        bool inLoaded = false; int addrLeft = 0;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        string line;
        while ((line = sr.ReadLine()) != null)
        {
            if (inLoaded)
            {
                var g = GprojRx.Match(line);
                if (g.Success)
                {
                    if (!g.Groups[1].Value.StartsWith("./addons/"))   // not the base game's own core / data
                    {
                        var guid = g.Groups[2].Value.ToUpperInvariant();
                        mods.Add(guid);
                        names[guid] = Regex.Replace(Path.GetFileName(Path.GetDirectoryName(g.Groups[1].Value)) ?? "", "_" + guid + "$", "", RegexOptions.IgnoreCase);
                    }
                    continue;
                }
                inLoaded = false;
            }
            if (addrLeft > 0)
            {
                var a = AddrLineRx.Match(line);
                if (a.Success)
                {
                    addrLeft = 0;   // the first address is the server
                    var addr = a.Groups[1].Value;
                    if (!addr.StartsWith("127.") && !addr.StartsWith("localhost", StringComparison.OrdinalIgnoreCase))
                        joins.Add(new Join
                        {
                            Addr = addr, Time = LogTime(folder, line), Name = join?.Name ?? "", Id = join?.Id ?? "", Known = mods != null,
                            Mods = mods != null ? new List<string>(mods) : new(), Names = names != null ? new(names, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase),
                        });
                    join = null;
                    continue;
                }
                addrLeft--;
            }
            if (line.Contains("Server Join Data")) { join = ("", ""); continue; }
            if (join != null)
            {
                var m = Regex.Match(line, @"Server ID:\s+'([^']*)'");
                if (m.Success) { join = (join.Value.Name, m.Groups[1].Value); continue; }
                m = Regex.Match(line, @"Server Name:\s+'(.*)'\s*$");
                if (m.Success) { join = (m.Groups[1].Value, join.Value.Id); continue; }
            }
            if (line.Contains("Loaded addons:")) { mods = new(); names = new(StringComparer.OrdinalIgnoreCase); inLoaded = true; continue; }
            var c = AddrCountRx.Match(line);
            if (c.Success) addrLeft = int.Parse(c.Groups[1].Value);
        }
        return joins;
    }

    // Adds the joins from game logs not read yet (or grown since) to the servers. Returns how many new joins it found.
    public int UpdateHistory(string logsDir)
    {
        if (!Directory.Exists(logsDir)) return 0;
        int found = 0;
        var present = new HashSet<string>();
        foreach (var f in new DirectoryInfo(logsDir).EnumerateFiles("console.log", SearchOption.AllDirectories))
        {
            var key = f.Directory.Name;
            present.Add(key);
            if (Logs.TryGetValue(key, out long len) && len == f.Length) continue;
            Logs[key] = f.Length;
            List<Join> joins;
            try { joins = ReadLogJoins(f.FullName); } catch { continue; }
            foreach (var j in joins)
            {
                if (string.IsNullOrEmpty(j.Addr)) continue;
                var rec = Record(j.Addr, true);
                if (j.Name.Length > 0) rec.Name = j.Name;
                if (j.Id.Length > 0) rec.ServerId = j.Id;
                var times = rec.Times.Where(t => !string.IsNullOrEmpty(t)).ToList();
                if (j.Time.Length > 0 && !times.Contains(j.Time))
                {
                    times = times.Append(j.Time).OrderByDescending(t => t, StringComparer.Ordinal).Take(20).ToList();
                    found++;
                }
                rec.Times = times;
                if (j.Known && (!rec.ModsKnown || string.CompareOrdinal(j.Time, times.FirstOrDefault() ?? "") >= 0))
                {
                    rec.Mods = j.Mods; rec.ModNames = j.Names; rec.ModsKnown = true;
                }
            }
        }
        foreach (var k in Logs.Keys.Where(k => !present.Contains(k)).ToList()) Logs.Remove(k);
        return found;
    }

    // Where a server may answer A2S: the port it answered on before, the game port + 1, the default 17777.
    public static int[] QueryPorts(ServerRecord rec)
    {
        int.TryParse(rec.Addr.Split(':').Last(), out int port);
        return new[] { rec.QueryPort, port + 1, 17777 }.Where(p => p > 0 && p <= 65535).Distinct().ToArray();
    }

    public static string HostOf(ServerRecord rec) => rec.Addr.Contains(':') ? rec.Addr[..rec.Addr.LastIndexOf(':')] : rec.Addr;
}

public class A2SInfo
{
    public bool Online; public string Name = ""; public string Map = ""; public string Version = "";
    public int Players; public int Max; public int Ping; public int Port; public bool Password;
}

public class A2SPlayer { public string Name = ""; public int Minutes; }

// Steam server queries (A2S_INFO / A2S_PLAYER), which Reforger answers on its a2s port. Run on worker threads; the
// round trip is timed there for an honest ping.
public static class A2S
{
    static readonly byte[] InfoRequest = Build(0x54, Encoding.ASCII.GetBytes("Source Engine Query\0"));

    static byte[] Build(byte kind, byte[] body)
    {
        var b = new byte[5 + body.Length];
        b[0] = b[1] = b[2] = b[3] = 0xFF; b[4] = kind;
        Buffer.BlockCopy(body, 0, b, 5, body.Length);
        return b;
    }

    // Sends a request; a challenge reply (0x41) is answered once. The ping is the first round trip.
    static byte[] Ask(UdpClient udp, byte[] request, Func<byte[], byte[]> withChallenge, out int ping)
    {
        var clock = Stopwatch.StartNew();
        udp.Send(request, request.Length);
        IPEndPoint ep = null;
        var reply = udp.Receive(ref ep);
        ping = (int)clock.ElapsedMilliseconds;
        if (reply.Length >= 9 && reply[4] == 0x41)
        {
            var challenge = new byte[4];
            Buffer.BlockCopy(reply, 5, challenge, 0, 4);
            var again = withChallenge(challenge);
            udp.Send(again, again.Length);
            reply = udp.Receive(ref ep);
        }
        return reply;
    }

    static string Str(byte[] b, ref int p)
    {
        int s = p;
        while (p < b.Length && b[p] != 0) p++;
        var t = Encoding.UTF8.GetString(b, s, p - s);
        p++;
        return t;
    }

    public static Task<A2SInfo> InfoAsync(string host, int[] ports, int timeoutMs) => Task.Run(() => Info(host, ports, timeoutMs));

    // A2S_INFO on each port in turn until one answers: name, map (Reforger: the scenario), players, password, version.
    public static A2SInfo Info(string host, int[] ports, int timeoutMs)
    {
        foreach (var port in ports)
        {
            try
            {
                using var udp = new UdpClient();
                udp.Client.ReceiveTimeout = timeoutMs;
                udp.Connect(host, port);
                var r = Ask(udp, InfoRequest, c => InfoRequest.Concat(c).ToArray(), out int ping);
                if (r.Length < 7 || r[4] != 0x49) continue;
                int p = 6;
                var info = new A2SInfo { Online = true, Ping = ping, Port = port };
                info.Name = Str(r, ref p); info.Map = Str(r, ref p); Str(r, ref p); Str(r, ref p);
                p += 2;
                if (p + 7 <= r.Length)
                {
                    info.Players = r[p]; info.Max = r[p + 1]; info.Password = r[p + 5] == 1;
                    p += 7;
                    info.Version = Str(r, ref p);
                }
                return info;
            }
            catch { }
        }
        return new A2SInfo { Online = false };
    }

    public static List<A2SPlayer> Players(string host, int port, int timeoutMs)
    {
        try
        {
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = timeoutMs;
            udp.Connect(host, port);
            var r = Ask(udp, Build(0x55, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }), c => Build(0x55, c), out _);
            if (r.Length < 6 || r[4] != 0x44) return null;
            var list = new List<A2SPlayer>();
            int p = 6;
            for (int i = 0; i < r[5] && p < r.Length; i++)
            {
                p++;
                var pl = new A2SPlayer { Name = Str(r, ref p) };
                if (p + 8 <= r.Length) pl.Minutes = (int)(BitConverter.ToSingle(r, p + 4) / 60);
                p += 8;
                list.Add(pl);
            }
            return list;
        }
        catch { return null; }
    }
}
