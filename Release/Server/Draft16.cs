#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// FUT REBORN - FUT Draft for FIFA 16 (single player: the server runs the draft, you play the matches).
// REPLACES Draft16.cs (UtBackend.cs already calls HandleDraft(...)). Back up the old one first.
//
// What your fifa16.exe string dump told us (real names inside the game):
//   GET  .../draft/state?mode=SINGLE_PLAYER | ONLINE     -> current state       (GetDraftCurrentState)
//   GET  .../{id}/draft                                  -> choices for the current step (GetDraftChoices)
//        .../choices/difficulty | formation | captain | player | manager
//   POST .../draft/choose/difficulty  and  .../{id}/draft/choose   -> pick one choice     (PickDraftChoice, "choiceIndex")
//   POST .../{id}/draft/autocomplete                     -> fill the rest of the squad    (PickDraftAutoChoice)
//   POST .../purchase/mode/{id}/draft                    -> pay the entry (tokens/coins)  (PurchaseDraftMode)
//   GET  .../{id}/stats    GET/POST .../grant/award      -> stats / prize                 (GetDraftStats / GetDraftAward)
//   draft steps: PICK_DIFFICULTY -> FORMATION_DRAFT -> CAPTAIN_DRAFT -> PLAYER_DRAFT (x11) -> (MANAGER_DRAFT) -> matches -> COMPLETED_DRAFT
//   JSON keys the game knows: draftState, choices, choiceIndex, draftsCompleted, draftChampion, draftSummary, awards, prizeLevel,
//                             gamesPlayed, gamesWon, gamesLost, difficultyName
//
// The exact reply SHAPES are still not confirmed, so draftmode.txt (next to the exe) still picks the state shape:
//   404 = plain error popup (safe) | 4 = {} | 6 / 7 / 8 = tiny shapes | 2 3 5 1 = old guesses (1 crashed the game)
//   9   = NEW: built from the real key names above   <-- try this first
//   auto = cycles 9,4,6,7,8,2,3,5,1 on every state request
// draftreset.txt (create an empty file next to the exe) = reset the draft to NOT_STARTED on the next request.
// Every request is printed as [DRAFT-REQ] and goes to capture_ut.log. Send me those lines after the state reply works.
static partial class UtBackend
{
    static int _draftCycle = 0;
    const int DraftMatches = 4;          // matches per draft run
    const int DraftEntryCoins = 15000;
    const bool DraftAskManager = false;  // MANAGER_DRAFT step: reply format unknown, skipped by default

    static readonly string[] DraftDifficulties = { "BEGINNER", "AMATEUR", "SEMIPRO", "PROFESSIONAL", "WORLDCLASS", "LEGENDARY", "ULTIMATE" };
    static readonly (string Name, string[] Pos)[] DraftFormations =
    {
        ("4-4-2",     new[] { "GK", "RB", "CB", "CB", "LB", "RM", "CM", "CM", "LM", "ST", "ST" }),
        ("4-3-3",     new[] { "GK", "RB", "CB", "CB", "LB", "CM", "CM", "CM", "RW", "ST", "LW" }),
        ("4-2-3-1",   new[] { "GK", "RB", "CB", "CB", "LB", "CDM", "CDM", "CAM", "RM", "LM", "ST" }),
        ("3-5-2",     new[] { "GK", "CB", "CB", "CB", "RM", "CM", "CDM", "CM", "LM", "ST", "ST" }),
        ("4-1-2-1-2", new[] { "GK", "RB", "CB", "CB", "LB", "CDM", "CM", "CM", "CAM", "ST", "ST" }),
    };

    static int DI(JsonObject o, string k) { try { return (int)L(o[k]); } catch { return 0; } }
    static string DS(JsonObject d) => d["state"]?.ToString() ?? "NOT_STARTED";
    // NOT_STARTED / IN_PROGRESS are OUR internal names. The game's own draftState strings are only:
    // PICK_DIFFICULTY FORMATION_DRAFT CAPTAIN_DRAFT PLAYER_DRAFT MANAGER_DRAFT COMPLETED_DRAFT  (unknown strings crash it)
    // -> no draft running = draftState left OUT, squad finished / playing the matches = COMPLETED_DRAFT
    static string DraftOutState(JsonObject d)
    {
        string st = DS(d);
        if (st == "NOT_STARTED") return null;
        if (st == "IN_PROGRESS") return "COMPLETED_DRAFT";
        return st;
    }

