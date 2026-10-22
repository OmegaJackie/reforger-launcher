using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ReforgerLauncher;

// One setting of a server config for its Configuration tab. P = path in the config, L = label, T = editor type, H = help,
// U = unit, W = field width, Min/Max = number range, MinLen/MaxLen = text length, Re/ReText = text pattern and how to
// say it, Item = list item pattern, On/Off = what true/false read as, Obj = JSON must be an object. S = a section heading.
public class Setting
{
    public string S, P, L, T, H, U, Re, ReText, Item, On, Off;
    public int W, MinLen, MaxLen, MaxItems;
    public double? Min, Max;
    public string[] Values;
    public bool Obj;
}

// The settings, ranges and wording follow the dedicated server's own schema (backend/DSConfigSchema.json in its
// addons\core\data.pak, 1.8). A setting missing from the file shows "not set" (the server uses its default) and is only
// written once changed; emptying a field removes it again.
public static class ServerSettings
{
    const string Ip4 = @"^\d{1,3}(\.\d{1,3}){3}$";
    const string GP = "game.gameProperties";

    public static readonly Setting[] All =
    {
        new() { S = "SERVER" },
        new() { P = "game.name", L = "Server name", T = "text", MinLen = 1, MaxLen = 100, W = 400, H = "As listed in the server browser, 1-100 characters (the game's own Host dialog wants at least 10)." },
        new() { P = "game.scenarioId", L = "Scenario", T = "scenario", H = "Picking one adds its mod and that mod's dependencies to the mod list." },
        new() { P = "game.maxPlayers", L = "Max players", T = "int", Min = 1, Max = 128 },
        new() { P = "game.visible", L = "Server browser", T = "bool", On = "Listed", Off = "Hidden" },
        new() { P = "game.password", L = "Password", T = "secret", MaxLen = 32, H = "Needed to join; empty = anyone can join. No spaces, up to 32 characters." },
        new() { P = "game.passwordAdmin", L = "Admin password", T = "secret", MaxLen = 32, H = "Logs a player in as admin in game. No spaces, up to 32 characters." },
        new() { P = "game.admins", L = "Admins", T = "list", W = 400, MaxItems = 50, Item = "^([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|[0-9]{17})$",
            H = "Always admins: identity IDs (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx, lower case) or 17-digit SteamIDs, comma separated, up to 50." },
        new() { P = "game.crossPlatform", L = "Crossplay", T = "bool", H = "Enabled adds every platform the server can support to Platforms." },
        new() { P = "game.supportedPlatforms", L = "Platforms", T = "platforms", H = "The game clients that can join." },
        new() { S = "NETWORK" },
        new() { P = "publicAddress", L = "Public address", T = "text", Re = @"^(local|\d{1,3}(\.\d{1,3}){3})$", ReText = "an IPv4 address or local",
            H = "The IP players connect to, as registered in the server browser. Not set = detected; local = Host LAN." },
        new() { P = "publicPort", L = "Public port", T = "int", Min = 1, Max = 65535, H = "The UDP port registered in the server browser (the one forwarded in the router)." },
        new() { P = "bindAddress", L = "Bind address", T = "text", Re = Ip4, ReText = "an IPv4 address", H = "The network interface the server listens on. Not set = all." },
        new() { P = "bindPort", L = "Bind port", T = "int", Min = 1, Max = 65535, H = "The UDP port the server listens on." },
        new() { P = "a2s.address", L = "A2S address", T = "text", Re = Ip4, ReText = "an IPv4 address", H = "Steam server queries (server lists, BattleMetrics): the interface, 0.0.0.0 = all. A2S needs it." },
        new() { P = "a2s.port", L = "A2S port", T = "int", Min = 1, Max = 65535, H = "The UDP port for Steam server queries." },
        new() { S = "GAMEPLAY" },
        new() { P = $"{GP}.disableThirdPerson", L = "Third person", T = "thirdperson" },
        new() { P = $"{GP}.serverMaxViewDistance", L = "Max draw distance", T = "num", Min = 500, Max = 12000, U = "m", H = "The most clients can set." },
        new() { P = $"{GP}.networkViewDistance", L = "Network view distance", T = "num", Min = 500, Max = 5000, U = "m", H = "How far replicated entities (players, AI, vehicles) stream to clients." },
        new() { P = $"{GP}.serverMinGrassDistance", L = "Min grass distance", T = "num", Min = 50, Max = 150, U = "m", H = "Clients draw grass at least this far. Not set = not forced." },
        new() { P = $"{GP}.fastValidation", L = "Fast validation", T = "bool" },
        new() { P = $"{GP}.battlEye", L = "BattlEye", T = "bool", H = "Anti-cheat." },
        new() { P = $"{GP}.VONDisableUI", L = "Hide VON UI", T = "bool", On = "Yes", Off = "No", H = "Clients get no voice (radio) UI." },
        new() { P = $"{GP}.VONDisableDirectSpeechUI", L = "Hide direct speech UI", T = "bool", On = "Yes", Off = "No", H = "Clients get no direct speech UI." },
        new() { P = $"{GP}.VONCanTransmitCrossFaction", L = "Enemy radios", T = "bool", On = "Can transmit", Off = "Listen only", H = "Whether players can transmit on other factions' radios." },
        new() { P = $"{GP}.missionHeader", L = "Mission header", T = "json", Obj = true, W = 400, H = "Overrides of the scenario's mission header values, as JSON, e.g. {\"m_iPlayerCount\": 40}" },
        new() { S = "SAVES (PERSISTENCE)" },
        new() { P = $"{GP}.persistence.loadSessionSave", L = "Continue from last save", T = "bool", On = "Yes", Off = "No", H = "On start, load the scenario's latest save." },
        new() { P = $"{GP}.persistence.keepSessionSave", L = "Keep finished saves", T = "bool", On = "Yes", Off = "No", H = "Keep the save of a playthrough that reached the end screen." },
        new() { P = $"{GP}.persistence.autoSaveInterval", L = "Autosave every", T = "num", Min = 0, Max = 60, U = "min" },
        new() { P = $"{GP}.persistence.saveRetention", L = "Saves kept", T = "num", Min = 1, Max = 128, H = "Save points kept per playthrough." },
        new() { P = $"{GP}.persistence.hiveId", L = "Hive ID", T = "num", Min = 0, Max = 16383, H = "Keeps apart the saves of several servers sharing one database." },
        new() { P = $"{GP}.persistence.databases", L = "Databases", T = "json", Obj = true, W = 400, H = "Named database instances (JSON)." },
        new() { P = $"{GP}.persistence.storages", L = "Storages", T = "json", Obj = true, W = 400, H = "Named storages and the database each uses (JSON)." },
        new() { S = "OPERATING" },
        new() { P = "operating.joinQueue.maxSize", L = "Join queue size", T = "int", Min = 0, Max = 50, H = "Players who can wait for a slot when the server is full." },
        new() { P = "operating.aiLimit", L = "AI limit", T = "int", H = "The most AI at once: nothing spawns more past it. -1 = no limit." },
        new() { P = "operating.disableAI", L = "Disable AI", T = "bool", On = "Yes", Off = "No", H = "The AI world isn't started at all." },
        new() { P = "operating.playerSaveTime", L = "Player save period", T = "int", U = "s", H = "How often players are saved." },
        new() { P = "operating.slotReservationTimeout", L = "Slot reservation", T = "int", Min = 5, Max = 300, U = "s", H = "How long a kicked player's slot is kept for them (replication kicks only)." },
        new() { P = "operating.lobbyPlayerSynchronise", L = "Lobby player sync", T = "bool", H = "Sends the list of players on the server to the backend with each heartbeat." },
        new() { P = "operating.disableNavmeshStreaming", L = "Navmesh streaming", T = "navmesh", W = 220, H = "Turn it off for every navmesh project, or only the ones listed (comma separated)." },
        new() { P = "operating.disableServerShutdown", L = "Keep running offline", T = "bool", On = "Yes", Off = "No", H = "Yes = the server doesn't shut down when it loses its connection to the backend." },
        new() { P = "operating.disableCrashReporter", L = "Disable crash reporter", T = "bool", On = "Yes", Off = "No" },
        new() { S = "RCON (REMOTE ADMIN TOOLS)" },
        new() { P = "rcon.address", L = "RCON address", T = "text", Re = Ip4, ReText = "an IPv4 address", H = "The interface for RCON tools, 0.0.0.0 = all. RCON needs an address and a password." },
        new() { P = "rcon.port", L = "RCON port", T = "int", Min = 1, Max = 65535 },
        new() { P = "rcon.password", L = "RCON password", T = "secret", MinLen = 3, MaxLen = 32, H = "3-32 characters, no spaces." },
        new() { P = "rcon.permission", L = "RCON permission", T = "enum", Values = new[] { "admin", "monitor" }, H = "For every RCON client." },
        new() { P = "rcon.maxClients", L = "RCON max clients", T = "int", Min = 1, Max = 16 },
        new() { P = "rcon.whitelist", L = "Allowed commands", T = "list", W = 400, H = "Only these commands may run over RCON (comma separated)." },
        new() { P = "rcon.blacklist", L = "Blocked commands", T = "list", W = 400, H = "These commands may not run over RCON (comma separated)." },
        new() { S = "MODS" },
        new() { P = "game.modsRequiredByDefault", L = "Mods required", T = "bool", On = "Yes", Off = "No", H = "No = the server skips a mod it can't download instead of failing." },
        new() { P = "", L = "Mod list", T = "mods", H = "Ticked on the Mods tab. Saving adds missing dependencies (footer checkbox)." },
    };

