# 12 — Business, Steam Publishing and Legal

Status: DRAFT v0.2 (2026-09-23) · Facts verified 2026-09-22 unless marked [VERIFY]. This is not legal or tax advice; confirm the items in §6 with an accountant/lawyer before launch.

---

## 1. Business model — free to play with paid packs

Decision (owner, 2026-09-23; [ADR-0007](adr/ADR-0007-monetization-free-to-play-dlc.md)): the Steam version is **free to play**. Players can buy optional packs inside the game: premium real boards such as the Mega and other higher-end real hardware, and customization such as body textures, decals and skins. The idea follows War Thunder's camouflages and premium content, but without the parts players criticise: nothing paid gives an unfair advantage, and there is no grind, premium currency or booster.

### 1.1 What is free and what is paid

| Free (base game) | Paid (Steam DLC packs) |
|---|---|
| The full game: sandbox, arenas, arena editor, measuring tools, Workshop, tutorial and Notebook | **Board packs.** Mega 2560 Pack first (same AVR core, launch scope). Later: ESP32 Pack, Uno R4 Pack, Raspberry Pi Pico Pack; each needs a new CPU core |
| Uno R3, Nano and every part in the base catalogue ([09](09-components-catalog.md)) | **Advanced part packs.** Real higher-end parts that no free content requires, for example a laser distance sensor, metal-gear encoder motors, stepper drivers or a colour display ([09 Appendix C](09-components-catalog.md)) |
| Body Studio with all shapes, physical materials, STL import/export and basic colours | **Customization packs.** Body finishes such as carbon fibre, brushed aluminium, wood and anodised colours; decals, numbers and stickers; board skins such as black or transparent PCBs; wire sets such as neon or braided sleeve; workbench, mat and arena themes |
| | **Supporter bundle.** All current packs at a discount |

Prices (owner decision, low level, [13 D7](13-open-questions-and-risks.md)): board pack $2.99–3.99; advanced part pack $2.99; customization pack $0.99–1.99; supporter bundle ≈ 30 % below the sum of its packs. Steam's lowest price tier is $0.99. Steam's regional pricing applies (Uzbekistan buyers are in the USD-CIS bucket).

### 1.2 Fair-play rules
1. Every paid part is a real product with its real specifications. No invented "faster Uno", no stat boosts.
2. Everything in the base game stays free, including every part in the base catalogue and every part the tutorial uses.
3. Cosmetics never change physics: a carbon-fibre finish keeps the part's physical material, mass and friction ([08 §2](08-body-designer-spec.md)).
4. There are no leaderboards in 1.0. If a competitive feature is ever added, it must offer a class that allows free parts only.
5. No loot boxes, no random rewards for money, no virtual currency, no energy timers, no XP boosters. Every price is a clear real-money price on a Steam store page.
6. Workshop: paid cosmetics on shared robots are visible to everyone. Anyone can open and run a robot that uses paid parts ("try" mode); saving an edited copy with those parts requires the pack.
7. School licences unlock everything.

### 1.3 Channels

