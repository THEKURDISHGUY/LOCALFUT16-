#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

static partial class UtBackend
{
    static void SPFixContracts()
    {
        try
        {
            int n = 0;
            if (Club?["items"] is JsonArray a)
                foreach (var x in a)
                {
                    if (x is not JsonObject o) continue;
                    string t = o["itemType"]?.ToString() ?? "";
                    if (t != "player" && t != "staff" && t != "manager") continue;
                    int c = 0; try { c = (int)L(o["contract"] ?? o["contracts"]); } catch { }
                    if (c < 5) { o["contract"] = 99; o["contracts"] = 99; n++; }
                }
            if (n > 0) { Save(); Console.WriteLine($"[SEASON-SP] contracts refreshed on {n} item(s)"); }
        }
        catch (Exception ex) { Console.WriteLine("[SEASON-SP] contract fix failed: " + ex.Message); }
    }

    static int MEI(JsonNode n) { try { return n == null ? 0 : (int)L(n); } catch { return 0; } }

    static bool MEPending() { try { return Club["offlineMatchPending"]?.GetValue<bool>() ?? false; } catch { return false; } }

    static (int Completion, int Skill, int Total) MEReward(JsonObject mine, JsonObject opp, bool completed)
    {
        if (!completed) return (0, 0, 0);
        int G(JsonObject o, string k) => Math.Max(0, o == null ? 0 : MEI(o[k]));
        int goals = G(mine, "goals"), against = G(opp, "goals"), shots = G(mine, "shotsOnTarget"), tackles = G(mine, "successfulTackles"), corners = G(mine, "corners");
        int pass = Math.Min(100, G(mine, "passingPercentage")), poss = Math.Min(100, G(mine, "possessionPercentage"));
        int fouls = G(mine, "fouls"), yellows = G(mine, "yellowCards"), reds = G(mine, "redCards"), offs = G(mine, "offsides");
        bool motm = G(mine, "manOfTheMatch") != 0, clean = against == 0;
        int completion = 325;
        int skill = Math.Min(goals * 40, 200) + Math.Min(shots * 5, 75) + Math.Min(tackles, 20) + Math.Min(corners * 5, 50) + (clean ? 75 : 0)
                  + Math.Min(pass, 80) + Math.Min(poss, 80) + (motm ? 15 : 0)
                  - Math.Min(against * 20, 80) - Math.Min(fouls, 20) - Math.Min((yellows + reds) * 10, 80) - Math.Min(offs, 15);
        skill = Math.Max(0, skill);
        return (completion, skill, completion + skill);
    }

    static string SPSettle(JsonObject s, bool won, bool draw, int gf, int ga)
    {
        if (won) { s["wins"] = SPI(s, "wins") + 1; s["points"] = SPI(s, "points") + 3; }
        else if (draw) { s["draws"] = SPI(s, "draws") + 1; s["points"] = SPI(s, "points") + 1; }
        else s["losses"] = SPI(s, "losses") + 1;
        s["played"] = SPI(s, "played") + 1; s["active"] = true;
        s["goalsFor"] = SPI(s, "goalsFor") + gf; s["goalsAgainst"] = SPI(s, "goalsAgainst") + ga;
        string note = "";
        if (SPI(s, "played") >= SeasonSPGames)
        {
            int div = SPI(s, "division"), pts = SPI(s, "points");
            var lad = SPLadder.First(x => x.Div == div);
            int reward = pts >= lad.Title ? lad.Coins : 500 + pts * 100;
            Club["coins"] = (int)L(Club["coins"]) + reward;
            if (pts >= lad.Promo && div > 1) s["division"] = div - 1;
            else if (pts < (div == 10 ? 0 : Math.Max(0, lad.Promo - 5)) && div < 10) s["division"] = div + 1;
            s["seasonsPlayed"] = SPI(s, "seasonsPlayed") + 1;
            note = $" | SEASON OVER: {pts} pts, +{reward} coins, division now {s["division"]}";
            SPResetRound(s);
        }
        return note;
    }

