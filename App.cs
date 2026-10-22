using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ReforgerLauncher;

// Everything the window works on: the game's presets and downloaded mods, the server's addons\ and the unpublished
// mod-src\ projects, this folder's server configs, every scenario on this PC, and launcher.json.
public class App
{
    public const string Title = "Reforger Launcher";
    public const string DefaultScenarioId = "{ECC61978EDCC2B5A}Missions/23_Campaign.conf";

    // The base game's scenarios: ID, name, players, mode (ArmaReforgerServer -listScenarios and the mission headers in
    // data007.pak, game 1.8), in the order the scenario list shows them.
    public static readonly (string Id, string Name, int Players, string Mode)[] Official =
    {
        ("{ECC61978EDCC2B5A}Missions/23_Campaign.conf", "Conflict - Everon", 64, "Conflict"),
        ("{C700DB41F0C546E1}Missions/23_Campaign_NorthCentral.conf", "Conflict - Northern Everon", 48, "Conflict"),
        ("{28802845ADA64D52}Missions/23_Campaign_SWCoast.conf", "Conflict - Southern Everon", 48, "Conflict"),
        ("{94992A3D7CE4FF8A}Missions/23_Campaign_Western.conf", "Conflict - Western Everon", 48, "Conflict"),
        ("{FDE33AFE2ED7875B}Missions/23_Campaign_Montignac.conf", "Conflict - Montignac", 48, "Conflict"),
        ("{C41618FD18E9D714}Missions/23_Campaign_Arland.conf", "Conflict - Arland", 64, "Conflict"),
        ("{0220741028718E7F}Missions/23_Campaign_HQC_Everon.conf", "Conflict: HQ Commander - Everon", 64, "HQ Commander"),
        ("{68D1240A11492545}Missions/23_Campaign_HQC_Arland.conf", "Conflict: HQ Commander - Arland", 64, "HQ Commander"),
        ("{BB5345C22DD2B655}Missions/23_Campaign_HQC_Cain.conf", "Conflict: HQ Commander - Kolguyev", 64, "HQ Commander"),
        ("{59AD59368755F41A}Missions/21_GM_Eden.conf", "Game Master - Everon", 64, "Game Master"),
        ("{2BBBE828037C6F4B}Missions/22_GM_Arland.conf", "Game Master - Arland", 64, "Game Master"),
        ("{F45C6C15D31252E6}Missions/27_GM_Cain.conf", "Game Master - Kolguyev", 64, "Game Master"),
        ("{DFAC5FABD11F2390}Missions/26_CombatOpsEveron.conf", "Combat Ops - Everon", 8, "Combat Ops"),
        ("{DAA03C6E6099D50F}Missions/24_CombatOps.conf", "Combat Ops - Arland", 6, "Combat Ops"),
        ("{CB347F2F10065C9C}Missions/CombatOpsCain.conf", "Combat Ops - Kolguyev", 12, "Combat Ops"),
        ("{3F2E005F43DBD2F8}Missions/CAH_Briars_Coast.conf", "Capture & Hold - Briars", 64, "Capture & Hold"),
        ("{F1A1BEA67132113E}Missions/CAH_Castle.conf", "Capture & Hold - Montfort Castle", 24, "Capture & Hold"),
        ("{589945FB9FA7B97D}Missions/CAH_Concrete_Plant.conf", "Capture & Hold - Concrete Plant", 24, "Capture & Hold"),
        ("{9405201CBD22A30C}Missions/CAH_Factory.conf", "Capture & Hold - Almara Factory", 24, "Capture & Hold"),
        ("{1CD06B409C6FAE56}Missions/CAH_Forest.conf", "Capture & Hold - Simon's Wood", 32, "Capture & Hold"),
        ("{7C491B1FCC0FF0E1}Missions/CAH_LeMoule.conf", "Capture & Hold - Le Moule", 32, "Capture & Hold"),
        ("{6EA2E454519E5869}Missions/CAH_Military_Base.conf", "Capture & Hold - Camp Blake", 32, "Capture & Hold"),
        ("{2B4183DF23E88249}Missions/CAH_Morton.conf", "Capture & Hold - Morton", 24, "Capture & Hold"),
        ("{002AF7323E0129AF}Missions/Tutorial.conf", "Training", 1, "Single-player"),
        ("{C47A1A6245A13B26}Missions/SP01_ReginaV2.conf", "Elimination", 1, "Single-player"),
        ("{0648CDB32D6B02B3}Missions/SP02_AirSupport.conf", "Air Support", 1, "Single-player"),
        ("{10B8582BAD9F7040}Missions/Scenario01_Intro.conf", "Omega 01: Over The Hills And Far Away", 1, "Single-player"),
        ("{1D76AF6DC4DF0577}Missions/Scenario02_Steal.conf", "Omega 02: Radio Check", 1, "Single-player"),
        ("{D1647575BCEA5A05}Missions/Scenario03_Villa.conf", "Omega 03: Light In The Dark", 1, "Single-player"),
        ("{6D224A109B973DD8}Missions/Scenario04_Sabotage.conf", "Omega 04: Red Silence", 1, "Single-player"),
        ("{FA2AB0181129CB16}Missions/Scenario05_Hill.conf", "Omega 05: Cliffhanger", 1, "Single-player"),
    };