    public static readonly HashSet<string> Known = new(All.Where(s => !string.IsNullOrEmpty(s.P)).Select(s => s.P).Append("game.mods"));
    static readonly HashSet<string> Sections = new() { "a2s", "rcon", "game", GP, $"{GP}.persistence", "operating", "operating.joinQueue" };
    public static readonly string[] OfficialOnly = { "dedicatedServerId", "region", "serverClientID", "serverClientSecret", "operating.storage", "operating.heartbeat" };
    public static readonly string[] PlatformIds = { "PLATFORM_PC", "PLATFORM_XBL", "PLATFORM_PSN" };
    public static readonly string[] PlatformNames = { "PC", "Xbox", "PlayStation" };

    // Settings in the file that the editor has no field for, recursively through the known sections.
    public static List<string> OtherPaths(JsonObject cfg)
    {
        var list = new List<string>();
        void Walk(JsonObject obj, string prefix)
        {
            foreach (var p in obj)
            {
                var path = prefix.Length > 0 ? $"{prefix}.{p.Key}" : p.Key;
                if (Known.Contains(path)) continue;
                if (Sections.Contains(path) && p.Value is JsonObject o) { Walk(o, path); continue; }
                list.Add(path);
            }
        }
        Walk(cfg, "");
        return list;
    }

