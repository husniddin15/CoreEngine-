# R3 — Engine, CSG/mesh libraries, Steam and distribution: fact sheet

Researched 2026-09-22. [VERIFY] = uncertain or single-source. Decisions live in the ADRs and [12-business-steam-legal.md](../12-business-steam-legal.md).

## 1. Unity
- Latest LTS: **Unity 6.3 LTS (6000.3.x)**, released 2025-12-04; supported until Dec 2027 (6.0 LTS until Oct 2026). Latest "Supported Update": 6.6 (6000.6.0f1, 2026-08-31); **6.7 LTS expected later in 2026** — https://unity.com/blog/unity-6-3-lts-is-now-available · https://unity.com/releases/unity-6/support · https://unity.com/releases/editor/whats-new/6000.6.0f1
- Licensing: Personal free up to **$200 k revenue + funding** (trailing 12 months); Pro above. **Runtime Fee cancelled 2024-09-12.** Splash screen optional for Personal on Unity 6. Pro = $2 310/yr/seat or $210/mo since 2026-01-12. Havok Physics removed from 6.3 — https://unity.com/products/pricing-updates · https://unity.com/blog/unity-is-canceling-the-runtime-fee
- Scripting runtime: Mono (JIT) or IL2CPP (AOT) today. **CoreCLR**: 6.7 LTS alphas ship an experimental CoreCLR desktop player; **6.8 drops Mono entirely**; IL2CPP stays; moves to .NET 10 / C# 14; BinaryFormatter obsolete; stricter IEEE-754 and static-ctor semantics — https://discussions.unity.com/t/path-to-coreclr-2026-upgrade-guide/1714279. "Unity 7 (6.8)" beta Dec 2026 / release Q1 2027 [VERIFY].
- Physics: GameObject physics is PhysX (4.1.x; last documented bump 4.1.2 in 2022.1) [VERIFY]; **ArticulationBody** (reduced-coordinate chains, TGS solver) available — https://docs.unity3d.com/6000.0/Documentation/Manual/physics-articulations.html
- UI Toolkit runtime: the 6.3 manual still lists uGUI as "recommended" and UI Toolkit as "alternative" for runtime; UI Toolkit lacks Animation Clip/Timeline integration and in-scene authoring; it has data binding, flex layout, SVG (6.3), UI test framework — https://docs.unity3d.com/6000.3/Documentation/Manual/UI-system-compare.html. Verdict: fine for tool-style panels; uGUI for world-space/animated HUD.
- Steamworks.NET: MIT; README builds against SDK 1.65; latest release tag 2025.164.1 (Aug 2025, SDK 1.64) [VERIFY] — https://github.com/rlabrecque/Steamworks.NET. Facepunch.Steamworks: MIT; last release 2.5.2 (Apr 2024) → less current — https://github.com/Facepunch/Facepunch.Steamworks/releases

## 2. Godot
- Latest stable **4.7.2 (2026-08-18)**; 4.7 (2026-06-18); 4.6 (2026-01-26) — https://godotengine.org/blog/release/. Godot.NET.Sdk 4.7.2 on NuGet, MIT.
- C#/.NET: requires **.NET 8 SDK or later**; Windows/Linux/macOS C# export fully supported; C# web export unsupported; mobile experimental — https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html
- Jolt: built in from 4.4 (experimental); **default for new 3D projects since 4.6**, which also made D3D12 the default renderer on Windows — https://godotengine.org/releases/4.6/
- CSG nodes: since 4.4 the backend is **Manifold**; docs say "mainly intended for prototyping"; no UV mapping; bake to MeshInstance3D; users report stutter with many runtime subtractions — https://docs.godotengine.org/en/stable/tutorials/3d/csg_tools.html · https://forum.godotengine.org/t/csg-is-lagging-my-game/62151
- GodotSteam: GDExtension 4.22.1 / Steamworks SDK 1.65 (2026-09-04) — https://godotengine.org/asset-library/asset/2445. **No official C# version**; use community C# bindings or Steamworks.NET directly — https://godotsteam.com/tutorials/c-sharp/
- Large-3D limits: draw calls are the main bottleneck; no built-in level/texture streaming — https://docs.godotengine.org/en/stable/tutorials/performance/optimizing_3d_performance.html

## 3. Unreal Engine 5
- Latest UE 5.8 (2026-06-17). Royalty 5 % of gross above $1 M lifetime per product; 3.5 % if launched day-and-date on EGS; 0 % on EGS sales — https://www.unrealengine.com/license
- Tool-heavy UI: UMG over Slate; editor-like UIs push to C++; no first-party C# → poor fit for a C# codebase.

