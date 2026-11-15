#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace LocalFut15;

static partial class Fut
{
    static readonly HashSet<string> SeenUnknown = new();
    static readonly Dictionary<string, int> _tryCount = new();
    static string NextTry(string path) { lock (_tryCount) { _tryCount.TryGetValue(path, out var n); _tryCount[path] = n + 1; return (n % 3 + 1).ToString(); } }

    static string PosName16(string p)
    {
        p = (p ?? "").Trim().ToUpperInvariant();
        if (int.TryParse(p, out var n))
        {
            string[] names = { "GK","SW","RWB","RB","RCB","CB","LCB","LB","LWB","RDM","CDM","LDM","RM","RCM","CM","LCM","LM","RAM","CAM","LAM","RF","CF","LF","RW","RS","ST","LS" };
            return n >= 0 && n < names.Length ? names[n] : "";
        }
        return p;
    }

    static JsonObject _modeFile; static DateTime _modeStamp;
    static HttpRes ModeOverride(string method, string low)
    {
        try
        {
            var f = Cfg.Find("mode_replies.json");
            if (!File.Exists(f)) return null;
            var stamp = File.GetLastWriteTimeUtc(f);
            if (_modeFile == null || stamp != _modeStamp)
            {
                _modeFile = JsonNode.Parse(File.ReadAllText(f), null, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject;
                _modeStamp = stamp;
                Log.Info("mode_replies.json loaded: " + (_modeFile?.Count ?? 0) + " rule(s)");
            }
            if (_modeFile == null) return null;
            foreach (var kv in _modeFile)
            {
                var parts = kv.Key.Split(' ', 2);
                if (parts.Length < 2 || kv.Value is not JsonObject v) continue;
                if (parts[0] != "*" && !parts[0].Equals(method, StringComparison.OrdinalIgnoreCase)) continue;
                var p = parts[1].Trim().ToLowerInvariant();
                bool hit = p.EndsWith("*") ? low.StartsWith(p[..^1]) : low == p;
                if (!hit) continue;
                var r = Ok(v["body"]?.DeepClone() ?? new JsonObject(), NS());
                r.Status = (int)U.L(v, "status", 200);
                Log.Warn($"MODE OVERRIDE {method} {low} -> {r.Status} (mode_replies.json)");
                return r;
            }
        }
        catch (Exception e) { Log.Warn("mode_replies.json: " + e.Message); _modeFile = null; }
        return null;
    }

    static HttpRes MarketRoute(string method, string low, Dictionary<string, string> query, JsonObject payload)
    {
        var ovr = ModeOverride(method, low);
        if (ovr != null) return ovr;

        if ((low == "/ut/delete/game/fifa16/item" || (low == "/ut/game/fifa16/item" && method == "DELETE")) && method != "GET")
        {
            var ids = new List<long>();
            if (payload?["itemId"] is JsonArray ia) foreach (var n in ia) { try { ids.Add(U.LN(n)); } catch { } }
            else if (payload?["itemId"] != null) { try { ids.Add(U.LN(payload["itemId"])); } catch { } }
            foreach (var key in new[] { "itemIds", "itemId" })
                if (query != null && query.TryGetValue(key, out var qv))
                    foreach (var part in qv.Split(',')) if (long.TryParse(part.Trim(), out var qid)) ids.Add(qid);
            long gained = St.QuickSell(ids, out int sold);
            Log.Warn($"QUICKSELL ids=[{string.Join(",", ids)}] sold={sold} +{gained} coins -> {St.GetCredits()}");
            var qo = CreditsPayload();
            qo["totalCredits"] = St.GetCredits();
            qo["itemId"] = new JsonArray(ids.Select(i => (JsonNode)JsonValue.Create(i)).ToArray());
            return Ok(qo, NS());
        }
        long QL(string k, long d) => query != null && query.TryGetValue(k, out var v) && long.TryParse(v, out var n) ? n : d;
        string QS(string k) => query != null && query.TryGetValue(k, out var v) ? v : "";

        if (low.StartsWith("/ut/game/fifa16/squad/mode/draft") || low.StartsWith("/ut/game/fifa16/draft"))
        {
            string tryNo = "0";
            try { var tf = Path.Combine(Directory.GetCurrentDirectory(), "draft_try.txt"); if (File.Exists(tf)) tryNo = File.ReadAllText(tf).Trim(); } catch { }
            Log.Warn($"DRAFT REQUEST {method} {low} query={string.Join("&", query.Select(kv => kv.Key + "=" + kv.Value))} body={payload?.ToJsonString()} try={tryNo}");
            if (tryNo != "1" && tryNo != "2" && tryNo != "3" && tryNo != "4" && tryNo != "5")
                return new HttpRes { Status = 404, ContentType = "application/json; charset=utf-8", Body = Encoding.UTF8.GetBytes("{\"code\":404,\"reason\":\"draft not available\",\"message\":\"draft not available\"}") };
            JsonObject d = new JsonObject();
            if (tryNo == "4") return Ok(new JsonObject(), NS());
            if (tryNo == "5") return Ok(new JsonObject { ["state"] = "NONE" }, NS());
            if (tryNo == "2") { d["draftState"] = "NONE"; d["tokens"] = 1L; d["entryFee"] = 15000L; d["currentWins"] = 0L; }
            else if (tryNo == "3") { d["state"] = "UNSTARTED"; d["status"] = "UNSTARTED"; d["active"] = false; d["wins"] = 0L; d["losses"] = 0L; d["draftTokens"] = 1L; }
            else { d["state"] = "NONE"; d["draftState"] = "NONE"; d["status"] = "NONE"; d["active"] = false; d["tokens"] = 1L; d["draftTokens"] = 1L; d["entryFee"] = 15000L; d["entryFeePoints"] = 300L; }
            return Ok(d, NS());
        }

        if (low == "/ut/delete/game/fifa16/user" && method == "POST") { St.ResetClub(); return Ok(new JsonObject(), NS()); }
        if (low == "/ut/delete/auth") return Ok(new JsonObject(), NS());

        if (low.StartsWith("/ut/game/fifa16/tournament") || low.StartsWith("/ut/game/fifa16/season"))
        {
            string tryNo = NextTry(low);
            try { var tf = Path.Combine(Directory.GetCurrentDirectory(), "draft_try.txt"); if (File.Exists(tf)) tryNo = File.ReadAllText(tf).Trim(); } catch { }
            Log.Warn($"MODE REQUEST {method} {low} query={string.Join("&", query.Select(kv => kv.Key + "=" + kv.Value))} body={payload?.ToJsonString()} try={tryNo}");
            bool tourOn = low.StartsWith("/ut/game/fifa16/tournament") && !File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "tournament_off.txt"));
            if (tourOn && (low == "/ut/game/fifa16/tournament/list" || low == "/ut/game/fifa16/tournament/user/list"))
            {
                JsonObject Entry(bool full)
                {
                    var t = new JsonObject { ["id"] = 1L, ["name"] = "FUT REBORN tournament", ["state"] = "OPEN" };
                    if (full)
                    {
                        t["tournamentId"] = 1L; t["description"] = "FUT REBORN tournament"; t["type"] = "SINGLE_PLAYER"; t["status"] = "OPEN"; t["active"] = true;
                        t["entryFee"] = 0L; t["wins"] = 0L; t["losses"] = 0L; t["rounds"] = 3L;
                        t["difficulty"] = 1L; t["bonus"] = 1000L; t["tournamentBonus"] = 1000L; t["unlocked"] = true; t["unlock"] = true;
                        t["requirements"] = new JsonArray(); t["entryRequirements"] = new JsonArray();
                    }
                    return t;
                }
                bool isUser = low.EndsWith("/user/list");
                JsonObject res16;
                if (tryNo == "2")      res16 = new JsonObject { ["count"] = 1L, ["total"] = 1L, ["tournaments"] = new JsonArray(Entry(false)) };
                else if (tryNo == "3") res16 = new JsonObject { ["count"] = 1L, ["total"] = 1L, ["tournamentList"] = new JsonArray(Entry(true)), ["userTournamentList"] = new JsonArray() };
                else                   res16 = new JsonObject { ["count"] = 1L, ["total"] = 1L, ["tournaments"] = new JsonArray(Entry(true)), ["tournamentList"] = new JsonArray(Entry(true)) };
                if (isUser) { res16["count"] = 0L; res16["total"] = 0L; foreach (var k in res16.Select(kv => kv.Key).ToList()) if (res16[k] is JsonArray) res16[k] = new JsonArray(); }
                return Ok(res16, NS());
            }
            var o = new JsonObject { ["count"] = 0L, ["total"] = 0L };
            if (low.Contains("season"))
            {
                if (tryNo == "2") o["seasons"] = new JsonArray();
                else if (tryNo == "3") { o["seasonInfo"] = new JsonArray(); o["userSeasons"] = new JsonArray(); }
                else { o["seasonList"] = new JsonArray(); o["seasons"] = new JsonArray(); }
            }
            else
            {
                if (tryNo == "2") o["tournaments"] = new JsonArray();
                else if (tryNo == "3") { o["tournamentList"] = new JsonArray(); o["userTournaments"] = new JsonArray(); }
                else { o["tournamentList"] = new JsonArray(); o["tournaments"] = new JsonArray(); o["userTournamentList"] = new JsonArray(); }
            }
            return Ok(o, NS());
        }

