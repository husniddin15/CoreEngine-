# ADR-0008 — Pure sandbox, full release, solo developer with AI

Status: **Accepted** (owner decisions, 2026-09-23; [13 D3, D4, D16, D18](../13-open-questions-and-risks.md)) · Supersedes the mission campaign in the first drafts of [01](../01-vision-and-scope.md), [03](../03-game-design.md) and [10](../10-content-arenas-tutorial-notebook.md) (archived at [archive/10-missions-campaign-v0.1.md](../archive/10-missions-campaign-v0.1.md))

## Context
The first drafts planned a 30-mission campaign, challenges with leaderboards, example robots and a Steam Early Access launch with a small team. The owner decided otherwise: "There are no missions. Users only create and test in the virtual world; it just works or doesn't work, same as real life. Users will have their own missions or can do whatever they want."

## Decision
- **Pure sandbox.** No missions, campaign, challenges, leaderboards, scores, stars, unlocks or example robots/templates.
- **Help without goals.** A short interactive tutorial (5–10 minutes, controls only, skippable) and a Notebook with datasheet cards, error help and "why it broke" cards. No lessons. The Arduino IDE's own code examples stay in the Code Desk.
- **Full release, no Early Access.** One 1.0 release on Steam, free to play with paid packs ([ADR-0007](ADR-0007-monetization-free-to-play-dlc.md)).
- **Languages at release:** English, Uzbek (Latin) and Russian.
- **No accounts at release.** Players use Steam only. An optional email-based account may be added after release (see Consequences).
- **Team:** the owner alone, with AI assistance for code, documentation, translation drafts and some art.

## Consequences
- Removes about 400 hours of mission content and the mission-check system from the plan; the headless runner stays for regression tests and replays.
- Learning risk rises: beginners get no guided path. Mitigations: tutorial, Notebook, clear event-log explanations, Workshop robots made by other players ([13](../13-open-questions-and-risks.md) R10).
- No Early Access means no revenue or large-scale feedback until 1.0; Steam Playtest and a public demo build are the substitutes (R21).
- Solo development puts 3D art for about 90 parts on the owner (R20); the schedule in [11](../11-roadmap.md) uses solo estimates.
- Email accounts later need a server, a privacy policy and consent rules for children (for example COPPA in the US and GDPR in the EU); this is decision D18.
- The simulation core is unchanged: fidelity, determinism and the headless runner remain requirements.
