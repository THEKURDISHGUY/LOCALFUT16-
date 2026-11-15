"""Build players16.csv for FUT REBORN from fifa_ng_db tables exported as CSV.
Export these 5 tables from FIFA 16 (FIFA Editor Tool / Database Master / FIFA Mod Manager -> fifa_ng_db) into one folder:
  players.csv  playernames.csv  teamplayerlinks.csv  leagueteamlinks.csv   (+ optional nothing else)
Run:  python make_players_csv.py <folder_with_csvs> players16.csv
Rare flag = 1 for overall >= 80 (approximation; edit RARE_FROM below)."""
import csv, sys, os
RARE_FROM = 80
POS = ["GK","SW","RWB","RB","RCB","CB","LCB","LB","LWB","RDM","CDM","LDM","RM","RCM","CM","LCM","LM","RAM","CAM","LAM","RF","CF","LF","RW","RS","ST","LS","LW"]
d, out = sys.argv[1], sys.argv[2]
def rows(n):
    with open(os.path.join(d, n), encoding="utf-8-sig", newline="") as f: return list(csv.DictReader(f))
names = {r["nameid"]: r["name"] for r in rows("playernames.csv")}
team = {}
for r in rows("teamplayerlinks.csv"): team.setdefault(r["playerid"], r["teamid"])
league = {}
for r in rows("leagueteamlinks.csv"): league.setdefault(r["teamid"], r["leagueid"])
n = 0
with open(out, "w", encoding="utf-8", newline="") as f:
    w = csv.writer(f); w.writerow(["id","name","rating","position","nation","league","club","rare"])
    for p in rows("players.csv"):
        pid = p["playerid"]
        nm = names.get(p.get("commonnameid","0")) or ((names.get(p.get("firstnameid","0"),"")[:1] + ". " if names.get(p.get("firstnameid","0")) else "") + names.get(p.get("lastnameid","0"), "Player"))
        ov = int(p["overallrating"]); pos = POS[int(p["preferredposition1"])] if int(p["preferredposition1"]) < len(POS) else "CM"
        t = team.get(pid, "0")
        w.writerow([pid, nm, ov, pos, p["nationality"], league.get(t, "0"), t, 1 if ov >= RARE_FROM else 0]); n += 1
print("wrote", n, "players to", out)
