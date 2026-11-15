#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

static partial class UtBackend
{
    static int _marketCountCache = -1;
    static int MarketCount()
    {
        if (_marketCountCache < 0) _marketCountCache = Db.Count(p => p.AssetId > 100 && p.AssetId < 900000 && KeepCard(p));
        return _marketCountCache;
    }

    static JsonObject PY()
    {
        if (Club["seasonPY"] is not JsonObject s) { s = new JsonObject(); Club["seasonPY"] = s; }
        void D(string k, JsonNode v) { if (s[k] == null) s[k] = v; }
        D("division", 10); D("points", 0); D("round", 0); D("wins", 0); D("draws", 0); D("losses", 0); D("gf", 0); D("ga", 0);
        D("active", false); D("wireData", ""); D("wireDV", 1); D("wireProgress", ""); D("wirePV", 1); D("wireRound", 0);
        D("pending", false); D("awaitPost", false); D("recW", 0); D("recD", 0); D("recL", 0); D("completed", 0);
        D("promotions", 0); D("relegations", 0); D("titles", 0); D("coinsWon", 0); D("startedAt", 0); D("created", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return s;
    }
    static int PI(string k) { try { return (int)L(PY()[k]); } catch { return 0; } }
    static bool PB(string k) { try { return PY()[k]?.GetValue<bool>() ?? false; } catch { return false; } }
    static string PS(string k) { try { return PY()[k]?.ToString() ?? ""; } catch { return ""; } }
    static int DivOffline() { try { return Math.Clamp(PI("division"), 1, 10); } catch { return 10; } }
    static int DivOnline() { try { return SI(SeasonState(), "divisionId"); } catch { return 10; } }

    static readonly (int div, int promote, int coins)[] PyLadder =
    {
        (1, 23, 10000), (2, 21, 7500), (3, 19, 6000), (4, 18, 5000), (5, 16, 4400),
        (6, 16, 3800), (7, 14, 3300), (8, 13, 2900), (9, 11, 2100), (10, 9, 1900),
    };
    static int PyDifficulty(int div) => Math.Max(1, Math.Min(5, 1 + (10 - div) / 2));
    static int PyTitlePts(int div) => div == 10 ? 12 : Math.Min(30, PyLadder[div - 1].promote + 3);
    static int PyKeepPts(int div) => div == 10 ? 0 : Math.Max(0, PyLadder[div - 1].promote - 5);

    static JsonObject PyPrize(string level, int threshold, int coins)
    {
        var awards = new JsonArray();
        if (coins > 0) awards.Add(new JsonObject { ["type"] = "coin", ["value"] = coins, ["assetId"] = 0, ["count"] = 1, ["halId"] = 0, ["teamId"] = 0 });
        return new JsonObject { ["prizeLevel"] = level, ["thresholdPoint"] = threshold, ["awardMappings"] = new JsonArray(new JsonObject { ["awards"] = awards }) };
    }

    static JsonObject PyListRecord(int div)
    {
        var (_, promote, champ) = PyLadder[div - 1];
        int diff = PyDifficulty(div), title = PyTitlePts(div), keep = PyKeepPts(div);
        int promoCoins = div == 10 ? 1500 : Math.Max(500, champ - 400);
        int keepCoins = div == 10 ? 300 : Math.Max(300, champ / 5);
        int[] teams = { 1, 5, 7, 9, 10, 11, 21, 22, 73, 243 };
        var matches = new JsonArray();
        for (int i = 0; i < 10; i++)
            matches.Add(new JsonObject { ["teamId"] = teams[i % teams.Length], ["difficulty"] = diff, ["rewardMult"] = 1, ["roundId"] = i, ["coins"] = 300 + i * 25 + (10 - div) * 50 });
        return new JsonObject
        {
            ["id"] = div, ["divisionId"] = div, ["type"] = "OFFLINE", ["numMatches"] = 10, ["matchLengthMin"] = 6, ["matches"] = matches,
            ["prizeSet"] = new JsonArray(PyPrize("RELEGATION", 0, 0), PyPrize("MAINTENANCE", keep, keepCoins), PyPrize("PROMOTION", promote, promoCoins), PyPrize("CHAMPIONSHIP", title, champ)),
            ["elgOperation"] = "AND", ["elgReq"] = new JsonArray(), ["trophyResourceId"] = 1100, ["trophyUseCount"] = 0,
            ["visStartDays"] = 3650, ["visEndDays"] = 3650, ["startDateTime"] = 0, ["endDateTime"] = 2147483647, ["untilStartSeconds"] = 0, ["untilEndSeconds"] = 315360000,
        };
    }

    static JsonObject PyUser()
    {
        var s = PY(); int div = DivOffline(); bool active = PB("active"); string data = PS("wireData");
        bool activeWire = active || data.Length > 0; int wireDiv = activeWire ? div : 11 - div;
        var user = new JsonObject
        {
            ["seasonId"] = div, ["divisionId"] = wireDiv, ["offlineDivision"] = div, ["type"] = "offline", ["round"] = Math.Clamp(PI("round") + 1, 1, 10),
            ["active"] = active, ["seasonState"] = active ? "active" : "inactive", ["seasonCompleted"] = false, ["seasonEndResult"] = 0,
            ["creationTime"] = L(s["created"]), ["points"] = PI("points"), ["wins"] = PI("wins"), ["draws"] = PI("draws"), ["losses"] = PI("losses"),
        };
        if (PI("round") > 0)
        {
            var e = new JsonObject();
            string prog = PS("wireProgress");
            if (data.Length > 0) { e["data"] = data; e["dataVersion"] = Math.Max(1, PI("wireDV")); e["seasonData"] = data; e["seasonDataVersion"] = Math.Max(1, PI("wireDV")); }
            if (prog.Length > 0) { e["progressData"] = prog; e["progressDataVersion"] = Math.Max(1, PI("wirePV")); e["progressdata"] = prog; }
            foreach (var kv in user) e[kv.Key] = kv.Value?.DeepClone();
            e["seasonGamesWon"] = PI("wins"); e["seasonGamesDraw"] = PI("draws"); e["seasonGamesLost"] = PI("losses");
            user = e;
        }
        return user;
    }

    static JsonObject PyProgress(int seasonId, int div)
    {
        div = Math.Clamp(div, 1, 10);
        var doc = new JsonObject(); string data = PS("wireData"), prog = PS("wireProgress");
        if (data.Length > 0) { doc["data"] = data; doc["dataVersion"] = Math.Max(1, PI("wireDV")); }
        if (prog.Length > 0) { doc["progressData"] = prog; doc["progressDataVersion"] = Math.Max(1, PI("wirePV")); }
        doc["seasonId"] = seasonId;
        doc["divisionId"] = (PB("active") || data.Length > 0) ? div : 11 - div;
        int semantic = Math.Clamp(PI("round") + 1, 1, 10); int saved = PI("wireRound") > 0 ? PI("wireRound") : semantic;
        doc["round"] = Math.Max(semantic, saved);
        return doc;
    }

    static bool PySaveBlob(JsonNode req)
    {
        if (req is not JsonObject o) return false;
        var s = PY(); int semantic = Math.Clamp(PI("round") + 1, 1, 10);
        int curWire = PI("wireRound") > 0 ? PI("wireRound") : semantic; int incoming = curWire;
        try { if (o["round"] != null) incoming = Math.Max(1, (int)L(o["round"])); } catch { }
        bool hasData = !string.IsNullOrEmpty(o["data"]?.ToString());
        if (PB("awaitPost") && hasData)
        {
            if (incoming < semantic) { Console.WriteLine($"[PYSEASON] stale post-match save refused (incoming round {incoming}, real round {semantic})"); return false; }
            s["wireRound"] = incoming; s["awaitPost"] = false;
        }
        else
        {
            if (PI("round") > 0 && incoming < Math.Max(semantic, curWire)) { Console.WriteLine($"[PYSEASON] stale rollback ignored (incoming round {incoming})"); return false; }
            s["wireRound"] = Math.Max(incoming, semantic);
        }
        foreach (var k in new[] { "data" }) if (o[k] != null) s["wireData"] = o[k]!.ToString();
        foreach (var k in new[] { "progressData", "progressdata" }) if (o[k] != null) s["wireProgress"] = o[k]!.ToString();
        try { if (o["dataVersion"] != null) s["wireDV"] = (int)L(o["dataVersion"]); } catch { }
        try { if (o["progressDataVersion"] != null) s["wirePV"] = (int)L(o["progressDataVersion"]); } catch { }
        return true;
    }

    static void PySettle(string result, int gf, int ga)
    {
        var s = PY();
        if (result == "win") { s["wins"] = PI("wins") + 1; s["points"] = PI("points") + 3; s["recW"] = PI("recW") + 1; }
        else if (result == "draw") { s["draws"] = PI("draws") + 1; s["points"] = PI("points") + 1; s["recD"] = PI("recD") + 1; }
        else { s["losses"] = PI("losses") + 1; s["recL"] = PI("recL") + 1; }
        s["round"] = PI("round") + 1; s["gf"] = PI("gf") + gf; s["ga"] = PI("ga") + ga;
        if (PI("round") >= 10)
        {
            int div = DivOffline(), pts = PI("points"), nd = div; string what = "stays";
            if (div > 1 && pts >= PyLadder[div - 1].promote) { nd = div - 1; what = "PROMOTED"; s["promotions"] = PI("promotions") + 1; }
            else if (div < 10 && pts < PyKeepPts(div)) { nd = div + 1; what = "relegated"; s["relegations"] = PI("relegations") + 1; }
            int prize = 0;
            if (pts >= PyTitlePts(div)) { prize = PyLadder[div - 1].coins; s["titles"] = PI("titles") + 1; what += " + CHAMPION"; }
            else if (what == "PROMOTED") prize = div == 10 ? 1500 : Math.Max(500, PyLadder[div - 1].coins - 400);
            else if (what == "stays") prize = div == 10 ? 300 : Math.Max(300, PyLadder[div - 1].coins / 5);
            if (prize > 0) { Club["coins"] = (int)L(Club["coins"]) + prize; s["coinsWon"] = PI("coinsWon") + prize; }
            Console.WriteLine($"[PYSEASON] season over: {pts} points in division {div} -> {what}, division {nd}, +{prize} coins");
            s["division"] = nd; s["completed"] = PI("completed") + 1;
            foreach (var k in new[] { "points", "round", "wins", "draws", "losses", "gf", "ga", "wireRound" }) s[k] = 0;
            s["active"] = false; s["wireData"] = ""; s["wireProgress"] = ""; s["wireDV"] = 1; s["wirePV"] = 1;
        }
    }

    static (int completion, int skill, int total) PyReward(JsonObject doc, string reason)
    {
        var mine = doc["myMatchStats"] as JsonObject; var opp = doc["opponentMatchStats"] as JsonObject;
        int G(JsonObject o, string k) { try { return o == null ? 0 : Math.Max(0, (int)L(o[k])); } catch { return 0; } }
        bool completed = reason == "WIN" || reason == "DRAW" || reason == "LOSS";
        if (!completed) return (0, 0, 0);
        int goals = G(mine, "goals"), against = G(opp, "goals"), shots = G(mine, "shotsOnTarget"), tackles = G(mine, "successfulTackles"), corners = G(mine, "corners");
        int pass = Math.Min(100, G(mine, "passingPercentage")), poss = Math.Min(100, G(mine, "possessionPercentage")), fouls = G(mine, "fouls");
        int yel = G(mine, "yellowCards"), red = G(mine, "redCards"), offs = G(mine, "offsides"); bool motm = G(mine, "manOfTheMatch") > 0; bool clean = against == 0;
        int raw = Math.Min(goals * 40, 200) + Math.Min(shots * 5, 75) + Math.Min(tackles, 20) + Math.Min(corners * 5, 50) + (clean ? 75 : 0) + Math.Min(pass, 80) + Math.Min(poss, 80)
                  + (motm ? 15 : 0) - Math.Min(against * 20, 80) - Math.Min(fouls, 20) - Math.Min((yel + red) * 10, 80) - Math.Min(offs, 15);
        int skill = Math.Max(0, raw);
        return (325, skill, 325 + skill);
    }

    static (int, string, byte[], Dictionary<string, string>?)? HandleReborn16(string method, string path, string target, byte[] body)
    {
        if (!path.Contains("fifa16/")) return null;
        (int, string, byte[], Dictionary<string, string>?) J(JsonNode n) => (200, "application/json", Encoding.UTF8.GetBytes(n.ToJsonString()), null);
        string tail = path.Substring(path.IndexOf("fifa16/") + 7);

        if (method == "GET" && tail == "hub" && !File.Exists(FindFile("hubcredits_off.txt")))
        {
            var hb = CreditsObj(); hb["clubName"] = ClubNameNow(); hb["personaName"] = Persona;
            var htp = TradePileJson(); long selling = L(htp["selling"]), sold = L(htp["sold"]); int market = MarketCount();
            hb["auctionCount"] = market + selling; hb["liveTransfers"] = market + selling;
            hb["clubPlayers"] = ((JsonArray)Club["items"]!).Count(x => x != null && x["totwOpp"] == null && x["itemType"]?.ToString() == "player");
            hb["watchlist"] = new JsonObject { ["count"] = 0L, ["outbid"] = 0L, ["winning"] = 0L };
            hb["tradePile"] = new JsonObject { ["count"] = htp["count"]!.DeepClone(), ["selling"] = selling, ["sold"] = sold, ["maximum"] = 100L };
            hb["divisionOnline"] = DivOnline(); hb["divisionOffline"] = DivOffline();
            Console.WriteLine($"[HUB] market {market} + selling {selling} = {market + selling} live transfers; transfer list {htp["count"]} (sold {sold})");
            return J(hb);
        }

        if (method == "GET" && (tail == "tradepile/counts" || tail == "auctionhouse/counts"))
        {
            var tpc = TradePileJson(); long active = L(tpc["selling"]), sold = L(tpc["sold"]), total = L(tpc["count"]); int market = MarketCount();
            tpc["auctionCount"] = active; tpc["maxAuctionsAllowed"] = 100L; tpc["maximumTradePileSize"] = 100L;
            tpc["currentTradePileSize"] = total; tpc["tradePileCount"] = total; tpc["tradePileItems"] = total; tpc["transferListCount"] = total;
            tpc["selling"] = active; tpc["sold"] = sold; tpc["liveTransfers"] = market + active;
            Console.WriteLine($"[HUB] tradepile/counts: active {active}, sold {sold}, liveTransfers {market + active}");
            return J(tpc);
        }

        if (method == "GET" && tail == "user" && !File.Exists(FindFile("user15_off.txt")))
        {
            var items = ((JsonArray)Club["items"]!).Where(x => x != null && x["totwOpp"] == null).ToList();
            long players = items.Count(x => x["itemType"]?.ToString() == "player");
            long cons = items.Count(x => x["itemType"]?.ToString() == "development");
            long unassigned = (Club["unassigned"] as JsonArray)?.Count ?? 0;
            var o = new JsonObject
            {
                ["pileClubPlayers"] = players, ["pileSquadPlayers"] = 0L, ["pileConsumables"] = cons, ["pileManagers"] = 0L, ["pileClubItems"] = 0L,
                ["pileClubTotal"] = players + cons, ["pilePack"] = unassigned, ["kitCount"] = 0L, ["badgeCount"] = 0L, ["stadiumCount"] = 0L, ["ballCount"] = 0L,
                ["clubPlayers"] = players, ["clubItemCount"] = cons, ["clubCount"] = players + cons,
                ["accountCreatedPlatformName"] = "pc", ["personaId"] = 1000000002L, ["clubName"] = ClubNameNow(), ["clubAbbr"] = ClubAbbrNow(),
                ["clubNameChangeAllowed"] = 0L, ["returningUser"] = 1L, ["established"] = L(PY()["created"]).ToString(),
            };
            foreach (var kv in CreditsObj()) o[kv.Key] = kv.Value?.DeepClone();
            o["bidTokens"] = new JsonObject { ["count"] = 0L, ["updateTime"] = 0L };
            var son = SeasonState();
            o["divisionOffline"] = DivOffline(); o["divisionOnline"] = DivOnline();
            o["won"] = PI("recW") + SI(son, "wins"); o["draw"] = PI("recD") + SI(son, "draws"); o["loss"] = PI("recL") + SI(son, "losses");
            o["trophies"] = PI("titles"); o["purchased"] = false; o["fifaPointsFromLastYear"] = 0L; o["fifaPointsTransferredStatus"] = 0L;
            o["reliability"] = new JsonObject { ["reliability"] = 100L, ["matchUnfinishedTime"] = 0L };
            Console.WriteLine($"[USER15] coins {Club["coins"]}, record {o["won"]}-{o["draw"]}-{o["loss"]}, division offline {DivOffline()} / online {DivOnline()}");
            return J(o);
        }

        if (File.Exists(FindFile("pyseason_off.txt"))) return null;
        var q = QueryOf(target);
        string type = q.TryGetValue("type", out var tv) ? tv.ToLowerInvariant() : "";
        JsonNode req = null; try { if (body.Length > 0) req = JsonNode.Parse(Encoding.UTF8.GetString(body)); } catch { }
        var reqObj = req as JsonObject;

        var m = Regex.Match(tail, @"^season/(\d+)/division/(\d+)(/user)?$");
        if (m.Success)
        {
            int sid = int.Parse(m.Groups[1].Value), dv = int.Parse(m.Groups[2].Value);
            if (sid < 1 || sid > 10) return null;
            var s = PY();
            if (m.Groups[3].Success)
            {
                if (method == "PUT" || method == "POST")
                {
                    s["active"] = true; s["division"] = Math.Clamp(dv, 1, 10);
                    if (PI("startedAt") == 0) s["startedAt"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    PySaveBlob(req); Save();
                }
                var resp = PyProgress(sid, dv);
                Console.WriteLine($"[PYSEASON] {method} season/{sid}/division/{dv}/user -> round {resp["round"]}, division {s["division"]}");
                return J(resp);
            }
            if (method == "DELETE" || method == "PUT" || method == "POST")
            {
                Console.WriteLine($"[PYSEASON] terminal ack season={sid} division={dv} (season state kept)");
                return J(new JsonObject { ["success"] = true, ["state"] = new JsonObject { ["division"] = DivOffline(), ["points"] = PI("points"), ["round"] = PI("round") } });
            }
            return null;
        }

        if (tail == "season/list" && (type == "offline" || type == ""))
        {
            var arr = new JsonArray(); for (int d = 1; d <= 10; d++) arr.Add(PyListRecord(d));
            Console.WriteLine($"[PYSEASON] season/list offline -> 10 division records");
            return J(new JsonObject { ["seasons"] = arr });
        }
        if (tail == "season/user" && (type == "offline" || (type == "" && (q.ContainsKey("divisionId") || q.ContainsKey("seasonId")))))
        {
            if (method == "GET" && q.ContainsKey("divisionId") && q.ContainsKey("seasonId"))
            {
                int.TryParse(q["divisionId"], out var rd); int.TryParse(q["seasonId"], out var rs);
                Console.WriteLine($"[PYSEASON] season/user load data division={rd} season={rs}");
                return J(PyProgress(rs == 0 ? DivOffline() : rs, rd == 0 ? DivOffline() : rd));
            }
            if (method == "PUT" || method == "POST") { PY()["active"] = true; PySaveBlob(req); Save(); }
            var u = PyUser();
            Console.WriteLine($"[PYSEASON] {method} season/user offline -> division {DivOffline()}, round {u["round"]}, active {PB("active")}");
            return J(u);
        }
        if (tail == "season" && (type == "offline" || type == ""))
        {
            if (method == "PUT" || method == "POST") { PY()["active"] = true; PySaveBlob(req); Save(); }
            return J(PyUser());
        }
        if (tail == "season/user/history" && (type == "offline" || type == ""))
        {
            var h = new JsonObject
            {
                ["seasonGamesWon"] = PI("recW"), ["seasonGamesDraw"] = PI("recD"), ["seasonGamesLost"] = PI("recL"), ["seasonWins"] = PI("recW"),
                ["seasonOnlineWins"] = 0, ["seasonOnlineDraws"] = 0, ["seasonOnlineLosses"] = 0, ["seasonCompleted"] = PI("completed"),
                ["seasonTitlesWon"] = PI("titles"), ["seasonPromotions"] = PI("promotions"), ["seasonRelegations"] = PI("relegations"),
                ["seasonCoins"] = PI("coinsWon"), ["seasonsScoredGoals"] = PI("gf"), ["seasonConcededGoals"] = PI("ga"),
            };
            return J(h);
        }
        var rm = Regex.Match(tail, @"^season/(offline|online)/reset$");
        if (rm.Success)
        {
            if (rm.Groups[1].Value == "offline")
            {
                var s = PY();
                if (q.TryGetValue("divisionId", out var rdv) && int.TryParse(rdv, out var rdn)) s["division"] = Math.Clamp(rdn, 1, 10);
                foreach (var k in new[] { "points", "round", "wins", "draws", "losses", "gf", "ga", "wireRound" }) s[k] = 0;
                s["active"] = false; s["wireData"] = ""; s["wireProgress"] = ""; Save();
                Console.WriteLine("[PYSEASON] offline season reset");
            }
            return J(new JsonObject { ["success"] = true });
        }

        if (tail == "match" && (method == "POST" || method == "PUT") && reqObj != null && reqObj["squadId"] != null)
        {
            string mt = reqObj["type"]?.ToString()?.ToUpperInvariant() ?? "";
            if (mt != "ONLINE")
            {
                PY()["pending"] = true; PY()["startedAt"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); Save();
                Console.WriteLine($"[PYMATCH] offline match started (type '{mt}'), division {DivOffline()} round {PI("round") + 1}");
            }
            return null;
        }
        if (tail == "match/end" && (method == "POST" || method == "PUT") && reqObj != null)
        {
            string reason = (reqObj["endReason"]?.ToString() ?? reqObj["result"]?.ToString() ?? "NO_CONTEST").Trim().ToUpperInvariant();
            bool terminal = reason == "WIN" || reason == "DRAW" || reason == "LOSS" || reason == "QUIT" || reason == "DNF" || reason == "FORFEIT";
            if (!PB("pending"))
            {
                if (terminal && Club["lastMatchEnd"] is JsonObject cached) { Console.WriteLine("[PYMATCH] match/end retry -> cached settlement"); return J((JsonObject)cached.DeepClone()); }
                return null;
            }
            string respReason = (reason == "QUIT" || reason == "DNF" || reason == "FORFEIT") ? "LOSS" : reason;
            var mine = reqObj["myMatchStats"] as JsonObject; var opp = reqObj["opponentMatchStats"] as JsonObject;
            int gf = 0, ga = 0; try { gf = Math.Max(0, (int)L(mine?["goals"])); } catch { } try { ga = Math.Max(0, (int)L(opp?["goals"])); } catch { }
            var (completion, skill, total) = PyReward(reqObj, reason);
            int div = DivOffline(), round = PI("round") + 1;
            if (terminal)
            {
                string key = reason == "WIN" ? "win" : reason == "DRAW" ? "draw" : "loss";
                if (total > 0) Club["coins"] = (int)L(Club["coins"]) + total;
                PySettle(key, gf, ga);
                PY()["awaitPost"] = true;
                Console.WriteLine($"[PYMATCH] {reason} {gf}-{ga}  +{total} coins  season: division {DivOffline()}, {PI("points")} pts, round {PI("round") + 1}, record {PI("recW")}-{PI("recD")}-{PI("recL")}");
            }
            long credits = L(Club["coins"]);
            int seconds = reason == "WIN" || reason == "DRAW" || reason == "LOSS" ? 5400 : Math.Max(1, (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - L(PY()["startedAt"])));
            var resp = new JsonObject
            {
                ["endReason"] = respReason, ["secondsPlayed"] = seconds, ["matchDifficulty"] = PyDifficulty(div), ["items"] = new JsonArray(),
                ["matchData"] = reqObj["matchData"]?.ToString() ?? "", ["credits"] = credits, ["coins"] = credits,
                ["allCoins"] = total, ["matchCoins"] = Math.Max(0, total - completion), ["participationAward"] = completion, ["gameModeAward"] = new JsonObject { ["coins"] = 0 },
                ["boostConis"] = 0, ["boostCountLeft"] = 0, ["matchCoinMultipliers"] = new JsonArray(), ["matchCoinPartials"] = new JsonArray(),
                ["seasonCoins"] = 0, ["tournamentCoins"] = 0, ["teamOfTournamentWinner"] = false,
                ["completionAward"] = completion, ["skillAward"] = skill, ["rewardCoins"] = total, ["totalCoins"] = total, ["dnfModifier"] = 1.0,
                ["seasonId"] = DivOffline(), ["divisionId"] = DivOffline(), ["seasonRound"] = PI("round") + 1, ["seasonPoints"] = PI("points"),
                ["seasonGamesWon"] = PI("wins"), ["seasonGamesDraw"] = PI("draws"), ["seasonGamesLost"] = PI("losses"),
            };
            if (mine != null) resp["myMatchStats"] = mine.DeepClone();
            if (opp != null) resp["opponentMatchStats"] = opp.DeepClone();
            if (terminal) { PY()["pending"] = false; Club["lastMatchEnd"] = resp.DeepClone(); }
            Save();
            return J(resp);
        }
        return null;
    }
}
