FUT REBORN FIFA 16 - Add-on status

Added:
- packs.json with 12 enabled pack definitions. The existing UtBackend.cs loader reads this file next to the executable/current working directory.
- Existing pack and mode IDs 1-7 are preserved; IDs 8-12 are additions.

Important limitations:
- Tournament routes and mode entries already exist in the supplied source, but Fut16Routes.cs explicitly labels the response formats experimental. This package does not claim tournament entry/progression/rewards are verified.
- The supplied players16.csv is a FUT card export and does not include all required numeric identifiers for a safe full-player import. No player IDs were fabricated.
- Build/test was not run in this environment because the .NET SDK is unavailable here.
- The ZIP you supplied includes a certificate/private-key file and key-dump material. Keep the project archive private and do not publish it.
