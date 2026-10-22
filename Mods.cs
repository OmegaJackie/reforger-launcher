using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ReforgerLauncher;

public static class Game
{
    public const string AppId = "1874880";
    public const string BaseGameGuid = "58D0FB3206B6F859";   // Arma Reforger itself; project files list it as a dependency
    public const string WorkshopUrl = "https://reforger.armaplatform.com/workshop/";

    // The running game (not the server or Workbench).
    public static List<Process> Running() =>
        Process.GetProcesses().Where(p => p.ProcessName.StartsWith("ArmaReforger", StringComparison.OrdinalIgnoreCase)
            && !Regex.IsMatch(p.ProcessName, "Server|Workbench", RegexOptions.IgnoreCase)).ToList();

    public static void AssertClosed()
    {
        var r = Running();
        if (r.Count > 0)
            throw new Exception($"Arma Reforger is running ({r[0].ProcessName}). Close it first - it rewrites ReforgerGameSettings.conf when it exits and would undo the change.");
    }

    public static string FindDocs()
    {
        var candidates = new List<string>
        {
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "ArmaReforger"),
            System.IO.Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE") ?? "", "Documents", "My Games", "ArmaReforger"),
        };
        var od = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrEmpty(od)) candidates.Add(System.IO.Path.Combine(od, "Documents", "My Games", "ArmaReforger"));
        foreach (var c in candidates) if (Directory.Exists(System.IO.Path.Combine(c, "profile"))) return c;
        throw new Exception("Arma Reforger's Documents folder (My Games\\ArmaReforger) wasn't found. Start the game once, or pass --settings and --addons.");
    }

    public static string FindSettingsFile(string docs)
    {
        var save = System.IO.Path.Combine(docs, "profile", ".save");
        var files = Directory.Exists(save)
            ? new DirectoryInfo(save).GetDirectories($"app{AppId}_user*")
                .Select(d => new FileInfo(System.IO.Path.Combine(d.FullName, "settings", "ReforgerGameSettings.conf")))
                .Where(f => f.Exists).OrderByDescending(f => f.LastWriteTime).ToList()
            : new List<FileInfo>();
        if (files.Count == 0) throw new Exception($"ReforgerGameSettings.conf wasn't found under {docs}\\profile\\.save. Start the game once so it creates it.");
        return files[0].FullName;
    }
}

// Mods on disk, the game's enabled flags, the workshop website, dependencies, mod lists in and out.
public static class Mods
{
    static readonly Regex GuidRx = new("^[0-9A-F]{16}$");

    public static ModInfo NewInfo(string guid, string name, string version, long size, JsonNode deps, JsonNode scenarios)
    {
        var info = new ModInfo { Guid = (guid ?? "").ToUpperInvariant(), Name = name ?? "", Version = version ?? "", Size = size };
        if (deps is JsonArray da)
            foreach (var d in da)
            {
                var id = Json.Str(d, "assetId");
                if (id.Length > 0 && !id.Equals(Game.BaseGameGuid, StringComparison.OrdinalIgnoreCase))
                    info.Deps.Add(new ModDep { Guid = id.ToUpperInvariant(), Name = Json.Str(d, "assetName") });
            }
        if (scenarios is JsonArray sa)
            foreach (var s in sa)
            {
                var id = Json.Str(s, "gameId");
                if (id.Length > 0)
                    info.Scenarios.Add(new ScenarioMeta { Id = id, Name = Json.Str(s, "name"), Players = (int)Json.Long(s, "playerCount"), Description = Json.Str(s, "description") });
            }
        return info;
    }

