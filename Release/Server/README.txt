SQUAD + MANAGER + MY CLUB FILTER FIX  (also contains the auto club / season fix)

Copy ALL files into D:\LocalFUT16\LocalFUT16 (AutoSquad16.cs is NEW; replace UtBackend.cs, Cosmetics16.cs, SeasonSP16.cs), dotnet run.

WHAT IT DOES
 1) AUTO SQUAD: on start (once per club) the best players of your club go into the squad by position, formation 4-4-2:
      XI  GK RB CB CB LB RM CM CM LM ST ST    subs  GK CB RB/LB CM RM/LM ST + 1
    The 8 starter golds (79) go straight into the club, so they are used at once.
    A RANDOM MANAGER (male_coaches.csv) is added and set as squad manager -> no more "no manager in the squad".
    Console: [AUTOSQUAD] random manager: ...   [AUTOSQUAD] f442, XI + 7 subs set automatically ...
 2) MY CLUB SEARCH now filters: type (player / staff / kit / badge / stadium / ball / equippables), position, level, nation, league, team.
    "club items" no longer returns players, a CB search returns only CBs.   Console: [CLUB] search type=... -> N cards
 3) The line-up can only hold player cards: a ball / kit / manager dropped into a slot is cleared.
 4) AUTO CLUB (crest, kits, club name, stadium, ball), single player season fix: see the earlier README (reset_season.txt, new_identity.txt ...).

SWITCHES (empty files next to the exe)
   no_autosquad.txt        do not build the squad automatically
   autosquad_now.txt       build it again on the next start (replaces your current squad)
   new_identity.txt        new random club (crest/kits/stadium/ball/name)
   reset_season.txt        restart the single player season
