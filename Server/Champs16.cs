#nullable disable
#pragma warning disable
using System.Text.Json.Nodes;

// SINGLE PLAYER FUT REBORN CHAMPIONS
// A Champions-style run on the Tournament screen: elite opponents at the hardest difficulty, a rank ladder with points,
// rank-up coin rewards and a big prize set. Rank + points are shown in the tournament description text.
// State is stored in the club save under "champs" (points, wins, losses, runs, titles, bestRank).
// NOTE: the tournament request/response format is still the experimental one of this project (see README); the rank
// system only moves when the game reports a tournament win/loss through the existing tournament handler.
static partial class UtBackend
{
    public const int ChampsId = 2008;

    static readonly (string Name, int Points)[] ChampsRanks =
    {
        ("Bronze", 0), ("Silver", 150), ("Gold", 400), ("Platinum", 800), ("Diamond", 1400), ("Elite", 2200), ("Champion", 3500),
    };

    static JsonObject ChampsState()
    {
        if (Club["champs"] is not JsonObject c) { c = new JsonObject(); Club["champs"] = c; }
        foreach (var k in new[] { "points", "wins", "losses", "runs", "titles", "bestRank" }) if (c[k] == null) c[k] = 0;
        return c;
    }

    static int ChampsRankIndex(int points)
    {
        int r = 0;
        for (int i = 0; i < ChampsRanks.Length; i++) if (points >= ChampsRanks[i].Points) r = i;
        return r;
    }

    static string TourDesc(Mode m)
    {
        if (m.Id != ChampsId) return Brand + " single player tournament";
        try
        {
            var c = ChampsState(); int pts = (int)L(c["points"]); int r = ChampsRankIndex(pts);
            string next = r + 1 < ChampsRanks.Length ? $" | next rank {ChampsRanks[r + 1].Name} at {ChampsRanks[r + 1].Points}" : " | top rank reached";
            return $"Rank: {ChampsRanks[r].Name} | {pts} pts{next} | Titles: {(int)L(c["titles"])}";
        }
        catch { return Brand + " champions"; }
    }

    // called by the tournament handler after every reported win / loss of the Champions run
    static void ChampsOnResult(bool won, int winsInRun, int winsNeeded)
    {
        var c = ChampsState();
        int oldPts = (int)L(c["points"]); int oldRank = ChampsRankIndex(oldPts);
        int pts = oldPts;
        if (won)
        {
            pts += 25 + 5 * Math.Max(0, winsInRun - 1);                 // win streak inside a run is worth more
            c["wins"] = (int)L(c["wins"]) + 1;
            if (winsInRun >= winsNeeded) { pts += 100; c["titles"] = (int)L(c["titles"]) + 1; c["runs"] = (int)L(c["runs"]) + 1; Console.WriteLine("[CHAMPS] run complete: CHAMPION (+100 bonus)"); }
        }
        else
        {
            pts = Math.Max(0, pts - 10);
            c["losses"] = (int)L(c["losses"]) + 1; c["runs"] = (int)L(c["runs"]) + 1;
        }
        c["points"] = pts;
        int newRank = ChampsRankIndex(pts);
        if (newRank > (int)L(c["bestRank"]))
        {
            int reward = 5000 * newRank;                                // rank-up reward, paid once per new best rank
            Club["coins"] = (int)L(Club["coins"]) + reward;
            c["bestRank"] = newRank;
            Console.WriteLine($"[CHAMPS] RANK UP -> {ChampsRanks[newRank].Name}: +{reward} coins");
        }
        Console.WriteLine($"[CHAMPS] {(won ? "win" : "loss")}: {oldPts} -> {pts} pts, rank {ChampsRanks[newRank].Name}");
    }
}