    // Settings for a new server config when this folder has no config.json to copy them from.
    const string ConfigTemplate = @"{
  ""bindAddress"": """", ""bindPort"": 2001, ""publicAddress"": """", ""publicPort"": 2001,
  ""a2s"": { ""address"": ""0.0.0.0"", ""port"": 17777 },
  ""game"": {
    ""name"": ""Arma Reforger server"", ""password"": """", ""passwordAdmin"": """", ""admins"": [],
    ""scenarioId"": ""{ECC61978EDCC2B5A}Missions/23_Campaign.conf"", ""maxPlayers"": 32, ""visible"": true,
    ""crossPlatform"": false, ""supportedPlatforms"": [ ""PLATFORM_PC"" ],
    ""gameProperties"": {
      ""serverMaxViewDistance"": 2500, ""networkViewDistance"": 1500, ""disableThirdPerson"": false, ""fastValidation"": true,
      ""battlEye"": true, ""VONDisableUI"": false, ""VONDisableDirectSpeechUI"": false, ""VONCanTransmitCrossFaction"": false
    },
    ""mods"": []
  },
  ""operating"": { ""lobbyPlayerSynchronise"": true, ""playerSaveTime"": 120, ""aiLimit"": -1, ""slotReservationTimeout"": 60 }
}";

    public readonly Options Opt;
    public string Root => Opt.Root;
    public string BackupDir => Path.Combine(Root, "mod-presets-backups");

    public GameSettings Doc;
    public Dictionary<string, ModInfo> Installed;
    public string AddonsDir;
    public Dictionary<string, ModInfo> ServerMods, ProjectMods, ExtraMods;
    public List<ConfigItem> Configs = new();
    public List<string> BadConfigs = new();
    public List<string> DeletedConfigs = new();
    public bool PresetsDirty;
    public List<Scenario> Scenarios = new();
    public string ServerDir, GameDir;
    public LauncherStore Data;
    public readonly Workshop Web = new();

    public App(Options opt) { Opt = opt; Web.Offline = opt.Offline; }

    public string DocsDir => Path.GetDirectoryName(AddonsDir);

    // Everything read from disk again (the window's Reload).
    public void Load()
    {
        string docs = null;
        var settings = Opt.SettingsFile;
        if (string.IsNullOrEmpty(settings)) { docs = Game.FindDocs(); settings = Game.FindSettingsFile(docs); }
        settings = Path.GetFullPath(settings);
        AddonsDir = Opt.AddonsDir;
        if (string.IsNullOrEmpty(AddonsDir)) AddonsDir = Path.Combine(docs ?? Game.FindDocs(), "addons");
        Installed = Mods.Installed(AddonsDir);
        foreach (var g in Mods.Enabled(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(settings)), "workshop")))
            if (Installed.TryGetValue(g, out var m)) m.Enabled = true;
        Doc = GameSettings.Read(settings);

        ServerMods = Mods.Installed(Path.Combine(Root, "addons"), "Server");
        ProjectMods = ReadProjectMods(Path.Combine(Root, "mod-src"));
        ExtraMods = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in ServerMods.Values) ExtraMods[m.Guid] = m;
        foreach (var m in ProjectMods.Values) ExtraMods[m.Guid] = m;
        ReadConfigs();
        DeletedConfigs.Clear();
        PresetsDirty = false;
        Scenarios = ScenarioIndex();
        ServerDir = FindServerDir();
        GameDir = FindSteamApp("Arma Reforger", "ArmaReforgerSteam.exe");
        Data = LauncherStore.Read(Path.Combine(Root, "launcher.json"));
    }

    public IEnumerable<ModList> AllItems() => Doc.Presets.Cast<ModList>().Concat(Configs);
    public bool Dirty => PresetsDirty || DeletedConfigs.Count > 0 || AllItems().Any(i => i.Changed);

    // ---------------------------------------------------------------------------------------------------------------
    // Finding the game, the server and the mods on this PC

    public static string FindSteamApp(string folder, string exe)
    {
        var libraries = new List<string>();
        var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
        if (!string.IsNullOrEmpty(steam))
        {
            libraries.Add(steam);
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libraries.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        }
        libraries.Add(@"C:\Program Files (x86)\Steam");
        foreach (var lib in libraries)
        {
            var dir = Path.Combine(lib, "steamapps", "common", folder);
            if (File.Exists(Path.Combine(dir, exe))) return Path.GetFullPath(dir);
        }
        return null;
    }

    string FindServerDir()
    {
        var bat = Path.Combine(Root, "start-server.bat");
        if (File.Exists(bat))
        {
            var m = Regex.Match(File.ReadAllText(bat), "set \"SERVER_DIR=([^\"]+)\"");
            if (m.Success && File.Exists(Path.Combine(m.Groups[1].Value, "ArmaReforgerServer.exe"))) return m.Groups[1].Value;
        }
        return FindSteamApp("Arma Reforger Server", "ArmaReforgerServer.exe");
    }

    // Unpublished mods in mod-src\<project>\ (addon.gproj + Missions\*.conf.meta), tagged Location "Project".
    static Dictionary<string, ModInfo> ReadProjectMods(string dir)
    {
        var mods = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(dir)) return mods;
        foreach (var d in new DirectoryInfo(dir).GetDirectories())
        {
            var gproj = d.GetFiles("*.gproj").FirstOrDefault();
            if (gproj == null) continue;
            var text = File.ReadAllText(gproj.FullName);
            var guid = Regex.Match(text, "(?m)^\\s*GUID\\s+\"([0-9A-Fa-f]{16})\"").Groups[1].Value;
            if (guid.Length == 0) continue;
            var title = Regex.Match(text, "(?m)^\\s*TITLE\\s+\"([^\"]*)\"").Groups[1].Value;
            if (title.Length == 0) title = d.Name;
            var info = Mods.NewInfo(guid, title, "", 0, null, null);
            var depBlock = Regex.Match(text, "Dependencies\\s*\\{([^}]*)\\}").Groups[1].Value;
            foreach (Match m in Regex.Matches(depBlock, "\"([0-9A-Fa-f]{16})\""))
                if (!m.Groups[1].Value.Equals(Game.BaseGameGuid, StringComparison.OrdinalIgnoreCase))
                    info.Deps.Add(new ModDep { Guid = m.Groups[1].Value.ToUpperInvariant(), Name = "" });
            var missions = new DirectoryInfo(Path.Combine(d.FullName, "Missions"));
            if (missions.Exists)
                foreach (var meta in missions.GetFiles("*.conf.meta"))
                {
                    var id = Regex.Match(File.ReadAllText(meta.FullName), "Name\\s+\"(\\{[0-9A-Fa-f]{16}\\}[^\"]+)\"").Groups[1].Value;
                    if (id.Length == 0) continue;
                    var confPath = meta.FullName[..^5];
                    var conf = File.Exists(confPath) ? File.ReadAllText(confPath) : "";
                    int.TryParse(Regex.Match(conf, "m_iPlayerCount\\s+(\\d+)").Groups[1].Value, out int players);
                    info.Scenarios.Add(new ScenarioMeta
                    {
                        Id = id, Name = Regex.Match(conf, "m_sName\\s+\"([^\"]*)\"").Groups[1].Value, Players = players,
                        Description = Regex.Match(conf, "m_sDescription\\s+\"([^\"]*)\"").Groups[1].Value,
                    });
                }
            info.Location = "Project"; info.Installed = false; info.Folder = d.FullName;
            mods[info.Guid] = info;
        }
        return mods;
    }

    // A mode for mod scenarios, guessed from the name (mod metadata has none).
    static string GuessMode(string title, int players)
    {
        if (players == 1) return "Single-player";
        foreach (var (rx, mode) in new[] {
            ("HQ Commander", "HQ Commander"), (@"Game ?Master|\bGM\b", "Game Master"), ("Combat ?Ops", "Combat Ops"), ("Capture", "Capture & Hold"),
            ("Warzone", "Warzone"), ("PvE|Co-?op", "PvE"), ("Conflict", "Conflict"), ("Training|Tutorial", "Training") })
            if (Regex.IsMatch(title, rx, RegexOptions.IgnoreCase)) return mode;
        return "";
    }

    // Every scenario known on this PC: base game, then those inside downloaded mods and mod-src projects.
    List<Scenario> ScenarioIndex()
    {
        var list = new List<Scenario>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in Official)
            if (seen.Add(o.Id)) list.Add(new Scenario { Id = o.Id, Name = o.Name, ModName = "Base game", Order = list.Count, Players = o.Players, Mode = o.Mode });
        foreach (var source in new[] { ProjectMods, Installed, ServerMods })
            foreach (var m in source.Values)
                foreach (var s in m.Scenarios)
                {
                    if (!seen.Add(s.Id)) continue;
                    var title = s.Name;
                    if (string.IsNullOrEmpty(title) || title.StartsWith("#")) title = Regex.Replace(Regex.Replace(s.Id, "^.*/", ""), @"\.conf$", "");
                    var desc = !string.IsNullOrEmpty(s.Description) && !s.Description.StartsWith("#") ? s.Description : "";
                    list.Add(new Scenario { Id = s.Id, Name = title, ModGuid = m.Guid, ModName = m.Name, Order = 1000, Players = s.Players, Mode = GuessMode(title, s.Players), Description = desc, Folder = m.Folder });
                }
        return list;
    }

    public Scenario FindScenario(string id) => Scenarios.FirstOrDefault(s => s.Id == id);

    // ---------------------------------------------------------------------------------------------------------------
    // Server configs (config*.json here)

    public static ConfigItem NewConfigItem(string fileName, JsonObject cfg, string path)
    {
        var item = new ConfigItem { Name = fileName, Config = cfg, Path = path };
        if (Json.Get(cfg, "game.mods") is JsonArray mods)
            foreach (var m in mods)
            {
                var id = Json.Str(m, "modId");
                if (id.Length == 0) continue;
                item.Mods.Add(id, Json.Str(m, "name"));
                var v = Json.Str(m, "version");
                if (v.Length > 0) item.Versions[id.ToUpperInvariant()] = v;
            }
        return item;
    }

    void ReadConfigs()
    {
        Configs = new List<ConfigItem>();
        BadConfigs = new List<string>();
        foreach (var f in new DirectoryInfo(Root).GetFiles("config*.json").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            JsonObject cfg;
            try { cfg = Json.Parse(File.ReadAllText(f.FullName)) as JsonObject; }
            catch (Exception ex) { BadConfigs.Add($"{f.Name}: {ex.Message}"); continue; }
            if (cfg == null || cfg["game"] is not JsonObject) { BadConfigs.Add($"{f.Name}: no \"game\" section, so it isn't a server config"); continue; }
            Configs.Add(NewConfigItem(f.Name, cfg, f.FullName));
        }
    }

    // Settings for a new config: this folder's config.json without its mods, or the built-in template.
    public JsonObject NewConfigSettings()
    {
        var live = Configs.FirstOrDefault(c => c.Name == "config.json");
        var cfg = live != null ? Json.Clone(live.Config) : (JsonObject)Json.Parse(ConfigTemplate);
        Json.Section(cfg, "game")["mods"] = new JsonArray();
        return cfg;
    }

    public void SaveConfig(ConfigItem item)
    {
        var mods = new JsonArray();
        foreach (var e in item.Mods)
        {
            var o = new JsonObject { ["modId"] = e.Key, ["name"] = e.Value ?? "" };
            if (item.Versions.TryGetValue(e.Key, out var v) && !string.IsNullOrEmpty(v)) o["version"] = v;
            mods.Add(o);
        }
        Json.Section(item.Config, "game")["mods"] = mods;
        var target = Path.Combine(Root, item.Name);
        if (!string.IsNullOrEmpty(item.Path) && !item.Path.Equals(target, StringComparison.OrdinalIgnoreCase) && File.Exists(item.Path))
            File.Move(item.Path, target);
        File.WriteAllText(target, Json.Write(item.Config) + "\r\n", new UTF8Encoding(false));
        item.Path = target;
        item.Changed = false;
    }

    public static string ConfigFileName(string text)
    {
        var slug = Regex.Replace(Regex.Replace(Regex.Replace(text.Trim().ToLowerInvariant(), @"^config\.", ""), @"\.json$", ""), "[^a-z0-9_-]+", "-").Trim('-');
        return slug.Length == 0 ? null : $"config.{slug}.json";
    }

    public string UniqueConfigName(string text)
    {
        var file = ConfigFileName(text) ?? "config.server.json";
        var stem = Regex.Replace(file, @"\.json$", "");
        int n = 2;
        while (File.Exists(Path.Combine(Root, file)) || Configs.Any(c => c.Name == file)) { file = $"{stem}-{n}.json"; n++; }
        return file;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Mods: info from anywhere, dependencies, lists

    public ModInfo ModInfoFor(string guid)
    {
        if (Installed.TryGetValue(guid, out var info)) return info;
        if (ExtraMods.TryGetValue(guid, out info)) return info;
        return Web.Lookup(guid);
    }

    public string ModName(string guid)
    {
        if (Installed.TryGetValue(guid, out var i)) return i.Name;
        if (ExtraMods.TryGetValue(guid, out i)) return i.Name;
        return null;
    }

    // Adds every missing dependency (recursively) of the list's mods to the list.
    public (List<string> Added, List<string> Unknown) AddDependencies(ModList list)
    {
        var added = new List<string>();
        var unknown = new List<string>();
        var seen = new HashSet<string>(list.Mods.Keys, StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(list.Mods.Keys);
        while (queue.Count > 0)
        {
            var g = queue.Dequeue();
            var info = ModInfoFor(g);
            if (info == null) { unknown.Add(g); continue; }
            foreach (var d in info.Deps)
            {
                if (d.Guid.Equals(Game.BaseGameGuid, StringComparison.OrdinalIgnoreCase) || !seen.Add(d.Guid)) continue;
                var name = ModName(d.Guid) ?? d.Name;
                if (string.IsNullOrEmpty(name)) name = d.Guid;
                list.Mods.Add(d.Guid, name);
                added.Add(d.Guid);
                queue.Enqueue(d.Guid);
            }
        }
        if (added.Count > 0) list.Changed = true;
        return (added, unknown);
    }

    public class ListResult { public List<string> Added = new(); public int AlreadyIn; public List<string> Unmatched = new(); }

    public ListResult AddList(ModList list, List<Mods.ListEntry> entries)
    {
        var r = new ListResult();
        foreach (var e in entries)
        {
            var guid = e.Guid; var name = e.Name;
            if (string.IsNullOrEmpty(guid))
            {
                var exact = Installed.Values.Where(m => m.Name == e.Query).ToList();
                var hits = exact.Count > 0 ? exact : Installed.Values.Where(m => m.Name.Contains(e.Query, StringComparison.OrdinalIgnoreCase)).ToList();
                if (hits.Count != 1)
                {
                    r.Unmatched.Add(hits.Count > 0 ? $"{e.Query}  (matches {hits.Count} downloaded mods - use the GUID)" : $"{e.Query}  (no downloaded mod has that name)");
                    continue;
                }
                guid = hits[0].Guid;
            }
            Installed.TryGetValue(guid, out var info);
            if (info == null) ExtraMods.TryGetValue(guid, out info);
            if (info == null && string.IsNullOrEmpty(name)) info = Web.Lookup(guid);
            if (info != null && !string.IsNullOrEmpty(info.Name)) name = info.Name;
            if (string.IsNullOrEmpty(name)) name = guid;
            if (list.Mods.Contains(guid)) r.AlreadyIn++;
            else { list.Mods.Add(guid, name); r.Added.Add(guid.ToUpperInvariant()); }
        }
        if (r.Added.Count > 0) list.Changed = true;
        return r;
    }

    // Missing = what the list can't use: a game preset needs the game's own download, a server config anything on disk.
    public bool IsMissing(ModList list, string guid)
    {
        if (Installed.ContainsKey(guid)) return false;
        if (list != null && list.IsConfig && ExtraMods.ContainsKey(guid)) return false;
        return true;
    }

    public List<string> MissingOf(ModList list) => list.Mods.Keys.Where(g => IsMissing(list, g)).ToList();

    public long SizeOf(ModList list)
    {
        long total = 0;
        foreach (var g in list.Mods.Keys)
        {
            if (Installed.TryGetValue(g, out var info) || (list.IsConfig && ExtraMods.TryGetValue(g, out info))) total += info.Size;
        }
        return total;
    }

    // The mods a scenario is launched with: the mod list's, the scenario's own mod, and every dependency.
    public Preset LaunchMods(ModList list, Scenario scenario)
    {
        var set = new Preset("launch");
        if (list != null) foreach (var e in list.Mods) set.Mods.Add(e.Key, e.Value);
        if (!string.IsNullOrEmpty(scenario.ModGuid)) set.Mods.Add(scenario.ModGuid, scenario.ModName);
        AddDependencies(set);
        return set;
    }

    public Preset RequiredMods(Scenario scenario)
    {
        var req = new Preset("required");
        if (scenario != null && !string.IsNullOrEmpty(scenario.ModGuid))
        {
            req.Mods.Add(scenario.ModGuid, scenario.ModName);
            AddDependencies(req);
        }
        return req;
    }
}