        if (low == "/ut/game/fifa16/club" && method == "GET")
        {
            string ctype = QS("type").ToLowerInvariant();
            if (ctype == "manager" || ctype == "staff")
            {
                var marr = new JsonArray();
                foreach (var mg in St.ListItems().Where(x => U.S(x, "itemType") == "manager")) marr.Add(NativePlayer(mg));
                Log.Info("CLUB list type=" + ctype + " -> " + marr.Count + " manager card(s)");
                return Ok(new JsonObject { ["itemData"] = marr }, NS());
            }
            if (ctype != "" && ctype != "player")
            {
                Log.Info("CLUB list type=" + ctype + " -> empty (no such cards yet)");
                return Ok(new JsonObject { ["itemData"] = new JsonArray() }, NS());
            }
            IEnumerable<JsonObject> q = St.ListItems().Where(x =>
                (U.S(x, "pile") == "club" || U.S(x, "pile") == "squad") && U.S(x, "itemType", "player") == "player");

            string pos = QS("position");
            if (pos != "" && !pos.Equals("any", StringComparison.OrdinalIgnoreCase))
                q = q.Where(x => PosName16(U.S(x, "preferredPosition", "")).Equals(pos, StringComparison.OrdinalIgnoreCase));

            string level = QS("level").ToLowerInvariant();
            if (level == "bronze") q = q.Where(x => U.L(x, "rating") < 65);
            else if (level == "silver") q = q.Where(x => U.L(x, "rating") >= 65 && U.L(x, "rating") < 75);
            else if (level == "gold") q = q.Where(x => U.L(x, "rating") >= 75);

            long nation = QL("nation", -1), league = QL("league", -1), team = QL("team", -1);
            if (nation > 0) q = q.Where(x => U.L(x, "nation") == nation);
            if (league > 0) q = q.Where(x => U.L(x, "leagueId") == league);
            if (team > 0) q = q.Where(x => U.L(x, "teamid") == team);

            var list = q.OrderByDescending(x => U.L(x, "rating")).ThenByDescending(x => U.L(x, "rareflag")).ThenBy(x => U.L(x, "id")).ToList();
            long start = QL("start", 0), count = QL("count", 50);
            var arr = new JsonArray();
            foreach (var it in list.Skip((int)start).Take((int)count)) arr.Add(NativePlayer(it));
            return Ok(new JsonObject { ["itemData"] = arr }, NS());
        }

