# CoreEngine Hub

The launcher that downloads, updates and starts CoreEngine outside Steam, like HoYoPlay does for Genshin Impact. The reasons and the design are in [docs/adr/ADR-0010-hub-launcher.md](../docs/adr/ADR-0010-hub-launcher.md).

| Project | What it is |
|---|---|
| `CoreEngine.Hub.Core` | Releases (manifest, signatures, blobs), downloads with resume, installs, updates, repairs, uninstall |
| `CoreEngine.Hub` | The window (WPF): `CoreEngineHub.exe` |
| `CoreEngine.Hub.Publish` | Makes the release key, publishes a game build as a release, checks a release |
| `CoreEngine.Hub.Tests` | xUnit tests, including a small local web server for downloads over HTTP |

## Build and test

```
dotnet build hub/CoreEngine.Hub -c Release
dotnet test hub/CoreEngine.Hub.Tests
```

## The Hub as one file

The Hub players get is one `CoreEngineHub.exe` that needs no .NET installed: .NET 10 and its Windows desktop part are inside it. That uses Microsoft's runtime packs from nuget.org, approved by the owner on 2026-09-30.

```
dotnet publish hub/CoreEngine.Hub -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "hub/.dist/CoreEngine Hub"
```

Put a release beside it, in a folder called `release`, and the folder is ready to hand out (on a USB stick, for example). Opened, the Hub finds the release there by itself, remembers it, and installs from it. So without a web server, the Hub still installs and updates from a folder.

## Publish a version

1. Build the game (`SpikeSetup.BuildWindowsIl2cpp`).
2. Write its notes: `hub/notes/<version>.json`, a few lines each in `en`, `uz` and `ru`.
3. Publish it into the release folder:

   ```
   dotnet run --project hub/CoreEngine.Hub.Publish -c Release -- publish --build app/Builds/Spike --toolchain tools/arduino --out hub/.release/stable --version <version> --notes hub/notes/<version>.json
   dotnet run --project hub/CoreEngine.Hub.Publish -c Release -- check --release hub/.release/stable
   ```

   A new version written into the same folder adds only what changed.
4. Copy the folder to the web server; the Hub reads `<address>/manifest.json`.

The release key's private half is `%USERPROFILE%\.coreengine\hub\release-key.pem`, made once by `dotnet run --project hub/CoreEngine.Hub.Publish -- keygen`. Keep a copy somewhere safe and never commit it: without it no update can be signed for the Hubs already installed. Its public half is in `CoreEngine.Hub.Core/ReleaseKeys.cs`.

## Try it

- `CoreEngineHub.exe --source hub/.release/stable` installs from the release folder, as players will from the web.
- `CoreEngineHub.exe --home hub/.test-home --source hub/.release/stable --shots <folder>` does a whole install into `hub/.test-home` without showing a window, and draws each step into a picture. Run on an installed copy, it shows an update instead.
- `CoreEngineHub.exe --icon hub/CoreEngine.Hub/Assets/hub.ico` draws the icon.

The page's picture (`Assets/keyart.jpg`) is rendered by the game: run the game with `-batchmode -spikeBench <report> -uiShots` and scale `report-hub-keyart.png` to 1920 × 1080.
