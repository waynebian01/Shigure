# Nameplate pixel regression checks

Run from the repository root:

```powershell
dotnet run --project tests/NameplatePixels/NameplatePixels.csproj
lua tests/NameplatePixels/addon.lua
```

The console checks configuration conversion and the main-row scanner/decoder across all 20 units, including the 255/256 index boundary, optional fields, zero health, removal and capacity overflow.

The Lua check executes the actual addon layout, texture and aura code with UI API stubs. It checks unit boundaries, aura placement, removal, reload and passing an opaque health channel through unchanged. It cannot reproduce WoW's secret-value boolean restrictions or actual rendering; those require an in-game check.
