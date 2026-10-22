using System.Collections;
using System.Text.Json.Nodes;

namespace ReforgerLauncher;

// Mods of a list in order, GUID (upper case) -> name.
public class ModSet : IEnumerable<KeyValuePair<string, string>>
{
    readonly List<string> keys = new();
    readonly Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);

    public int Count => keys.Count;
    public IEnumerable<string> Keys => keys;
    public bool Contains(string guid) => guid != null && names.ContainsKey(guid);

    public string this[string guid]
    {
        get => guid != null && names.TryGetValue(guid, out var n) ? n : null;
        set { var g = guid.ToUpperInvariant(); if (!names.ContainsKey(g)) keys.Add(g); names[g] = value; }
    }

    // Adds a mod; a name only fills in a missing one.
    public void Add(string guid, string name)
    {
        var g = guid.ToUpperInvariant();
        if (!names.ContainsKey(g)) { keys.Add(g); names[g] = name ?? ""; }
        else if (string.IsNullOrEmpty(names[g]) && !string.IsNullOrEmpty(name)) names[g] = name;
    }

    public bool Remove(string guid)
    {
        if (guid == null || !names.Remove(guid)) return false;
        keys.RemoveAll(k => string.Equals(k, guid, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    public void Clear() { keys.Clear(); names.Clear(); }

    public ModSet Copy()
    {
        var c = new ModSet();
        foreach (var e in this) c.Add(e.Key, e.Value);
        return c;
    }

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
    {
        foreach (var k in keys.ToArray()) yield return new(k, names[k]);
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

// A mod list on the left: a game preset or a server config.
public abstract class ModList
{
    public string Name;
    public ModSet Mods = new();
    public bool Changed;
    public abstract bool IsConfig { get; }
}

public class Preset : ModList
{
    public Preset(string name) { Name = name; }
    public override bool IsConfig => false;
}

// A server config (config*.json): its JSON (kept as written, unknown keys included) plus its mods and pinned versions.
public class ConfigItem : ModList
{
    public string Path;
    public JsonObject Config;
    public Dictionary<string, string> Versions = new(StringComparer.OrdinalIgnoreCase);
    public override bool IsConfig => true;
    public JsonObject Game => Json.Section(Config, "game");
    public string ScenarioId => Json.Str(Config, "game.scenarioId");
    public string ServerName => Json.Str(Config, "game.name");
}

// A folder in the Server Configs list ("Kunar/Tests"); folders live in launcher.json, the config files stay put.
public class ConfigFolder
{
    public string Path;
    public string Name => Path[(Path.LastIndexOf('/') + 1)..];
    public string Parent => Path.Contains('/') ? Path[..Path.LastIndexOf('/')] : "";
}

public class ModDep { public string Guid; public string Name; }

public class ScenarioMeta { public string Id; public string Name; public int Players; public string Description; }

public class ModInfo
{
    public string Guid;
    public string Name = "";
    public string Version = "";
    public long Size;
    public List<ModDep> Deps = new();
    public List<ScenarioMeta> Scenarios = new();
    public bool Installed = true;
    public bool Enabled;
    public DateTime? LastPlayed;
    public string Folder = "";
    public string Location = "Game";   // Game (the client's downloads), Server (the server's addons\), Project (mod-src)
}

public class Scenario
{
    public string Id;
    public string Name;
    public string ModGuid = "";
    public string ModName = "";
    public int Order;
    public int Players;
    public string Mode = "";
    public string Description = "";
    public string Folder = "";
}
