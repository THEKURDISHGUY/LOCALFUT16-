#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

static partial class UtBackend
{
    const int SeasonSPGames = 10;
    const long SeasonSPForever = 2147483647L;
    const int SPTrophyResourceId = 1100;

    static int SPI(JsonObject o, string k) { try { return o[k] == null ? 0 : (int)L(o[k]); } catch { return 0; } }

    static JsonObject SeasonSPState()
    {
        if (Club["seasonSP2"] is not JsonObject s)
        {
            s = new JsonObject { ["division"] = 10, ["points"] = 0, ["played"] = 0, ["wins"] = 0, ["draws"] = 0, ["losses"] = 0,
                                 ["goalsFor"] = 0, ["goalsAgainst"] = 0, ["active"] = false, ["seasonsPlayed"] = 0, ["wireRound"] = 1 };
            Club["seasonSP2"] = s;
        }
        if (s["division"] == null) s["division"] = 10;
        // The game bakes the club crest into the saved fixture list. If the crest was changed since this season started,
        // start the season over so the fixture list shows the new crest (club, coins, players are kept).
        try
        {
            string bk = SPBadgeKey();
            string saved = s["badgeKey"]?.ToString();
            if (s["data"] != null && bk.Length > 0 && !string.IsNullOrEmpty(saved) && saved != bk && !File.Exists(FindFile("keep_season_on_badge.txt")))
            {
                SPResetRound(s); s.Remove("badgeKey");
                Console.WriteLine("[SEASON-SP] club badge changed -> season restarted so the fixture list uses the new crest");
            }
            else if (s["data"] == null) s["badgeKey"] = bk;
        }
        catch { }
        try { var rf = FindFile("reset_season.txt"); if (File.Exists(rf)) { SPResetRound(s); s["division"] = 10; File.Move(rf, rf + ".done", true); Console.WriteLine("[SEASON-SP] reset by reset_season.txt"); } } catch { }
        return s;
    }

    static readonly (int Div, int Promo, int Title, int Coins)[] SPLadder =
    {
        (1, 23, 26, 10000), (2, 21, 24, 7500), (3, 19, 22, 6000), (4, 18, 21, 5000), (5, 16, 19, 4400),
        (6, 16, 19, 3800), (7, 14, 17, 3300), (8, 13, 16, 2900), (9, 11, 14, 2100), (10, 9, 12, 1900),
    };

    static JsonObject SPPrize(string level, int threshold, int coins)
    {
        var awards = new JsonArray();
        if (coins > 0) awards.Add(new JsonObject { ["type"] = "coin", ["value"] = coins, ["assetId"] = 0, ["count"] = 1, ["halId"] = 0, ["teamId"] = 0 });
        return new JsonObject { ["prizeLevel"] = level, ["thresholdPoint"] = threshold, ["awardMappings"] = new JsonArray(new JsonObject { ["awards"] = awards }) };
    }

    // Opponents: ONLY the very best clubs (top 12 by the average of their best 11 players), always at the top difficulty.
    // Optional overrides (empty-able text files next to the exe):
    //   sp_teams.txt       team ids separated by comma/space/newline, e.g.  241 243 21 10 5 73  (replaces the automatic top list)
    //   sp_difficulty.txt  a number 1-5 (default 5 = hardest)
    const int SPElitePool = 12;
    static int SPDifficulty()
    {
        try { var f = FindFile("sp_difficulty.txt"); if (File.Exists(f) && int.TryParse(File.ReadAllText(f).Trim(), out var v)) return Math.Max(1, Math.Min(5, v)); } catch { }
        return 5;
    }

