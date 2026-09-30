# ADR-0010 — CoreEngine Hub, a launcher for downloads outside Steam

Status: **Accepted** 2026-09-30 (the owner's request of 2026-09-29, D31). Where releases are hosted is still open.

## Context
The owner asked for a hub "to easy download and to start the program like hoyo hub you enter genshin with this hub also it downloads updates and easy to download": one small program that installs the game, keeps it up to date and starts it, as HoYoPlay does for Genshin Impact.

Steam is the main channel ([01](../01-vision-and-scope.md), [12](../12-business-steam-legal.md)), and Steam downloads and updates games itself. The Hub is for everything else:
- schools and the website's direct downloads ([12 §1](../12-business-steam-legal.md), "Direct / education");
- testers and friends before the game is on Steam;
- classrooms with slow internet, where one release folder copied onto a USB stick serves every PC (a folder is a release source like a web address).

Things found while building it:
- The game found its Arduino toolchain only because the build sat inside the repository: `ArduinoCliCompiler.FindBundled` walks up from the game's data folder to `tools/arduino`. A copy installed anywhere else could not compile. So the toolchain must ship beside the game.
- `tools/arduino/arduino-cli.yaml` names the developer's folders (`D:/Projects/…`). A copy installed elsewhere now writes `arduino-cli.local.yaml` for its own folder (`ArduinoCliCompiler.ConfigFor`). The shipped file names no folders of the publisher's PC.
- The first compile on a PC is slow: the antivirus reads each new compiler program, and the Arduino core is built once. On a busy PC it took over 2 minutes and the game gave up. The Hub now compiles a small sketch once after an install ("Preparing the Arduino compiler (first time only)…", 9 s here), and the game waits up to 5 minutes. After the Hub's warm-up, the installed game's first compile took 9.4 s instead of more than 120.

## Options
| Option | For | Against |
|---|---|---|
| A zip on the website | Nothing to build | No updates; players unzip anywhere; nothing checks the files |
| An installer (Inno Setup, MSIX) | Familiar | Every update is a new full installer; no repair; MSIX needs signing and the Store's rules |
| **A launcher like HoYoPlay** (chosen) | One button that is always the next step; updates download only what changed; repair; the game's look | A program of our own to keep working |
| Steam only | Steam does all of it | Not for schools without Steam, nor before the Steam release |

## Decision
**CoreEngine Hub** (`hub/`, .NET 10, WPF), laid out like HoYoPlay's page for a game:
- the game's picture over the window, rendered by the game itself (`-uiShots`): a hero's view of the robot on the right of the page, drawn twice as large and halved, with a warm key light and a cool rim light; it settles in as the window opens and leans a few pixels away from the mouse;
- the column of games at the left, on frosted glass (the picture behind it, blurred);
- the window's round buttons (settings, minimise, close) at the top right;
- the news at the bottom left, on frosted glass: three banners rendered by the game (building, coding, driving) that change every 6.5 s, then What's new in this version;
- at the bottom right, one big button that is always the next step: Download, Pause, Resume, Update, Start, Retry, each with its icon. The progress (per cent, speed, time left, MB) shows above it on a gold bar, and the game's menu (≡: Repair game files, Open game folder, Find the game on this PC, Uninstall game) sits beside it.

It is dressed as Genshin Impact's launcher (the owner, 2026-09-30: "we need beautiful design like hoyoverse hub and genshin impact style ... also use better render for background"): ivory and gold on the dark picture; the game's name turning from ivory to gold, with a diamond and a line under it; a gold Start button that glows under the mouse; cream dialogs with a thin inner frame, an ornament under the title and pill buttons with a round badge; motes of dust drifting in the light round the robot. In English, Uzbek and Russian. The settings hold the language, a download speed limit, what happens once the game's window is up (close the Hub, the default; minimise it until the game closes; or keep it open) and the download server.

**Releases** are plain files that any web server (or folder) can hold, written by `CoreEngine.Hub.Publish`:
- `manifest.json`: the version, the program to start, notes in three languages, and every file with its size and SHA-256;
- `manifest.json.sig`: its ECDSA P-256 signature;
- `blobs/<sha256>`: each content once, Brotli-compressed.

A new version written into the same folder adds only the contents that changed. The first release (0.1.0) is 2,243 files and 493 MB installed, 110 MB to download. The next one (0.1.1) added 4 blobs and 8.3 MB, and an installed 0.1.0 downloaded just those 8.3 MB.

**Safety:**
- The Hub carries the release key's public half (`ReleaseKeys.cs`) and installs nothing that key did not sign.
- Each file's SHA-256 is checked after unpacking, so a changed file on the server or on the way is refused; a damaged download is fetched once more, then refused.
- Paths are checked before anything is written: relative, no `..`, no drives, never the Hub's own `.hub/`, always inside the game's folder.
- The private key stays on the publisher's PC (`%USERPROFILE%\.coreengine\hub\release-key.pem`), never in the repository. A second public key can be added before the first is retired.

**Installing:**
- Per user, no administrator: the game goes to `%LOCALAPPDATA%\Programs\CoreEngine` or a folder the player chooses. The space needed is shown first.
- Downloads run three at a time and resume after Pause, a lost connection or a closed Hub (a ".part" file; HTTP Range on the web).
- Everything is unpacked and checked beside the game, then moved into place at once, so a stopped update never leaves a half-updated game.
- The Hub records what it wrote (`.hub/state.json`), so a later check trusts unchanged files. Repair reads every file; Uninstall deletes only the files the Hub wrote, and the player's robots in their own folder stay.
- On the first install it copies itself to `%LOCALAPPDATA%\Programs\CoreEngine Hub` and puts CoreEngine shortcuts on the desktop and in the Start menu that open the Hub, so each start checks for updates.
- A newer Hub started from elsewhere (a new download, a USB stick) replaces that copy, so the shortcuts open the newest Hub the player has had.

**Starting the game:**
- Start runs the game from its folder. The Hub shows Running until the game's window is up, then closes: the owner asked on 2026-09-30 "why hub isn't closing when simulator running". The shortcuts open the Hub again, and it checks for updates then. In its settings the Hub can instead minimise itself until the game closes, or stay open.
- A Hub opened while the game runs finds it (the game's program running from the game's folder) and shows Running until it closes. So the game is not started twice, and no update, repair or uninstall changes its files while they are in use.
- The compiler's first compile after an install finishes by itself if the Hub closes meanwhile.

**Testing without a window** (the owner's PC is in use while tests run):
- 33 xUnit tests cover paths, signatures, manifests, install, update, pause and resume from a folder and over HTTP (a small local server), repair, damaged downloads, uninstall, adopting a copied game and finding the game while it runs.
- `CoreEngineHub.exe --home <folder> --source <release> --shots <folder>` drives the real page through a whole install into a test folder and draws each step into a picture; the same run on an installed copy shows an update.
- `--icon` draws the Hub's icon: the game's chip mark.
- The game draws the Hub's pictures without a window: `CoreEngineSpike.exe -batchmode -spikeBench <report> -uiShots` writes `<report>-hub-keyart.jpg` (2560 × 1440), `-hub-tile.jpg` and `-hub-banner-build`, `-code` and `-drive.jpg` (1380 × 540), which go into `hub/CoreEngine.Hub/Assets`.

## Consequences
- Publishing a version: build the game, then run `CoreEngine.Hub.Publish publish --build app/Builds/Spike --toolchain tools/arduino --out <release folder> --version <v> --notes hub/notes/<v>.json`, then copy the folder to the server. `check` verifies a release as the Hub will.
- **Open, the owner's call:** where releases are hosted. It needs a web server that serves plain files with Range requests: any static host, a storage bucket, or the website's server. Until then the built-in address is empty: the Hub says "No download server is set yet" and takes one in Settings or from `--source`.
- The Hub players get is one `CoreEngineHub.exe` of 65 MB that needs no .NET installed: .NET 10 and its Windows desktop part are inside it. Making it took Microsoft's runtime packs from nuget.org, approved by the owner on 2026-09-30: 40.0 and 39.2 MB, plus the ASP.NET Core pack (12.8 MB) that the SDK fetched although the Hub does not use it. With a `release` folder beside it, the Hub installs from that folder by itself and remembers it. Until releases have a web address, that folder is how the Hub is handed out.
- Windows SmartScreen warns about unsigned programs downloaded from the web. A code-signing certificate costs money every year, and is a decision for before public downloads.
- Later: the Hub updating itself (it shows its version now), more games in its column, news with pictures.