        if (low == "/ut/game/fifa16/tradepile" && method == "GET")
        {
            var o = TradePileEmpty();
            var arr = new JsonArray();
            foreach (var it in St.ListItems("trade"))
                arr.Add(new JsonObject
                {
                    ["tradeId"] = U.L(it, "id"), ["tradeState"] = null, ["buyNowPrice"] = 0L, ["currentBid"] = 0L, ["startingBid"] = 0L,
                    ["offers"] = 0L, ["watched"] = false, ["bidState"] = "none", ["expires"] = -1L, ["tradeOwner"] = true,
                    ["itemData"] = NativePlayer(it),
                });
            o["auctionInfo"] = arr;
            o["currentTradePileSize"] = (long)arr.Count; o["tradePileCount"] = (long)arr.Count; o["total"] = (long)arr.Count; o["count"] = (long)arr.Count;
            return Ok(o, NS());
        }

        if (low == "/ut/game/fifa16/clientdata/totw" && method == "GET")
        {
            var pick = Catalog.Players.Where(kv => U.L(kv.Value, "rating") >= 75 && U.L(kv.Value, "rareflag", 0) == 0)
                .OrderByDescending(kv => U.L(kv.Value, "rating")).Take(23).Select(kv => kv.Key).ToList();
            var arr = new JsonArray();
            long i = 0;
            foreach (var rid in pick)
            {
                var it = Catalog.MakePlayer(rid, "totw", 800000000000L + i++, U.NowS());
                it["rareflag"] = 3L;
                arr.Add(NativePlayer(it));
            }
            Log.Warn("TOTW served (guessed format), cards=" + arr.Count);
            return Ok(new JsonObject { ["itemData"] = arr, ["total"] = (long)arr.Count, ["count"] = (long)arr.Count }, NS());
        }
        return null;
    }

    public static HttpRes Handle16(HttpReq req)
    {
        string target = req.Target;
        while (target.Contains("/ut/ut/", StringComparison.OrdinalIgnoreCase)) target = target.Replace("/ut/ut/", "/ut/", StringComparison.OrdinalIgnoreCase);
        var r2 = new HttpReq { Method = req.Method, Target = target, Body = req.Body, Headers = req.Headers };

        string low = target.ToLowerInvariant();
        int qi = low.IndexOf('?'); string pathOnly = qi >= 0 ? low[..qi] : low;
        if (pathOnly.EndsWith(".dds")) Console.WriteLine("[HEADS v2] " + pathOnly + "  gameDir=" + (GameDir() ?? "NOT FOUND") + "  cwd=" + Directory.GetCurrentDirectory());
        if (pathOnly.EndsWith(".dds") || pathOnly.EndsWith(".png") || pathOnly.EndsWith(".jpg") || pathOnly.Contains("playerheads"))
            Log.Info("IMAGE REQUEST " + req.Method + " " + target + (GameDir() == null ? "   [game folder NOT found: create game_dir.txt next to the exe with your FIFA 16 folder, e.g. D:\\FIFA 16]" : ""));

        if (pathOnly.StartsWith("/fut/") && !pathOnly.EndsWith(".xml"))
        {
            var cands = new List<string> { pathOnly };
            var m = Regex.Match(pathOnly, @"/p(\d+)\.dds$");
            if (m.Success && long.TryParse(m.Groups[1].Value, out var rid))
                cands.Add(pathOnly[..(m.Index + 2)] + (rid & 0xFFFFFF) + ".dds");
            foreach (var c in cands)
                foreach (var root in new[] { Cfg.Find("fut_assets"), Path.Combine(Directory.GetCurrentDirectory(), "fut_assets") })
                {
                    try
                    {
                        var rel = c.Substring(5).Replace('/', Path.DirectorySeparatorChar);
                        var f = Path.GetFullPath(Path.Combine(root, rel));
                        if (f.StartsWith(Path.GetFullPath(root)) && File.Exists(f))
                            return new HttpRes { Status = 200, ContentType = "application/octet-stream", Body = File.ReadAllBytes(f) };
                    }
                    catch { }
                }
            if (pathOnly.EndsWith(".dds"))
            {
                try
                {
                    var mm = Regex.Match(pathOnly, @"/p(\d+)\.dds$");
                    if (mm.Success && long.TryParse(mm.Groups[1].Value, out var rr))
                        foreach (var root2 in new[] { Cfg.Find("fut_assets"), Path.Combine(Directory.GetCurrentDirectory(), "fut_assets") })
                        {
                            if (!Directory.Exists(root2)) continue;
                            var hit = Directory.EnumerateFiles(root2, "p" + (rr & 0xFFFFFF) + ".dds", SearchOption.AllDirectories).FirstOrDefault();
                            if (hit != null) { Log.Info("HEAD " + pathOnly + " -> " + hit); return new HttpRes { Status = 200, ContentType = "application/octet-stream", Body = File.ReadAllBytes(hit) }; }
                        }
                }
                catch { }
                Log.Info("HEAD request (no loose file yet): " + pathOnly);
            }
            if (m.Success && long.TryParse(m.Groups[1].Value, out var rid2))
            {
                var dds = HeadFromBig(rid2 & 0xFFFFFF);
                if (dds != null)
                {
                    try
                    {
                        var dst = Path.Combine(Directory.GetCurrentDirectory(), "fut_assets", "playerheads", "g4", "single", "p" + (rid2 & 0xFFFFFF) + ".dds");
                        Directory.CreateDirectory(Path.GetDirectoryName(dst)); File.WriteAllBytes(dst, dds);
                    }
                    catch { }
                    return new HttpRes { Status = 200, ContentType = "application/octet-stream", Body = dds };
                }
            }
        }

        if (pathOnly.StartsWith("/fut/playerheads/") && pathOnly.EndsWith(".dds") && !File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "heads_empty200.txt")))
        {
            Console.WriteLine("[HEADS v2] no head found -> 404: " + pathOnly); Log.Warn("HEAD: no head found for " + pathOnly + " -> 404 (game falls back to its own file)");
            return new HttpRes { Status = 404, ContentType = "text/plain", Body = Array.Empty<byte>() };
        }

        var res = Handle(r2);

        if (pathOnly.Contains("/squad/mode/draft") && res.Headers != null && res.Headers.ContainsKey("X-LocalFUT15-Unknown"))
            return new HttpRes { Status = 404, ContentType = "application/json; charset=utf-8", Body = Encoding.UTF8.GetBytes("{\"code\":404,\"reason\":\"draft not available\",\"message\":\"draft not available\"}") };

        bool unknown = res.Headers != null && res.Headers.ContainsKey("X-LocalFUT15-Unknown");
        if (unknown)
        {
            string body = Encoding.UTF8.GetString(req.Body, 0, Math.Min(req.Body.Length, 1500));
            string key = req.Method + " " + pathOnly;
            lock (SeenUnknown)
            {
                if (SeenUnknown.Add(key))
                {
                    try { File.AppendAllText(Path.Combine(Log.Dir, "unknown-routes.txt"), $"{DateTime.Now:O}  {req.Method} {req.Target}\n  body: {body}\n\n"); } catch { }
                }
            }
        }
        return res;
    }
}