    // Downloaded mods in an addons folder by GUID; location tags where they are: Game (the client's downloads) or Server.
    public static Dictionary<string, ModInfo> Installed(string dir, string location = "Game")
    {
        var mods = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(dir)) return mods;
        foreach (var d in new DirectoryInfo(dir).GetDirectories())
        {
            ModInfo info = null;
            var metaPath = System.IO.Path.Combine(d.FullName, "meta");
            if (File.Exists(metaPath))
            {
                try
                {
                    var m = Json.Get(Json.Parse(File.ReadAllText(metaPath)), "meta");
                    var versions = m?["versions"] as JsonArray;
                    JsonNode v = null;
                    if (versions != null && versions.Count > 0)
                        v = versions[(int)Math.Min(Math.Max(Json.Long(m, "selectedRev"), 0), versions.Count - 1)];
                    info = NewInfo(Json.Str(m, "id"), Json.Str(m, "name"), Json.Str(v, "version"), Json.Long(v, "package.totalSize"), v?["dependencies"], v?["scenarios"]);
                    long last = Json.Long(m, "timeLastPlay");
                    if (last > 0) info.LastPlayed = DateTimeOffset.FromUnixTimeSeconds(last).LocalDateTime;
                }
                catch { info = null; }
            }
            if (info == null)
            {
                var sd = System.IO.Path.Combine(d.FullName, "ServerData.json");
                if (File.Exists(sd))
                {
                    try
                    {
                        var s = Json.Parse(File.ReadAllText(sd));
                        info = NewInfo(Json.Str(s, "id"), Json.Str(s, "name"), Json.Str(s, "revision.version"), 0, Json.Get(s, "revision.dependencies"), Json.Get(s, "revision.scenarios"));
                    }
                    catch { info = null; }
                }
            }
            if (info == null)
            {
                var m = Regex.Match(d.Name, "^(.+)_([0-9A-Fa-f]{16})$");
                if (m.Success) info = NewInfo(m.Groups[2].Value, m.Groups[1].Value, "", 0, null, null);
            }
            if (info != null && GuidRx.IsMatch(info.Guid))
            {
                info.Folder = d.FullName;
                info.Location = location;
                info.Installed = location == "Game";
                mods[info.Guid] = info;
            }
        }
        return mods;
    }

    // GUIDs the game has enabled now (workshop\I<GUID>.json next to the settings folder).
    public static HashSet<string> Enabled(string workshopDir)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(workshopDir)) return set;
        foreach (var f in new DirectoryInfo(workshopDir).GetFiles("I*.json"))
        {
            var m = Regex.Match(System.IO.Path.GetFileNameWithoutExtension(f.Name), "^I([0-9A-Fa-f]{16})$");
            if (!m.Success) continue;
            try { if (Regex.IsMatch(File.ReadAllText(f.FullName), "\"m_bIsEnabled\"\\s*:\\s*true")) set.Add(m.Groups[1].Value); } catch { }
        }
        return set;
    }

    public static string FormatSize(double bytes)
    {
        if (bytes <= 0) return "";
        if (bytes >= 1L << 30) return $"{bytes / (1L << 30):N1} GB";
        if (bytes >= 1L << 20) return $"{bytes / (1L << 20):N0} MB";
        return $"{Math.Max(1, bytes / 1024):N0} KB";
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Mod lists in and out

    public class ListEntry { public string Guid = ""; public string Name = ""; public string Query = ""; }

    // A server config, the game's own JSON or "-addons" export, workshop links, GUIDs, "GUID name" lines or names.
    public static List<ListEntry> ParseList(string text)
    {
        var found = new List<ListEntry>();
        var objects = Regex.Matches(text, "\\{[^{}]*\"modId\"\\s*:\\s*\"([0-9A-Fa-f]{16})\"[^{}]*\\}");
        if (objects.Count > 0)
        {
            foreach (Match o in objects)
            {
                var n = Regex.Match(o.Value, "\"name\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                string name = "";
                if (n.Success) { try { name = Regex.Unescape(n.Groups[1].Value); } catch { name = n.Groups[1].Value; } }
                found.Add(new ListEntry { Guid = o.Groups[1].Value.ToUpperInvariant(), Name = name });
            }
            return found;
        }
        foreach (var raw in Regex.Split(text, "\r?\n"))
        {
            var line = Regex.Replace(raw, @"^\s*(#|//|;).*$", "").Trim();
            if (line.Length == 0) continue;
            var guids = Regex.Matches(line, "(?<![0-9A-Fa-f])[0-9A-Fa-f]{16}(?![0-9A-Fa-f])");
            if (guids.Count == 1)
            {
                string name;
                var w = Regex.Match(line, "workshop/[0-9A-Fa-f]{16}-([^/?#\\s]+)");
                if (w.Success) name = Uri.UnescapeDataString(w.Groups[1].Value).Replace('-', ' ');
                else if (Regex.IsMatch(line, "https?://")) name = "";
                else name = line.Remove(guids[0].Index, 16);
                name = name.Trim(" \t-:,;|=()[]{}\"'".ToCharArray());
                found.Add(new ListEntry { Guid = guids[0].Value.ToUpperInvariant(), Name = name });
            }
            else if (guids.Count > 1)
            {
                foreach (Match g in guids) found.Add(new ListEntry { Guid = g.Value.ToUpperInvariant() });
            }
            else found.Add(new ListEntry { Query = line });
        }
        return found;
    }

    public static string ListText(ModList list)
    {
        var sb = new StringBuilder();
        sb.Append($"# Arma Reforger mod preset: {list.Name} ({list.Mods.Count} mods)\r\n");
        sb.Append("# Add it to a mod list with MODS > Add from list in Reforger Launcher\r\n\r\n");
        foreach (var e in list.Mods) sb.Append($"{e.Key}  {e.Value}\r\n");
        return sb.ToString();
    }

    public static string ServerModsJson(ModList list)
    {
        var entries = list.Mods.Select(e => $"    {{ \"modId\": \"{e.Key}\", \"name\": {Json.Quote(e.Value)} }}");
        return "[\r\n" + string.Join(",\r\n", entries) + "\r\n]\r\n";
    }
}

// Mod names, versions and dependencies from the workshop website (reforger.armaplatform.com/workshop/<GUID> embeds
// __NEXT_DATA__ JSON), cached for the session.
public class Workshop
{
    readonly Dictionary<string, ModInfo> cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    public bool Offline;
    public Action<string> OnLookup;

    public bool TryCached(string guid, out ModInfo info) => cache.TryGetValue(guid, out info);

    public ModInfo Lookup(string guid)
    {
        var g = guid.ToUpperInvariant();
        if (cache.TryGetValue(g, out var hit)) return hit;
        ModInfo info = null;
        if (!Offline)
        {
            OnLookup?.Invoke(g);
            try
            {
                var html = http.GetStringAsync(Game.WorkshopUrl + g).GetAwaiter().GetResult();
                var m = Regex.Match(html, "<script id=\"__NEXT_DATA__\"[^>]*>(.*?)</script>", RegexOptions.Singleline);
                if (m.Success)
                {
                    var a = Json.Get(Json.Parse(m.Groups[1].Value), "props.pageProps.asset");
                    if (a != null && Json.Str(a, "id").Length > 0)
                    {
                        info = Mods.NewInfo(Json.Str(a, "id"), Json.Str(a, "name"), Json.Str(a, "currentVersionNumber"), Json.Long(a, "currentVersionSize"), null, null);
                        info.Installed = false;
                        if (a["dependencies"] is JsonArray deps)
                            foreach (var d in deps)
                            {
                                var id = Json.Str(d, "asset.id");
                                if (id.Length > 0) info.Deps.Add(new ModDep { Guid = id.ToUpperInvariant(), Name = Json.Str(d, "asset.name") });
                            }
                    }
                }
            }
            catch { info = null; }
        }
        cache[g] = info;
        return info;
    }
}