## 4. Mesh boolean / CSG and import-export (C#/Unity)
- **Manifold** — Apache-2.0; guaranteed-manifold booleans; parallelised; bindings for C, C++, JS/WASM, Python, Java, Rust, C#/.NET; users: OpenSCAD, Blender, Godot, Babylon.js, trimesh — https://github.com/elalish/manifold. Latest tag found v3.5.3 (2025-09-07) [VERIFY newer].
- **ManifoldNET** (C#): NuGet 1.0.7-alpha (Aug 2024), net8/netstandard2.0; alpha and lagging Manifold 3.x → build own P/Invoke over the C API [VERIFY] — https://www.nuget.org/packages/ManifoldNET · https://github.com/weianweigan/manifold-csharp. Unity normals gotcha — https://github.com/elalish/manifold/issues/1151
- RealtimeCSG: MIT wrapper, closed-source native binary, no runtime editing API, stale — https://github.com/LogicalError/realtime-CSG-for-unity. Chisel (com.chisel): MIT, Unity 6+, "not yet ready for production"; prototype archived Nov 2024 — https://github.com/RadicalCSG/com.chisel
- csg.js ports: pb_CSG (MIT) and csg.cs (MIT) — BSP booleans, fine for simple runtime ops, no manifold guarantee — https://github.com/karl-/pb_CSG · https://github.com/omgwtfgames/csg.cs. geometry3Sharp — pure C#, Boost licence — https://github.com/gradientspace/geometry3Sharp. MeshLib — commercial licence — https://meshlib.io/license/
- Avoid for closed source: libigl `mesh_boolean` depends on GPL CGAL — https://github.com/libigl/libigl · https://www.cgal.org/license.html
- Import/export: pb_Stl MIT (ASCII+binary, runtime) — https://github.com/karl-/pb_Stl. glTFast = Unity-maintained `com.unity.cloud.gltfast` 6.20.0, Apache-2.0, runtime import and export — https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@6.20/manual/index.html. UnityGLTF MIT (Khronos) — https://github.com/KhronosGroup/UnityGLTF. AssimpNetter MIT (.NET wrapper for Assimp, BSD-3) — https://github.com/Saalvage/AssimpNetter

## 5. Steam publishing (Steamworks)
- Fee: $100 per app, non-refundable, recouped after $1 000 adjusted gross revenue — https://partner.steamgames.com/doc/gettingstarted/appfee
- Onboarding: legal name must match bank + tax docs (no DBA); sole proprietorship allowed; tax interview ≈ W-9 / W-8BEN, 2–7 business days; 30-day wait after fee before release; "Coming Soon" ≥ 2 weeks — https://partner.steamgames.com/doc/gettingstarted/onboarding · https://partner.steamgames.com/steamdirect
- Review: store page 3–5 business days (plan 7); build review 3–5 (plan 7); page approved before build — https://partner.steamgames.com/doc/store/review_process
- Store assets: header 920×430, small 462×174, main 1232×706, vertical 748×896; ≥ 5 screenshots 1920×1080; capsules: no text beyond title — https://partner.steamgames.com/doc/store/assets/standard. Description images < 5 MB each / 15 MB total; no external links — https://partner.steamgames.com/doc/store/page/description
- Builds: SteamPipe; app/depot VDF via steamcmd or SteamPipeGUI; beta branches — https://partner.steamgames.com/doc/sdk/uploading
- Workshop (ISteamUGC): `CreateItem` with consumer AppID; items hidden until the user accepts the Workshop legal agreement; previews consume Steam Cloud quota — https://partner.steamgames.com/doc/features/workshop/implementation
- Steam Cloud: per-user byte + file-count quota; 100 MB max per FileWrite; Auto-Cloud or API — https://partner.steamgames.com/doc/features/cloud
- Early Access: playable alpha/beta; not a funding substitute; no higher price than elsewhere; no discount within 30 days of a price increase; EA Q&A required; cannot re-enter EA — https://partner.steamgames.com/doc/store/earlyaccess
- Revenue share: 30 % to $10 M, 25 % $10–50 M, 20 % above (per game) — https://steamcommunity.com/groups/steamworks/announcements/detail/1697191267930157838
- Pricing: 37 currencies; conversion helper; no price changes within 30 days of release; Uzbekistan buyers pay in USD-CIS — https://partner.steamgames.com/doc/store/pricing · https://partner.steamgames.com/doc/store/pricing/currencies
- Refunds: 14 days and < 2 h; EA playtime counts (since Apr 2024) — https://store.steampowered.com/steam_refunds/
- Age ratings: Steam is not in IARC; own questionnaire; since 2024-11-15 titles without USK/self-rating hidden in Germany — https://partner.steamgames.com/doc/gettingstarted/contentsurvey/germany
- Steam keys: 5 000 default, more case-by-case; price parity; giveaways > 100 need approval — https://partner.steamgames.com/doc/features/keys
- **Payout**: USD only; ACH (US) or USD SWIFT wire; account in the partner's own name; by the 30th of the following month; $100 minimum; no PayPal/Payoneer — https://partner.steamgames.com/doc/finance/payments_salesreporting/faq. No country whitelist; refuses OFAC-SDN parties and Crimea, Cuba, Iran, North Korea (Workshop adds Sudan, Syria) — https://partner.steamgames.com/doc/gettingstarted/faq
- **Uzbekistan**: not sanctioned; gating factor = USD-SWIFT bank account in the exact legal name + card for the fee. Existence proof: Windrose (Kraken Express, Uzbekistan) was a 2026 Steam hit; 73 studios, $82.3 M exports in 2025 — https://www.telecomreviewasia.com/news/industry-news/29087-uzbekistan-builds-global-presence-in-games-development/ [VERIFY with bank]
- Tax: IRS applies the 1973 USSR treaty to Uzbekistan (covers royalties) → potentially 0 % withholding via W-8BEN with a foreign TIN [VERIFY with advisor] — https://www.irs.gov/businesses/international-businesses/uzbekistan-tax-treaty-documents
- CIS workarounds: foreign bank account (e.g., Kazakhstan); foreign co-owner with payment permissions; app transfer from a foreign partner; publisher; foreign company + Wise/Payoneer business — https://wnhub.io/news/investment/item-44141

## 6. Alternatives for Windows distribution
- itch.io: default 10 % share (adjustable); payouts PayPal or Payoneer only; $5 min; 30 % default withholding unless treaty — https://itch.io/docs/creators/payments. PayPal unavailable in Uzbekistan; Payoneer available → itch via Payoneer feasible [VERIFY].
- Microsoft Store: PC games 88/12 since 2021-08-01; registration fee removed (ID verification); payouts by bank/PayPal region-dependent [VERIFY Uzbekistan] — https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account
- Merchant of record: Paddle (sellers anywhere except 29 listed countries; Uzbekistan not listed; pays by bank/PayPal/Payoneer; $100 min) — https://www.paddle.com/help/start/intro-to-paddle/which-countries-are-supported-by-paddle. FastSpring (excludes only Cuba, Iran, Iraq, Myanmar, N. Korea, Russia, Somalia, Sudan, Syria; $100 min) — https://developer.fastspring.com/docs/fastspring-payouts-portal. Gumroad: Uzbekistan unconfirmed [VERIFY].
- Epic Games Store: self-publishing; $100 submission fee per product; 88/12; requires achievements and IARC ratings [VERIFY current terms] — https://store.epicgames.com/en-US/publish
- Education licensing benchmarks: Minecraft Education $5.04/user/yr; KerbalEdu $17/licence, 25-pack $330 perpetual; CircuitSim per-seat school licences. Steam has no edu licence programme.

## 7. Steam policy changes 2024–2026
- AI disclosure rewritten 2026-01-17: disclose only AI content shipped to players; live generation needs written guardrails — https://store.steampowered.com/news/group/4145017/view/3862463747997849618
- Steam Families (2024; legacy sharing retired early 2025): sharing on by default; developers can opt out; detect via `BIsSubscribedFromFamilySharing` — https://partner.steamgames.com/doc/features/families
- Refund loophole closed (Apr 2024): EA playtime counts.
- Store descriptions (Sept 2024): .mp4/.webm allowed (≤ 100 MB, ≤ 12 s); no ads for other products.
- Germany age-rating gate (2024-11-15).
- No change found to the $100 fee, 30/25/20 split or build-review process.

## Implications
- Unity 6.3 LTS + Steamworks.NET is the lowest-risk C# stack; plan a CoreCLR migration window at 6.8 (Mono removed).
- Runtime CSG in C#: Manifold via own P/Invoke over its C API is the robust, closed-source-safe option; csg.js ports as fallback; avoid CGAL/libigl booleans.
- Steam payout from Uzbekistan is feasible in principle (USD SWIFT, no sanction); confirm bank acceptance and the W-8BEN treaty claim before committing; keep itch.io (Payoneer) and Paddle/FastSpring as fallbacks.
