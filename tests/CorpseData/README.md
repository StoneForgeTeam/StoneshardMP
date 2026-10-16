# Corpse data regression

Run with the installed StoneForge SDK:

```powershell
$env:STONESHARD_DIR = 'C:\path\to\Stoneshard'
dotnet run --project tests/CorpseData
```

Tests the production item-list conversion without starting the game. Butchering's flat name/count/chance lists must
remain unchanged even inside a saved location containing inventory containers. Test source uses `.cs.txt` so the
game does not compile a test program as part of the mod.