    // What a value looks like in its text field.
    public static string Format(Setting s, JsonNode value)
    {
        switch (s.T)
        {
            case "list":
            case "navmesh":
                return value is JsonArray a ? string.Join(", ", a.Select(x => Json.Str(x))) : Json.Str(value);
            case "json":
                return Json.Compact(value);
            default:
                return value is JsonValue v && v.TryGetValue(out double d) && !v.TryGetValue(out string _)
                    ? d.ToString(CultureInfo.InvariantCulture) : Json.Str(value);
        }
    }

    public class Parsed
    {
        public bool Ok, Unset;
        public JsonNode Value;
        public string Error;
        public static Parsed Fail(string e) => new() { Error = e };
        public static Parsed Clear() => new() { Ok = true, Unset = true };
        public static Parsed Of(JsonNode v) => new() { Ok = true, Value = v };
    }

    // Text typed into a field -> a value, "not set", or what's wrong with it.
    public static Parsed ParseText(Setting s, string text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return Parsed.Clear();
        switch (s.T)
        {
            case "text":
            case "secret":
                if (s.T == "secret")
                {
                    if (text.Contains(' ')) return Parsed.Fail("no spaces allowed");
                    t = text;
                }
                if (s.MinLen > 0 && t.Length < s.MinLen) return Parsed.Fail($"at least {s.MinLen} characters");
                if (s.MaxLen > 0 && t.Length > s.MaxLen) return Parsed.Fail($"at most {s.MaxLen} characters");
                if (s.Re != null && !Regex.IsMatch(t, s.Re, RegexOptions.IgnoreCase)) return Parsed.Fail($"must be {s.ReText}");
                return Parsed.Of(JsonValue.Create(t));
            case "int":
            case "num":
                if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) return Parsed.Fail("must be a number");
                bool whole = n == Math.Floor(n);
                if (s.T == "int" && !whole) return Parsed.Fail("must be a whole number");
                if ((s.Min.HasValue && n < s.Min) || (s.Max.HasValue && n > s.Max)) return Parsed.Fail($"must be {s.Min}-{s.Max}");
                return Parsed.Of(whole ? JsonValue.Create((long)n) : JsonValue.Create(n));
            case "list":
            case "navmesh":
                var items = Regex.Split(t, "[,;\r\n]+").Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
                if (s.Item != null)
                {
                    var bad = items.FirstOrDefault(x => !Regex.IsMatch(x, s.Item));
                    if (bad != null) return Parsed.Fail($"'{bad}' isn't an identity ID or SteamID");
                }
                if (s.MaxItems > 0 && items.Count > s.MaxItems) return Parsed.Fail($"at most {s.MaxItems}");
                return Parsed.Of(new JsonArray(items.Select(x => (JsonNode)JsonValue.Create(x)).ToArray()));
            case "json":
                JsonNode v;
                try { v = Json.Parse(t); } catch { return Parsed.Fail("isn't valid JSON"); }
                if (s.Obj && v is not JsonObject) return Parsed.Fail("must be a JSON object: { ... }");
                return Parsed.Of(v);
        }
        return Parsed.Fail("unknown setting type");
    }
}