    static (int, string, byte[], Dictionary<string, string>?)? HandleOfflineMatchEnd(string method, string path, byte[] body)
    {
        bool matchRoute = (method == "POST" || method == "PUT") && Regex.IsMatch(path ?? "", @"/game/fifa16/match(/\d+)?(/(ready|end))?$");
        if (!matchRoute) return HandleOfflineMatchEndCore(method, path, body);
        try { return HandleOfflineMatchEndCore(method, path, body); }
        catch (Exception ex)
        {
            Console.WriteLine("[MATCH-END] ERROR " + ex.GetType().Name + ": " + ex.Message + "  -> generic reply so the game can leave the match");
            try { Club["offlineMatchPending"] = false; Save(); } catch { }
            if (Regex.IsMatch(path ?? "", @"/(ready)$")) return (200, "application/json", Encoding.UTF8.GetBytes(new JsonObject { ["startDateTime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }.ToJsonString()), null);
            return (200, "application/json", Encoding.UTF8.GetBytes(MEGenericReply(null, "LOSS").ToJsonString()), null);
        }
    }

    static JsonObject MEGenericReply(JsonObject doc, string reason)
    {
        long coins = 0; try { coins = L(Club["coins"]); } catch { }
        var o = new JsonObject
        {
            ["endReason"] = reason, ["secondsPlayed"] = (reason == "WIN" || reason == "DRAW" || reason == "LOSS") ? 5400 : 1, ["matchDifficulty"] = 3,
            ["items"] = new JsonArray(), ["matchData"] = doc?["matchData"]?.ToString() ?? "",
            ["credits"] = coins, ["coins"] = coins, ["allCoins"] = 0, ["matchCoins"] = 0, ["participationAward"] = 0,
            ["gameModeAward"] = new JsonObject { ["coins"] = 0 }, ["boostConis"] = 0, ["boostCountLeft"] = 0,
            ["matchCoinMultipliers"] = new JsonArray(), ["matchCoinPartials"] = new JsonArray(),
            ["seasonCoins"] = 0, ["tournamentCoins"] = 0, ["teamOfTournamentWinner"] = false,
            ["completionAward"] = 0, ["skillAward"] = 0, ["rewardCoins"] = 0, ["totalCoins"] = 0, ["dnfModifier"] = 1.0,
        };
        if (doc?["myMatchStats"] is JsonObject m) o["myMatchStats"] = m.DeepClone();
        if (doc?["opponentMatchStats"] is JsonObject op) o["opponentMatchStats"] = op.DeepClone();
        return o;
    }

    static (int, string, byte[], Dictionary<string, string>?)? HandleOfflineMatchEndCore(string method, string path, byte[] body)
    {
        (int, string, byte[], Dictionary<string, string>?) J(JsonNode n) => (200, "application/json", Encoding.UTF8.GetBytes(n.ToJsonString()), null);

        var qm = Regex.Match(path, @"/game/fifa16/season/(\d+)/division/(\d+)$");
        if (qm.Success && (method == "DELETE" || method == "PUT" || method == "POST") && int.TryParse(qm.Groups[1].Value, out var qs) && qs >= 1 && qs <= 10)
        {
            Console.WriteLine($"[MATCH-END] division terminal ack season={qs} division={qm.Groups[2].Value} (season kept)");
            return J(new JsonObject { ["success"] = true });
        }

        if (method != "POST" && method != "PUT") return null;
        bool isEnd = Regex.IsMatch(path, @"/game/fifa16/match(/\d+)?/end$");
        bool isReady = Regex.IsMatch(path, @"/game/fifa16/match(/\d+)?/ready$") || (method == "PUT" && path.EndsWith("/game/fifa16/match"));
        if (!isEnd && !isReady) return null;

        JsonObject doc = null; try { doc = JsonNode.Parse(Encoding.UTF8.GetString(body)) as JsonObject; } catch { }
        doc ??= new JsonObject();

        if (isReady)
        { SPFixContracts();
            if (!MEPending()) return null;
            var ids = new JsonArray();
            if (doc["items"] is JsonArray its)
                foreach (var it in its) { long id = 0; try { id = L(it?["id"] ?? it?["itemId"]); } catch { } if (id > 0 && ids.Count < 11) ids.Add(id); }
            Club["offlineMatchStarters"] = ids;
            Console.WriteLine($"[MATCH-END] MatchReady, {ids.Count} starters");
            try
            {
                EnsureSquads();
                var sqSrc = SquadById(ActiveSquadId());
                if (sqSrc != null)
                {
                    var sq = (JsonObject)sqSrc.DeepClone(); SquadCleanSlots(sq);
                    return J(new JsonObject { ["squad"] = NativeSquadReply(sq), ["startDateTime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
                }
            }
            catch (Exception ex) { Console.WriteLine("[MATCH-END] ready reply failed: " + ex.Message); }
            return J(new JsonObject { ["startDateTime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        }

        string endReason = (doc["endReason"]?.ToString() ?? doc["result"]?.ToString() ?? "NO_CONTEST").Trim().ToUpperInvariant();
        bool terminal = endReason == "WIN" || endReason == "DRAW" || endReason == "LOSS" || endReason == "QUIT" || endReason == "DNF" || endReason == "FORFEIT";
        bool pending = MEPending();
        if (!pending)
        {
            long at = 0; try { at = L(Club["lastMatchEndAt"]); } catch { }
            if (terminal && Club["lastMatchEnd"] is JsonObject cached && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - at < 120)
            { Console.WriteLine("[MATCH-END] retry -> cached answer"); return J(cached.DeepClone()); }
            Console.WriteLine($"[MATCH-END] {endReason}: not a single player season match (friendly / tournament / draft / online) -> generic end reply, nothing counted");
            return J(MEGenericReply(doc, (endReason == "QUIT" || endReason == "DNF" || endReason == "FORFEIT") ? "LOSS" : (terminal ? endReason : "NO_CONTEST")));
        }

        string respReason = (endReason == "QUIT" || endReason == "DNF" || endReason == "FORFEIT") ? "LOSS" : endReason;
        var s = SeasonSPState();
        var mine = doc["myMatchStats"] as JsonObject; var opp = doc["opponentMatchStats"] as JsonObject;
        int gf = Math.Max(0, mine == null ? 0 : MEI(mine["goals"])), ga = Math.Max(0, opp == null ? 0 : MEI(opp["goals"]));
        bool forfeitEnd = endReason == "QUIT" || endReason == "DNF" || endReason == "FORFEIT";
        if (forfeitEnd) { gf = 0; ga = Math.Max(3, ga); }

        var stat = new JsonArray();
        if (doc["items"] is JsonArray raw)
            foreach (var it in raw)
            {
                if (it is not JsonObject io) continue;
                long id = 0; try { id = L(io["id"] ?? io["itemId"]); } catch { }
                if (id <= 0) continue;
                var row = new JsonObject { ["id"] = id };
                foreach (var k in new[] { "shots", "goals", "yellowCards", "redCards", "suspension", "injuryType", "injuryGames", "fitness", "assists" })
                    if (io[k] != null) row[k] = io[k].DeepClone();
                stat.Add(row);
                var local = FindItem(id);
                if (local != null) foreach (var k in new[] { "fitness", "suspension", "injuryType", "injuryGames" }) if (io[k] != null) local[k] = io[k].DeepClone();
            }

        bool completed = endReason == "WIN" || endReason == "DRAW" || endReason == "LOSS";
        var rw = MEReward(mine, opp, completed);
        int coins = 0;
        string note = "";
        if (terminal)
        {
            bool won = endReason == "WIN", draw = endReason == "DRAW";
            if (forfeitEnd) { won = false; draw = false; }
            bool isTour = false; try { isTour = Club["offlineMatchTournament"]?.GetValue<bool>() ?? false; } catch { }
            if (isTour) note = " | tournament match: not counted in the season";
            else note = SPSettle(s, won, draw, gf, ga);
            if (completed) { coins = rw.Total; Club["coins"] = (int)L(Club["coins"]) + coins; }
            try
            {
                if (Club["offlineMatchStarters"] is JsonArray st)
                    foreach (var idn in st) { var it = FindItem(L(idn)); if (it == null) continue; /* single player season: contracts are never used up */ }
            }
            catch { }
            Club["offlineMatchStarters"] = new JsonArray();
            Console.WriteLine($"[MATCH-END] {endReason} {gf}-{ga}  +{coins} coins  season {SPI(s, "played")}/{SeasonSPGames}, {SPI(s, "points")} pts, W{SPI(s, "wins")} D{SPI(s, "draws")} L{SPI(s, "losses")}{note}");
        }
        Club["offlineMatchPending"] = false;

        int total = completed ? coins : 0, part = completed ? rw.Completion : 0;
        var resp = new JsonObject
        {
            ["endReason"] = respReason, ["secondsPlayed"] = completed ? 5400 : 1, ["matchDifficulty"] = Math.Max(1, Math.Min(5, 1 + (10 - SPI(s, "division")) / 2)),
            ["items"] = stat, ["matchData"] = doc["matchData"]?.ToString() ?? "",
            ["credits"] = Club["coins"].DeepClone(), ["coins"] = Club["coins"].DeepClone(),
            ["allCoins"] = total, ["matchCoins"] = Math.Max(0, total - part), ["participationAward"] = part,
            ["gameModeAward"] = new JsonObject { ["coins"] = 0 }, ["boostConis"] = 0, ["boostCountLeft"] = 0,
            ["matchCoinMultipliers"] = new JsonArray(), ["matchCoinPartials"] = new JsonArray(),
            ["seasonCoins"] = 0, ["tournamentCoins"] = 0, ["teamOfTournamentWinner"] = false,
            ["completionAward"] = part, ["skillAward"] = Math.Max(0, total - part), ["rewardCoins"] = total, ["totalCoins"] = total, ["dnfModifier"] = 1.0,
            ["seasonId"] = SPI(s, "division"), ["divisionId"] = SPI(s, "division"), ["seasonRound"] = Math.Max(1, Math.Min(10, SPI(s, "played") + 1)),
            ["seasonPoints"] = SPI(s, "points"), ["seasonGamesWon"] = SPI(s, "wins"), ["seasonGamesDraw"] = SPI(s, "draws"), ["seasonGamesLost"] = SPI(s, "losses"),
        };
        if (mine != null) resp["myMatchStats"] = mine.DeepClone();
        if (opp != null) resp["opponentMatchStats"] = opp.DeepClone();
        Club["lastMatchEnd"] = resp.DeepClone(); Club["lastMatchEndAt"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Save();
        return J(resp);
    }
}
