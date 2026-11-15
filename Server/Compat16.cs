#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

static partial class UtBackend
{
    static readonly HashSet<string> _backedUp = new();
    static void WriteClubAtomic()
    {
        try
        {
            if (_backedUp.Add(ClubFile) && File.Exists(ClubFile) && new FileInfo(ClubFile).Length > 2000)
            {
                string bd = Path.Combine(Dir, "backups"); Directory.CreateDirectory(bd);
                File.Copy(ClubFile, Path.Combine(bd, Path.GetFileNameWithoutExtension(ClubFile) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json"), true);
                foreach (var old in Directory.GetFiles(bd, "*.json").OrderByDescending(f => File.GetLastWriteTime(f)).Skip(15)) { try { File.Delete(old); } catch { } }
            }
        }
        catch { }
        string tmp = ClubFile + ".tmp";
        File.WriteAllText(tmp, Club.ToJsonString());
        if (File.Exists(ClubFile))
        {
            try { File.Copy(ClubFile, ClubFile + ".bak", true); } catch { }
            File.Move(tmp, ClubFile, true);
        }
        else File.Move(tmp, ClubFile);
    }

    static System.Threading.Timer _autosave;
    static bool _exitHooks;

    static void SaveOnExit(string why)
    {
        bool got = false;
        try
        {
            got = System.Threading.Monitor.TryEnter(Lock, 3000);
            Directory.CreateDirectory(Dir);
            WriteClubAtomic();
            Console.WriteLine($"[SAVE] progress saved on exit ({why})");
        }
        catch (Exception ex) { Console.WriteLine("[SAVE] exit save failed: " + ex.Message); }
        finally { if (got) System.Threading.Monitor.Exit(Lock); }
    }

    delegate bool ConsoleCtrlDelegate(int ctrlType);
    static ConsoleCtrlDelegate _ctrlHandler;
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

    static void StartAutosave()
    {
        try { _autosave?.Dispose(); } catch { }
        _autosave = new System.Threading.Timer(_ => { try { Save(); } catch { } }, null, 10000, 10000);
        if (_exitHooks) return;
        _exitHooks = true;
        try { AppDomain.CurrentDomain.ProcessExit += (s, e) => SaveOnExit("process exit"); } catch { }
        try { Console.CancelKeyPress += (s, e) => SaveOnExit("Ctrl+C"); } catch { }
        try
        {
            if (OperatingSystem.IsWindows())
            {
                _ctrlHandler = type => { SaveOnExit("console closed / shutdown (" + type + ")"); return false; };
                SetConsoleCtrlHandler(_ctrlHandler, true);
            }
        }
        catch { }
    }

    static bool ClubIsFake(JsonObject c)
    {
        try
        {
            int real = 0, fake = 0;
            if (c["items"] is JsonArray a)
                foreach (var n in a)
                {
                    if (n is not JsonObject o || (o["itemType"]?.ToString() ?? "player") != "player") continue;
                    long aid = 0; try { aid = L(o["assetId"]); } catch { }
                    if (aid >= 900000) fake++; else real++;
                }
            return real == 0 && fake > 0;
        }
        catch { return false; }
    }

    static void FillCard(JsonObject c)
    {
        if (c == null) return;
        string t = c["itemType"]?.ToString() ?? "player";
        if (t != "player") return;
        void S(string k, JsonNode v) { if (c[k] == null) c[k] = v; }
        try { c["discardValue"] = QuickSellFor(L(c["rating"]), L(c["rareflag"])); } catch { }
        long ct = 7; try { ct = Math.Max(1, L(c["contracts"] ?? c["contract"])); } catch { }
        S("contract", ct); S("contracts", ct);
        S("loans", 0); S("injuryGames", 0); S("injuryType", "none"); S("suspension", 0); S("loyaltyBonus", 1);
        S("playStyle", 0); S("posMods", new JsonArray()); S("statsList", new JsonArray());
        S("untradeable", false); S("tradeable", true); S("assists", 0); S("lifetimeAssists", 0);
        S("marketDataMinPrice", 150); S("marketDataMaxPrice", 15000000);
        S("fitness", 99);
        S("timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 86400);
    }

    static void FixPlayers()
    {
        try
        {
            if (Club["items"] is JsonArray a)
                foreach (var n in a) if (n is JsonObject o) FillCard(o);
        }
        catch (Exception ex) { Console.WriteLine("[PLAYERS] fix failed: " + ex.Message); }
    }

    static bool IdentityApply(long id, string st, JsonArray outArr)
    {
        var it = FindItem(id); if (it == null) return false;
        string ty = it["itemType"]?.ToString() ?? "";
        long cat = 0; try { cat = L(it["category"]); } catch { }
        string want = ty == "custom" ? "activeBadge" : ty == "stadium" ? "activeStadium" : ty == "ball" ? "activeBall"
                    : ty == "kit" ? (cat == 3 ? "activeAwayKit" : "activeHomeKit") : null;
        if (want == null) return false;
        if (ty == "kit") foreach (var slot in new[] { "activeHomeKit", "activeAwayKit" }) if (st.Equals(slot, StringComparison.OrdinalIgnoreCase)) want = slot;
        bool on = st.StartsWith("active", StringComparison.OrdinalIgnoreCase);
        if (on)
            foreach (var o in ((JsonArray)Club["items"]!).ToList())
                if (o is JsonObject oo && L(oo["id"]) != id && (oo["itemState"]?.ToString() ?? "") == want)
                { oo["itemState"] = "free"; outArr.Add(new JsonObject { ["id"] = L(oo["id"]), ["itemState"] = "free", ["success"] = true }); }
        it["itemState"] = on ? want : "free";
        try
        {
            if (Club["unassigned"] is JsonArray ua) { var keep = new JsonArray(); foreach (var n in ua) if (L(n) != id) keep.Add(L(n)); Club["unassigned"] = keep; }
        }
        catch { }
        outArr.Add(new JsonObject { ["id"] = id, ["itemState"] = it["itemState"]!.DeepClone(), ["success"] = true });
        Console.WriteLine($"[IDENTITY] {(it["displayName"] ?? it["name"])?.ToString()} ({ty}, category {cat}) -> {it["itemState"]}");
        return true;
    }

    static (int, string, byte[], Dictionary<string, string>?)? HandleIdentityActivate(string method, string path, byte[] body)
    {
        if (method != "PUT" && method != "POST") return null;
        var m = System.Text.RegularExpressions.Regex.Match(path ?? "", @"/game/fifa16/item(?:/(\d+))?$");
        if (!m.Success) return null;
        string txt = Encoding.UTF8.GetString(body ?? Array.Empty<byte>());
        if (txt.Contains("itemState")) Console.WriteLine($"[IDENTITY-REQ] {method} {path}  body: {(txt.Length > 300 ? txt[..300] : txt)}");
        JsonObject rq = null; try { rq = JsonNode.Parse(txt) as JsonObject; } catch { }
        if (rq == null) return null;
        var res = new JsonArray();
        if (m.Groups[1].Success)
        {
            string st = rq["itemState"]?.ToString();
            if (string.IsNullOrEmpty(st) || !IdentityApply(long.Parse(m.Groups[1].Value), st, res)) return null;
        }
        else
        {
            if (rq["itemData"] is not JsonArray arr || arr.Count == 0) return null;
            foreach (var e in arr)
            {
                if (e is not JsonObject eo || eo["itemState"] == null || eo["pile"] != null) return null;
                var fi = FindItem(L(eo["id"])); string ft = fi?["itemType"]?.ToString() ?? "";
                if (ft != "custom" && ft != "kit" && ft != "stadium" && ft != "ball") return null;
            }
            foreach (var e in arr) IdentityApply(L(e["id"]), e["itemState"]!.ToString(), res);
        }
        Save();
        return (200, "application/json", Encoding.UTF8.GetBytes(new JsonObject { ["itemData"] = res }.ToJsonString()), null);
    }

    static long SquadChemistry(JsonObject sq)
    {
        try
        {
            var cards = new List<JsonObject>();
            if (sq?["players"] is JsonArray pl)
                foreach (var sl in pl)
                {
                    if (sl == null || L(sl["index"]) >= 11) continue;
                    long id = L(sl["itemData"]?["id"]); if (id <= 0) continue;
                    if (FindItem(id) is JsonObject c && (c["itemType"]?.ToString() ?? "player") == "player") cards.Add(c);
                }
            if (cards.Count == 0) return 0;
            long Club_(JsonObject c) => L(c["teamid"] ?? c["teamId"]);
            int total = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                int club = 0, nat = 0, lg = 0;
                for (int j2 = 0; j2 < cards.Count; j2++)
                {
                    if (i == j2) continue;
                    if (Club_(cards[i]) != 0 && Club_(cards[i]) == Club_(cards[j2])) club++;
                    if (L(cards[i]["nation"]) != 0 && L(cards[i]["nation"]) == L(cards[j2]["nation"])) nat++;
                    if (L(cards[i]["leagueId"]) != 0 && L(cards[i]["leagueId"]) == L(cards[j2]["leagueId"])) lg++;
                }
                int pts = (club >= 3 ? 4 : club == 2 ? 3 : club == 1 ? 2 : 0) + (nat >= 3 ? 4 : nat == 2 ? 3 : nat == 1 ? 2 : 0) + (lg >= 3 ? 2 : lg >= 1 ? 1 : 0);
                total += Math.Min(10, pts);
            }
            return Math.Min(100, total);
        }
        catch { return 0; }
    }

    static int QuickSellFor(long rating, long rare)
    {
        if (rare == 5) return 70000;
        if (rare == 3) return rating >= 90 ? 20000 : 15000;
        if (rare >= 10) return 40000;
        if (rare >= 2) return 12000;
        double v = rating >= 75 ? 300 * Math.Pow(1.2, rating - 75) : rating >= 65 ? 150 + (rating - 65) * 15 : 50 + Math.Max(0, rating - 45) * 5;
        if (rare == 1) v *= 1.5;
        v = Math.Min(v, 12000);
        return (int)Math.Max(50, Math.Round(v / 50.0) * 50);
    }

    static void DupPromote(long soldId)
    {
        try { var dm = DupMap(); foreach (var kv in dm.ToList()) if (L(kv.Value) == soldId) dm.Remove(kv.Key); }
        catch { }
    }
    static bool DupOriginalAlive(long dupId)
    {
        try { return FindItem(L(DupMap()[dupId.ToString()])) != null; }
        catch { return false; }
    }

    static Dictionary<long, (int lg, int nat, string stad)> _teamInfo;
    static Dictionary<long, (int lg, int nat, string stad)> TeamInfo()
    {
        if (_teamInfo != null) return _teamInfo;
        var d = new Dictionary<long, (int, int, string)>();
        try
        {
            var f = FindFile("male_teams.csv");
            if (File.Exists(f))
            {
                bool first = true;
                foreach (var line in File.ReadLines(f))
                {
                    if (first) { first = false; continue; }
                    var c = line.Split(',');
                    if (c.Length < 17 || !long.TryParse(c[0], out var id)) continue;
                    int.TryParse(c[6], out var lg); int.TryParse(c[9], out var na);
                    d[id] = (lg, na, c[16].Trim());
                }
            }
        }
        catch (Exception ex) { Console.WriteLine("[MARKET] male_teams.csv read failed: " + ex.Message); }
        try
        {
            foreach (var g in Db.Where(p => p.Club > 0).GroupBy(p => p.Club))
            {
                int lg = g.GroupBy(p => p.League).OrderByDescending(x => x.Count()).First().Key;
                int na = g.GroupBy(p => p.Nation).OrderByDescending(x => x.Count()).First().Key;
                d.TryGetValue(g.Key, out var old);
                d[g.Key] = (lg, old.Item2 != 0 ? old.Item2 : na, old.Item3 ?? "");
            }
        }
        catch { }
        _teamInfo = d;
        return d;
    }
    static string NormNameC(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in (s ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark && (char.IsLetterOrDigit(ch) || ch == ' ')) sb.Append(ch);
        return sb.ToString().Trim();
    }
    static readonly Dictionary<long, long> _stadTeam = new();
    static long IdTeam(JsonObject it)
    {
        try
        {
            if (it["teamid"] != null) return L(it["teamid"]);
            if (it["stadiumid"] == null) return 0;
            long sid = L(it["stadiumid"]);
            if (_stadTeam.TryGetValue(sid, out var t)) return t;
            string nm = NormNameC(it["displayName"]?.ToString());
            long best = 0;
            if (nm.Length > 3)
                foreach (var kv in TeamInfo()) if (kv.Value.stad != null && kv.Value.stad.Length > 3 && NormNameC(kv.Value.stad).Contains(nm)) { best = kv.Key; break; }
            _stadTeam[sid] = best;
            return best;
        }
        catch { return 0; }
    }
    static IEnumerable<int> IdentityFilter(IEnumerable<int> r, List<JsonObject> all, Dictionary<string, string> q)
    {
        string G(string k) => q.TryGetValue(k, out var v) ? (v ?? "") : "";
        long N(string k) => long.TryParse(G(k), out var n) ? n : 0;
        long tv = N("team"), lv = N("leag"), nv = N("nat");
        string lev = G("lev").ToLowerInvariant(), rare = G("rare").ToLowerInvariant();
        if (tv > 0) r = r.Where(i => IdTeam(all[i]) == tv);
        if (lv > 0 || nv > 0)
        {
            var ti = TeamInfo();
            r = r.Where(i => ti.TryGetValue(IdTeam(all[i]), out var inf) && (lv == 0 || inf.lg == lv) && (nv == 0 || inf.nat == nv));
        }
        if (lev == "bronze" || lev == "silver" || lev == "gold") r = r.Where(i => Q((int)L(all[i]["rating"])) == lev);
        if (lev == "special" || lev == "sp" || rare == "sp" || rare == "special") r = r.Where(i => L(all[i]["rareflag"]) >= 1);
        return r;
    }
}
