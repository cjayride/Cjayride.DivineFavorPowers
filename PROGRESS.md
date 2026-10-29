# Divine Favor Powers — Valheim 1.0.21 release

## Goal
- Verify build against Valheim 1.0.21
- BepInEx manifest `denikson-BepInExPack_Valheim-5.4.2360`
- Changelog, Thunderstore zip in `/dist`, git tag `1.0.7`

## Status
- Completed: version 1.0.7, Release build succeeded, dist zip created

## Files changed
- `Package/manifest.json`, `CHANGELOG.md`, `Package/CHANGELOG.md`, `Cjayride.DivineFavorPowers.csproj`, `src/Plugin.cs`
- Restored `Package/LICENSE`
- `dist/package/` staging + `dist/cjayride-DivineFavorPowers-1.0.7.zip`

## Build
```
dotnet build -c Release
-> D:\AI-Agent-Projects\cjaycraft\Cjayride.DivineFavorPowers\bin\Release\net481\Cjayride.DivineFavorPowers.dll
```

## Next
- Commit, push, tag on GitHub
