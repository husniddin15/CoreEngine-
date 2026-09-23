# ADR-0007 — Monetization: free to play with paid DLC packs

Status: **Accepted** (owner decision, 2026-09-23) · Supersedes the premium-price model in the first draft of [12 §1](../12-business-steam-legal.md) and [13 D7](../13-open-questions-and-risks.md) · Detail: [12 §1](../12-business-steam-legal.md), [04 §14](../04-technical-design.md)

## Context
The owner wants the Steam version to be free, with paid extras: premium boards such as the Mega and other higher-end real hardware, plus customization such as body textures, decals and skins, in the spirit of War Thunder's camouflages and premium content. The product's core promise is fidelity: what works in the game works on the desk. The audience includes children and schools. The game is offline-first and has no servers ([04 §1](../04-technical-design.md)).

Steam rule: every purchase inside a Steam game must use the Steam Wallet, either as DLC or through the microtransaction API; the $100 Steam Direct fee still applies to a free base game ([Steamworks: Free To Play](https://partner.steamgames.com/doc/store/freetoplay), [Microtransactions](https://partner.steamgames.com/doc/features/microtransactions)).

## Options
| Option | For | Against |
|---|---|---|
| Premium price ($19.99) | Simple; proven for this genre ([02 §5](../02-market-research.md)) | Owner prefers free; smaller reach in low-income markets |
| Free + microtransaction API (items, virtual currency) | Flexible prices and offers | Needs our own backend server to start each purchase; breaks offline-first; currency systems are a poor fit for children |
| **Free + DLC packs as ownership flags** | No server; Steam tracks ownership, works offline; each pack gets its own store page for discovery; simple to audit for fairness | Coarser products (packs, not single items); many store pages to maintain |
| Free + donations/supporter pack only | Goodwill | Very low revenue |

## Decision
Free base game on Steam. Revenue from **DLC packs**: board packs (Mega 2560 first), advanced real-part packs, customization packs, and a supporter bundle. All pack content ships in the base depot; a DLC is only an ownership flag. Direct school licences unlock everything. No microtransaction API, no virtual currency, no loot boxes, no boosters or timers.

Fair-play rules (binding for design and content):
1. Every paid part is a real product with its real specifications. No invented "faster" or "stronger" versions of real parts.
2. Everything in the base game is free: the Uno R3, the Nano, every part in the base catalogue (including every part the tutorial uses), the full Body Studio with STL import/export, and basic colours.
3. Cosmetics never change physics. Visual finish is separate from physical material ([08 §2](../08-body-designer-spec.md)).
4. There are no leaderboards in 1.0 ([ADR-0008](ADR-0008-pure-sandbox-full-release.md)); any future competitive feature must offer a class that allows free parts only.
5. Paid cosmetics are visible to everyone. Anyone can open and run a Workshop robot that uses paid parts; saving an edited copy with those parts requires the pack.

## Consequences
- Needs an entitlement layer in the app ([04 §14](../04-technical-design.md)); the simulation core never checks ownership, so headless tests and Workshop "try" mode keep working.
- The Mega 2560 moves from post-launch into the launch scope as the first paid pack, so the game has something to sell on day one ([11](../11-roadmap.md)).
- A cosmetics system (finishes, decals, board and wire skins) becomes launch scope.
- Revenue depends on a large player base: free-to-play PC games typically convert 1–5 % of players into buyers. This is risk R16 in [13](../13-open-questions-and-risks.md).
- Later board packs (ESP32, Uno R4, Raspberry Pi Pico) each need a new CPU core; they are large engineering projects, planned one at a time after launch.