    static JsonObject DraftState()
    {
        if (Club["draft"] is not JsonObject d) { d = new JsonObject(); Club["draft"] = d; }
        foreach (var (k, v) in new (string, int)[] { ("wins", 0), ("losses", 0), ("played", 0), ("runs", 0), ("bestWins", 0), ("difficulty", 0), ("formation", 0), ("slot", 0), ("rewardPaid", 0), ("paid", 1) })
            if (d[k] == null) d[k] = v;
        if (d["state"] == null) d["state"] = "NOT_STARTED";
        if (d["squad"] is not JsonArray) DraftClearSquad(d);
        if (d["options"] is not JsonArray) d["options"] = new JsonArray();
        return d;
    }

    static void DraftClearSquad(JsonObject d)
    {
        var sq = new JsonArray();
        for (int i = 0; i < 11; i++) sq.Add(null);
        d["squad"] = sq;
        d["options"] = new JsonArray();
        d["slot"] = 0;
    }

    static void DraftReset(JsonObject d)
    {
        d["state"] = "NOT_STARTED"; d["wins"] = 0; d["losses"] = 0; d["played"] = 0; d["rewardPaid"] = 0;
        DraftClearSquad(d);
    }

    static JsonArray DraftTokens()
    {
        var tokens = new JsonArray();
        foreach (var m in Modes.Where(x => x.Kind == "draft"))
            tokens.Add(new JsonObject { ["id"] = m.Id, ["name"] = m.Name, ["coins"] = m.Coins, ["points"] = m.Points, ["winsNeeded"] = m.Wins });
        return tokens;
    }

    static string PosFam(string pos)
    {
        pos = (pos ?? "").ToUpperInvariant();
        if (pos == "GK") return "GK";
        if (pos is "CB" or "LB" or "RB" or "LWB" or "RWB") return "DEF";
        if (pos is "CDM" or "CM" or "CAM" or "LM" or "RM") return "MID";
        return "ATT";
    }

    static HashSet<long> DraftPickedAssets(JsonObject d)
    {
        var s = new HashSet<long>();
        foreach (var n in (JsonArray)d["squad"]) if (n != null) s.Add(L(n["assetId"]));
        return s;
    }

