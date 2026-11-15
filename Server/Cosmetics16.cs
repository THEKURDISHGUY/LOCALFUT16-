#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

static partial class UtBackend
{
    static readonly string[] IdentityStates = { "activeStadium", "activeHomeKit", "activeAwayKit", "activeBadge", "activeBall" };

    static JsonObject IdentitySpec(long rid, string type, long asset, long team, long cardAsset, long cardSub, int rating, int rare, int category, string name)
    {
        var o = new JsonObject { ["resourceId"] = rid, ["assetId"] = asset, ["cardassetid"] = cardAsset, ["cardsubtypeid"] = cardSub, ["itemType"] = type,
                                 ["rating"] = rating, ["rareflag"] = rare, ["category"] = category, ["displayName"] = name };
        if (type == "stadium") o["stadiumid"] = asset; else if (type == "ball") o["ballid"] = asset; else o["teamid"] = team;
        return o;
    }

    static List<JsonObject> IdentityCatalogPick(IEnumerable<long> ids)
    {
        var want = ids.ToList(); var found = new Dictionary<long, JsonObject>();
        try
        {
            var f = FindFile("club_identity_catalog.json");
            if (File.Exists(f))
                foreach (var n in JsonNode.Parse(File.ReadAllText(f))!["items"]!.AsArray())
                {
                    long rid = L(n!["resourceId"]); if (want.Contains(rid) && !found.ContainsKey(rid)) found[rid] = (JsonObject)n.DeepClone();
                }
        }
        catch (Exception ex) { Console.WriteLine("[IDENTITY] catalog read failed: " + ex.Message); }
        var fb = new Dictionary<long, JsonObject>
        {
            [6200058] = IdentitySpec(6200058, "stadium", 261, 0, 36, 10, 64, 0, 4, "Sanderson Park"),
            [6300074] = IdentitySpec(6300074, "kit", 14, 1, 35, 9, 83, 1, 2, "Arsenal Home"),
            [6400057] = IdentitySpec(6400057, "kit", 15, 1, 35, 9, 83, 1, 3, "Arsenal Away"),
            [6000057] = IdentitySpec(6000057, "custom", 1, 1, 39, 11, 83, 1, 1, "Arsenal"),
            [8120212] = IdentitySpec(8120212, "ball", 191, 0, 37, 30, 74, 0, 1, "Ball 191"),
        };
        var res = new List<JsonObject>();
        foreach (var id in want) { if (found.TryGetValue(id, out var j)) res.Add(j); else if (fb.TryGetValue(id, out var k)) res.Add(k); else Console.WriteLine("[IDENTITY] resource id " + id + " not in the catalog"); }
        return res;
    }

    static string _randomClubName = null;
    static List<long> RandomIdentityIds()
    {
        try
        {
            var f = FindFile("club_identity_catalog.json");
            if (!File.Exists(f)) return null;
            var items = JsonNode.Parse(File.ReadAllText(f))!["items"]!.AsArray().Select(n => (JsonObject)n!).ToList();
            var dbClubs = new HashSet<long>(Db.Where(p => p.AssetId < 900000 && p.Club > 0).Select(p => (long)p.Club));
            var badge = items.Where(x => x["family"]?.ToString() == "badges").GroupBy(x => L(x["teamid"])).ToDictionary(g => g.Key, g => g.First());
            var home = items.Where(x => x["family"]?.ToString() == "kits" && L(x["category"]) == 2).GroupBy(x => L(x["teamid"])).ToDictionary(g => g.Key, g => g.First());
            var away = items.Where(x => x["family"]?.ToString() == "kits" && L(x["category"]) == 3).GroupBy(x => L(x["teamid"])).ToDictionary(g => g.Key, g => g.First());
            var teams = badge.Keys.Where(t => home.ContainsKey(t) && away.ContainsKey(t) && dbClubs.Contains(t)).ToList();
            if (teams.Count == 0) return null;
            long team = teams[Rng.Next(teams.Count)];
            var stad = items.Where(x => x["itemType"]?.ToString() == "stadium").ToList();
            var ball = items.Where(x => x["itemType"]?.ToString() == "ball").ToList();
            if (stad.Count == 0 || ball.Count == 0) return null;
            var st = stad[Rng.Next(stad.Count)]; var bl = ball[Rng.Next(ball.Count)];
            _randomClubName = badge[team]["displayName"]?.ToString();
            Console.WriteLine($"[IDENTITY] random club for this user: {badge[team]["displayName"]} (team {team}), stadium {st["displayName"]}");
            return new List<long> { L(st["resourceId"]), L(home[team]["resourceId"]), L(away[team]["resourceId"]), L(badge[team]["resourceId"]), L(bl["resourceId"]) };
        }
        catch (Exception ex) { Console.WriteLine("[IDENTITY] random club failed: " + ex.Message); return null; }
    }

