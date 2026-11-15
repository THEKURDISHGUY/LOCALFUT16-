using System.Text;
using System.Globalization;
using LocalFut15;
using System.Text.Json;
using System.Text.Json.Nodes;

static partial class UtBackend
{
    public const string Brand = "FUT REBORN";
    public const string Persona = "TheKurdishGuy";
    static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "save");
    static string ClubFile = Path.Combine(Dir, "club.json");
    public static string FindFile(string name)
    {
        foreach (var d in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory(), Path.Combine(AppContext.BaseDirectory, "..", "..", "..") })
        {
            try { var p = Path.GetFullPath(Path.Combine(d, name)); if (File.Exists(p)) return p; } catch { }
        }
        return Path.Combine(AppContext.BaseDirectory, name);
    }
    static readonly string PlayersCsv = FindFile("players16.csv");
    static readonly object Lock = new();
    static readonly Random Rng = new();

    public record P(long Id, long AssetId, string Name, int Rating, string Pos, int Nation, int League, int Club, int Rare, int[]? Face = null, bool Special = false, string Set = "");
    static List<P> Db = new();
    public record M(long Id, string Name, string NationName);
    static List<M> Managers = new();
    static string DbSource => Db.Any(p => p.AssetId < 900000 && p.AssetId > 100) && Db.Count > 500 ? "PlayersData" : "fallback";
    static JsonObject Club = new();

    public record Pack(int Id, string Name, string Category, string Bio, int Coins, int Points, int Cards,
                       int GoldW, int SilverW, int BronzeW, int RareSlots, double Special, string Mode = "", double Manager = -1);
    public static readonly Pack[] Packs;

    static UtBackend()
    {
        try { Db = LoadPlayers(); }
        catch (Exception ex) { Console.WriteLine("[FUT REBORN] LoadPlayers FAILED: " + ex); Db = new(); }
        try { Db = PackStore.Apply(Db); }
        catch (Exception ex) { Console.WriteLine("[PACKS] PackStore failed: " + ex); }
        try { Db = AddTotw(Db); }
        catch (Exception ex) { Console.WriteLine("[TOTW] failed: " + ex.Message); }
        try { Db = IconStore.Apply(Db); }
        catch (Exception ex) { Console.WriteLine("[ICONS] failed: " + ex.Message); }
        try { Managers = LoadManagers16(PackStore.LoadManagers()); }
        catch (Exception ex) { Console.WriteLine("[MANAGERS] failed: " + ex.Message); }
        if (Db.Count == 0) Db.Add(new P(158023, 158023, "L. Messi", 94, "RW", 52, 53, 241, 1));
        try { Club = LoadClub(); FixStaff(); FixPlayers(); StartAutosave(); }
        catch (Exception ex)
        {
            Console.WriteLine("[FUT REBORN] LoadClub FAILED (using an empty in-memory club): " + ex);
            Club = new JsonObject
            {
                ["persona"] = Persona, ["clubName"] = Brand, ["coins"] = 100000, ["points"] = 5000,
                ["nextId"] = 1L, ["items"] = new JsonArray(), ["squad"] = new JsonArray(),
                ["unopened"] = new JsonArray(), ["draftWins"] = 0,
            };
        }
        try { ApplyCheats(); }
        catch (Exception ex) { Console.WriteLine("[CHEATS] failed: " + ex.Message); }
        try { Packs = LoadPacks(); }
        catch (Exception ex)
        {
            Console.WriteLine("[FUT REBORN] LoadPacks FAILED: " + ex);
            Packs = new[] { new Pack(1, "FUT REBORN Bronze Pack", "bronze", "3 bronze players", 400, 0, 3, 0, 5, 95, 0, 0) };
        }
        try { Packs = PackStore.AddRebornPacks(Packs); }
        catch (Exception ex) { Console.WriteLine("[PACKS] AddRebornPacks failed: " + ex.Message); }
        try { Packs = AddSetPacks(Packs); }
        catch (Exception ex) { Console.WriteLine("[PACKS] AddSetPacks failed: " + ex.Message); }
        try { Packs = Rebalance(Packs); }
        catch (Exception ex) { Console.WriteLine("[PACKS] Rebalance failed: " + ex.Message); }
        try { Packs = AddStarterPack(Packs); }
        catch (Exception ex) { Console.WriteLine("[PACKS] AddStarterPack failed: " + ex.Message); }
    }

    static Pack[] LoadPacks()
    {
        var list = new List<Pack>();
        string f = FindFile("packs.json");
        try
        {
            if (File.Exists(f))
            {
                var root = JsonNode.Parse(File.ReadAllText(f))!["packs"]!.AsObject();
                foreach (var kv in root)
                {
                    var o = kv.Value!.AsObject();
                    if (o["enabled"] is JsonNode en && !en.GetValue<bool>()) continue;
                    int I(string k) => o[k] is JsonNode n ? (int)n.GetValue<double>() : 0;
                    string S(string k) => o[k]?.GetValue<string>() ?? "";
                    list.Add(new Pack(int.Parse(kv.Key), S("name"), S("category"), S("bio"), I("price"), I("fifa_points"),
                        Math.Max(1, I("size")), I("gold_weight"), I("silver_weight"), I("bronze_weight"),
                        Math.Max(I("rare_slots"), I("player_rare_slots")), o["special_chance"] is JsonNode sc ? sc.GetValue<double>() : 0, S("mode"),
                        o["manager_chance"] is JsonNode mcn ? mcn.GetValue<double>() : -1));
                }
                Console.WriteLine($"[FUT REBORN] loaded {list.Count} packs from packs.json");
            }
        }
        catch (Exception ex) { Console.WriteLine("[FUT REBORN] packs.json error: " + ex.Message); list.Clear(); }
        if (list.Count == 0)
        {
            list.Add(new Pack(100, "Bronze Pack", "bronze", "3 bronze players", 400, 0, 3, 0, 5, 95, 0, 0));
            list.Add(new Pack(101, "Premium Bronze Pack", "bronze", "3 bronze players, 1 rare", 750, 0, 3, 0, 5, 95, 1, 0));
            list.Add(new Pack(102, "Jumbo Bronze Pack", "bronze", "6 bronze players", 800, 0, 6, 0, 5, 95, 1, 0));
            list.Add(new Pack(103, "Jumbo Premium Bronze Pack", "bronze", "6 bronze players, 2 rares", 1500, 0, 6, 0, 5, 95, 2, 0));
            list.Add(new Pack(200, "Silver Pack", "silver", "3 silver players, 1 rare", 2500, 0, 3, 5, 95, 0, 1, 0));
            list.Add(new Pack(201, "Premium Silver Pack", "silver", "3 silver players, 2 rares", 3750, 0, 3, 5, 95, 0, 2, 0));
            list.Add(new Pack(202, "Jumbo Silver Pack", "silver", "6 silver players, 2 rares", 4000, 0, 6, 5, 95, 0, 2, 0));
            list.Add(new Pack(300, "Gold Pack", "gold", "3 gold players, 1 rare", 5000, 0, 3, 100, 0, 0, 1, 0));
            list.Add(new Pack(301, "Gold Chance Pack", "gold", "5 gold players, 2 rares. Chance of a 85+ star", 150000, 0, 5, 100, 0, 0, 2, 0.10));
            list.Add(new Pack(302, "Gold Superstar Pack", "gold", "3 rare gold players. Big chance of a Messi / Ronaldo level star", 400000, 0, 3, 100, 0, 0, 3, 0.35));
            list.Add(new Pack(400, "FUT REBORN Special Pack", "special", "5 special cards: 2 TOTY + 1 TOTS + 2 special", 800000, 0, 5, 100, 0, 0, 5, 0, "special"));
        }
        if (File.Exists(FindFile("packlow.txt")))
            for (int i = 0; i < list.Count; i++) list[i] = list[i] with { Id = i + 1 };
        return list.ToArray();
    }

    public record Mode(int Id, string Name, string Kind, int Coins, int Points, int Wins);
    public static readonly Mode[] Modes =
    {
        new(2001, "FUT REBORN Draft",             "draft",  15000, 0, 4),
        new(2002, "FUT REBORN Draft - Premium",    "draft",  30000, 0, 4),
        new(2003, "FUT REBORN Tournament - Bronze","tournament", 0, 0, 3),
        new(2004, "FUT REBORN Tournament - Gold",  "tournament", 0, 0, 5),
        new(2005, "FUT REBORN Tournament - Silver","tournament", 0, 0, 4),
        new(2006, "FUT REBORN Cup",                "tournament", 0, 0, 4),
        new(2007, "FUT REBORN Legends Cup",        "tournament", 0, 0, 6),
        new(2008, "SINGLE PLAYER FUT REBORN CHAMPIONS", "tournament", 0, 0, 8),
    };
    static int _ttCycle = 0;

    static List<P> LoadPlayers()
    {
        var l = new List<P>();
        try { l = PlayersBuilder.Load(); if (l.Count > 0) Console.WriteLine($"[FUT REBORN] loaded {l.Count / 2} real FIFA 16 players ({l.Count} cards) from PlayersData.cs"); }
        catch (Exception ex) { Console.WriteLine("[FUT REBORN] PlayersData load failed: " + ex.Message); l = new(); }
        if (l.Count == 0 && File.Exists(PlayersCsv))
        {
            foreach (var line in File.ReadLines(PlayersCsv, Encoding.UTF8).Skip(1))
            {
                var c = line.Split(',');
                if (c.Length < 8) continue;
                try { long pid = long.Parse(c[0]); int rr = int.Parse(c[7]); l.Add(new P(rr == 1 ? 16777216L + pid : pid, pid, c[1], int.Parse(c[2]), c[3], int.Parse(c[4]), int.Parse(c[5]), int.Parse(c[6]), rr)); } catch { }
            }
            Console.WriteLine($"[FUT REBORN] loaded {l.Count} players from players16.csv");
        }
        if (l.Count < 50)
        {
            Console.WriteLine("[FUT REBORN] NO REAL PLAYERS (PlayersData.cs is the empty stub). Run:  dotnet run -- --make-players D:\\path\\male_players.csv   then rebuild. Using a tiny built-in list for now.");
            l.AddRange(new P[]
            {
                new(158023,158023,"L. Messi",94,"RW",52,53,241,1), new(20801,20801,"Cristiano Ronaldo",93,"LW",38,53,243,1),
                new(190871,190871,"Neymar Jr",89,"LW",54,53,241,1), new(176580,176580,"L. Suarez",88,"ST",60,53,241,1),
                new(188545,188545,"R. Lewandowski",88,"ST",37,19,21,1), new(167495,167495,"M. Neuer",90,"GK",21,19,21,1),
                new(183277,183277,"E. Hazard",88,"LW",7,14,5,1), new(41236,41236,"Z. Ibrahimovic",87,"ST",46,16,73,1),
                new(41,41,"A. Iniesta",89,"CM",45,53,241,1), new(177003,177003,"L. Modric",87,"CM",10,53,243,1), new(182521,182521,"T. Kroos",86,"CM",21,53,243,1),
                new(155862,155862,"Sergio Ramos",87,"CB",45,53,243,1), new(173731,173731,"G. Bale",87,"RW",110,53,243,1), new(165153,165153,"K. Benzema",87,"ST",18,53,243,1),
                new(153079,153079,"S. Aguero",87,"ST",52,13,10,1), new(192985,192985,"K. De Bruyne",86,"CAM",7,13,10,1), new(192119,192119,"T. Courtois",86,"GK",7,14,5,1),
                new(184941,184941,"A. Sanchez",86,"LW",55,14,1,1), new(9014,9014,"A. Robben",87,"RW",34,19,21,1), new(156616,156616,"F. Ribery",86,"LW",18,19,21,1),
                new(164240,164240,"Thiago Silva",86,"CB",54,16,73,1), new(152729,152729,"G. Pique",86,"CB",45,53,241,1), new(189511,189511,"S. Busquets",86,"CDM",45,53,241,1),
                new(168651,168651,"I. Rakitic",85,"CM",10,53,241,1), new(194765,194765,"A. Griezmann",84,"ST",18,53,240,1), new(188567,188567,"P. Aubameyang",85,"ST",57,19,22,1),
                new(192318,192318,"M. Gotze",83,"CAM",21,19,21,1), new(188350,188350,"M. Reus",86,"LW",21,19,22,1), new(189596,189596,"T. Muller",86,"CF",21,19,21,1),
                new(197445,197445,"D. Alaba",84,"LB",4,19,21,1), new(162895,162895,"C. Fabregas",84,"CM",45,14,5,1), new(189509,189509,"Diego Costa",85,"ST",45,14,5,1),
                new(179844,179844,"E. Cavani",85,"ST",60,16,73,1), new(183898,183898,"A. Di Maria",85,"LW",52,16,73,1), new(195864,195864,"P. Pogba",84,"CM",18,45,11,1),
                new(202126,202126,"H. Kane",83,"ST",14,13,18,1), new(202652,202652,"R. Sterling",80,"LW",14,13,10,1), new(176635,176635,"M. Ozil",86,"CAM",21,13,1,1),
                new(54050,54050,"W. Rooney",85,"ST",14,13,11,1), new(192505,192505,"R. Lukaku",81,"ST",7,13,7,1), new(178603,178603,"M. Hummels",85,"CB",21,19,22,1),
                new(1179,1179,"G. Buffon",87,"GK",27,31,45,1), new(181872,181872,"A. Vidal",86,"CM",55,31,45,1), new(176676,176676,"Marcelo",85,"LB",54,53,243,1),
                new(192448,192448,"M. ter Stegen",84,"GK",21,53,241,1), new(146530,146530,"Dani Alves",84,"RB",54,53,241,1),
            });
            string[] pos = { "GK","RB","CB","CB","LB","CM","CM","CAM","RW","LW","ST" };
            for (int i = 0; i < 60; i++) l.Add(new P(900000 + i, 900000 + i, "Reborn Player " + (i + 1), 45 + (i % 20) * 2, pos[i % pos.Length], 1, 1, 1, 0));
        }
        return l;
    }

    static JsonObject LoadClub()
    {
        Directory.CreateDirectory(Dir);
        if (File.Exists(ClubFile))
        {
            try
            {
                var old = JsonNode.Parse(File.ReadAllText(ClubFile))!.AsObject();
                if (DbSource == "PlayersData" && ClubIsFake(old))
                {
                    Console.WriteLine("[FUT REBORN] club.json had no real players -> building a new starter squad from PlayersData.cs");
                    old["items"] = new JsonArray(); old["squad"] = new JsonArray(); old["nextId"] = 1L; old.Remove("squadBody");
                    foreach (var stale in new[] { "squads", "activeSquadId", "autoSquadV3", "autoSquadLayout", "unassigned", "starterPackIds", "squadFixV1" }) old.Remove(stale);
                    Club = old;
                    GiveStarterSquad(); return Club;
                }
                return old;
            }
            catch (Exception ex0)
            {
                Console.WriteLine("[SAVE] club.json unreadable (" + ex0.Message + ") -> trying club.json.bak");
                try { var bk = JsonNode.Parse(File.ReadAllText(ClubFile + ".bak"))!.AsObject(); Console.WriteLine("[SAVE] restored from club.json.bak"); return bk; } catch { }
            }
        }
        var c = new JsonObject
        {
            ["persona"] = Persona, ["clubName"] = Brand, ["coins"] = 100000, ["points"] = 5000,
            ["nextId"] = 1L, ["items"] = new JsonArray(), ["squad"] = new JsonArray(),
            ["unopened"] = new JsonArray(), ["draftWins"] = 0,
        };
        Club = c;
        try { GiveStarterSquad(); }
        catch (Exception ex) { Console.WriteLine("[FUT REBORN] starter squad FAILED: " + ex); }
        return Club;
    }
    static void Save() { try { lock (Lock) { Directory.CreateDirectory(Dir); WriteClubAtomic(); } } catch (Exception ex) { Console.WriteLine("[FUT REBORN] save failed: " + ex.Message); } }

    static JsonObject Item(P p, string quality)
    {
        long id = Math.Max(1, L(Club["nextId"])); Club["nextId"] = id + 1;
        return new JsonObject
        {
            ["id"] = id, ["resourceId"] = p.Id, ["assetId"] = p.AssetId, ["itemType"] = "player", ["cardsubtypeid"] = p.Rare >= 1 ? 3 : 2,
            ["rating"] = p.Rating, ["preferredPosition"] = p.Pos, ["rareflag"] = p.Rare, ["nation"] = p.Nation, ["leagueId"] = p.League,
            ["teamid"] = p.Club, ["lastSalePrice"] = 0, ["discardValue"] = QuickSellFor(p.Rating, p.Rare), ["contract"] = 7, ["contracts"] = 7, ["itemId"] = id, ["definitionId"] = p.AssetId, ["resourceGameYear"] = 2016, ["loans"] = 0, ["teamId"] = p.Club, ["fitness"] = 99,
            ["owners"] = 1, ["training"] = 0, ["morale"] = 50, ["itemState"] = "free", ["pile"] = 0, ["lifetimeStats"] = new JsonArray(),
            ["name"] = p.Name, ["quality"] = quality,
            ["attributeList"] = new JsonArray((p.Face ?? new int[6]).Select((v, i) => (JsonNode)new JsonObject { ["index"] = i, ["value"] = v }).ToArray()),
            ["face"] = new JsonArray((p.Face ?? new int[6]).Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()),
        };
    }
    static string Q(int r) => r >= 75 ? "gold" : r >= 65 ? "silver" : "bronze";

    static void GiveStarterSquad()
    {
        var items = (JsonArray)Club["items"]!;
        var squad = (JsonArray)Club["squad"]!;
        string[] formation = { "GK","RB","CB","CB","LB","CM","CM","CM","RW","ST","LW" };
        var real = Db.Where(p => p.AssetId < 900000 && !p.Special).ToList();
        if (real.Count == 0) real = Db.ToList();
        var used = new HashSet<long>();
        P Pick(params string[] poss)
        {
            var byPos = real.Where(p => poss.Contains(p.Pos) && !used.Contains(p.AssetId)).ToList();
            if (byPos.Count == 0) byPos = real.Where(p => !used.Contains(p.AssetId)).ToList();
            var pool = byPos.Where(p => p.Rating <= 74 && p.Rare == 0).ToList();
            if (pool.Count < 3) pool = byPos.OrderBy(p => p.Rating).Take(8).ToList();
            var x = pool[Rng.Next(pool.Count)]; used.Add(x.AssetId); return x;
        }
        foreach (var pos in formation)
        {
            var p = Pick(pos); var it = Item(p, Q(p.Rating)); items.Add(it); squad.Add(L(it["id"]));
        }
        foreach (var grp in new[] { new[] { "GK" }, new[] { "CB" }, new[] { "RB", "LB" }, new[] { "CM", "CDM" }, new[] { "CAM", "RM", "LM" }, new[] { "ST", "CF" }, new[] { "RW", "LW" } })
        {
            var p = Pick(grp); var it = Item(p, Q(p.Rating)); items.Add(it); squad.Add(L(it["id"]));
        }
        Club["dbSource"] = DbSource;
        if (Environment.GetEnvironmentVariable("LOCALFUT_ALLPLAYERS") == "1")
            foreach (var p in Db) items.Add(Item(p, Q(p.Rating)));
        Save();
    }

    static bool InQ(P p, string q) => q == "gold" ? p.Rating >= 75 : q == "silver" ? (p.Rating >= 65 && p.Rating <= 74) : p.Rating <= 64;

    static string PickQuality(Pack pk)
    {
        int total = pk.GoldW + pk.SilverW + pk.BronzeW; if (total <= 0) return "bronze";
        int r = Rng.Next(total);
        return r < pk.GoldW ? "gold" : r < pk.GoldW + pk.SilverW ? "silver" : "bronze";
    }

    static JsonObject CreditsObj() => new JsonObject
    {
        ["credits"] = Club["coins"]!.DeepClone(),
        ["bidTokens"] = new JsonObject { ["count"] = 0, ["updateTime"] = null },
        ["currencies"] = new JsonArray(
            new JsonObject { ["name"] = "COINS", ["funds"] = Club["coins"]!.DeepClone(), ["finalFunds"] = Club["coins"]!.DeepClone() },
            new JsonObject { ["name"] = "POINTS", ["funds"] = Club["points"]!.DeepClone(), ["finalFunds"] = Club["points"]!.DeepClone() },
            new JsonObject { ["name"] = "BIDTOKEN", ["funds"] = 0, ["finalFunds"] = 0 }),
        ["unopenedPacks"] = new JsonObject { ["preOrderPacks"] = 0, ["recoveredPacks"] = 0 },
    };

    static Pack[] Rebalance(Pack[] ps)
    {
        var o = new List<Pack>();
        foreach (var pk in ps)
        {
            var p = pk;
            if (p.Mode == "icon" && !Db.Any(x => x.Set == "icon")) { Console.WriteLine("[ICONS] no icon cards loaded -> the FUT REBORN Icon Pack is hidden (check icons16.tsv / save\\icons_unmatched.txt)"); continue; }
            if (p.Mode == "all") p = p with { Coins = Math.Max(p.Coins, 150000), Special = 0 };
            else if (p.Mode == "special") p = p with { Coins = Math.Max(p.Coins, 100000), Category = "special" };
            else if (p.Coins < 50000) p = p with { Special = 0 };
            string cat = (p.Category ?? "").ToLowerInvariant();
            if (cat == "gold" || cat == "special") p = p with { GoldW = 100, SilverW = 0, BronzeW = 0 };
            else if (cat == "silver") p = p with { BronzeW = 0, GoldW = Math.Min(p.GoldW, 5), SilverW = 100 - Math.Min(p.GoldW, 5) };
            else if (cat == "bronze") p = p with { GoldW = 0, SilverW = Math.Min(p.SilverW, 5), BronzeW = Math.Max(1, p.BronzeW) };
            o.Add(p);
        }
        return o.ToArray();
    }
    const int TotyPerPack = 2;
    const int TotsPerPack = 1;
    const double TotyDecay = 0.75;
    static int Floor(Pack pk) { string c = (pk.Category ?? "").ToLowerInvariant(); return c == "gold" || c == "special" ? 75 : c == "silver" ? 65 : 0; }
    static double Luck(Pack pk) => pk.Coins >= 100000 ? 1.0 : pk.Coins >= 50000 ? 0.995 : pk.Coins >= 10000 ? 0.985 : 0.975;
    static P WeightedPick(List<P> pool, double decay)
    {
        int lo = pool.Min(p => p.Rating);
        double W(P p) => Math.Pow(decay, p.Rating - lo);
        double total = 0; foreach (var p in pool) total += W(p);
        double r = Rng.NextDouble() * total;
        foreach (var p in pool) { r -= W(p); if (r <= 0) return p; }
        return pool[pool.Count - 1];
    }

    static double ManagerChance(Pack pk)
    {
        if (Managers.Count == 0 || File.Exists(FindFile("nomanagers.txt"))) return 0;
        if (pk.Manager >= 0) return pk.Manager;
        if (pk.Cards >= 12) return 1.0;
        string c = (pk.Category ?? "").ToLowerInvariant();
        double b = c == "gold" ? 0.30 : c == "silver" ? 0.20 : 0.12;
        return Math.Min(1.0, b + (pk.Cards >= 6 ? 0.10 : 0));
    }
    static JsonObject ManagerItem(M m)
    {
        long id = Math.Max(1, L(Club["nextId"])); Club["nextId"] = id + 1;
        var mo = new JsonObject
        {
            ["id"] = id, ["resourceId"] = m.Id, ["assetId"] = m.Id, ["itemType"] = "staff", ["cardsubtypeid"] = 2,
            ["rating"] = 0, ["rareflag"] = 0, ["lastSalePrice"] = 0, ["discardValue"] = 100, ["contract"] = 7,
            ["owners"] = 1, ["itemState"] = "free", ["pile"] = 0, ["lifetimeStats"] = new JsonArray(),
            ["name"] = m.Name, ["quality"] = "gold", ["nation"] = 0, ["leagueId"] = 0, ["teamid"] = 0,
        };
        if (MgrExtra.TryGetValue(m.Id, out var mx)) { mo["nation"] = mx.nat; mo["leagueId"] = mx.lg; mo["rating"] = mx.rt; }
        StaffFill(mo);
        return mo;
    }

    const int SellAfterSec = 15;
    static JsonArray Auctions() { if (Club["auctions"] is not JsonArray a) { a = new JsonArray(); Club["auctions"] = a; } return a; }
    static JsonObject? FindItem(long id) => ((JsonArray)Club["items"]!).FirstOrDefault(i => L(i?["id"]) == id) as JsonObject;
    static void RemoveAuctionFor(long itemId)
    {
        var a = Auctions();
        for (int k = a.Count - 1; k >= 0; k--)
            if (a[k] is JsonObject ao && L(ao["itemId"]) == itemId && !(ao["sold"]?.GetValue<bool>() ?? false)) a.RemoveAt(k);
    }
    static void SetPile(long id, string pile)
    {
        var it = FindItem(id); if (it == null) return;
        if (pile == "trade")
        {
            it["pile"] = "trade";
            bool listed = Auctions().Any(n => n is JsonObject ao && L(ao["itemId"]) == id && !(ao["sold"]?.GetValue<bool>() ?? false));
            if (!listed)
            {
                int rt = (int)L(it["rating"]);
                long buy = rt >= 85 ? 20000 : rt >= 75 ? 1500 : rt >= 65 ? 500 : 200;
                long tid = Math.Max(1, L(Club["nextId"])); Club["nextId"] = tid + 1;
                Auctions().Add(new JsonObject { ["tradeId"] = tid, ["itemId"] = id, ["startingBid"] = buy / 2, ["buyNowPrice"] = buy, ["duration"] = 3600L, ["listedAt"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["sold"] = false });
                Console.WriteLine($"[LIST] card {id} moved to the transfer list (default price {buy}), tradeId {tid}");
            }
            Save();
        }
        else { it["pile"] = 0; RemoveAuctionFor(id); Save(); }
    }
    static long CreateAuction(JsonNode? rq)
    {
        JsonNode? idn = rq?["itemData"]; if (idn is JsonArray ia) idn = ia.Count > 0 ? ia[0] : null;
        long itemId = L(idn?["id"]); if (itemId == 0) itemId = L(rq?["itemId"]);
        long start = L(rq?["startingBid"]), buy = L(rq?["buyNowPrice"]), dur = L(rq?["duration"]); if (dur <= 0) dur = 3600;
        long tid = Math.Max(1, L(Club["nextId"])); Club["nextId"] = tid + 1;
        RemoveAuctionFor(itemId);
        Auctions().Add(new JsonObject { ["tradeId"] = tid, ["itemId"] = itemId, ["startingBid"] = start, ["buyNowPrice"] = buy, ["duration"] = dur, ["listedAt"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["sold"] = false });
        var it = FindItem(itemId); if (it != null) it["pile"] = "trade";
        Console.WriteLine($"[LIST] card {itemId} put on the transfer list, tradeId {tid}, start {start}, buy now {buy}" + (it == null ? "   (CARD NOT FOUND IN CLUB)" : ""));
        Save(); return tid;
    }
    static void SettleAuctions()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); bool any = false;
        foreach (var n in Auctions())
        {
            if (n is not JsonObject a || (a["sold"]?.GetValue<bool>() ?? false)) continue;
            if (now - L(a["listedAt"]) < SellAfterSec) continue;
            long price = L(a["buyNowPrice"]); if (price <= 0) price = L(a["startingBid"]);
            long net = (long)Math.Floor(price * 0.95);
            long iid = L(a["itemId"]); var it = FindItem(iid);
            if (it != null)
            {
                a["item"] = it.DeepClone(); ((JsonArray)Club["items"]!).Remove(it);
                var sqa = (JsonArray)Club["squad"]!; var sh = sqa.FirstOrDefault(x => L(x) == iid); if (sh != null) sqa.Remove(sh);
                if (Club["squadBody"]?["players"] is JsonArray sp)
                    foreach (var slot in sp) if (L(slot?["itemData"]?["id"]) == iid) slot!["itemData"] = new JsonObject { ["id"] = 0, ["dream"] = false };
                PurgeFromSquads(iid);
            }
            Club["coins"] = (int)L(Club["coins"]) + (int)net;
            a["sold"] = true; a["price"] = price; any = true;
        }
        if (any) Save();
    }
    static JsonObject TradePileJson()
    {
        SettleAuctions();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var arr = new JsonArray(); long sold = 0, selling = 0;
        foreach (var n in Auctions())
        {
            if (n is not JsonObject a) continue;
            bool isSold = a["sold"]?.GetValue<bool>() ?? false;
            JsonNode? card = isSold ? a["item"]?.DeepClone() : FindItem(L(a["itemId"]))?.DeepClone();
            if (card == null) continue;
            long price = isSold ? L(a["price"]) : 0L;
            long left = Math.Max(1L, L(a["duration"]) - (now - L(a["listedAt"])));
            arr.Add(new JsonObject
            {
                ["tradeId"] = L(a["tradeId"]), ["tradeState"] = isSold ? "closed" : "active", ["buyNowPrice"] = L(a["buyNowPrice"]), ["currentBid"] = price,
                ["startingBid"] = L(a["startingBid"]), ["offers"] = 0L, ["watched"] = false, ["bidState"] = isSold ? "highest" : "none",
                ["expires"] = isSold ? -1L : left, ["tradeOwner"] = true, ["currencyType"] = "COINS", ["itemData"] = card,
            });
            if (isSold) sold++; else selling++;
        }
        var o = CreditsObj();
        o["auctionInfo"] = arr; o["bidTokens"] = new JsonObject(); o["total"] = arr.Count; o["count"] = arr.Count;
        o["currentTradePileSize"] = arr.Count; o["tradePileCount"] = arr.Count; o["maxAuctionsAllowed"] = 100L; o["maximumTradePileSize"] = 100L;
        o["selling"] = selling; o["sold"] = sold;
        return o;
    }

    const long TopUpCoins = 80000000;
    static void ApplyCheats()
    {
        try { StarterGift(); } catch (Exception ex) { Console.WriteLine("[STARTER] failed: " + ex.Message); }
        try { EnsureStarterPlayers(); } catch (Exception ex) { Console.WriteLine("[STARTER] players failed: " + ex); }
        try { GiveStarterIdentity(); } catch (Exception ex) { Console.WriteLine("[IDENTITY] failed: " + ex.Message); }
        try { GiveStarterPack(); } catch (Exception ex) { Console.WriteLine("[STARTERPACK] failed: " + ex.Message); }
        try { EnsureAutoSquad(); } catch (Exception ex) { Console.WriteLine("[AUTOSQUAD] failed: " + ex.Message); }
        try { EnsureSquads(); } catch (Exception ex) { Console.WriteLine("[SQUADS] failed: " + ex); }
        if (TopUpCoins > 0 && L(Club["coins"]) < TopUpCoins) { Club["coins"] = (int)Math.Min(TopUpCoins, 2000000000L); Console.WriteLine("[CHEATS] coins topped up to " + Club["coins"]); Save(); }
        string cf = FindFile("coins.txt");
        if (File.Exists(cf) && long.TryParse(File.ReadAllText(cf).Trim().Replace(",", "").Replace("_", ""), out var cn) && cn > 0)
        {
            Club["coins"] = (int)Math.Min(cn, 2000000000L);
            Console.WriteLine("[CHEATS] coins set to " + Club["coins"]);
            try { File.Move(cf, cf + ".done", true); } catch { }
        }
        string af = FindFile("allcards.txt");
        if (File.Exists(af))
        {
            string txt = File.ReadAllText(af).Trim().ToLowerInvariant();
            int minR = txt == "all" ? 0 : int.TryParse(txt, out var mr) ? mr : 80;
            var items = (JsonArray)Club["items"]!;
            var have = new HashSet<long>(); foreach (var i in items) have.Add(L(i?["resourceId"]));
            int added = 0;
            foreach (var p in Db)
            {
                if (p.AssetId >= 900000 || p.Rating < minR || have.Contains(p.Id)) continue;
                items.Add(Item(p, Q(p.Rating))); have.Add(p.Id); added++;
            }
            Console.WriteLine($"[CHEATS] added {added} cards (rating >= {minR}), club now has {items.Count}");
            try { File.Move(af, af + ".done", true); } catch { }
        }
        Save();
    }

    static bool SpecialCat(Pack pk) => (pk.Category ?? "").ToLowerInvariant() == "special" && !File.Exists(FindFile("nospecialcat.txt"));
    static string PackGroup(Pack pk) => pk.Category == "bronze" || pk.Category == "silver" ? pk.Category : SpecialCat(pk) ? "special" : "gold";
    static JsonObject PackJson(Pack pk)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string grp = PackGroup(pk); long asset = grp == "bronze" ? 1 : grp == "silver" ? 2 : grp == "special" ? 5 : 3;
        long primary = pk.Cards - 1;
        long gold = grp == "gold" || grp == "special" ? primary : grp == "silver" ? 1 : 0;
        long silver = grp == "silver" ? primary : 1;
        long bronze = grp == "bronze" ? primary : 0;
        if (grp == "gold" || grp == "special") { silver = 1; bronze = 0; }
        string grpName = char.ToUpper(grp[0]) + grp.Substring(1) + " Packs";
        return new JsonObject
        {
            ["actionType"] = "CREATEPACK", ["assetId"] = asset, ["bonus"] = 0L,
            ["currencies"] = new JsonArray(
                new JsonObject { ["name"] = "COINS", ["funds"] = (long)pk.Coins, ["finalFunds"] = (long)pk.Coins },
                new JsonObject { ["name"] = "POINTS", ["funds"] = (long)pk.Points, ["finalFunds"] = (long)pk.Points }),
            ["name"] = pk.Name, ["packName"] = pk.Name, ["displayName"] = pk.Name, ["title"] = pk.Name, ["description"] = pk.Name,
            ["packDescription"] = pk.Bio, ["longDescription"] = pk.Bio, ["shortDescription"] = pk.Bio, ["bio"] = pk.Bio,
            ["label"] = pk.Name, ["displayLabel"] = pk.Name, ["storePackName"] = pk.Name,
            ["nameToken"] = $"FUT_STORE_PACK_{pk.Id}_NAME", ["descriptionToken"] = $"FUT_STORE_PACK_{pk.Id}_DESC",
            ["displayGroup"] = new JsonObject { ["priority"] = asset, ["value"] = grp }, ["displayGroupAssetId"] = asset, ["displayGroupUseDefaultImage"] = true,
            ["end"] = now + 315360000, ["firstPartyStoreId"] = "0", ["id"] = (long)pk.Id, ["packId"] = (long)pk.Id,
            ["packType"] = grp.ToUpperInvariant(), ["packTypeId"] = (long)pk.Id, ["purchasePackType"] = "CARDPACK",
            ["purchaseCount"] = 0L, ["purchaseLimit"] = 999999L, ["quantity"] = (long)pk.Cards,
            ["isPremium"] = pk.Name.Contains("Premium"), ["isSeasonTicketDiscount"] = false, ["points"] = (long)pk.Points, ["coins"] = (long)pk.Coins,
            ["priority"] = (long)pk.Id, ["saleType"] = "NONE", ["sortPriority"] = (long)pk.Id, ["start"] = now - 86400, ["state"] = "active",
            ["unopened"] = false, ["useDefaultImage"] = true, ["visible"] = true, ["isPurchaseable"] = true,
            ["finalPrice"] = (long)pk.Coins, ["originalPrice"] = (long)pk.Coins, ["group"] = grp, ["groupName"] = grpName,
            ["isPromo"] = false, ["category"] = grp, ["categoryId"] = asset,
            ["packContentInfo"] = new JsonObject
            {
                ["itemQuantity"] = (long)pk.Cards, ["goldQuantity"] = gold, ["silverQuantity"] = silver, ["bronzeQuantity"] = bronze, ["rareQuantity"] = (long)pk.RareSlots,
            },
        };
    }
    static JsonArray PackCategories()
    {
        var a = new JsonArray();
        foreach (var (v, id, vis) in new[] { ("bronze", 1, true), ("silver", 2, true), ("gold", 3, true), ("promo", 4, false), ("special", 5, !File.Exists(FindFile("nospecialcat.txt"))) })
        {
            var nm = char.ToUpper(v[0]) + v.Substring(1) + " Packs";
            a.Add(new JsonObject { ["id"] = (long)id, ["categoryId"] = (long)id, ["name"] = nm, ["groupName"] = nm, ["value"] = v, ["displayGroup"] = v, ["sortPriority"] = (long)id, ["visible"] = vis });
        }
        return a;
    }

    static JsonObject DupMap() { if (Club["dupMap"] is not JsonObject d) { d = new JsonObject(); Club["dupMap"] = d; } return d; }
    static bool IsDup(long id) => DupMap().ContainsKey(id.ToString());
    static JsonArray DupList(IEnumerable<long> ids)
    {
        var a = new JsonArray();
        foreach (var id in ids) if (DupMap().TryGetPropertyValue(id.ToString(), out var old) && old != null) a.Add(new JsonObject { ["itemId"] = id, ["duplicateItemId"] = L(old) });
        return a;
    }

    const string Totw26Csv = @"player_id,name,rating,position,pace,shooting,passing,dribbling,defending,physical,type