sealed partial class State
{
    public long QuickSell(List<long> ids, out int sold)
    {
        lock (_l)
        {
            long total = 0; sold = 0;
            foreach (var id in ids.Distinct())
            {
                var it = Items.FirstOrDefault(x => U.L(x, "id") == id);
                if (it == null) continue;
                long v = U.L(it, "discardValue", 0);
                if (v <= 0) { long r = U.L(it, "rating", 0); v = r >= 75 ? 700L : r >= 65 ? 300L : 100L; }
                Items.Remove(it);
                foreach (var sq in Squads)
                    if (sq["players"] is JsonArray pl)
                        for (int k = pl.Count - 1; k >= 0; k--)
                            if (pl[k] is JsonObject sl && U.L(sl, "itemId", 0) == id) pl.RemoveAt(k);
                total += v; sold++;
            }
            if (sold > 0) { Credits += total; SaveLocked(); }
            return total;
        }
    }
}

static partial class Fut
{
    static readonly Dictionary<string, byte[]> _bigIndex = new();
    static readonly HashSet<long> _headMiss = new();
    static string _gameDir;
    static DateTime _gameDirRetry = DateTime.MinValue;
    static readonly Dictionary<string, byte[]> _bigIndexLow = new();

    static string GameDir()
    {
        if (_gameDir != null) return _gameDir;
        var cands = new List<string>();
        try { var e = Environment.GetEnvironmentVariable("FIFA16_DIR"); if (!string.IsNullOrWhiteSpace(e)) cands.Add(e.Trim()); } catch { }
        foreach (var f in new[] { Path.Combine(Directory.GetCurrentDirectory(), "game_dir.txt"), Path.Combine(AppContext.BaseDirectory, "game_dir.txt") })
            try { if (File.Exists(f)) cands.Add(File.ReadAllText(f).Trim().Trim('"')); } catch { }
        try { foreach (var pr in System.Diagnostics.Process.GetProcessesByName("fifa16")) { var fn = pr.MainModule?.FileName; if (fn != null) cands.Add(Path.GetDirectoryName(fn)); } } catch { }
        foreach (var d in new[] { "D", "C", "E", "F", "G" })
            foreach (var sub in new[] { @"FIFA 16", @"Games\FIFA 16", @"Origin Games\FIFA 16", @"Program Files (x86)\Origin Games\FIFA 16", @"Program Files\Origin Games\FIFA 16", @"Program Files (x86)\EA SPORTS\FIFA 16", @"EA SPORTS\FIFA 16" })
                cands.Add(d + @":\" + sub);
        if (DateTime.UtcNow < _gameDirRetry) return null;
        foreach (var c in cands)
            if (!string.IsNullOrEmpty(c) && Directory.Exists(Path.Combine(c, "data", "ui", "imgAssets", "heads"))) { _gameDir = c; Log.Info("game folder for heads: " + c); return c; }
        _gameDirRetry = DateTime.UtcNow.AddSeconds(15);
        Log.Warn("HEAD: FIFA 16 folder NOT found. Tried: " + string.Join(" | ", cands.Where(x => !string.IsNullOrEmpty(x)).Take(8)) + " ... Create game_dir.txt next to the exe containing the folder that has data\\ui\\imgAssets\\heads");
        return null;
    }

    static uint BE(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
    static uint LE(byte[] b, int o) => (uint)(b[o + 3] << 24 | b[o + 2] << 16 | b[o + 1] << 8 | b[o]);
    static bool IsDds(byte[] d) => d != null && d.Length > 128 && d[0] == 0x44 && d[1] == 0x44 && d[2] == 0x53 && d[3] == 0x20;

    static bool HasDdsMagic(byte[] r, int i) => r[i] == 0x44 && r[i + 1] == 0x44 && r[i + 2] == 0x53 && r[i + 3] == 0x20;

    static byte[] DecodeChunkZip(byte[] d, long baseId)
    {
        if (d == null || d.Length < 32 || Encoding.ASCII.GetString(d, 0, 8) != "chunkzip") return null;
        for (int start = 8; start < Math.Min(d.Length - 16, 96); start++)
            for (int mode = 0; mode < 2; mode++)
            {
                try
                {
                    using var ms = new MemoryStream(d, start, d.Length - start);
                    using Stream z = mode == 0 ? new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionMode.Decompress)
                                               : new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress);
                    using var o = new MemoryStream();
                    try { z.CopyTo(o); } catch { }
                    var r = o.ToArray();
                    for (int i = 0; i + 130 < r.Length && i < 64; i++)
                        if (HasDdsMagic(r, i))
                        {
                            var dds = new byte[r.Length - i]; Buffer.BlockCopy(r, i, dds, 0, dds.Length);
                            Log.Info($"HEAD p{baseId}: chunkzip unpacked ({(mode == 0 ? "zlib" : "deflate")} @{start}, dds@{i}) {d.Length} -> {dds.Length} bytes; header says {BE(d, 12)}");
                            return dds;
                        }
                }
                catch { }
            }
        Log.Warn($"HEAD p{baseId}: chunkzip could not be unpacked. First 48 bytes: {BitConverter.ToString(d, 0, Math.Min(48, d.Length))}");
        return null;
    }

