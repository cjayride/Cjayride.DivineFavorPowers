# Divine Favor Powers — TalentTree 3.0 compatibility

## Goal
- Disable TalentTree Divine Favor guardian cycling on TalentTree 3.0.0+ while keeping cooldown reduction (`DivineFavorPatch`).
- Keep Passive Powers hotkey integration working.

## Acceptance criteria
- No error log about missing `UpdatePostfix` on TT 3.0.
- Success log: removed Divine Favor cycle.
- Build 1.0.9 Release succeeds.

## Status
- Completed: 1.0.9 rebuild. HarmonyX warning on missing `UpdatePostfix` was from `AccessTools.Method`; lookups now use `Type.GetMethod` so 3.0 does not log that warning. Version stays 1.0.9.

## Files changed
- `src/Plugin.cs` — TT 3.0 `OnPlayerUpdatePostfix` block; partial unpatch; legacy `UpdatePostfix` path kept.
- Version bumps: csproj, Plugin.cs, Package/manifest.json, CHANGELOG files.

## Investigation (TT 3.0.0)
- `DivineFavorCyclePatch` still exists; hold-cycle moved to `OnPlayerUpdatePostfix`, called from `PlayerUpdatePatches.Postfix` on `Player.Update`.
- Old mod required both `StartGuardianPowerPrefix` and `UpdatePostfix`; missing `UpdatePostfix` caused early return and left start prefix active.

## Build
```
dotnet build -c Release
-> D:\AI-Agent-Projects\cjaycraft\Cjayride.DivineFavorPowers\bin\Release\net481\Cjayride.DivineFavorPowers.dll
```

## Zip
`D:\AI-Agent-Projects\cjaycraft\Cjayride.DivineFavorPowers\dist\cjayride-DivineFavorPowers-1.0.9.zip`

## Next
- Ship 1.0.9 in client modpack when ready.
- In-game smoke test when someone can: Divine Favor + Passive Powers stones, F / Shift+F.