    static List<int> SPEliteClubs()
    {
        try
        {
            var f = FindFile("sp_teams.txt");
            if (File.Exists(f))
            {
                var ids = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), @"\d+").Select(m => int.Parse(m.Value)).Distinct().ToList();
                if (ids.Count > 0) return ids;
            }
        }
        catch { }
        var clubs = Db.Where(p => p.Club > 0 && p.AssetId < 900000 && !p.Special)
                      .GroupBy(p => p.Club)
                      .Select(g => new { Id = g.Key, N = g.Count(), R = g.OrderByDescending(x => x.Rating).Take(11).Average(x => x.Rating) })
                      .Where(c => c.N >= 11).OrderByDescending(c => c.R).Take(SPElitePool).Select(c => c.Id).ToList();
        if (clubs.Count < 10) clubs = new List<int> { 21, 241, 243, 5, 73, 10, 45, 1, 11, 22, 240, 52 };
        return clubs;
    }

    // elite=true  -> Single Player Season: only the top clubs, hardest difficulty.
    // elite=false -> everything else (Tournaments): exactly the original behaviour (top 40 clubs, difficulty as given).
    static JsonArray SPMatches(int difficulty, int count, int seed, bool elite = false)
    {
        List<int> clubs;
        if (elite) { difficulty = SPDifficulty(); clubs = SPEliteClubs(); }
        else
        {
            clubs = Db.Where(p => p.Club > 0 && p.AssetId < 900000 && !p.Special)
                      .GroupBy(p => p.Club)
                      .Select(g => new { Id = g.Key, N = g.Count(), R = g.OrderByDescending(x => x.Rating).Take(11).Average(x => x.Rating) })
                      .Where(c => c.N >= 11).OrderByDescending(c => c.R).Take(40).Select(c => c.Id).ToList();
            if (clubs.Count == 0) clubs = new List<int> { 1, 5, 7, 9, 10, 11, 21, 22, 73, 243 };
        }
        var rnd = new Random(seed);
        var pick = clubs.OrderBy(_ => rnd.Next()).Take(count).ToList();
        var arr = new JsonArray();
        for (int i = 0; i < pick.Count; i++)
            arr.Add(new JsonObject { ["teamId"] = pick[i], ["difficulty"] = difficulty, ["rewardMult"] = 1, ["roundId"] = i, ["coins"] = 300 + i * 25 });
        return arr;
    }

    static JsonObject SPListRecord((int Div, int Promo, int Title, int Coins) d)
    {
        int difficulty = Math.Max(1, Math.Min(5, 1 + (10 - d.Div) / 2));
        int maint = d.Div == 10 ? 0 : Math.Max(0, d.Promo - 5);
        int promoCoins = d.Div == 10 ? 1500 : Math.Max(500, d.Coins - 400);
        int maintCoins = d.Div == 10 ? 300 : Math.Max(300, d.Coins / 5);
        return new JsonObject
        {
            ["id"] = d.Div, ["divisionId"] = d.Div, ["type"] = "OFFLINE", ["numMatches"] = SeasonSPGames, ["matchLengthMin"] = 6,
            ["matches"] = SPMatches(difficulty, SeasonSPGames, 3100 + d.Div + SPI(SeasonSPState(), "seasonsPlayed") * 97, true),
            ["prizeSet"] = new JsonArray(SPPrize("RELEGATION", 0, 0), SPPrize("MAINTENANCE", maint, maintCoins), SPPrize("PROMOTION", d.Promo, promoCoins), SPPrize("CHAMPIONSHIP", d.Title, d.Coins)),
            ["elgOperation"] = "AND", ["elgReq"] = new JsonArray(), ["trophyResourceId"] = SPTrophyResourceId, ["trophyUseCount"] = 0,
            ["visStartDays"] = 3650, ["visEndDays"] = 3650, ["startDateTime"] = 0, ["endDateTime"] = SeasonSPForever,
            ["untilStartSeconds"] = 0, ["untilEndSeconds"] = 315360000,
        };
    }

    static JsonObject SPUserPayload()
    {
        SPFixContracts();
        var s = SeasonSPState();
        int div = Math.Max(1, Math.Min(10, SPI(s, "division")));
        bool active = s["active"]?.GetValue<bool>() ?? false;
        bool hasData = s["data"] != null;
        int played = SPI(s, "played");
        int spRound = Math.Max(Math.Max(1, Math.Min(10, played + 1)), Math.Max(1, SPI(s, "wireRound")));
        bool midSeason = hasData && spRound > 1;
        var user = new JsonObject
        {
            ["seasonId"] = div, ["divisionId"] = (active || hasData) ? div : 11 - div, ["offlineDivision"] = div, ["type"] = "offline",
            ["round"] = spRound, ["active"] = active, ["seasonState"] = active ? "active" : "inactive",
            ["seasonCompleted"] = false, ["seasonEndResult"] = 0, ["creationTime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 86400,
            ["points"] = SPI(s, "points"), ["wins"] = SPI(s, "wins"), ["draws"] = SPI(s, "draws"), ["losses"] = SPI(s, "losses"),
        };
        if (played > 0 || midSeason || hasData)
        {
            var e = new JsonObject();
            if (s["data"] != null) { e["data"] = s["data"].DeepClone(); e["dataVersion"] = Math.Max(1, SPI(s, "dataVersion")); e["seasonData"] = s["data"].DeepClone(); e["seasonDataVersion"] = Math.Max(1, SPI(s, "dataVersion")); }
            if (s["progressData"] != null) { e["progressData"] = s["progressData"].DeepClone(); e["progressDataVersion"] = Math.Max(1, SPI(s, "progressDataVersion")); e["progressdata"] = s["progressData"].DeepClone(); }
            foreach (var kv in user) e[kv.Key] = kv.Value?.DeepClone();
            e["seasonGamesWon"] = SPI(s, "wins"); e["seasonGamesDraw"] = SPI(s, "draws"); e["seasonGamesLost"] = SPI(s, "losses");
            user = e;
        }
        return user;
    }

    static JsonObject SPLoadPayload(int seasonId, int div)
    {
        var s = SeasonSPState();
        div = Math.Max(1, Math.Min(10, div));
        var doc = new JsonObject();
        if (s["data"] != null) { doc["data"] = s["data"].DeepClone(); doc["dataVersion"] = Math.Max(1, SPI(s, "dataVersion")); }
        if (s["progressData"] != null) { doc["progressData"] = s["progressData"].DeepClone(); doc["progressDataVersion"] = Math.Max(1, SPI(s, "progressDataVersion")); }
        bool active = (s["active"]?.GetValue<bool>() ?? false) || s["data"] != null;
        doc["seasonId"] = seasonId; doc["divisionId"] = active ? div : 11 - div;
        doc["round"] = Math.Max(Math.Max(1, Math.Min(10, SPI(s, "played") + 1)), Math.Max(1, SPI(s, "wireRound")));
        return doc;
    }

    static (int, string, byte[], Dictionary<string, string>?)? HandleSeasonSP(string method, string path, string target, byte[] body)
    {
        if (!path.Contains("fifa16/season")) return null;
        string bodyTxt = Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 2000));
        string low = ((target ?? "") + " " + (body.Length < 600 ? bodyTxt : "")).ToLowerInvariant();
        var pm = System.Text.RegularExpressions.Regex.Match(path, @"season/(\d+)/division/(\d+)/user");
        bool offlineIds = pm.Success && int.TryParse(pm.Groups[1].Value, out var sidTest) && sidTest >= 1 && sidTest <= 10;
        var qd = QueryOf(target ?? "");
        string qsid = null; int qsidv = 0;
        bool loadQuery = path.EndsWith("season/user") && qd.TryGetValue("seasonId", out qsid) && int.TryParse(qsid, out qsidv) && qsidv >= 1 && qsidv <= 10;
        string reqWords = ((path ?? "") + " " + (target ?? "") + " " + (body.Length < 200 ? bodyTxt : "")).ToLowerInvariant();
        bool forfeitReq = method != "GET" && (method == "DELETE" || reqWords.Contains("forfeit") || reqWords.Contains("abandon") || reqWords.Contains("restart") || path.Contains("/reset"));
        bool offline = low.Contains("type=offline") || path.Contains("season/offline") || offlineIds || loadQuery || forfeitReq;
        if (!offline) return null;

        (int, string, byte[], Dictionary<string, string>?) J(JsonNode n) => (200, "application/json", Encoding.UTF8.GetBytes(n.ToJsonString()), null);
        Console.WriteLine($"[SEASON-SP] {method} {target} body: {(bodyTxt.Length > 300 ? bodyTxt[..300] : bodyTxt)}");
        var s = SeasonSPState();

        if (forfeitReq)
        {
            // FORFEIT SEASON: start the single player season over. Club, coins, players, items and the all-time record stay untouched.
            if (qd.TryGetValue("divisionId", out var fd) && int.TryParse(fd, out var fdv) && fdv >= 1 && fdv <= 10) s["division"] = fdv;
            SPResetRound(s); Save();
            Console.WriteLine($"[SEASON-SP] FORFEIT -> season restarted (division {s["division"]}), club kept");
            var fa = SPUserPayload(); fa["credits"] = Club["coins"]!.DeepClone();
            return J(fa);
        }

        if (method == "GET")
        {
            if (path.EndsWith("season/list"))
            {
                var defs = new JsonArray(); foreach (var d in SPLadder.OrderBy(x => x.Div)) defs.Add(SPListRecord(d));
                Console.WriteLine($"[SEASON-SP] list: all 10 divisions (FIFA 15 contract), asked: {(qd.TryGetValue("divisionList", out var dl) ? dl : "-")}");
                return J(new JsonObject { ["seasons"] = defs });
            }
            if (loadQuery && qd.TryGetValue("divisionId", out var qdiv) && int.TryParse(qdiv, out var qdivv))
            { Console.WriteLine($"[SEASON-SP] load data season={qsid} division={qdivv}"); return J(SPLoadPayload(int.Parse(qsid), qdivv)); }
            if (path.EndsWith("season/user/history"))
                return J(new JsonObject { ["seasonGamesWon"] = SPI(s, "wins"), ["seasonGamesDraw"] = SPI(s, "draws"), ["seasonGamesLost"] = SPI(s, "losses"), ["seasonCompleted"] = SPI(s, "seasonsPlayed"),
                                          ["seasonsScoredGoals"] = SPI(s, "goalsFor"), ["seasonConcededGoals"] = SPI(s, "goalsAgainst") });
            if (pm.Success) return J(SPLoadPayload(int.Parse(pm.Groups[1].Value), int.Parse(pm.Groups[2].Value)));
            var u = SPUserPayload();
            Console.WriteLine($"[SEASON-SP] user: division={u["offlineDivision"]} wireDivisionId={u["divisionId"]} active={u["active"]} round={u["round"]}");
            return J(u);
        }

        if ((method == "PUT" || method == "POST") && pm.Success)
        {
            int dv = int.Parse(pm.Groups[2].Value);
            s["division"] = Math.Max(1, Math.Min(10, dv)); s["active"] = true;
            JsonNode rq = null; try { rq = JsonNode.Parse(Encoding.UTF8.GetString(body)); } catch { }
            if (rq != null)
            {
                foreach (var k in new[] { "data", "dataVersion", "progressData", "progressDataVersion" }) if (rq[k] != null) s[k] = rq[k].DeepClone();
                if (rq["round"] != null) s["wireRound"] = (int)L(rq["round"]);
            }
            s["badgeKey"] = SPBadgeKey();
            Console.WriteLine($"[SEASON-SP] saved the game's season data: division={dv} round={s["wireRound"]}");
            Save();
            return J(SPLoadPayload(int.Parse(pm.Groups[1].Value), dv));
        }
        if (path.Contains("season/offline/reset"))
        {
            if (qd.TryGetValue("divisionId", out var rd) && int.TryParse(rd, out var rdv)) s["division"] = Math.Max(1, Math.Min(10, rdv));
            SPResetRound(s); Save();
            return J(new JsonObject { ["success"] = true });
        }

        JsonNode req = null; try { req = JsonNode.Parse(bodyTxt.Length == 0 ? "{}" : Encoding.UTF8.GetString(body)); } catch { }
        string joined = (path + " " + (req?.ToJsonString() ?? "")).ToLowerInvariant();
        bool isResult = joined.Contains("result") || joined.Contains("win") || joined.Contains("loss") || joined.Contains("draw") || joined.Contains("goal") || joined.Contains("match");
        if (isResult)
        {
            int gf = 0, ga = 0;
            try { gf = (int)L(req?["goalsFor"] ?? req?["homeGoals"] ?? req?["goals"] ?? req?["scoreFor"]); } catch { }
            try { ga = (int)L(req?["goalsAgainst"] ?? req?["awayGoals"] ?? req?["scoreAgainst"]); } catch { }
            bool won = gf > ga || joined.Contains("\"win\"") || joined.Contains("/win");
            bool draw = !won && (joined.Contains("draw") || (gf == ga && (gf > 0 || joined.Contains("goal"))));
            if (won) { s["wins"] = SPI(s, "wins") + 1; s["points"] = SPI(s, "points") + 3; }
            else if (draw) { s["draws"] = SPI(s, "draws") + 1; s["points"] = SPI(s, "points") + 1; }
            else s["losses"] = SPI(s, "losses") + 1;
            s["played"] = SPI(s, "played") + 1; s["active"] = true;
            s["goalsFor"] = SPI(s, "goalsFor") + gf; s["goalsAgainst"] = SPI(s, "goalsAgainst") + ga;
            Console.WriteLine($"[SEASON-SP] result: {(won ? "WIN" : draw ? "DRAW" : "LOSS")}  ({SPI(s, "played")}/{SeasonSPGames}, {SPI(s, "points")} pts)");
            if (SPI(s, "played") >= SeasonSPGames)
            {
                int div = SPI(s, "division"), pts = SPI(s, "points");
                var lad = SPLadder.First(x => x.Div == div);
                int reward = pts >= lad.Title ? lad.Coins : 500 + pts * 100;
                Club["coins"] = (int)L(Club["coins"]) + reward;
                if (pts >= lad.Promo && div > 1) s["division"] = div - 1;
                else if (pts < (div == 10 ? 0 : Math.Max(0, lad.Promo - 5)) && div < 10) s["division"] = div + 1;
                s["seasonsPlayed"] = SPI(s, "seasonsPlayed") + 1;
                Console.WriteLine($"[SEASON-SP] finished with {pts} points, +{reward} coins, division now {s["division"]}");
                SPResetRound(s);
            }
        }
        Save();
        var ans = SPUserPayload(); ans["credits"] = Club["coins"]!.DeepClone();
        return J(ans);
    }

    static string SPBadgeKey()
    {
        try
        {
            if (Club["items"] is not JsonArray items) return "";
            var bd = items.FirstOrDefault(x => x != null && (x["itemState"]?.ToString() ?? "") == "activeBadge");
            if (bd == null) return "";
            return (bd["id"]?.ToString() ?? "") + "|" + (bd["resourceId"]?.ToString() ?? "") + "|" + (bd["displayName"]?.ToString() ?? "");
        }
        catch { return ""; }
    }

    static void SPResetRound(JsonObject s)
    {
        foreach (var k in new[] { "points", "played", "wins", "draws", "losses", "goalsFor", "goalsAgainst" }) s[k] = 0;
        s["active"] = false; s["wireRound"] = 1; s.Remove("badgeKey");
        foreach (var k in new[] { "data", "dataVersion", "progressData", "progressDataVersion" }) s.Remove(k);
    }
}
