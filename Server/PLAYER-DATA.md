# Player dataset contract

The local server accepts a UTF-8 CSV with this exact logical schema:

```text
id,name,rating,position,nation,league,club,rare
```

`id`, `nation`, `league`, `club`, and `rare` must be numeric EA/FIFA values from the same reliable FIFA 16 source. `rare` is `0` for common and `1` for rare. The player ID is also the in-game asset/face lookup ID; do not substitute IDs by matching player names from another title or season.

`TestData/players16.example.csv` is a one-card shape example only. It is not an all-player database.

Validate first:

```powershell
dotnet run -c Release -- --validate-players C:\path\players16.csv player-report.json
```

Only a compatible file can be compiled into `PlayersData.cs`:

```powershell
dotnet run -c Release -- --make-players C:\path\players16.csv
```

The provided `players16.csv` contains names, club and league names, card attributes, and 17,871 data rows, but no player/nation/league/club IDs and no `rare` field. It is intentionally rejected. It cannot safely provide an all-player database or player faces for this server without a reliable FIFA 16 ID-bearing source.

The FUT REBORN / THEKURDISHGUY server branding and the existing UT route/opening behaviour are unchanged by the validator and importer.