167495,Neuer,91,GK,91,88,92,91,62,93,totw
1179,Buffon,88,GK,90,88,71,90,49,93,totw
200389,Oblak,84,GK,83,86,73,87,50,86,totw
146530,Dani Alves,87,RB,89,76,84,87,86,78,totw
176676,Marcelo,84,LB,85,71,81,87,86,84,totw
155862,Sergio Ramos,89,CB,82,66,76,70,91,86,totw
164240,Thiago Silva,88,CB,76,60,76,75,91,84,totw
182493,Godín,90,CB,74,60,76,72,94,92,totw
137186,Barzagli,85,CB,77,37,59,66,91,82,totw
184087,Alderweireld,83,CB,66,68,73,68,86,82,totw
41,Iniesta,90,CM,78,77,94,92,63,68,totw
195864,Pogba,87,CM,79,82,85,88,76,89,totw
177003,Modric,87,CM,78,78,86,90,73,70,totw
176635,Özil,89,CAM,76,82,94,91,31,66,totw
161648,Ben Arfa,85,CAM,84,89,87,92,43,83,totw
204485,Mahrez,88,RM,89,88,88,90,41,76,totw
190483,Douglas Costa,84,RM,91,82,87,88,52,68,totw
158023,Messi,93,RW,94,93,92,94,31,67,totw
20801,Ronaldo,93,LW,94,94,87,94,41,85,totw
190871,Neymar Jr,91,LW,93,90,84,94,35,67,totw
188545,Lewandowski,93,ST,86,95,85,93,46,90,totw
176580,Suárez,93,ST,88,95,92,94,56,92,totw
41236,Ibrahimović,93,ST,80,95,92,92,39,93,totw
202126,Kane,88,ST,79,91,82,88,51,87,totw
188567,Aubameyang,88,ST,95,91,84,87,41,79,totw
208830,Vardy,86,ST,92,87,73,84,56,85,totw
";

    static string NormNameTok(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in (s ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetter(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return sb.ToString();
    }
    static string[] NameToks(string s) => NormNameTok(s).Split(' ', StringSplitOptions.RemoveEmptyEntries);
    static int PosGroup(string p) => p switch { "GK" => 0, "CB" or "RB" or "LB" or "RWB" or "LWB" => 1, "CDM" or "CM" or "CAM" or "RM" or "LM" => 2, _ => 3 };

    static List<P> AddTotw(List<P> db)
    {
        string csv = Totw26Csv;
        try { var f = FindFile("totw_week1.csv"); if (File.Exists(f)) { csv = File.ReadAllText(f, Encoding.UTF8); Console.WriteLine("[TOTW] using totw_week1.csv"); } } catch { }
        var real = db.Where(p => p.AssetId > 100 && p.AssetId < 900000 && !p.Special).GroupBy(p => p.AssetId).Select(g => g.First()).ToList();
        var byAsset = real.ToDictionary(p => p.AssetId);
        var toks = real.Select(p => (p, t: NameToks(p.Name))).ToList();
        var outList = new List<P>(db);
        int added = 0, missing = 0;
        foreach (var raw in csv.Split('\n').Skip(1))
        {
            var c = raw.Trim().Split(','); if (c.Length < 11) continue;
            try
            {
                string name = c[1].Trim(); int rating = int.Parse(c[2]); string pos = c[3].Trim().ToUpperInvariant();
                var stats = new int[] { int.Parse(c[4]), int.Parse(c[5]), int.Parse(c[6]), int.Parse(c[7]), int.Parse(c[8]), int.Parse(c[9]) };
                string set = c[10].Trim().ToLowerInvariant(); if (set == "") set = "totw";
                P? m = null;
                if (long.TryParse(c[0].Trim(), out var pid) && pid > 0) byAsset.TryGetValue(pid, out m);
                else
                {
                    var need = NameToks(name).Where(t => t.Length >= 3).ToArray();
                    double best = double.MinValue;
                    foreach (var (cp, ct) in toks)
                    {
                        if (need.Length == 0 || !need.All(n => ct.Contains(n))) continue;
                        double sc = (cp.Pos == pos ? 3 : PosGroup(cp.Pos) == PosGroup(pos) ? 1 : -3) - 0.4 * Math.Abs(cp.Rating - rating);
                        if (sc > best) { best = sc; m = cp; }
                    }
                }
                if (m == null) { missing++; Console.WriteLine($"[TOTW] NOT FOUND in the player database: {name} ({rating} {pos}) - put his FIFA 16 player id in the first column of totw_week1.csv"); continue; }
                long asset = m.AssetId;
                outList.Add(new P((3L << 24) + asset, asset, name, rating, pos, m.Nation, m.League, m.Club, 3, stats, true, set));
                added++;
                Console.WriteLine($"[TOTW] {name} {rating} {pos}  <-  database \"{m.Name}\" (id {asset}, base {m.Rating} {m.Pos})");
            }
            catch (Exception ex) { Console.WriteLine("[TOTW] bad csv line: " + raw.Trim() + "  (" + ex.Message + ")"); }
        }
        Console.WriteLine($"[TOTW] {added} TOTW cards added, {missing} not found");
        return outList;
    }

    static Pack[] AddSetPacks(Pack[] ps)
    {
        var l = ps.ToList();
        void Add(string mode, string name, string bio, int coins, int cards)
        {
            if (l.Any(x => x.Mode == mode)) return;
            int id = 401; while (l.Any(x => x.Id == id)) id++;
            l.Add(new Pack(id, name, "special", bio, coins, 0, cards, 100, 0, 0, 0, 0, mode));
        }
        Add("totw", "FUT REBORN TOTW Pack", "4 cards from FUT REBORN Team of the Week 1", 250000, 4);
        Add("toty", "FUT REBORN TOTY Pack", "2 FUT REBOR Team of the Year cards", 500000, 2);
        return l.ToArray();
    }

    static JsonArray OpenPack(Pack pk)
    {
        var real = Db.Where(p => p.AssetId < 900000 && KeepCard(p)).ToList(); if (real.Count == 0) real = Db;
        int floor = Floor(pk);
        var floored = floor > 0 ? real.Where(p => p.Rating >= floor).ToList() : real;
        if (floored.Count == 0) floored = real;
        if (pk.Mode != "icon") { var noIcons = floored.Where(p => p.Set != "icon").ToList(); if (noIcons.Count > 0) floored = noIcons; }
        bool specialPack = pk.Mode == "special" || (pk.Category ?? "").ToLowerInvariant() == "special";
        var basePool = specialPack || pk.Mode == "all" ? floored : floored.Where(p => !p.Special).ToList();
        if (basePool.Count == 0) basePool = floored;
        int starMin = pk.Coins >= 300000 ? 88 : 85;
        var made = new List<JsonObject>();
        var usedIds = new HashSet<long>();
        if (pk.Mode == "starter") made.AddRange(StarterPackCards());
        for (int i = 0; i < (pk.Mode == "starter" ? 0 : pk.Cards); i++)
        {
            double decay = Luck(pk);
            string q = PickQuality(pk);
            var pool = basePool.Where(p => InQ(p, q)).ToList(); if (pool.Count == 0) pool = basePool;
            if (pk.Mode == "all") pool = floored;
            else if (pk.Mode == "special")
            {
                string want = i < TotyPerPack ? "toty" : i < TotyPerPack + TotsPerPack ? "tots" : "";
                bool Match(P c) => want == "" ? (c.Set != "toty" && c.Set != "tots") : c.Set == want;
                var sp = floored.Where(p => p.Special && !usedIds.Contains(p.Id) && Match(p)).ToList();
                if (sp.Count == 0) sp = floored.Where(p => p.Special && !usedIds.Contains(p.Id)).ToList();
                if (sp.Count > 0) pool = sp;
                if (want != "") decay = TotyDecay;
            }
            else if (pk.Mode == "icon")
            {
                var ic = real.Where(p => p.Set == "icon" && !usedIds.Contains(p.Id)).ToList();
                if (ic.Count == 0) ic = real.Where(p => p.Set == "icon").ToList();
                if (ic.Count == 0) throw new Exception("Icon Pack opened but no icon cards are loaded (see [ICONS] lines at startup)");
                pool = ic;
                decay = 0.90;
            }
            else if (pk.Mode == "totw" || pk.Mode == "toty")
            {
                var setPool = real.Where(p => p.Set == pk.Mode).ToList();
                if (setPool.Count > 0) pool = setPool;
                decay = pk.Mode == "toty" ? TotyDecay : 0.93;
            }
            else if (i < pk.RareSlots) { var rr = pool.Where(p => p.Rare == 1).ToList(); if (rr.Count > 0) pool = rr; }
            var fresh = pool.Where(p => !usedIds.Contains(p.Id)).ToList(); if (fresh.Count > 0) pool = fresh;
            var pick = WeightedPick(pool, decay);
            usedIds.Add(pick.Id);
            if (i == pk.Cards - 1 && pk.Special > 0 && Rng.NextDouble() < pk.Special)
            {
                var top = basePool.Where(p => p.Rare == 1 && p.Rating >= starMin).ToList(); if (top.Count > 0) pick = top[Rng.Next(top.Count)];
            }
            made.Add(Item(pick, Q(pick.Rating)));
        }
        var ids = made.Select(m => L(m["id"])).OrderByDescending(x => x).ToList();
        made = made.OrderByDescending(m => L(m["rating"])).ThenByDescending(m => L(m["rareflag"])).ToList();
        if (made.Count > 0 && Managers.Count > 0 && pk.Mode != "totw" && pk.Mode != "toty" && pk.Mode != "icon" && pk.Mode != "starter" && Rng.NextDouble() < ManagerChance(pk))
        {
            var mg = ManagerItem(Managers[Rng.Next(Managers.Count)]);
            made[made.Count - 1] = mg;
            Console.WriteLine($"[PACK] {pk.Name}: manager {mg["name"]} added");
        }
        var res = new JsonArray(); var un = new JsonArray();
        if (Club["unassigned"] is JsonArray oldUn) foreach (var o0 in oldUn) if (IsDup(L(o0))) un.Add(L(o0));
        for (int i = 0; i < made.Count; i++)
        {
            made[i]["id"] = ids[i];
            var itemsArr = (JsonArray)Club["items"]!;
            if (made[i]["itemType"]?.ToString() == "player")
            {
                long asset = L(made[i]["assetId"]);
                var have = itemsArr.FirstOrDefault(x => x != null && x["itemType"]?.ToString() == "player" && L(x["assetId"]) == asset && !IsDup(L(x["id"])) && x["pile"]?.ToString() != "trade");
                if (have != null) { DupMap()[ids[i].ToString()] = L(have["id"]); Console.WriteLine($"[DUPLICATE] {made[i]["name"]} (id {ids[i]}) - you already own this player"); }
            }
            itemsArr.Add(made[i].DeepClone()); un.Add(ids[i]); res.Add(made[i]);
        }
        Club["unassigned"] = un;
        if (pk.Mode == "starter") StarterPackOpened(res);
        Save(); return res;
    }

    static JsonObject PackReply(Pack pk, JsonArray cards)
    {
        var o = CreditsObj();
        o["numberItems"] = cards.Count; o["purchasedPackId"] = pk.Id; o["packName"] = pk.Name;
        o["duplicateItemIdList"] = DupList(cards.Select(c => L(c!["id"])));
        o["itemList"] = cards.DeepClone(); o["items"] = cards.DeepClone(); o["itemData"] = cards;
        return o;
    }

    static long L(JsonNode? n)
    {
        if (n == null) return 0;
        return long.TryParse(n.ToJsonString().Trim('"').Split('.')[0], out var v) ? v : 0;
    }
    static string? _mode;
    public static string GetMode()
    {
        if (_mode != null) return _mode;
        string? m = Environment.GetEnvironmentVariable("LOCALFUT_UT");
        if (string.IsNullOrWhiteSpace(m)) { try { m = File.ReadAllText(FindFile("mode.txt")); } catch { } }
        _mode = string.IsNullOrWhiteSpace(m) ? "on" : m.Trim().ToLowerInvariant();
        Console.WriteLine("[FUT REBORN] UT mode = " + _mode + "   (change it in mode.txt: off / credits / store / tournament / draft / on)");
        return _mode;
    }

    static bool _failedOnce;
    static bool _bannerShown;
    public static (int, string, byte[], Dictionary<string, string>?) Handle(string method, string target, byte[] body)
    {
        try { return HandleGuarded(method, target, body); }
        catch (Exception ex)
        {
            if (!_failedOnce) { _failedOnce = true; Console.WriteLine("[FUT REBORN] BACKEND CRASH (answering {} instead): " + ex); }
            return (200, "application/json", Encoding.UTF8.GetBytes("{}"), null);
        }
    }

    static (int, string, byte[], Dictionary<string, string>?) HandleGuarded(string method, string target, byte[] body)
    {
        if (!_bannerShown) { _bannerShown = true; Console.WriteLine("[BUILD] FUT REBORN 2026-10-25-c RUNNING: My Squads (list / create / save, ids from 1), real manager in every squad"); }
        {
            string pth = (target.Contains('?') ? target[..target.IndexOf('?')] : target).ToLowerInvariant();
            if (pth.StartsWith("/fut/playerheads/"))
            {
                Console.WriteLine("[HEADS v3] request " + pth);
                if (pth.EndsWith(".dds"))
                {
                    var head = Fut.TryHead(pth); head = SpecialFaces.Apply(pth, head);
                    if (head != null) return (200, "application/octet-stream", head, null);
                }
                if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "heads_empty200.txt")))
                {
                    Console.WriteLine("[HEADS v3] not found -> 404 " + pth);
                    return (404, "text/plain", Array.Empty<byte>(), null);
                }
            }
        }
        { var tr = HandleTrophyAssets(target); if (tr != null) return tr.Value; }
        {
            string ap = (target.Contains('?') ? target[..target.IndexOf('?')] : target);
            string apl = ap.ToLowerInvariant();
            if (apl.StartsWith("/fut/") && !apl.StartsWith("/fut/playerheads/") && !apl.Contains(".."))
            {
                string rel = ap.Substring(5).Replace('/', Path.DirectorySeparatorChar);
                foreach (var root in new[] { Path.Combine(Directory.GetCurrentDirectory(), "fut_assets"), Path.Combine(AppContext.BaseDirectory, "fut_assets") })
                {
                    try
                    {
                        string f = Path.Combine(root, rel);
                        if (File.Exists(f))
                        {
                            string ext = Path.GetExtension(f).ToLowerInvariant();
                            string ct = ext switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".xml" => "text/xml", ".json" => "application/json", ".dds" or ".big" => "application/octet-stream", _ => "application/octet-stream" };
                            Console.WriteLine("[ASSET] served " + f);
                            return (200, ct, File.ReadAllBytes(f), null);
                        }
                    }
                    catch { }
                }
                if (!apl.EndsWith(".xml") && !apl.StartsWith("/fut/packs/")) Console.WriteLine("[ASSET-MISSING] " + ap + "   (put the file at fut_assets\\" + rel + ")");
            }
        }
        var empty = (200, "application/json", Encoding.UTF8.GetBytes("{}"), (Dictionary<string, string>?)null);
        string mode = GetMode();
        string lp = (target.Contains('?') ? target[..target.IndexOf('?')] : target).ToLowerInvariant();
        bool allowed = mode switch
        {
            "credits" => lp.EndsWith("/user/credits"),
            "store"   => lp.EndsWith("/user/credits") || lp.Contains("purchasegroup") || lp.Contains("purchased/items")
                         || lp.Contains("store/transaction") || lp.Contains("store/purchase"),
            "tournament" => lp.EndsWith("/user/credits") || lp.Contains("tournament"),
            "draft"   => lp.EndsWith("/user/credits") || lp.Contains("squad/mode/draft") || lp.Contains("draft") || lp.Contains("tournament"),
            "on"      => true,
            _         => false,
        };
        if (!allowed) return empty;
        try
        {
            var r = HandleInner(method, target, body);
            try
            {
                string txt = Encoding.UTF8.GetString(r.Item3);
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "capture_ut.log"),
                    $"{DateTime.Now:O} {method} {target}\n  REQ : {Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 800))}\n  RESP: {r.Item1} {(txt.Length > 1500 ? txt[..1500] + "..." : txt)}\n\n");
            }
            catch { }
            return r;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FUT REBORN] ERROR in " + target + " : " + ex);
            try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "capture_ut.log"), $"{DateTime.Now:O} ERROR {target}: {ex}\n\n"); } catch { }
            return empty;
        }
    }

    static (int, string, byte[], Dictionary<string, string>?) HandleInner(string method, string target, byte[] body)
    {
        string path = (target.Contains('?') ? target[..target.IndexOf('?')] : target).ToLowerInvariant().TrimEnd('/');
        JsonNode? Req() { try { return JsonNode.Parse(Encoding.UTF8.GetString(body)); } catch { return null; } }
        (int, string, byte[], Dictionary<string, string>?) J(JsonNode n) => (200, "application/json", Encoding.UTF8.GetBytes(n.ToJsonString()), null);

        if (method == "POST" && path.EndsWith("/ut/auth")) { try { SwitchClubForDevice(body); } catch (Exception ex) { Console.WriteLine("[USERS] switch failed: " + ex.Message); } }

        lock (Lock)
        {
            { var dr = HandleDraft(method, path, target, body); if (dr != null) return dr.Value; }
            { var om = HandleOfflineMatchStart(method, path, body); if (om != null) return om.Value; }
            { var me = HandleOfflineMatchEnd(method, path, body); if (me != null) return me.Value; }
            { var sp = HandleSeasonSP(method, path, target, body); if (sp != null) return sp.Value; }
            { var sr = HandleSeason(method, path, target, body); if (sr != null) return sr.Value; }
            { var tw = HandleTotw(method, path, target, body); if (tw != null) return tw.Value; }
            { var mk = HandleMarket(method, path, target, body); if (mk != null) return mk.Value; }
            { var sqh = HandleSquads(method, path, target, body); if (sqh != null) return sqh.Value; }
            { var ln = HandleLoan(method, path, target, body); if (ln != null) return ln.Value; }
            { var pf = HandleProfile(method, path, target, body); if (pf != null) return pf.Value; }
            { var cs = HandleConsumables16(method, path, target, body); if (cs != null) return cs.Value; }
            { var f15 = HandleFifa15Blank(method, path, target, body); if (f15 != null) return f15.Value; }

            if (path.EndsWith("/game/fifa16/user") && !File.Exists(FindFile("user_empty.txt")))
                { var uo = UserObjFifa16();
                  if (!File.Exists(FindFile("user_noactives.txt"))) { var act = IdentityActives(); uo["actives"] = act; Console.WriteLine("[USER] actives sent: " + string.Join(", ", act.Select(x => x["itemState"] + "=" + x["resourceId"]))); }
                  return J(uo); }
            if (path.Contains("phishing/") || path.EndsWith("/settings") || path.EndsWith("/user") || path.EndsWith("/userdata")
                || path.Contains("clientdata/tutorialpopups") || path.Contains("match/reset")
                || path.EndsWith("/ut/auth") || path.EndsWith("/pow/auth"))
                return J(new JsonObject());

            if (path.EndsWith("/user/credits"))
                return J(CreditsObj());
            if (path.EndsWith("/hub") && !File.Exists(FindFile("hubcredits_off.txt")))
            {
                var hb = CreditsObj(); hb["clubName"] = ClubNameNow(); hb["personaName"] = Persona; hb["divisionOffline"] = Math.Max(1, Math.Min(10, SPI(SeasonSPState(), "division"))); hb["divisionOnline"] = 10;
                if (!File.Exists(FindFile("hubtrade_off.txt")))
                {
                    var htp = TradePileJson();
                    hb["auctionCount"] = MarketLiveCount(); hb["liveTransfers"] = MarketLiveCount(); hb["liveTransferCount"] = MarketLiveCount(); hb["marketCount"] = MarketLiveCount();
                    hb["clubPlayers"] = ((JsonArray)Club["items"]!).Count(x => x != null && x["totwOpp"] == null && x["itemType"]?.ToString() == "player");
                    hb["watchlist"] = new JsonObject { ["count"] = 0L, ["outbid"] = 0L, ["winning"] = 0L };
                    hb["tradePile"] = new JsonObject { ["count"] = htp["count"]!.DeepClone(), ["selling"] = htp["selling"]!.DeepClone(), ["sold"] = htp["sold"]!.DeepClone(), ["maximum"] = 100L };
                }
                return J(hb);
            }
            if (path.Contains("clientdata/pilesize"))
                {
                var pe = new JsonArray(new JsonObject { ["value"] = 100, ["key"] = 2 }, new JsonObject { ["value"] = 100, ["key"] = 4 });
                if (!File.Exists(FindFile("pilesize_basic.txt")))
                    foreach (var k in new[] { 0, 1, 3, 5, 6, 7, 8, 9, 10 }) pe.Add(new JsonObject { ["value"] = 2000, ["key"] = k });
                return J(new JsonObject { ["entries"] = pe });
            }
            if (path.EndsWith("/hub") && File.Exists(FindFile("hubtrade.txt")))
            {
                var htp = TradePileJson();
                return J(new JsonObject { ["auctionCount"] = 0L, ["liveTransfers"] = 0L, ["tradePile"] = new JsonObject { ["count"] = htp["count"]!.DeepClone(), ["selling"] = htp["selling"]!.DeepClone(), ["sold"] = htp["sold"]!.DeepClone(), ["maximum"] = 100L } });
            }
            if (path.EndsWith("/hub") || path.Contains("clientdata/userhubdata")) return J(new JsonObject());

            if (path.EndsWith("/ut/auth") || path.EndsWith("/pow/auth"))
                return J(new JsonObject { ["sid"] = "fut-reborn-session", ["protocol"] = "http", ["ipPort"] = "127.0.0.1:8080", ["serverTime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });

            if (path.EndsWith("/user") || path.EndsWith("/clubuser") || path.EndsWith("/userdata") || path.EndsWith("/user/club"))
                return J(new JsonObject
                {
                    ["userInfo"] = new JsonObject { ["nucleusId"] = 1000000002, ["personaId"] = 1000000002, ["personaName"] = Persona, ["clubName"] = ClubNameNow(), ["clubAbbr"] = ClubAbbrNow(), ["established"] = "2026-10-11" },
                    ["clubName"] = ClubNameNow(), ["personaName"] = Persona,
                });

            if (path.Contains("bank") || path.EndsWith("/credits"))
                return J(new JsonObject { ["credits"] = Club["coins"]!.DeepClone(), ["balance"] = Club["points"]!.DeepClone(), ["currencies"] = new JsonArray(
                    new JsonObject { ["name"] = "COINS", ["funds"] = Club["coins"]!.DeepClone(), ["finalFunds"] = Club["coins"]!.DeepClone() },
                    new JsonObject { ["name"] = "POINTS", ["funds"] = Club["points"]!.DeepClone(), ["finalFunds"] = Club["points"]!.DeepClone() }) });

            if (path.Contains("purchased/items"))
            {
                if (method != "GET")
                {
                    var rq = Req(); int pidBuy = rq?["packId"]?.GetValue<int>() ?? rq?["id"]?.GetValue<int>() ?? 0;
                    var pkBuy = Packs.FirstOrDefault(x => x.Id == pidBuy);
                    if (pkBuy != null)
                    {
                        if (StarterBlocked(pkBuy)) return (470, "application/json", Encoding.UTF8.GetBytes("{\"reason\":\"starter pack already claimed\"}"), null);
                        int have = (int)L(Club["coins"]);
                        if (have < pkBuy.Coins) return (470, "application/json", Encoding.UTF8.GetBytes("{\"reason\":\"not enough coins\"}"), null);
                        Club["coins"] = have - pkBuy.Coins;
                        return J(PackReply(pkBuy, OpenPack(pkBuy)));
                    }
                }
                {
                    var unIds = new HashSet<long>(((Club["unassigned"] as JsonArray) ?? new JsonArray()).Select(n => L(n)));
                    var unArr = new JsonArray();
                    foreach (var c in (JsonArray)Club["items"]!) if (unIds.Contains(L(c!["id"]))) unArr.Add(c.DeepClone());
                    return J(new JsonObject { ["numberItems"] = unArr.Count, ["duplicateItemIdList"] = DupList(unArr.Select(c => L(c!["id"]))), ["itemData"] = unArr });
                }
            }

            if (method == "PUT" && System.Text.RegularExpressions.Regex.IsMatch(path, @"/squad/\d+$"))
            {
                var sq = Req();
                if (sq?["players"] is JsonArray sqp) foreach (var slot in sqp) if (IsDup(L(slot?["itemData"]?["id"]))) slot!["itemData"] = new JsonObject { ["id"] = 0, ["dream"] = false };
                if (sq?["players"] is JsonArray sqp2) foreach (var slot2 in sqp2) { long sid2 = L(slot2?["itemData"]?["id"]); if (sid2 > 0 && !IsPlayerCard(sid2)) slot2!["itemData"] = new JsonObject { ["id"] = 0, ["dream"] = false }; }
                if (sq != null)
                {
                    var oldMg = Club["squadBody"]?["manager"]?.DeepClone();
                    if (sq["manager"] == null && oldMg != null) sq["manager"] = oldMg;
                    long mgId = 0; try { mgId = L(sq["manager"]?[0]?["id"]); } catch { }
                    bool okMg = mgId > 0 && ((JsonArray)Club["items"]!).Any(mgi => L(mgi?["id"]) == mgId && mgi?["itemType"]?.ToString() == "staff");
                    if (!okMg) { sq["manager"] = new JsonArray(new JsonObject { ["id"] = 0, ["dream"] = false }); Console.WriteLine("[SQUAD] no manager in the squad -> put a manager from your club into the squad before playing"); }
                    Club["squadBody"] = sq.DeepClone(); Save();
                }
                return J(new JsonObject());
            }
            if (method == "GET" && (path.Contains("squad/active") || path.EndsWith("/squad") || System.Text.RegularExpressions.Regex.IsMatch(path, @"/squad/\d+$")))
            {
                if (File.Exists(FindFile("nosquad.txt"))) return J(new JsonObject());
                var sb0 = (Club["squadBody"] as JsonObject)?.DeepClone() as JsonObject;
                var allCards = (JsonArray)Club["items"]!;
                if (sb0 == null || (sb0["players"] as JsonArray)?.Count is null or 0)
                {
                    var ids = ((JsonArray)Club["squad"]!).Select(n => L(n)).ToList();
                    var slots = new JsonArray();
                    for (int i = 0; i < 18; i++)
                        slots.Add(new JsonObject { ["index"] = i, ["itemData"] = new JsonObject { ["id"] = i < ids.Count ? ids[i] : 0L, ["dream"] = false }, ["kitNumber"] = i + 1 });
                    int ovr = ids.Take(11).Select(id => (int)L(allCards.FirstOrDefault(c => L(c!["id"]) == id)?["rating"])).DefaultIfEmpty(0).Sum() / Math.Max(1, Math.Min(11, ids.Count));
                    sb0 = new JsonObject { ["id"] = 0, ["custom"] = "[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]", ["squadName"] = Brand + " XI", ["chemistry"] = 100, ["starRating"] = ovr >= 75 ? 5 : ovr >= 65 ? 3 : 2, ["rating"] = ovr,
                        ["formation"] = "f433", ["squadType"] = "REGULAR_SQUAD", ["manager"] = new JsonArray(new JsonObject { ["id"] = 0, ["dream"] = false }), ["players"] = slots };
                    Club["squadBody"] = sb0.DeepClone(); Save();
                }
                CleanSquadBody(sb0);
                Club["squadBody"] = sb0.DeepClone();
                if (string.IsNullOrEmpty(sb0["squadName"]?.GetValue<string>())) sb0["squadName"] = Brand + " XI";
                if (!File.Exists(FindFile("oldsquad.txt"))) return J(NativeSquadReply(sb0));
                if (!File.Exists(FindFile("slimsquad.txt")) && sb0["players"] is JsonArray pls)
                    foreach (var pl in pls)
                    {
                        long pid2 = L(pl?["itemData"]?["id"]); if (pid2 <= 0) continue;
                        var card = allCards.FirstOrDefault(c => L(c!["id"]) == pid2)?.DeepClone() as JsonObject; if (card == null) continue;
                        card["dream"] = false; pl!["itemData"] = card;
                    }
                return J(sb0);
            }

            if ((method == "DELETE" || (method == "POST" && path.Contains("/delete/"))) && (path.EndsWith("/item") || System.Text.RegularExpressions.Regex.IsMatch(path, @"/item/\d+$")))
            {
                var sellIds = new List<long>();
                var mId = System.Text.RegularExpressions.Regex.Match(path, @"/item/(\d+)$"); if (mId.Success) sellIds.Add(long.Parse(mId.Groups[1].Value));
                foreach (System.Text.RegularExpressions.Match mq in System.Text.RegularExpressions.Regex.Matches(target, @"[?&]item(?:Id|Ids)=([\d,]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    foreach (var part in mq.Groups[1].Value.Split(',')) if (long.TryParse(part, out var qid)) sellIds.Add(qid);
                var rb = Req();
                if (rb?["itemId"] is JsonArray ra) { foreach (var x in ra) sellIds.Add(L(x)); }
                else if (rb?["itemId"] != null) sellIds.Add(L(rb["itemId"]));
                var its = (JsonArray)Club["items"]!; long gained = 0; var gone = new JsonArray();
                foreach (var did in sellIds.Distinct())
                {
                    var hit = its.FirstOrDefault(i => L(i!["id"]) == did); if (hit == null) continue;
                    int r = (int)L(hit["rating"]); bool rr = L(hit["rareflag"]) == 1;
                    gained += (hit["itemType"]?.ToString() ?? "player") == "player" ? QuickSellFor(r, L(hit["rareflag"])) : Math.Max(50, (int)L(hit["discardValue"]));
                    its.Remove(hit); DupMap().Remove(did.ToString()); DupPromote(did); RemoveAuctionFor(did); gone.Add(new JsonObject { ["id"] = did });
                    var sqa = (JsonArray)Club["squad"]!; var sh = sqa.FirstOrDefault(n => L(n) == did); if (sh != null) sqa.Remove(sh);
                    if (Club["squadBody"]?["players"] is JsonArray sp)
                        foreach (var slot in sp) if (L(slot?["itemData"]?["id"]) == did) slot!["itemData"] = new JsonObject { ["id"] = 0, ["dream"] = false };
                    PurgeFromSquads(did);
                }
                Club["coins"] = (int)L(Club["coins"]) + (int)gained;
                Save();
                Console.WriteLine($"[QUICKSELL] asked for ids=[{string.Join(",", sellIds)}], sold {gone.Count}, +{gained} coins -> balance {Club["coins"]}" + (gone.Count == 0 ? "   (NO MATCHING CARD IN THE CLUB FILE)" : ""));
                var qs = CreditsObj(); qs["totalCredits"] = gained; qs["items"] = gone; qs["itemData"] = gone.DeepClone(); qs["coins"] = Club["coins"]!.DeepClone(); qs["auctionInfo"] = new JsonArray(); qs["total"] = 0;
                return J(qs);
            }

            if (path.Contains("marketdata/pricelimits"))
            {
                long def = 0; var qi = target.IndexOf("defId=", StringComparison.OrdinalIgnoreCase); if (qi >= 0) long.TryParse(new string(target[(qi + 6)..].TakeWhile(char.IsDigit).ToArray()), out def);
                return J(new JsonArray(new JsonObject { ["itemId"] = def, ["minPrice"] = 150, ["maxPrice"] = 15000000 }));
            }
            if (path.EndsWith("/tradepile/counts") || path.EndsWith("/auctionhouse/counts"))
            {
                var tpc = TradePileJson();
                tpc["auctionCount"] = tpc["count"]!.DeepClone(); tpc["tradePileItems"] = tpc["count"]!.DeepClone(); tpc["transferListCount"] = tpc["count"]!.DeepClone();
                return J(tpc);
            }
            if (path.EndsWith("/tradepile") && method == "GET") return J(TradePileJson());
            if (method == "DELETE" && path.Contains("/trade/"))
            {
                var au = Auctions(); var tail = path[(path.LastIndexOf('/') + 1)..];
                for (int k = au.Count - 1; k >= 0; k--)
                    if (au[k] is JsonObject ao && (ao["sold"]?.GetValue<bool>() ?? false) && (tail == "sold" || L(ao["tradeId"]) == (long.TryParse(tail, out var tidDel) ? tidDel : -1))) au.RemoveAt(k);
                Save(); return J(new JsonObject());
            }
            if (path.EndsWith("/watchlist") || path.Contains("/transfermarket") || path.EndsWith("/tradepile"))
                return J(new JsonObject { ["auctionInfo"] = new JsonArray(), ["bidTokens"] = new JsonObject(), ["credits"] = Club["coins"]!.DeepClone(), ["total"] = 0,
                    ["currencies"] = new JsonArray(new JsonObject { ["name"] = "COINS", ["funds"] = Club["coins"]!.DeepClone(), ["finalFunds"] = Club["coins"]!.DeepClone() }) });
            if (path.EndsWith("/auctionhouse") && method == "POST")
                return J(new JsonObject { ["id"] = CreateAuction(Req()) });
            if (method == "PUT" && path.EndsWith("/item"))
            {
                var mv = Req(); var arr2 = new JsonArray();
                Console.WriteLine("[MOVE] PUT /item body: " + (mv?.ToJsonString() ?? "(none)"));
                var okIds = new HashSet<long>();
                foreach (var d in (mv?["itemData"] as JsonArray) ?? new JsonArray())
                {
                    long did0 = L(d?["id"]); string pile0 = d?["pile"]?.ToString() ?? "";
                    if (IsDup(did0) && pile0 != "trade" && DupOriginalAlive(did0))
                    {
                        try
                        {
                            long oldId = L(DupMap()[did0.ToString()]);
                            var oldI = FindItem(oldId); var newI = FindItem(did0);
                            if (oldI != null && newI != null)
                            {
                                foreach (var k in new[] { "contract", "contracts" }) { long a = 0, c = 0; try { a = L(oldI[k]); } catch { } try { c = L(newI[k]); } catch { } if (c > a) oldI[k] = c; }
                                long fo = 0, fn = 0; try { fo = L(oldI["fitness"]); } catch { } try { fn = L(newI["fitness"]); } catch { } if (fn > fo) oldI["fitness"] = fn;
                            }
                            Console.WriteLine($"[DUPLICATE] card {did0} swapped with {oldId} (swap={d?["swap"]}); the duplicate stays in new items - quick sell or list it");
                        }
                        catch (Exception exs) { Console.WriteLine("[DUPLICATE] swap failed: " + exs.Message); }
                        arr2.Add(new JsonObject { ["id"] = d?["id"]?.DeepClone(), ["pile"] = d?["pile"]?.DeepClone(), ["success"] = true });
                        continue;
                    }
                    if (IsDup(did0)) DupMap().Remove(did0.ToString());
                    arr2.Add(new JsonObject { ["id"] = d?["id"]?.DeepClone(), ["pile"] = d?["pile"]?.DeepClone(), ["success"] = true }); SetPile(did0, pile0); okIds.Add(did0);
                }
                if (Club["unassigned"] is JsonArray unOld && unOld.Count > 0)
                {
                    var moved = okIds;
                    var keep = new JsonArray(); foreach (var n in unOld) if (!moved.Contains(L(n))) keep.Add(L(n));
                    Club["unassigned"] = keep; Save();
                }
                try { StarterPackSettled(); } catch (Exception ex) { Console.WriteLine("[AUTOSQUAD] after starter pack failed: " + ex.Message); }
                return J(new JsonObject { ["itemData"] = arr2 });
            }

            if (path.Contains("inventory/item/list") || path.EndsWith("/club") && method == "GET")
                {
                var qc = QueryOf(target);
                bool isClubSearch = path.EndsWith("/club");
                int pgStart = 0, pgCount = int.MaxValue;
                foreach (var sk in new[] { "start", "offset", "startIndex" }) if (qc.TryGetValue(sk, out var sv) && int.TryParse(sv, out var svi)) { pgStart = Math.Max(0, svi); break; }
                if (qc.TryGetValue("count", out var cv) && int.TryParse(cv, out var cvi) && cvi > 0) pgCount = cvi;
                var clubOnly = new JsonArray(); int pgIdx = 0, pgTotal = 0;
                foreach (var c0 in ((JsonArray)Club["items"]!).Reverse())
                {
                    if (c0 == null || c0["pile"]?.ToString() == "trade" || IsDup(L(c0["id"]))) continue;
                    if (File.Exists(FindFile("hide_identity.txt")) && IdentityStates.Contains(c0["itemState"]?.ToString() ?? "")) continue;
                    if (isClubSearch && !ClubSearchMatch(c0, qc)) continue;
                    pgTotal++; if (pgIdx++ >= pgStart && clubOnly.Count < pgCount) clubOnly.Add(c0.DeepClone());
                }
                if (isClubSearch) Console.WriteLine($"[CLUB] search type={(qc.TryGetValue("type", out var ctp) ? ctp : "-")} position={(qc.TryGetValue("position", out var cps) ? cps : "-")} level={(qc.TryGetValue("level", out var clv) ? clv : "-")} -> {pgTotal} cards, page {clubOnly.Count}");
                return J(new JsonObject { ["itemData"] = clubOnly, ["items"] = clubOnly.DeepClone(), ["totalResults"] = pgTotal });
            }

            if (path.Contains("squad/active") || path.EndsWith("/squad"))
            {
                if (!File.Exists(FindFile("realsquad.txt"))) return J(new JsonObject());
                var allItems = (JsonArray)Club["items"]!;
                var starterIds = ((JsonArray)Club["squad"]!).Select(n => L(n)).ToList();
                JsonNode? Find(long id) => allItems.FirstOrDefault(i => L(i!["id"]) == id);
                var bench = allItems.Where(i => !starterIds.Contains(L(i!["id"]))).Take(7).ToList();

                var slots = new JsonArray();
                for (int i = 0; i < 18; i++)
                {
                    JsonNode? card = i < 11
                        ? (i < starterIds.Count ? Find(starterIds[i]) : null)
                        : (i - 11 < bench.Count ? bench[i - 11] : null);
                    slots.Add(new JsonObject
                    {
                        ["index"] = i,
                        ["itemData"] = card?.DeepClone() ?? new JsonObject { ["id"] = 0 },
                        ["loyaltyBonus"] = 0,
                    });
                }
                return J(new JsonObject { ["id"] = 0, ["squadName"] = Brand, ["formation"] = "f433", ["rating"] = 60, ["chemistry"] = 0, ["players"] = slots });
            }

            if (path.Contains("/fut/packs/loc/") && path.EndsWith(".xml"))
            {
                string xf = FindFile("storepackdescriptions.xml");
                if (File.Exists(xf)) return (200, "text/xml", File.ReadAllBytes(xf), null);
                var xs = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<xliff version=\"1.2\">\n  <file original=\"storepackdescriptions\" source-language=\"en-US\" datatype=\"plaintext\">\n    <body>\n");
                foreach (var pk0 in Packs.OrderBy(x => x.Id))
                {
                    string nm = System.Security.SecurityElement.Escape(pk0.Name), ds = System.Security.SecurityElement.Escape(pk0.Bio);
                    xs.Append($"      <trans-unit id=\"FUT_STORE_PACK_{pk0.Id}_NAME\" resname=\"FUT_STORE_PACK_{pk0.Id}_NAME\"><source>{nm}</source></trans-unit>\n");
                    xs.Append($"      <trans-unit id=\"FUT_STORE_PACK_{pk0.Id}_DESC\" resname=\"FUT_STORE_PACK_{pk0.Id}_DESC\"><source>{ds}</source></trans-unit>\n");
                    xs.Append($"      <trans-unit id=\"FUT_STORE_PACK_{pk0.Id}_NAME_MOBILE\" resname=\"FUT_STORE_PACK_{pk0.Id}_NAME_MOBILE\"><source>{nm}</source></trans-unit>\n");
                }
                xs.Append("    </body>\n  </file>\n</xliff>");
                return (200, "application/xml; charset=utf-8", Encoding.UTF8.GetBytes(xs.ToString()), null);
            }

            if (path.Contains("/pow/") && path.EndsWith("/catalog/list"))
                return J(new JsonObject { ["catalogs"] = new JsonArray(
                    new JsonObject { ["catalogId"] = 0L, ["name"] = "FUT Store" }, new JsonObject { ["catalogId"] = 1L, ["name"] = "FIFAPoints" }) });
            if (path.Contains("/pow/") && path.Contains("catalog") && path.EndsWith("/item/list"))
            {
                string pf = FindFile("powcatalog.json");
                if (File.Exists(pf)) return (200, "application/json", File.ReadAllBytes(pf), null);
                string pm = "json-empty"; try { var pmf = FindFile("powmode.txt"); if (File.Exists(pmf)) pm = File.ReadAllText(pmf).Trim().ToLowerInvariant(); } catch { }
                var items = new JsonArray();
                foreach (var pk in Packs)
                    items.Add(new JsonObject { ["itemId"] = pk.Id, ["id"] = pk.Id, ["name"] = pk.Name, ["title"] = pk.Name, ["description"] = pk.Bio, ["price"] = pk.Coins, ["currency"] = "COINS", ["type"] = "pack", ["available"] = true });
                switch (pm)
                {
                    case "404": return (404, "text/plain", Encoding.UTF8.GetBytes("not found"), null);
                    case "204": return (204, "application/json", new byte[0], null);
                    case "500": return (500, "text/plain", Encoding.UTF8.GetBytes("error"), null);
                    case "empty": return (200, "application/json", new byte[0], null);
                    case "xml-empty": return (200, "text/xml", Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?><items totalCount=\"0\" offset=\"0\" count=\"0\"/>"), null);
                    case "xml-packs":
                        var xs2 = new StringBuilder($"<?xml version=\"1.0\" encoding=\"utf-8\"?><items totalCount=\"{Packs.Count()}\" offset=\"0\" count=\"{Packs.Count()}\">");
                        foreach (var pk in Packs) xs2.Append($"<item><itemId>{pk.Id}</itemId><name>{System.Security.SecurityElement.Escape(pk.Name)}</name><description>{System.Security.SecurityElement.Escape(pk.Bio)}</description><price>{pk.Coins}</price></item>");
                        return (200, "text/xml", Encoding.UTF8.GetBytes(xs2.Append("</items>").ToString()), null);
                    case "json-packs": return J(new JsonObject { ["totalCount"] = items.Count, ["offset"] = 0, ["count"] = items.Count, ["items"] = items, ["endOfList"] = true });
                    default: return J(new JsonObject { ["items"] = new JsonArray(), ["endOfList"] = true });
                }
            }

            if (path.Contains("store/catalog") || path.Contains("catalog/list") || path.Contains("/item/list") && path.Contains("store") && !path.Contains("/pow/") || path.Contains("store/purchasegroup"))
            {
                var arr = new JsonArray();
                foreach (var pk in Packs)
                {
                    if (StarterBlocked(pk)) continue;
                    arr.Add(PackJson(pk));
                }
                return J(new JsonObject { ["purchase"] = arr, ["categoryInfo"] = PackCategories(), ["categories"] = PackCategories(), ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            }
            if (path.Contains("store/purchase") || path.Contains("/pack/open") || (path.Contains("transaction") && method == "PUT"))
            {
                var r = Req();
                int pid = r?["packId"]?.GetValue<int>() ?? r?["id"]?.GetValue<int>() ?? 0;
                var pk = Packs.FirstOrDefault(x => x.Id == pid);
                if (pk != null)
                {
                    if (StarterBlocked(pk)) return (470, "application/json", Encoding.UTF8.GetBytes("{\"reason\":\"starter pack already claimed\"}"), null);
                    int coins = (int)L(Club["coins"]);
                    if (coins < pk.Coins) return (470, "application/json", Encoding.UTF8.GetBytes("{\"reason\":\"not enough coins\"}"), null);
                    Club["coins"] = coins - pk.Coins;
                    return J(PackReply(pk, OpenPack(pk)));
                }
            }

            if (path.Contains("tournament"))
                Console.WriteLine($"[TOURNAMENT-REQ] {method} {target} body: {Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 400))}");
            if (path.Contains("tournament/user/list"))
            {
                var mine = Club["tournaments"] as JsonArray ?? new JsonArray();
                string ut = "1"; try { var utf = FindFile("tournament_try.txt"); if (File.Exists(utf)) ut = File.ReadAllText(utf).Trim(); } catch { }
                if (ut == "10" || ut == "13" || ut == "14") return J(new JsonObject { ["tournament"] = mine.DeepClone() });
                if (ut == "11") return J(new JsonObject { ["tournamentData"] = mine.DeepClone() });
                if (ut == "12") return J(new JsonObject { ["tournament"] = mine.DeepClone(), ["tournamentData"] = mine.DeepClone(), ["tournaments"] = mine.DeepClone(), ["userTournaments"] = mine.DeepClone(), ["totalResults"] = mine.Count });
                return J(new JsonObject { ["tournaments"] = mine.DeepClone(), ["userTournaments"] = mine.DeepClone(), ["totalResults"] = mine.Count });
            }
            if (path.Contains("tournament/list"))
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var ts = new JsonArray(); int ord = 0;
                foreach (var m in Modes.Where(x => x.Kind == "tournament"))
                    ts.Add(new JsonObject
                    {
                        ["id"] = m.Id, ["tournamentId"] = m.Id, ["name"] = m.Name, ["tournamentName"] = m.Name, ["title"] = m.Name,
                        ["description"] = TourDesc(m), ["desc"] = TourDesc(m),
                        ["state"] = "ACTIVE", ["status"] = "ACTIVE", ["active"] = true, ["isActive"] = true, ["available"] = true, ["unlocked"] = true,
                        ["type"] = "SINGLE_PLAYER", ["mode"] = "SINGLE_PLAYER", ["tournamentType"] = "SINGLE_PLAYER", ["category"] = "FIXED", ["order"] = ord++,
                        ["difficulty"] = m.Wins <= 3 ? "BEGINNER" : "PROFESSIONAL", ["rounds"] = 4, ["totalRounds"] = 4, ["numRounds"] = 4, ["bestOf"] = 16, ["size"] = 16,
                        ["entryFeeCoins"] = m.Coins, ["entryFeePoints"] = m.Points, ["entryCostCoins"] = m.Coins, ["coinsCost"] = m.Coins, ["pointsCost"] = m.Points, ["price"] = m.Coins,
                        ["startTime"] = now - 86400, ["endTime"] = now + 86400L * 365, ["openTime"] = now - 86400, ["closeTime"] = now + 86400L * 365,
                        ["requirements"] = new JsonArray(), ["rewards"] = new JsonArray(), ["prizes"] = new JsonArray(), ["trophies"] = 0,
                        ["tournamentState"] = "ACTIVE", ["enabled"] = true, ["visible"] = true, ["joinable"] = true, ["entered"] = false, ["currentRound"] = 0,
                        ["matches"] = m.Wins, ["winsNeeded"] = m.Wins, ["maxLosses"] = 1, ["currencyType"] = "COINS", ["assetId"] = Math.Min(7, m.Id - 2000),
                        ["tournamentStatus"] = "ACTIVE", ["tournamentLevel"] = m.Wins <= 3 ? 1 : 2, ["tournamentRule"] = new JsonObject(), ["tournamentResult"] = new JsonObject(),
                        ["tournament_id"] = m.Id, ["tournament_category"] = 1, ["tournamentofficial"] = false, ["tournamentballid"] = 0, ["tournamentEligible"] = true,
                    });
                string tt = "13"; try { var ttf = FindFile("tournament_try.txt"); if (File.Exists(ttf)) tt = File.ReadAllText(ttf).Trim(); } catch { }
                if (tt == "auto") tt = (((_ttCycle++) % 8) + 1).ToString();
                foreach (var tn in ts) { var tjo = (JsonObject)tn; var tmd = Modes.First(x => x.Id == (int)L(tjo["id"])); TournamentFifa15Fields(tjo, tmd); }
                var minT = new JsonArray();
                foreach (var m in Modes.Where(x => x.Kind == "tournament"))
                    minT.Add(new JsonObject { ["id"] = m.Id, ["name"] = m.Name, ["state"] = "ACTIVE", ["type"] = "SINGLE_PLAYER" });
                Console.WriteLine("[TOURNAMENT] sending list variant " + tt + " (" + ts.Count + " tournaments)");
                switch (tt)
                {
                    default: return J(new JsonObject { ["tournaments"] = ts });
                    case "2": return J(new JsonObject { ["tournaments"] = minT });
                    case "3": return J(new JsonObject { ["tournamentList"] = ts.DeepClone() });
                    case "4": return J(ts.DeepClone());
                    // 10-12: wrapper names taken from the game's own JSON key table (it knows "tournament" and "tournamentData", not "tournaments")
                    // 13/14: "tournament" wrapper (accepted by the game) + LEAN entries that only carry the same fields the working season record has.
                    //   13 = lean + id + name + description,  14 = lean + id only
                    case "13":
                    case "14":
                        {
                            // FIX: the game's tables are sized for small ids (season divisions are 1..10), so the list now sends 1..N
                            // instead of 2003..2008. The POST handler below maps them back. tour_count.txt (optional) limits how many are sent.
                            int maxT = 6; try { var cf = FindFile("tour_count.txt"); if (File.Exists(cf) && int.TryParse(File.ReadAllText(cf).Trim(), out var cn)) maxT = Math.Max(1, cn); } catch { }
                            var lean = new JsonArray();
                            foreach (var lm in Modes.Where(x => x.Kind == "tournament").Take(maxT))
                            {
                                var lo = new JsonObject { ["id"] = lm.Id - 2002 };
                                if (tt == "13") { lo["name"] = lm.Name; lo["description"] = TourDesc(lm); }
                                TournamentFifa15Fields(lo, lm);
                                lean.Add(lo);
                            }
                            return J(new JsonObject { ["tournament"] = lean });
                        }
                    case "10": return J(new JsonObject { ["tournament"] = ts.DeepClone() });
                    case "11": return J(new JsonObject { ["tournamentData"] = ts.DeepClone() });
                    case "12": return J(new JsonObject { ["tournament"] = ts.DeepClone(), ["tournamentData"] = ts.DeepClone(), ["tournaments"] = ts.DeepClone(), ["tournamentList"] = ts.DeepClone(), ["totalResults"] = ts.Count, ["count"] = ts.Count, ["total"] = ts.Count });
                    case "5": return J(new JsonObject { ["tournaments"] = minT.DeepClone(), ["totalResults"] = minT.Count, ["count"] = minT.Count });
                    case "6": return J(new JsonObject { ["tournamentInfo"] = ts.DeepClone(), ["tournament"] = minT.DeepClone() });
                    case "7":
                        {
                            var xt = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><tournaments totalResults=\"" + minT.Count + "\">");
                            foreach (var m in Modes.Where(x => x.Kind == "tournament"))
                                xt.Append($"<tournament id=\"{m.Id}\" state=\"ACTIVE\" type=\"SINGLE_PLAYER\"><name>{System.Security.SecurityElement.Escape(m.Name)}</name></tournament>");
                            return (200, "text/xml", Encoding.UTF8.GetBytes(xt.Append("</tournaments>").ToString()), null);
                        }
                    case "8": return J(new JsonObject { ["list"] = ts.DeepClone(), ["tournaments"] = minT.DeepClone(), ["tournamentIds"] = new JsonArray(Modes.Where(x => x.Kind == "tournament").Select(x => (JsonNode)JsonValue.Create(x.Id)!).ToArray()) });
                    case "9": return J(new JsonObject { ["tournaments"] = ts, ["tournamentList"] = ts.DeepClone(), ["tournamentInfo"] = ts.DeepClone(), ["tournamentInfos"] = ts.DeepClone(),
                        ["list"] = ts.DeepClone(), ["items"] = ts.DeepClone(), ["data"] = ts.DeepClone(), ["entries"] = ts.DeepClone(),
                        ["totalResults"] = ts.Count, ["totalCount"] = ts.Count, ["count"] = ts.Count, ["total"] = ts.Count, ["offset"] = 0, ["endOfList"] = true,
                        ["credits"] = Club["coins"]!.DeepClone() });
                }
            }
            if (path.Contains("tournament") && method != "GET")
            {
                var mineT = Club["tournaments"] as JsonArray ?? new JsonArray();
                int tid = Modes.Where(x => x.Kind == "tournament").Select(x => x.Id).FirstOrDefault(tk => path.Contains("/" + tk));
                if (tid == 0) tid = Req()?["tournamentId"]?.GetValue<int>() ?? Req()?["id"]?.GetValue<int>() ?? 0;
                if (tid == 0) { var mm = System.Text.RegularExpressions.Regex.Match(path, @"tournament/(?:user/)?(\d{1,2})(?:/|$)"); if (mm.Success) tid = 2002 + int.Parse(mm.Groups[1].Value); }
                if (tid > 0 && tid < 100) tid += 2002;
                var md = Modes.FirstOrDefault(x => x.Id == tid);
                if (md != null)
                {
                    var cur = mineT.FirstOrDefault(t => (int)L(t!["id"]) == tid) as JsonObject;
                    bool won = path.Contains("win") || (Req()?["won"]?.GetValue<bool>() ?? false);
                    if (cur == null)
                    {
                        int fee = (int)L(Club["coins"]); if (fee >= md.Coins) Club["coins"] = fee - md.Coins;
                        cur = new JsonObject { ["id"] = tid, ["tournamentId"] = tid, ["name"] = md.Name, ["state"] = "IN_PROGRESS", ["wins"] = 0, ["losses"] = 0, ["rounds"] = md.Wins };
                        mineT.Add(cur);
                    }
                    else if (path.Contains("result") || path.Contains("win") || path.Contains("loss"))
                    {
                        if (won) cur["wins"] = (int)L(cur["wins"]) + 1; else cur["losses"] = (int)L(cur["losses"]) + 1;
                        if ((int)L(cur["wins"]) >= md.Wins) cur["state"] = "COMPLETE";
                        if (tid == ChampsId) ChampsOnResult(won, (int)L(cur["wins"]), md.Wins);
                    }
                    Club["tournaments"] = mineT; Save();
                    return J(new JsonObject { ["id"] = tid, ["tournamentId"] = tid, ["state"] = cur["state"]!.DeepClone(), ["wins"] = cur["wins"]!.DeepClone(), ["losses"] = cur["losses"]!.DeepClone(), ["credits"] = Club["coins"]!.DeepClone() });
                }
            }

            if (path.Contains("squad/mode/draft"))
            {
                var tokens = new JsonArray();
                foreach (var m in Modes.Where(x => x.Kind == "draft"))
                    tokens.Add(new JsonObject { ["id"] = m.Id, ["name"] = m.Name, ["coins"] = m.Coins, ["points"] = m.Points, ["winsNeeded"] = m.Wins });
                return J(new JsonObject { ["state"] = "NOT_STARTED", ["mode"] = "SINGLE_PLAYER", ["brand"] = Brand,
                    ["wins"] = Club["draftWins"]!.DeepClone(), ["losses"] = 0, ["draftTokens"] = tokens });
            }

            if (path.Contains("draft") || path.Contains("tournament"))
            {
                var arr = new JsonArray();
                foreach (var m in Modes) arr.Add(new JsonObject { ["id"] = m.Id, ["name"] = m.Name, ["kind"] = m.Kind, ["coins"] = m.Coins, ["points"] = m.Points, ["winsNeeded"] = m.Wins, ["brand"] = Brand });
                return J(new JsonObject { ["draftTokens"] = arr, ["tournaments"] = arr.DeepClone(), ["modes"] = arr });
            }
        }

        return (200, "application/json", Encoding.UTF8.GetBytes("{}"), null);
    }
}