    static void GiveStarterIdentity()
    {
        var items = (JsonArray)Club["items"]!;
        try
        {
            if (File.Exists(FindFile("no_identity.txt")))
            {
                var rm = items.Where(x => x != null && IdentityStates.Contains(x["itemState"]?.ToString() ?? "")).ToList();
                foreach (var r in rm) items.Remove(r);
                Club.Remove("identityV1"); if (rm.Count > 0) Save();
                Console.WriteLine($"[IDENTITY] identity off (no_identity.txt) ({rm.Count} items removed)");
                return;
            }
        }
        catch { }
        try
        {
            var nf = FindFile("new_identity.txt");
            if (File.Exists(nf))
            {
                foreach (var r in items.Where(x => x != null && IdentityStates.Contains(x["itemState"]?.ToString() ?? "")).ToList()) items.Remove(r);
                Club.Remove("identityV1"); Club.Remove("clubName"); Club.Remove("clubAbbr"); File.Move(nf, nf + ".done", true);
            }
        }
        catch { }
        bool has = items.Any(x => x != null && IdentityStates.Contains(x["itemState"]?.ToString() ?? ""));
        if (has && Club["identityV1"] != null)
        {
            try
            {
                string cn0 = Club["clubName"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(cn0) || cn0 == Brand)
                {
                    var bd = items.FirstOrDefault(x => x != null && (x["itemState"]?.ToString() ?? "") == "activeBadge");
                    string nm0 = bd?["displayName"]?.ToString() ?? bd?["name"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(nm0))
                    {
                        nm0 = nm0.Length > 32 ? nm0[..32] : nm0;
                        var ab0 = new string(nm0.Where(char.IsLetterOrDigit).Take(3).ToArray()).ToUpperInvariant();
                        Club["clubName"] = nm0; Club["clubAbbr"] = ab0.Length >= 2 ? ab0 : "FUT"; Save();
                        Console.WriteLine($"[IDENTITY] club name set from the crest: {nm0}");
                    }
                }
            }
            catch { }
            return;
        }
        var ids = new List<long> { 6200058, 6300074, 6400057, 6000057, 8120212 };
        try
        {
            var f = FindFile("starter_identity.txt");
            if (File.Exists(f))
            {
                var custom = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), @"\d+").Select(m => long.Parse(m.Value)).ToList();
                if (custom.Count > 0) ids = custom;
            }
        }
        catch { }
        bool customIds = File.Exists(FindFile("starter_identity.txt"));
        if (!customIds) { var rid = RandomIdentityIds(); if (rid != null) ids = rid; }
        var picked = IdentityCatalogPick(ids);
        var activeDone = new HashSet<string>();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var spec in picked)
        {
            string type = spec["itemType"]!.ToString(); long cat = L(spec["category"]);
            string state = "free";
            string want = type == "stadium" ? "activeStadium" : type == "ball" ? "activeBall" : type == "custom" ? "activeBadge" : cat == 3 ? "activeAwayKit" : "activeHomeKit";
            if (!activeDone.Contains(want)) { state = want; activeDone.Add(want); }
            long id = Math.Max(1, L(Club["nextId"])); Club["nextId"] = id + 1;
            var it = (JsonObject)spec.DeepClone();
            it["id"] = id; it["timestamp"] = now; it["pile"] = 0; it["itemState"] = state; it["owners"] = 1; it["untradeable"] = false;
            it["discardValue"] = 10; it["lastSalePrice"] = 0; it["weightrare"] = L(spec["rareflag"]) > 0 ? 100 : 0; it["resourceGameYear"] = 2016;
            it["marketDataMinPrice"] = 150; it["marketDataMaxPrice"] = 15000000; it["name"] = spec["displayName"]?.ToString() ?? "";
            items.Add(it);
        }
        if (string.IsNullOrEmpty(_randomClubName)) _randomClubName = picked.FirstOrDefault(x => x["itemType"]?.ToString() == "custom")?["displayName"]?.ToString();
        if (!string.IsNullOrEmpty(_randomClubName) && (string.IsNullOrWhiteSpace(Club["clubName"]?.ToString()) || Club["clubName"]?.ToString() == Brand))
        {
            var nm = _randomClubName.Length > 32 ? _randomClubName[..32] : _randomClubName;
            var ab = new string(nm.Where(char.IsLetterOrDigit).Take(3).ToArray()).ToUpperInvariant();
            Club["clubName"] = nm; Club["clubAbbr"] = ab.Length >= 2 ? ab : "FUT";
            Console.WriteLine($"[IDENTITY] club name set automatically: {nm} ({Club["clubAbbr"]})");
        }
        Club["identityV1"] = 1;
        Save();
        Console.WriteLine($"[IDENTITY] starter club identity given: {picked.Count} items ({string.Join(", ", picked.Select(p => p["displayName"]))}); active: {string.Join(", ", activeDone)}");
    }

    const int StarterPackId = 450;
    static Pack[] AddStarterPack(Pack[] ps)
    {
        if (File.Exists(FindFile("no_starterpack.txt"))) return ps;
        var l = ps.ToList();
        if (l.Any(x => x.Mode == "starter")) return ps;
        int id = StarterPackId; while (l.Any(x => x.Id == id)) id++;
        l.Insert(0, new Pack(id, "Starter Pack", "bronze", "FREE one-time pack: 8 gold players to start your club", 0, 0, 8, 100, 0, 0, 0, 0, "starter"));
        return l.ToArray();
    }

    static bool StarterBlocked(Pack pk)
    {
        if (pk == null || pk.Mode != "starter") return false;
        try
        {
            string again = FindFile("starterpack_again.txt");
            if (File.Exists(again)) { Club.Remove("starterPackClaimed"); Club.Remove("starterPackIds"); try { File.Move(again, again + ".done", true); } catch { } Save(); }
        }
        catch { }
        return Club["starterPackClaimed"] != null;
    }

    static List<JsonObject> StarterPackCards()
    {
        var real = Db.Where(p => p.AssetId > 100 && p.AssetId < 900000 && !p.Special && p.Rating == 79).ToList();
        if (real.Count < 8) real = Db.Where(p => p.AssetId > 100 && p.AssetId < 900000 && !p.Special && p.Rating >= 78 && p.Rating <= 80).ToList();
        if (real.Count == 0) real = Db.Where(p => p.AssetId < 900000 && !p.Special).OrderByDescending(p => p.Rating).Take(200).ToList();
        string[][] slots = { new[] { "GK" }, new[] { "RB" }, new[] { "CB" }, new[] { "LB" }, new[] { "CM", "CDM" }, new[] { "CAM", "RM", "LM" }, new[] { "RW", "LW" }, new[] { "ST", "CF" } };
        var used = new HashSet<long>(); var cards = new List<JsonObject>(); var names = new List<string>();
        foreach (var slot in slots)
        {
            var pool = real.Where(p => slot.Contains(p.Pos) && !used.Contains(p.AssetId)).ToList();
            if (pool.Count == 0) pool = real.Where(p => !used.Contains(p.AssetId)).ToList();
            if (pool.Count == 0) break;
            var pick = pool[Rng.Next(pool.Count)]; used.Add(pick.AssetId);
            cards.Add(Item(pick, Q(pick.Rating))); names.Add($"{pick.Name} {pick.Rating} {pick.Pos}");
        }
        Console.WriteLine("[STARTERPACK] opened from the store: " + string.Join(", ", names));
        return cards;
    }

    static string _clubDevice = null;
    static void SwitchClubForDevice(byte[] body)
    {
        string dev = null;
        try { dev = JsonNode.Parse(Encoding.UTF8.GetString(body))?["deviceId"]?.ToString(); } catch { }
        if (string.IsNullOrWhiteSpace(dev)) return;
        dev = new string(dev.Where(char.IsLetterOrDigit).Take(16).ToArray());
        if (dev == "" || dev == _clubDevice) return;
        Directory.CreateDirectory(Dir);
        string mapFile = Path.Combine(Dir, "devices.json");
        var map = new JsonObject();
        try { if (File.Exists(mapFile)) map = JsonNode.Parse(File.ReadAllText(mapFile))!.AsObject(); } catch { }
        if (map[dev] == null)
        {
            string legacy = Path.Combine(Dir, "club.json");
            map[dev] = (map.Count == 0 && File.Exists(legacy)) ? "club.json" : "club_" + dev + ".json";
            File.WriteAllText(mapFile, map.ToJsonString());
        }
        string target = Path.Combine(Dir, map[dev]!.ToString());
        if (_clubDevice != null || !string.Equals(target, ClubFile, StringComparison.OrdinalIgnoreCase))
        {
            Save();
            ClubFile = target;
            Club = LoadClub();
            ApplyCheats();
            Console.WriteLine($"[USERS] player {dev} -> {Path.GetFileName(ClubFile)} (club {ClubNameNow()})");
        }
        _clubDevice = dev;
    }

    static void GiveStarterPack()
    {
        if (!File.Exists(FindFile("starterpack_boot.txt")) || Club["starterPackClaimed"] != null) return;
        var items = (JsonArray)Club["items"]!;
        foreach (var c in StarterPackCards()) items.Add(c);
        Club["starterPackClaimed"] = 1; Save();
    }

    static void StarterPackOpened(JsonArray res)
    {
        Club["starterPackClaimed"] = 1;
        var ids = new JsonArray(); foreach (var c in res) ids.Add(L(c?["id"]));
        Club["starterPackIds"] = ids;
    }

    static void StarterPackSettled()
    {
        if (Club["starterPackIds"] is not JsonArray sp || sp.Count == 0) return;
        var un = new HashSet<long>(((Club["unassigned"] as JsonArray) ?? new JsonArray()).Select(n => L(n)));
        if (sp.Any(n => un.Contains(L(n)))) return;
        Club.Remove("starterPackIds");
        EnsureAutoSquad(true);
    }

    static JsonObject UserObjFifa16()
    {
        int div = Math.Max(1, Math.Min(10, SPI(SeasonSPState(), "division")));
        var uo = new JsonObject { ["divisionOffline"] = div, ["divisionOnline"] = 10 };
        if (File.Exists(FindFile("user_small.txt"))) return uo;
        bool setupDone = !File.Exists(FindFile("manual_setup.txt"));
        if (Club["established"] == null) { Club["established"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); Save(); }
        uo["personaId"] = 1000000002; uo["accountCreatedPlatformName"] = "pc";
        uo["clubName"] = ClubNameNow(); uo["clubAbbr"] = ClubAbbrNow();
        uo["clubNameChangeAllowed"] = setupDone ? 0 : 1; uo["returningUser"] = setupDone ? 1 : 0;
        uo["established"] = L(Club["established"]).ToString();
        uo["won"] = 0; uo["draw"] = 0; uo["loss"] = 0; uo["trophies"] = 0;
        if (!File.Exists(FindFile("user_nocredits.txt")))
        {
            var cr = CreditsObj();
            uo["credits"] = cr["credits"]!.DeepClone(); uo["currencies"] = cr["currencies"]!.DeepClone(); uo["bidTokens"] = cr["bidTokens"]!.DeepClone();
        }
        return uo;
    }

    static JsonArray IdentityActives()
    {
        var order = IdentityStates.ToList();
        var arr = new JsonArray();
        foreach (var x in ((JsonArray)Club["items"]!).Where(i => i != null && IdentityStates.Contains(i["itemState"]?.ToString() ?? "")).OrderBy(i => order.IndexOf(i["itemState"]!.ToString())))
            arr.Add(x.DeepClone());
        return arr;
    }
}