    // 5 random real cards for one pick (rating >= minRating, no specials / placeholders, no duplicates of already picked players)
    static JsonArray DraftOptions(string pos, HashSet<long> used, int minRating)
    {
        var real = Db.Where(p => p.AssetId < 900000 && !p.Special && p.Rare == 0 && p.Rating >= minRating && !used.Contains(p.AssetId)).ToList();
        var pool = real;
        if (!string.IsNullOrEmpty(pos))
        {
            var exact = real.Where(p => p.Pos.Equals(pos, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count >= 5) pool = exact;
            else
            {
                var fam = real.Where(p => PosFam(p.Pos) == PosFam(pos)).ToList();
                if (fam.Count >= 5) pool = fam;
            }
        }
        var arr = new JsonArray();
        // never return an empty choice list (the game would wait forever): relax the filters step by step
        if (pool.Count == 0) pool = Db.Where(p => p.AssetId < 900000 && !p.Special && !used.Contains(p.AssetId) && (string.IsNullOrEmpty(pos) || PosFam(p.Pos) == PosFam(pos))).OrderByDescending(p => p.Rating).Take(200).ToList();
        if (pool.Count == 0) pool = Db.Where(p => !used.Contains(p.AssetId)).OrderByDescending(p => p.Rating).Take(200).ToList();
        if (pool.Count == 0) pool = Db.ToList();
        if (pool.Count == 0) return arr;
        var picked = new HashSet<long>();
        for (int i = 0; i < 5 && picked.Count < pool.Count; i++)
        {
            P p; int guard = 0;
            do { p = pool[Rng.Next(pool.Count)]; } while (!picked.Add(p.AssetId) && ++guard < 80);
            var it = Item(p, Q(p.Rating));
            it["choiceIndex"] = i;
            arr.Add(it);
        }
        return arr;
    }

    // build the choices for the CURRENT step and remember them (so a repeated GET returns the same cards)
    static void DraftPrepare(JsonObject d)
    {
        var opts = new JsonArray();
        var used = DraftPickedAssets(d);
        switch (DS(d))
        {
            case "PICK_DIFFICULTY":
                for (int i = 0; i < DraftDifficulties.Length; i++)
                    opts.Add(new JsonObject { ["choiceIndex"] = i, ["index"] = i, ["id"] = i, ["difficultyName"] = DraftDifficulties[i], ["name"] = DraftDifficulties[i] });
                break;
            case "FORMATION_DRAFT":
                for (int i = 0; i < DraftFormations.Length; i++)
                    opts.Add(new JsonObject { ["choiceIndex"] = i, ["index"] = i, ["id"] = i, ["name"] = DraftFormations[i].Name, ["formation"] = DraftFormations[i].Name });
                break;
            case "CAPTAIN_DRAFT":
                opts = DraftOptions("", used, 80);
                break;
            case "PLAYER_DRAFT":
                {
                    int slot = DI(d, "slot");
                    var f = DraftFormations[Math.Clamp(DI(d, "formation"), 0, DraftFormations.Length - 1)];
                    opts = DraftOptions(f.Pos[Math.Clamp(slot, 0, 10)], used, 70);
                    break;
                }
        }
        d["options"] = opts;
    }

    static int DraftNextEmptySlot(JsonObject d)
    {
        var sq = (JsonArray)d["squad"];
        for (int i = 0; i < sq.Count; i++) if (sq[i] == null) return i;
        return -1;
    }

    static void DraftAfterSquadStep(JsonObject d)
    {
        int next = DraftNextEmptySlot(d);
        if (next >= 0) { d["state"] = "PLAYER_DRAFT"; d["slot"] = next; DraftPrepare(d); return; }
        d["state"] = DraftAskManager ? "MANAGER_DRAFT" : "IN_PROGRESS";
        d["options"] = new JsonArray();
        Console.WriteLine($"[DRAFT] squad complete -> {d["state"]}");
    }

    // apply the player's choice for the current step
    static bool DraftChoose(JsonObject d, int idx)
    {
        var opts = (JsonArray)d["options"];
        string st = DS(d);
        if (opts.Count == 0) DraftPrepare(d);
        opts = (JsonArray)d["options"];
        if (idx < 0 || idx >= opts.Count) { Console.WriteLine($"[DRAFT] choice {idx} out of range (0..{opts.Count - 1}) in {st}"); return false; }

        switch (st)
        {
            case "PICK_DIFFICULTY":
                d["difficulty"] = idx; d["state"] = "FORMATION_DRAFT"; DraftPrepare(d);
                Console.WriteLine($"[DRAFT] difficulty = {DraftDifficulties[idx]}");
                return true;
            case "FORMATION_DRAFT":
                d["formation"] = idx; d["state"] = "CAPTAIN_DRAFT"; DraftClearSquadKeepState(d); DraftPrepare(d);
                Console.WriteLine($"[DRAFT] formation = {DraftFormations[idx].Name}");
                return true;
            case "CAPTAIN_DRAFT":
                {
                    var card = opts[idx].DeepClone() as JsonObject;
                    var f = DraftFormations[Math.Clamp(DI(d, "formation"), 0, DraftFormations.Length - 1)];
                    string fam = PosFam(card["preferredPosition"]?.ToString());
                    int slot = -1;
                    for (int i = 1; i < 11 && slot < 0; i++) if (f.Pos[i] == (card["preferredPosition"]?.ToString() ?? "")) slot = i;
                    for (int i = 1; i < 11 && slot < 0; i++) if (PosFam(f.Pos[i]) == fam) slot = i;
                    if (slot < 0) slot = 10;
                    card["captain"] = true;
                    ((JsonArray)d["squad"])[slot] = card;
                    d["captainSlot"] = slot;
                    Console.WriteLine($"[DRAFT] captain = {card["name"]} -> slot {slot} ({f.Pos[slot]})");
                    DraftAfterSquadStep(d);
                    return true;
                }
            case "PLAYER_DRAFT":
                {
                    int slot = DI(d, "slot");
                    var card = opts[idx].DeepClone() as JsonObject;
                    ((JsonArray)d["squad"])[slot] = card;
                    Console.WriteLine($"[DRAFT] slot {slot}: {card["name"]} ({card["rating"]})");
                    DraftAfterSquadStep(d);
                    return true;
                }
            case "MANAGER_DRAFT":
                d["state"] = "IN_PROGRESS"; d["options"] = new JsonArray();
                return true;
        }
        return false;
    }

    // keep difficulty / formation while the squad restarts
    static void DraftClearSquadKeepState(JsonObject d)
    {
        int diff = DI(d, "difficulty"), form = DI(d, "formation");
        string st = DS(d);
        DraftClearSquad(d);
        d["difficulty"] = diff; d["formation"] = form; d["state"] = st;
    }

    static void DraftAutoComplete(JsonObject d)
    {
        var f = DraftFormations[Math.Clamp(DI(d, "formation"), 0, DraftFormations.Length - 1)];
        var sq = (JsonArray)d["squad"];
        var used = DraftPickedAssets(d);
        for (int i = 0; i < 11; i++)
        {
            if (sq[i] != null) continue;
            var o = DraftOptions(f.Pos[i], used, 70);
            if (o.Count == 0) continue;
            var card = o[0].DeepClone() as JsonObject;
            sq[i] = card; used.Add(L(card["assetId"]));
        }
        d["state"] = DraftAskManager ? "MANAGER_DRAFT" : "IN_PROGRESS";
        d["options"] = new JsonArray();
        Console.WriteLine($"[DRAFT] autocomplete -> {d["state"]}");
    }

    static JsonArray DraftSquadOut(JsonObject d)
    {
        var arr = new JsonArray();
        foreach (var n in (JsonArray)d["squad"]) if (n != null) arr.Add(n.DeepClone());
        return arr;
    }

    static JsonObject DraftBody(int variant)
    {
        var d = DraftState();
        string st = DS(d);
        var tokens = DraftTokens();
        switch (variant)
        {
            case 9:
                {
                    var o = new JsonObject();
                    string os = DraftOutState(d);
                    if (os != null) o["draftState"] = os;
                    o["draftsCompleted"] = d["runs"]!.DeepClone();
                    o["draftChampion"] = DI(d, "bestWins") >= DraftMatches;
                    o["draftSummary"] = new JsonObject { ["gamesPlayed"] = d["played"]!.DeepClone(), ["gamesWon"] = d["wins"]!.DeepClone(), ["gamesLost"] = d["losses"]!.DeepClone() };
                    if (os != null) o["difficultyName"] = DraftDifficulties[Math.Clamp(DI(d, "difficulty"), 0, DraftDifficulties.Length - 1)];
                    return o;
                }
            case 10: return new JsonObject { ["draftState"] = "PICK_DIFFICULTY" };
            case 11: return new JsonObject { ["draftState"] = "PICK_DIFFICULTY", ["draftsCompleted"] = 0, ["draftChampion"] = false };
            case 12: return new JsonObject { ["draftsCompleted"] = 0, ["draftChampion"] = false };
            case 13: return new JsonObject { ["draftSummary"] = new JsonObject { ["gamesPlayed"] = 0, ["gamesWon"] = 0, ["gamesLost"] = 0 } };
            case 14: return new JsonObject { ["draftState"] = "PICK_DIFFICULTY", ["draftsCompleted"] = 0, ["draftChampion"] = false,
                ["draftSummary"] = new JsonObject { ["gamesPlayed"] = 0, ["gamesWon"] = 0, ["gamesLost"] = 0 } };
            case 2:
                return new JsonObject { ["draftState"] = st == "NOT_STARTED" ? "NONE" : st, ["tokens"] = 1, ["entryFee"] = DraftEntryCoins, ["currentWins"] = d["wins"]!.DeepClone() };
            case 3:
                return new JsonObject { ["state"] = st == "NOT_STARTED" ? "UNSTARTED" : st, ["status"] = st == "NOT_STARTED" ? "UNSTARTED" : st, ["active"] = st != "NOT_STARTED",
                    ["wins"] = d["wins"]!.DeepClone(), ["losses"] = d["losses"]!.DeepClone(), ["draftTokens"] = 1 };
            case 4: return new JsonObject();
            case 6: return new JsonObject { ["state"] = "NONE" };
            case 7: return new JsonObject { ["draftState"] = "NONE" };
            case 8: return new JsonObject { ["state"] = st };
            case 5:
                return new JsonObject
                {
                    ["draft"] = new JsonObject { ["state"] = st, ["wins"] = d["wins"]!.DeepClone(), ["losses"] = d["losses"]!.DeepClone(), ["gamesPlayed"] = d["played"]!.DeepClone(), ["totalGames"] = DraftMatches },
                    ["draftTokens"] = tokens,
                };
            default:
                return new JsonObject
                {
                    ["state"] = st, ["mode"] = "SINGLE_PLAYER", ["brand"] = Brand,
                    ["wins"] = d["wins"]!.DeepClone(), ["losses"] = d["losses"]!.DeepClone(), ["gamesPlayed"] = d["played"]!.DeepClone(), ["totalGames"] = DraftMatches,
                    ["draftTokens"] = tokens, ["tokens"] = 1, ["entryFee"] = DraftEntryCoins,
                };
        }
    }

    static JsonObject DraftChoicesBody(JsonObject d)
    {
        if (((JsonArray)d["options"]).Count == 0) DraftPrepare(d);
        var o = (JsonArray)d["options"];
        return new JsonObject
        {
            ["draftState"] = DraftOutState(d) ?? "PICK_DIFFICULTY", ["choiceIndex"] = 0,
            ["choices"] = o.DeepClone(), ["count"] = o.Count,
            ["credits"] = Club["coins"]!.DeepClone(),
        };
    }

    static int DraftReadIndex(string target, JsonNode req)
    {
        foreach (var k in new[] { "choiceIndex", "index", "choice", "id" })
            try { var n = req?[k]; if (n != null) return (int)L(n); } catch { }
        var m = Regex.Match(target ?? "", @"(?:choiceIndex|index|choice)=(\d+)", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    static bool DraftPay(JsonObject d, out (int, string, byte[], Dictionary<string, string>?) err)
    {
        err = default;
        int coins = (int)L(Club["coins"]);
        if (coins < DraftEntryCoins)
        {
            Console.WriteLine($"[DRAFT] not enough coins ({coins} < {DraftEntryCoins})");
            err = (460, "application/json", Encoding.UTF8.GetBytes("{\"code\":460,\"reason\":\"not enough credits\"}"), null);
            return false;
        }
        Club["coins"] = coins - DraftEntryCoins;
        DraftReset(d);
        d["paid"] = 1;
        d["state"] = "PICK_DIFFICULTY";
        DraftPrepare(d);
        Console.WriteLine($"[DRAFT] entered: -{DraftEntryCoins} coins, now {Club["coins"]}");
        return true;
    }

    static (int, string, byte[], Dictionary<string, string>?)? HandleDraft(string method, string path, string target, byte[] body)
    {
        string lpath = (path ?? "").ToLowerInvariant();
        // any URL with "draft" in it is ours (an unmatched draft URL used to fall into a catch-all that returned the wrong shape -> freeze)
        bool mine = lpath.Contains("draft") && !lpath.Contains("tournament");
        if (!mine) return null;
        (int, string, byte[], Dictionary<string, string>?) J(JsonNode n) => (200, "application/json", Encoding.UTF8.GetBytes(n.ToJsonString()), null);

        string bodyTxt = Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 600));
        string mode = "10";   // default: {"draftState":"PICK_DIFFICULTY"} (variant 9 left draftState out when NOT_STARTED)
        try { var f = FindFile("draftmode.txt"); if (File.Exists(f)) mode = File.ReadAllText(f).Trim(); } catch { }
        Console.WriteLine($"[DRAFT-REQ] {method} {target} body: {bodyTxt}   (draftmode={mode})");

        if (mode == "404")
        {
            Console.WriteLine("[DRAFT] answer (draftmode=404): HTTP 404");
            return (404, "application/json", Encoding.UTF8.GetBytes("{\"code\":404,\"reason\":\"draft not available\"}"), null);
        }

        var d = DraftState();
        try { var rf = FindFile("draftreset.txt"); if (File.Exists(rf)) { File.Delete(rf); DraftReset(d); Save(); Console.WriteLine("[DRAFT] reset by draftreset.txt"); } } catch { }

        var req = default(JsonNode); try { req = JsonNode.Parse(body.Length == 0 ? "{}" : Encoding.UTF8.GetString(body)); } catch { }

        // ---- state / menu (GET .../draft/state?mode=SINGLE_PLAYER)
        if (method == "GET" && lpath.EndsWith("/state"))
        {
            int v;
            int[] order = { 10, 11, 12, 13, 14, 9, 4, 6, 7, 8, 2, 3, 5, 1 };
            if (mode == "auto") v = order[(_draftCycle++) % order.Length]; else int.TryParse(mode, out v);
            if (v < 1 || v > 14) v = 4;
            Console.WriteLine($"[DRAFT] sending state variant {v} (state={DS(d)})");
            return J(DraftBody(v));
        }

        // ---- purchase / enter: POST .../purchase/mode/{id}/draft
        if (lpath.Contains("purchase/mode"))
        {
            if (DS(d) != "NOT_STARTED" && DS(d) != "COMPLETED_DRAFT") { Console.WriteLine($"[DRAFT] purchase ignored, draft already {DS(d)}"); return J(DraftBody(9)); }
            if (!DraftPay(d, out var err)) return err;
            Save();
            var a = DraftBody(9); return J(a);
        }

        // ---- prize: .../grant/award
        if (lpath.Contains("grant/award") || lpath.EndsWith("/award"))
        {
            int w = DI(d, "wins");
            // the squad is sent as COMPLETED_DRAFT while the matches are still to be played, so the game may ask for the award early.
            // Only pay + reset when the matches are really finished; otherwise keep the run alive.
            if (DS(d) != "COMPLETED_DRAFT")
            {
                Console.WriteLine($"[DRAFT] award requested but run is {DS(d)} -> ignored (not reset)");
                return J(new JsonObject { ["awards"] = new JsonArray(), ["credits"] = Club["coins"]!.DeepClone(), ["draftState"] = "COMPLETED_DRAFT" });
            }
            // coins were already paid when the last match was recorded (rewardPaid = 1)
            var res = new JsonObject { ["awards"] = new JsonArray(), ["credits"] = Club["coins"]!.DeepClone(), ["draftState"] = "COMPLETED_DRAFT" };
            DraftReset(d); Save();
            return J(res);
        }

        // ---- stats: GET .../{id}/stats
        if (method == "GET" && lpath.EndsWith("/stats"))
            return J(new JsonObject { ["draftsCompleted"] = d["runs"]!.DeepClone(), ["bestWins"] = d["bestWins"]!.DeepClone(), ["draftChampion"] = DI(d, "bestWins") >= DraftMatches,
                ["draftSummary"] = new JsonObject { ["gamesPlayed"] = d["played"]!.DeepClone(), ["gamesWon"] = d["wins"]!.DeepClone(), ["gamesLost"] = d["losses"]!.DeepClone() } });

        // ---- autocomplete: POST .../{id}/draft/autocomplete
        if (lpath.Contains("autocomplete"))
        {
            if (DS(d) == "PLAYER_DRAFT" || DS(d) == "CAPTAIN_DRAFT") DraftAutoComplete(d);
            Save();
            var a = DraftChoicesBody(d); a["squad"] = DraftSquadOut(d); return J(a);
        }

        // ---- choose: POST .../draft/choose/difficulty  |  .../{id}/draft/choose
        if (method != "GET" && lpath.Contains("/choose"))
        {
            if (DS(d) == "NOT_STARTED") { if (!DraftPay(d, out var err)) return err; }   // game skipped the purchase call -> pay now
            else if (DI(d, "paid") == 0)                                                   // entered by looking at the choices -> pay on the first pick
            {
                int have = (int)L(Club["coins"]);
                if (have < DraftEntryCoins) return (460, "application/json", Encoding.UTF8.GetBytes("{\"code\":460,\"reason\":\"not enough credits\"}"), null);
                Club["coins"] = have - DraftEntryCoins; d["paid"] = 1;
                Console.WriteLine($"[DRAFT] entry fee taken on first pick: -{DraftEntryCoins}");
            }
            int idx = DraftReadIndex(target, req);
            DraftChoose(d, idx);
            Save();
            var a = DraftChoicesBody(d); a["squad"] = DraftSquadOut(d); return J(a);
        }

        // ---- choices: GET .../{id}/draft  |  .../choices/difficulty|formation|captain|player|manager
        if (method == "GET" && (lpath.Contains("/choices") || Regex.IsMatch(lpath, @"/\d+/draft$")))
        {
            // just LOOKING at the choices does not cost coins any more: the fee is taken on the first pick (see /choose)
            if (DS(d) == "NOT_STARTED") { DraftReset(d); d["state"] = "PICK_DIFFICULTY"; d["paid"] = 0; DraftPrepare(d); Save(); }
            var a = DraftChoicesBody(d); a["squad"] = DraftSquadOut(d);
            Console.WriteLine($"[DRAFT] sending {((JsonArray)a["choices"]).Count} choices for {DS(d)}");
            return J(a);
        }

        // ---- other GETs
        if (method == "GET") return J(DraftBody(9));

        // ---- everything else (match result etc.): generic handling
        string joined = (lpath + " " + (req?.ToJsonString() ?? "")).ToLowerInvariant();
        bool isResult = joined.Contains("result") || joined.Contains("\"win\"") || joined.Contains("/win") || joined.Contains("loss")
                        || joined.Contains("goal") || joined.Contains("match") || joined.Contains("score");

        if (isResult && DS(d) == "IN_PROGRESS")
        {
            int gf = 0, ga = 0;
            try { gf = (int)L(req?["goalsFor"] ?? req?["homeGoals"] ?? req?["goals"] ?? req?["scoreFor"]); } catch { }
            try { ga = (int)L(req?["goalsAgainst"] ?? req?["awayGoals"] ?? req?["scoreAgainst"]); } catch { }
            bool? wonFlag = null; try { wonFlag = req?["won"]?.GetValue<bool>(); } catch { }
            bool won = wonFlag ?? (joined.Contains("\"win\"") || joined.Contains("/win") || gf > ga);
            if (won) d["wins"] = DI(d, "wins") + 1; else d["losses"] = DI(d, "losses") + 1;
            d["played"] = DI(d, "played") + 1;
            Console.WriteLine($"[DRAFT] result recorded: {(won ? "WIN" : "LOSS")}  ({DI(d, "wins")}W {DI(d, "losses")}L, {DI(d, "played")}/{DraftMatches})");

            if (DI(d, "played") >= DraftMatches)
            {
                int w = DI(d, "wins");
                int reward = 2000 + w * 4000;
                Club["coins"] = (int)L(Club["coins"]) + reward;
                d["rewardPaid"] = 1;
                d["runs"] = DI(d, "runs") + 1;
                if (w > DI(d, "bestWins")) d["bestWins"] = w;
                Club["draftWins"] = d["bestWins"]!.DeepClone();
                Console.WriteLine($"[DRAFT] run finished with {w} wins, +{reward} coins -> COMPLETED_DRAFT");
                d["state"] = "COMPLETED_DRAFT";
            }
        }

        Save();
        var ans = DraftBody(9);
        ans["credits"] = Club["coins"]!.DeepClone();
        return J(ans);
    }
}