| Channel | Model | Notes |
|---|---|---|
| Steam (Valve) | Free base game + DLC packs | Steam takes 30 % (25 % above $10 M, 20 % above $50 M lifetime per game). All purchases in a Steam game must go through the Steam Wallet, as DLC or through the microtransaction API; we use DLC only, so no server is needed and ownership works offline ([Steamworks: Free To Play](https://partner.steamgames.com/doc/store/freetoplay), [Microtransactions](https://partner.steamgames.com/doc/features/microtransactions)). |
| Direct / education | School site licence ≈ $300–600 per 30 seats/year, DRM-free build with everything unlocked + teacher tools; sold via a merchant of record (Paddle or FastSpring) | Steam has no education licence programme; Steam keys are capped (5 000 by default) and must respect price parity. |
| itch.io | Optional later | Payouts via Payoneer (PayPal is unavailable in Uzbekistan) [VERIFY Payoneer availability for the studio]. |
| Microsoft Store | Optional later | 88/12 split for PC games; no registration fee; check payout support for the studio's country [VERIFY]. |
| Epic Games Store | Optional later | $100 submission fee per product, 88/12; requires achievements and IARC ratings [VERIFY current terms]. |

No ads. No separate game account: players use their Steam account, and Steam handles payments and parental controls.

Revenue expectation: typical free-to-play PC games convert 1–5 % of players into buyers ([Medium: The Economics of Free-to-Play](https://medium.com/@maya.l.hazarika/the-economics-of-free-to-play-3afb1633c716)). The model therefore needs a much larger player base than a $19.99 game to earn the same; see risk R16 in [13](13-open-questions-and-risks.md).

## 2. Steam publishing checklist (Steamworks facts)

| Step | Fact | Source |
|---|---|---|
| Fee | $100 per app, non-refundable, recouped after $1 000 adjusted gross revenue | [appfee](https://partner.steamgames.com/doc/gettingstarted/appfee) |
| Identity/tax | Legal name must match bank and tax documents (sole proprietorship allowed); tax interview (W-8BEN for non-US), 2–7 business days | [onboarding](https://partner.steamgames.com/doc/gettingstarted/onboarding) |
| Waiting periods | 30 days after paying the fee before release; "Coming Soon" store page live ≥ 2 weeks before release | [Steam Direct](https://partner.steamgames.com/steamdirect) |
| Reviews | Store page review 3–5 business days (plan 7); build review 3–5 (plan 7); page must be approved before the build | [review process](https://partner.steamgames.com/doc/store/review_process) |
| Store assets | Header 920×430, small 462×174, main 1232×706, vertical 748×896; ≥ 5 screenshots 1920×1080; capsules must not carry text beyond the title | [assets](https://partner.steamgames.com/doc/store/assets/standard) |
| Builds | SteamPipe via `steamcmd` with app/depot VDF scripts; beta branches; set the default branch live manually | [uploading](https://partner.steamgames.com/doc/sdk/uploading) |
| Workshop | ISteamUGC `CreateItem` with the app ID; items hidden until the user accepts the Workshop legal agreement; preview images count against Steam Cloud quota, so configure Cloud | [workshop](https://partner.steamgames.com/doc/features/workshop/implementation) |
| Cloud | Per-user byte and file quotas; 100 MB max per file write; Auto-Cloud or API | [cloud](https://partner.steamgames.com/doc/features/cloud) |
| Free to play | The $100 fee still applies to the free base app; DLC needs no separate fee and gets its own store page | [free to play](https://partner.steamgames.com/doc/store/freetoplay), [DLC](https://partner.steamgames.com/doc/store/application/dlc) |
| In-game sales | Every purchase inside a Steam game must use the Steam Wallet, either as DLC or via the microtransaction API (which needs a backend server). We sell DLC only | [microtransactions](https://partner.steamgames.com/doc/features/microtransactions) |
| Early Access (not used: full release, [ADR-0008](adr/ADR-0008-pure-sandbox-full-release.md)) | Must be a playable alpha/beta; not a pre-order substitute; EA Q&A required; no re-entering EA; cannot be priced higher than elsewhere | [EA](https://partner.steamgames.com/doc/store/earlyaccess) |
| Pricing | 37 currencies; no price changes within 30 days of release; a price rise triggers a 30-day discount cooldown | [pricing](https://partner.steamgames.com/doc/store/pricing) |
| Refunds | 14 days and under 2 hours played; EA playtime counts | [refunds](https://store.steampowered.com/steam_refunds/) |
| Age rating | Steam uses its own content questionnaire (not IARC); since Nov 2024, titles without USK or a Valve self-rating are hidden in Germany — fill in the survey | [Germany](https://partner.steamgames.com/doc/gettingstarted/contentsurvey/germany) |
| AI disclosure | Rewritten Jan 2026: disclose AI-generated content that ships to players; dev-side efficiency tools need no disclosure | [Steam news](https://store.steampowered.com/news/group/4145017/view/3862463747997849618) |
| Steam Families | Sharing on by default; developer may opt out; detect via `BIsSubscribedFromFamilySharing` | [families](https://partner.steamgames.com/doc/features/families) |

### 2.1 Payouts and the studio's location (Uzbekistan)
- Valve pays **USD only**, by ACH (US) or **USD SWIFT wire** to an account in the partner's exact legal name; $100 minimum; paid by the 30th of the following month; no PayPal/Payoneer. ([finance FAQ](https://partner.steamgames.com/doc/finance/payments_salesreporting/faq))
- Valve publishes no country whitelist; it refuses sanctioned parties and Crimea, Cuba, Iran, North Korea (Workshop adds Sudan, Syria). **Uzbekistan is not excluded**; the gating factor is a USD-SWIFT-capable bank account in the legal name and a card to pay the $100 fee. Existence proof: Uzbek studios ship on Steam (e.g., Windrose by Kraken Express in 2026; 73 studios and $82.3 M game exports reported for 2025). ([Telecom Review Asia](https://www.telecomreviewasia.com/news/industry-news/29087-uzbekistan-builds-global-presence-in-games-development/)) [VERIFY with the bank that inbound USD wires from Valve clear]
- Tax: the IRS applies the 1973 USSR treaty to Uzbekistan, which covers royalties → potentially 0 % US withholding via a W-8BEN treaty claim (needs a foreign TIN). [VERIFY with a tax advisor] ([IRS](https://www.irs.gov/businesses/international-businesses/uzbekistan-tax-treaty-documents))
- **Owner decision (D12):** payouts go to a **company (LLC) registered in Uzbekistan** with a USD SWIFT account in the company's exact legal name; Steamworks onboarding, the tax form and the Steam Distribution Agreement are done in the company's name. A company normally uses the W-8BEN-E form rather than W-8BEN [VERIFY with an accountant].
- Common CIS workarounds if the bank is a problem: a foreign bank account, adding a foreign co-owner with payment permissions to the partner account, a publisher, or a foreign company with a Wise/Payoneer business account.

### 2.2 Launch timeline (minimum)
1. Create the Steamworks partner account, pay $100, complete tax/identity (allow 2–3 weeks).
2. Publish the "Coming Soon" page at least 6 months before release to collect wishlists (Steam's minimum is 2 weeks), in English, Uzbek and Russian (assets, trailer, tags: Education, Programming, Building, Simulation, Robots, Sandbox, Physics, Free to Play).
3. Create the DLC store pages (Mega 2560 Pack, first customization packs, supporter bundle) with their own capsules and screenshots; they are reviewed like the main page.
4. Upload the build to a beta branch; run a Steam Playtest with the community (wishlist driver).
5. Submit the build for review ≥ 7 days before the date; release 1.0; afterwards, updates every 2–4 weeks and a new pack every 1–2 months.

## 3. Third-party software licences (shipping build)

| Component | Licence | How we comply |
|---|---|---|
| arduino-cli | GPLv3 | Shipped as a **separate executable** in `tools/`, invoked as a subprocess; licence text included; written offer / link to the exact source version in `tools/licenses/`. |
| avr-gcc, binutils | GPLv3 (+ runtime library exception for the libraries linked into user binaries) | Same as above; compiler output (the user's `.hex`) is not GPL. |
| avr-libc | BSD-3 | Attribution. |
| Arduino AVR core (`arduino:avr`) | LGPL-2.1 (core), some GPL tools | The core is compiled into the user's sketch binary, which runs in the emulator; LGPL permits this with source availability (core sources are shipped). |
| Bundled Arduino libraries | Mostly MIT/BSD/LGPL (verify each; e.g., Adafruit MIT/BSD, FastLED MIT, IRremote MIT, NewPing GPL? [VERIFY]) | Ship sources; exclude GPL-only libraries from the bundle if any and let users install them online. |
| avr8js (design reference, test vectors) | MIT | Attribution in the credits if any test data is reused. Our emulator is an independent implementation. |
| simavr (CI oracle only) | GPLv3 | Never shipped. |
| Manifold (CSG) | Apache-2.0 | Attribution; NOTICE file. |
| glTFast / UnityGLTF | Apache-2.0 / MIT | Attribution. |
| pb_Stl | MIT | Attribution. |
| Steamworks.NET | MIT | Attribution; Steamworks SDK under Valve's terms. |
| Unity | Unity Personal free below $200 k revenue+funding per 12 months; Pro $2 310/yr/seat above; Runtime Fee cancelled; splash optional on Unity 6 | Track revenue; budget Pro seats when needed. |
| Fonts, audio, 3D assets | Per asset | Keep a `CREDITS.md`; prefer CC0/own assets; no manufacturer logos. |

A `THIRD-PARTY-NOTICES.txt` is generated at build time and shown in Settings → About.

## 4. Trademarks and naming

- **"Arduino"** and the Arduino logo are trademarks of Arduino (the policy page names Arduino S.r.l.). Verified policy ([arduino.cc/en/trademark](https://www.arduino.cc/en/trademark), [compatible-products guide, updated 2025-10-31](https://www.arduino.cc/en/trademark/guides/trademark-guide-for-compatible-products/)):
  - Forbidden: using an Arduino trademark as part of a company name, product name, logo or commercial domain name; using the Arduino logo on the product, packaging or promotion; naming the product "Arduino …" or "… Arduino Board".
  - Allowed: "Compatible with Arduino", "For Arduino", "Based on Arduino" — always with the word Arduino **last** (e.g., "robot simulator for Arduino"), and with our own distinguishable logo.
  - Required acknowledgement text: "Arduino® is a trademark of Arduino S.r.l." — placed in the credits, the About screen and the store description.
  - The guide is written for hardware products; our software use is nominative (we describe compatibility). Store the policy links in `tools/licenses/TRADEMARKS.md`.
- Part names such as **L298N** (STMicroelectronics part number), **HC-SR04**, **SG90**, **MPU-6050** (InvenSense/TDK), **WS2812B** (WorldSemi) are used descriptively (nominative fair use). No manufacturer logos on 3D models; silkscreen text uses generic layouts.
- "Uno", "Nano", "Mega" are Arduino product names; the in-game parts are labelled "Uno R3-compatible board" style names in the UI where practical, with the exact real name in the datasheet page for search clarity [decision needed — see doc 13].
- Product name: **CoreEngine** (owner decision, doc 13 D2). Existing software products use "Core Engine", so before the Steam page is created, check Steam store search, trademark databases (USPTO, EUIPO, WIPO and the Uzbek patent office) and domain availability; if a conflict appears, a subtitle or a distinctive form (e.g., "CoreEngine: Robot Lab") is the fallback.

## 5. Privacy, minors and content
- The audience includes minors. 1.0 has **no accounts, no chat, no user-to-user messaging**; Workshop uses Steam's own identity and moderation.
- Optional email accounts may come after release ([13 D18](13-open-questions-and-risks.md)). They need a server, a privacy policy, secure password handling, and parental-consent rules for children (for example COPPA in the US and GDPR in the EU).
- AI use: the owner builds the game with AI help. AI-generated content that ships to players (art, text, translations, audio) must be disclosed in Steam's content survey; AI used only as a development tool, such as help with code, does not need disclosure under the January 2026 rules [VERIFY the wording when filling in the survey].
- Purchases: every pack is a Steam DLC with a fixed real-money price, bought through the Steam store; Steam's family and parental controls apply. No loot boxes, random paid rewards or virtual currency, which also avoids loot-box regulation in some countries. No personal data collected; optional crash/analytics is opt-in with a plain-language dialog.
- Workshop content is data only (JSON, meshes, sketch text); no code execution from shared content outside the emulator; report/block via Steam.
- Store content survey: no violence/mature content; "magic smoke" is cartoon-level.

## 6. Items to confirm with professionals
1. Bank: USD SWIFT inbound from Valve; account in the legal name; card for the $100 fee.
2. Tax: W-8BEN treaty claim; local tax registration for foreign income; VAT/MoR implications for direct sales (Paddle/FastSpring act as merchant of record and handle VAT).
3. Legal entity: decided, a company (LLC) in Uzbekistan. Confirm the USD account, who signs the Steam Distribution Agreement, the company's tax form (W-8BEN-E), and local tax on foreign income.
4. Trademark clearance for the final product name (check Steam, USPTO/EUIPO/WIPO databases, domain availability).
5. EULA/privacy policy text (Steam requires none beyond the SSA, but the direct/education channel needs both).

## 7. Costs (rough, USD)

| Item | Cost |
|---|---|
| Steam Direct fee | 100 per app |
| Unity Personal / tooling | 0 (Pro only above the threshold) |
| Logic analyser for fidelity tests (the owner already has a Uno kit) | 10–15 |
| Company registration and USD bank account in Uzbekistan | local fees [VERIFY] |
| AI tool subscriptions | monthly, depends on the plan |
| Asset store / fonts / audio | 0–500 |
| Trademark search & registration (optional) | 0–1 500 |
| Trailer/capsule art (freelance) | 300–1 500 |
| DLC capsule art and cosmetic textures/decals (freelance, per pack; 0 if self-made) | 200–800 |
| Paddle/FastSpring | % of sales, no fixed fee |
| Total cash to the 1.0 release (solo, excluding company and AI subscriptions) | ≈ 500–4 000 |
