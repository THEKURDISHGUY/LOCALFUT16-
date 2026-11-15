#nullable disable
#pragma warning disable
using System.Text.Json.Nodes;

static partial class UtBackend
{
    static void TournamentFifa15Fields(JsonObject t, Mode m)
    {
        int difficulty = m.Wins <= 3 ? 2 : m.Wins <= 4 ? 3 : m.Wins <= 5 ? 4 : 5;
        int champion = 2000 + m.Wins * 2500;
        bool champs = m.Id == ChampsId;
        if (champs) { difficulty = SPDifficulty(); champion = 100000; }
        t["type"] = "OFFLINE"; t["numMatches"] = m.Wins; t["matchLengthMin"] = 6;
        t["matches"] = SPMatches(difficulty, m.Wins, champs ? m.Id + 17 * (int)L(ChampsState()["runs"]) : m.Id, champs);
        t["prizeSet"] = new JsonArray(SPPrize("RELEGATION", 0, 0), SPPrize("MAINTENANCE", 1, champion / 10), SPPrize("PROMOTION", m.Wins - 1, champion / 2), SPPrize("CHAMPIONSHIP", m.Wins, champion));
        t["elgOperation"] = "AND"; t["elgReq"] = new JsonArray();
        t["trophyResourceId"] = SPTrophyResourceId; t["trophyUseCount"] = 0;
        t["visStartDays"] = 3650; t["visEndDays"] = 3650; t["startDateTime"] = 0; t["endDateTime"] = SeasonSPForever;
        t["untilStartSeconds"] = 0; t["untilEndSeconds"] = 315360000;
    }
}