    internal static byte[]? TryHead(string pathOnly)
    {
        var m = Regex.Match(pathOnly, @"/p(\d+)\.dds$");
        var cands = new List<string> { pathOnly };
        long rid = 0;
        if (m.Success && long.TryParse(m.Groups[1].Value, out rid))
            cands.Add(pathOnly[..(m.Index + 2)] + (rid & 0xFFFFFF) + ".dds");
        var roots = new List<string> { Path.Combine(Directory.GetCurrentDirectory(), "fut_assets"), Path.Combine(AppContext.BaseDirectory, "fut_assets") };
        try { roots.Insert(0, Cfg.Find("fut_assets")); } catch { }
        foreach (var c in cands)
            foreach (var root in roots)
                try
                {
                    var f = Path.GetFullPath(Path.Combine(root, c.Substring(5).Replace('/', Path.DirectorySeparatorChar)));
                    if (File.Exists(f)) { Console.WriteLine("[HEADS v3] loose file " + f); return File.ReadAllBytes(f); }
                }
                catch { }
        if (!m.Success) return null;
        long plain = rid & 0xFFFFFF;
        foreach (var root in roots)
            try
            {
                if (!Directory.Exists(root)) continue;
                var hit = Directory.EnumerateFiles(root, "p" + plain + ".dds", SearchOption.AllDirectories).FirstOrDefault();
                if (hit != null) return File.ReadAllBytes(hit);
            }
            catch { }
        var dds = HeadFromBig(plain);
        if (dds != null)
        {
            try
            {
                var dst = Path.Combine(Directory.GetCurrentDirectory(), "fut_assets", "playerheads", "g4", "single", "p" + plain + ".dds");
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!); File.WriteAllBytes(dst, dds);
            }
            catch { }
        }
        return dds;
    }

    static byte[] HeadFromBig(long baseId)
    {
        lock (_headMiss)
        {
            if (_headMiss.Contains(baseId)) return null;
            var dir = GameDir();
            if (dir == null) return null;
            var needle = Encoding.ASCII.GetBytes("data/ui/imgassets/heads/p" + baseId + ".dds\0");
            var bigNames = new List<string> { "2dheads0.big", "2dheads0_extra.big" };
            try { foreach (var bf in Directory.GetFiles(Path.Combine(dir, "data", "ui", "imgAssets", "heads"), "*.big")) { var bn = Path.GetFileName(bf); if (!bigNames.Contains(bn, StringComparer.OrdinalIgnoreCase)) bigNames.Add(bn); } } catch { }
            foreach (var name in bigNames)
            {
                try
                {
                    var path = Path.Combine(dir, "data", "ui", "imgAssets", "heads", name);
                    if (!File.Exists(path)) { Log.Warn("HEAD: missing " + path); continue; }
                    if (!_bigIndex.TryGetValue(path, out var idx))
                    {
                        using var fs0 = File.OpenRead(path);
                        idx = new byte[(int)Math.Min(fs0.Length, 64 * 1024 * 1024)];
                        int got = 0; while (got < idx.Length) { int n = fs0.Read(idx, got, idx.Length - got); if (n <= 0) break; got += n; }
                        _bigIndex[path] = idx;
                        var low = (byte[])idx.Clone();
                        for (int q = 0; q < low.Length; q++) if (low[q] >= 65 && low[q] <= 90) low[q] += 32;
                        _bigIndexLow[path] = low;
                    }
                    int at = _bigIndexLow[path].AsSpan().IndexOf(needle);
                    if (at < 8) continue;
                    foreach (bool big in new[] { true, false })
                    {
                        long off = big ? BE(idx, at - 8) : LE(idx, at - 8), size = big ? BE(idx, at - 4) : LE(idx, at - 4);
                        using var fs = File.OpenRead(path);
                        if (size < 128 || size > 8 * 1024 * 1024 || off + size > fs.Length) continue;
                        var d = new byte[size]; fs.Position = off;
                        int r = 0; while (r < d.Length) { int n = fs.Read(d, r, d.Length - r); if (n <= 0) break; r += n; }
                        if (IsDds(d)) { Log.Info($"HEAD p{baseId}.dds from {name} ({d.Length} bytes)"); return d; }
                        var dec = DecodeChunkZip(d, baseId);
                        if (dec != null)
                        {
                            try { if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "head_raw.txt"))) return d; } catch { }
                            return dec;
                        }
                        Log.Warn($"HEAD p{baseId} in {name}: offset {off} size {size} is not a DDS (first bytes {BitConverter.ToString(d, 0, Math.Min(16, d.Length))})");
                    }
                }
                catch (Exception e) { Log.Warn("HEAD extract " + name + ": " + e.Message); }
            }
            _headMiss.Add(baseId);
            Log.Warn("HEAD: no usable head for player " + baseId);
            return null;
        }
    }
}
