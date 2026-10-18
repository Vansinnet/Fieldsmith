# Building Fieldsmith

This repository mirrors the source of Fieldsmith 0.2.0 from the author's mod workspace (commit 94a3782). Releases are built and validated there; the downloads are on the [releases page](https://github.com/Vansinnet/Fieldsmith/releases).

Requirements: the .NET SDK (10 or newer) and Valheim 1.0.20 with BepInEx 5.4.23.5.

1. Create `reference/assemblies/` in the repository root and copy these assemblies into it from `Valheim/valheim_Data/Managed/` and `Valheim/BepInEx/core/`: `BepInEx.dll`, `0Harmony.dll`, `assembly_valheim.dll`, `assembly_utils.dll`, `assembly_guiutils.dll`, `Splatform.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`, `UnityEngine.IMGUIModule.dll`, `UnityEngine.PhysicsModule.dll`, `UnityEngine.TextRenderingModule.dll`. Alternatively pass `-p:ValheimReferenceDir=<folder>/` to the build.
2. Build: `dotnet build src/Fieldsmith/Fieldsmith.csproj -c Release`. The plugin is `src/Fieldsmith/bin/Release/net472/Fieldsmith.dll`.
3. Tests (no game needed): `dotnet run --project tests/Fieldsmith.Tests -c Release`.

Never commit game, Unity or BepInEx assemblies. Engine-facing details are in `src/Fieldsmith/docs/CONTRACTS.md`, the in-game test plan in `src/Fieldsmith/docs/TESTING.md`.
