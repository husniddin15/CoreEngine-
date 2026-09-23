# 02 — Market Research and Competitive Analysis

Status: Accepted DRAFT v0.1 · Data collected 2026-09-22 (Steam prices are US base prices from the Steam store API; review totals from the Steam reviews API with all languages/purchase types, so store-page numbers may look lower). Items marked (?) are unverified.

Legend for "MCU sim": **bin** = executes compiled firmware binaries (what we do); **src** = source-code interpreter; **API** = models the Arduino API only, not the CPU.

---

## 1. Circuit and MCU simulators

| Product | Platform · price · licence | MCU sim | 2D/3D · physics · body design | Wiring realism | Popularity / edu | Strengths / gaps for us | Source |
|---|---|---|---|---|---|---|---|
| **Wokwi** | Web (+VS Code). Free tier; Hobby ≈ €5.6/mo, Hobby+ ≈ €8.1/mo, Pro €20/seat/mo; classroom by quote. Core emulator avr8js is MIT; platform proprietary. | **bin**: avr8js for AVR; ESP32 runs real firmware binaries; RP2040, STM32 | 2D schematic canvas; no physics, no world, no bodies | Nets, logic analyser, Wi-Fi sim; no damage modelling found | ≈1.3–1.8 M visits/month (third-party estimate) | Best emulation breadth; zero 3D/physics/missions; offline use is paid | [pricing](https://wokwi.com/pricing) · [ESP32 guide](https://docs.wokwi.com/guides/esp32) · [avr8js](https://github.com/wokwi/avr8js) |
| **Tinkercad Circuits** (Autodesk) | Web, free | Uno (+micro:bit); blocks or C++; binary-level accuracy unverified (?) | 2D breadboard view; no world | LED "pops" on overcurrent with tooltip; resistor warnings | Tinkercad overall: 100 M+ users (whole Tinkercad) | Huge classroom base; Uno-only, 2D, no robots/missions | [Autodesk news](https://adsknews.autodesk.com/en/news/150m-students-educators/) · [help](https://www.tinkercad.com/help-center/circuits) |
| **CRUMB Circuit Simulator** | Steam Windows **$8.99** (also iOS/Android/macOS); proprietary | v1.2 ships one "experimental" programmable Arduino Nano; community reports basic code/I/O only → **API**-level (?). ESP32/Wi-Fi belong to CRUMB 2.0, announced 2024, **not released as of 2026-09-22** | 3D workbench with real breadboard/jumpers; no physics world, no bodies, no missions | Real-time nodal sim, oscilloscope, multimeter | Steam: 814 reviews, 79 % positive; dev reported **100 000 copies** by Sept 2024 | Proves paid demand for 3D electronics on Steam; MCU support thin | [store](https://store.steampowered.com/app/2198800/) · [dev page](https://www.crumbsim.com/development) · [SteamSpy](https://steamspy.com/app/2198800) |
| **Shortcuit** (KoiJam) | Steam Windows, "Coming soon" with demo only; no news posts; forum asks if development stopped (Oct 2024) | **bin**: "emulates the Arduino AVR architecture… runs real compiled Arduino code"; user installs Arduino CLI | 3D sandbox; primitive shapes for frames; physics extent unclear | Nodal analysis; breadboards, motors, drivers, distance sensors, LCDs | Successor to Short Circuit VR (free, 133 reviews) | **Closest conceptual competitor**; appears stalled/unreleased (?) | [store](https://store.steampowered.com/app/2125820/Shortcuit/) · [forum](https://steamcommunity.com/app/2125820/discussions/) |
| **Velxio** (2025–26) | Web; AGPLv3 + Free / Maker $5.75/mo / Pro $15.75/mo; edu $34/student/yr | **bin**: avr8js, rp2040js, QEMU (ESP32, RISC-V, Pi 3); 35 boards | 2D canvas; no physics/robots | ngspice in WASM ≈ 60 Hz (real analog) | AI tutor; user numbers unpublished | Strong emulation + SPICE; no 3D/world | [site](https://velxio.dev/) · [repo](https://github.com/Kyrioslearn/velxio-circuit-simulator) |
| **Tinkered.ai** | Web, free | Claims cycle-accurate AVR, 1 300+ boards (marketing, unverified) | "3D simulation" is a 3D viewer; no physics world | Claims SPICE | Claims 200 K makers (unverified) | AI-generated circuits; not a game | [site](https://www.tinkered.ai/) |
| **SimulIDE** | Win/Linux/Mac; free; AGPLv3 | **bin** via simavr (GPL) | 2D | Self-described "not an accurate simulator for circuit analysis" | Hobbyist | Fast, free, offline; crude electrics | [site](https://simulide.com/p/) |
| **PICSimLab** | Win/Linux; GPL-2.0 | **bin**: simavr, qemu-stm32/esp32 | 2D board pictures | Fixed boards | Academic | Broad MCU coverage; no world | [repo](https://github.com/lcgamboa/picsimlab) |
| **Proteus VSM** (Labcenter) | Windows; commercial, quote-based | **bin** MCU + mixed-mode SPICE | 2D schematic/PCB | Full SPICE | Universities/industry | Gold-standard accuracy; expensive, not a game | [site](https://www.labcenter.com/whyvsm/) |
| **Virtual Breadboard** | Windows; core free; legacy AVR sim add-on | AVR sim flagged legacy | 2D breadboard | Layout tool | Maker | Aging | [site](https://www.virtualbreadboard.com/) |
| **UnoArduSim** | Windows, free | **src** interpreter; Uno/Mega; virtual motors/servos | None | No nets | Classroom code tracing | Not a circuit | [site](https://www.sites.google.com/site/unoardusim/home) |
| **Fritzing** | Win/Mac/Linux; €8–25 donation; GPL-3 | None | 2D; DC-only sim | Diagrams (breadboard/schematic/PCB) | Maker documentation | Diagrams only | [wiki](https://en.wikipedia.org/wiki/Fritzing) |

## 2. Robot and education simulators

| Product | Platform · price | Controller model | 3D physics · body design | Audience | Gap vs us | Source |
|---|---|---|---|---|---|---|
| **Webots** | Free, Apache-2.0; paid support | C/C++/Python/Java/ROS controllers; no MCU | ODE physics; URDF/CAD import | Universities | No electronics/wiring; not gamified | [site](https://cyberbotics.com/) |
| **CoppeliaSim** | Edu free; Pro by quote | Lua/Python, ROS | MuJoCo/Bullet/ODE | Research | Same | [site](https://www.coppeliarobotics.com/) |
| **Gazebo** | Free, Apache | ROS | ODE/Bullet | ROS ecosystem | Same | [wiki](https://en.wikipedia.org/wiki/Gazebo_(simulator)) |
| **VEXcode VR** | Browser; free tier; $199–499 per educator/yr | Blocks/Python; fixed VEX robot | 3D playgrounds; no body design, no wiring | K-12, mass adoption | No electronics | [site](https://www.vexrobotics.com/vexcode-vr.html) |
| **Robot Virtual Worlds** | Windows; $49/yr, $79 perpetual; classroom $299 | ROBOTC; predefined robots | 3D worlds | Legacy classroom | No wiring/bodies | [site](https://www.robotvirtualworlds.com/) |
| **Virtual Robotics Toolkit** | $120/user/yr | EV3 software | Physics; import own robot files | LEGO classrooms | EV3 discontinued; no electronics | [site](https://www.virtualroboticstoolkit.com/get_started) |
| **Open Roberta Lab** | Web, free, Apache | NEPO blocks → real Uno/Nano/Mega | 2D two-wheel sim | Schools, 120 countries | No 3D, no electronics | [site](https://www.open-roberta.org/features/) |
| **MakeCode micro:bit** | Web, free | JavaScript simulator (not hardware emulation) | 2D mock-up | 70 M learners reached | No circuits/world | [site](https://makecode.microbit.org/device/simulator) |
| **CoderZ** | Web; $660/class/yr | Blockly/Python; fixed robots | 3D, 50+ gamified missions | K-8 | No wiring/bodies | [reseller](https://stemfinity.com/products/coderz-cyber-robotics-101-class-license) |

## 3. Steam games in the neighbourhood (US base price; review totals 2026-09-22)

| Game | Price | Reviews (% positive) | What it is | Relevance |
|---|---|---|---|---|
| [Turing Complete](https://store.steampowered.com/app/1444480/) | $19.99 (EA) | 5 763 (96 %) | NAND → CPU → own assembly | "Learn real hardware" sells; 2D |
| [SHENZHEN I/O](https://store.steampowered.com/app/504210/) | $14.99 | 4 530 (95 %) | Fictional MCUs + assembly | Same |
| [TIS-100](https://store.steampowered.com/app/370360/) | $6.99 | 4 113 (97 %) | Assembly puzzles | — |
| [Logic World](https://store.steampowered.com/app/1054340/) | $25.00 (EA) | 369 (94 %) | 3D digital logic | No MCU/robots |
| [Virtual Circuit Board](https://store.steampowered.com/app/1885690/) | $14.99 | 322 (92 %) | Drawn logic sim | — |
| [Stormworks](https://store.steampowered.com/app/573090/) | $24.99 | 60 112 (90 %) | Vehicle builder, Lua "microcontrollers", physics, missions | Not real MCU/wiring |
| [Scrap Mechanic](https://store.steampowered.com/app/387990/) | $28.99 (1.0 July 2026) | 122 920 (92 %) | Block builder, logic gates | — |
| [Besiege](https://store.steampowered.com/app/346010/) | $14.99 | 53 275 (95 %) | Physics machines | — |
| [Trailmakers](https://store.steampowered.com/app/585420/) | $24.99 | 39 284 (92 %) | Vehicle builder | — |
| [Main Assembly](https://store.steampowered.com/app/1078920/) | $19.99 | 1 356 (83 %) | 3D robot body design, visual programming + C#, 100+ challenges | Closest "robot builder game"; no electronics |
| [RoboCo](https://store.steampowered.com/app/1067220/RoboCo/) | $9.99 (1.0 Feb 2026) | 218 (88 %) | Snap-together robots, Python, challenges, Workshop, classrooms | No MCU/wiring |
| [Robot Rumble 2](https://store.steampowered.com/app/884180/) | Free | 295 (80 %) | Combat robots with motor/ESC/battery model, scripting | No MCU, no breadboard |
| [Nimbatus](https://store.steampowered.com/app/383840/) | $19.99 | 2 172 (78 %) | 2D drone builder with sensor logic | — |
| [Autonauts](https://store.steampowered.com/app/979120/) | $19.99 | 4 968 (89 %) | Visual-programmed bots | — |
| [LogicBots](https://store.steampowered.com/app/290020/) (2015) | $19.99 | 187 (79 %) | Wire up robots, logic puzzles | Dated |
| [Craftomation 101](https://store.steampowered.com/app/1724140/) | $14.99 (EA) | 694 (83 %) | Block-programmed robots | — |
| [MoSimulator](https://store.steampowered.com/app/4398690/) | Free (EA May 2026) | 182 (98 %) | FRC-style robots, CAD modding, no programming | — |
| [FutureKreate](https://store.steampowered.com/app/1487230/) | $89.99 (EA, dormant) | 2 | ROS robots, model import | Cautionary tale (price, scope) |
| [Virtual Robots](https://store.steampowered.com/app/692170/) | $9.99 | 6 (0 %) | Robot programming | Cautionary tale (quality) |

Checked and not competitors: Circuit Canvas (diagram drawing only), ElectroSim (electrician trainer app), Roboteq simulators (motor-controller specific).

## 4. Gap analysis

Capability map (who has what):

| Capability | Who has it |
|---|---|
| (1) Binary-accurate MCU emulation | Wokwi, Velxio, SimulIDE, PICSimLab, Proteus, Shortcuit (unreleased); Tinkered claims it |
| (2) Real wiring with electrical consequences | Tinkercad (LED pops), CRUMB (3D bench, nodal), Velxio/Proteus (SPICE), Robot Rumble 2 (motor/ESC/battery, no pins) |
| (3) 3D physics world | Webots, CoppeliaSim, Gazebo, RoboCo, Main Assembly, Stormworks, Robot Rumble 2, VEXcode VR, CoderZ |
| (4) Body design in-app | RoboCo, Main Assembly, Stormworks, Robot Rumble 2; mesh import in Webots, VRT, MoSimulator. **No Arduino simulator offers STL import/export.** |
| (5) Gamified missions/curriculum | RoboCo, Main Assembly, VEXcode VR, CoderZ, Stormworks |

**No product combines (1)+(2)+(3)+(4)+(5).** Closest: Shortcuit (1+2 with rudimentary 3/4, apparently stalled), Robot Rumble 2 (2-lite+3+4, scripting instead of an MCU), RoboCo (3+4+5, Python, no electronics), Wokwi/Velxio (1+2-lite, no world), CRUMB (2 in 3D, MCU experimental, 2.0 unshipped).

Owner decision (2026-09-23, [ADR-0008](adr/ADR-0008-pure-sandbox-full-release.md)): the game is a pure sandbox, so capability (5), gamified missions, is deliberately not pursued; the combination of (1)–(4) remains unique.

Concrete differentiators to state on the store page and in the GDD:
1. **Real part numbers** (Uno/Nano/Mega, L298N, HC-SR04, SG90, TT motor) whose real failure modes decide whether a robot works.
2. **Offline Windows build**: Wokwi charges for offline; Tinkercad is online-only, which blocks restricted school networks.
3. **STL round-trip**: design in-app, print for real, run the same `.ino`.
4. **A physics arena**, not a canvas: robots drive, bump, fall and run out of battery.

Competitive risks: CRUMB 2.0 shipping ESP32 on a 3D bench; Velxio/Tinkered iterating fast with AI tutors; Wokwi adding any kind of playground; Shortcuit reviving. Our moat is the combination and the curriculum, not any single feature — ship the vertical slice quickly and keep fidelity as the brand.

## 5. Pricing observations and recommendation

- Programming/building sims cluster at **$14.99–$24.99** (Shenzhen $14.99; Turing Complete, Main Assembly, Autonauts $19.99; Stormworks, Trailmakers $24.99; Logic World $25). $9.99 titles (RoboCo, CRUMB $8.99) did not buy scale, although CRUMB still sold 100 k copies at ~$9.
- Education SaaS is priced per seat-year ($34 Velxio, $120 VRT, $199–499 per educator VEXcode VR) or per class ($299 RVW, $660 CoderZ).
- **Decision (2026-09-23):** the owner chose **free to play with paid DLC packs** instead of a premium price ([ADR-0007](adr/ADR-0007-monetization-free-to-play-dlc.md), [12 §1](12-business-steam-legal.md)). Free-to-play PC games typically convert 1–5 % of players into buyers, so success depends on a much larger player base than the premium figures below. The premium analysis is kept for comparison.
- Original premium recommendation (superseded): $19.99 base on Steam; Early Access at $14.99–17.99 if launching with a partial campaign, raising at 1.0 (Steam requires no discount within 30 days of a price increase). Separate **school site licence ≈ $300–600 per 30 seats/year** with a free demo, sold directly (Steam has no education licensing programme; keys are capped). Regional pricing via Steam's recommended conversion (Uzbekistan buyers fall in the USD-CIS bucket).

## 6. Audience size signals
- Tinkercad (100 M+ users) and Wokwi (≈1.5 M visits/month) show the Arduino-learning population is large and mostly on free web tools; our conversion story is "the next step": 3D, physics, offline, a real robot.
- Steam's "learn hardware" games (Turing Complete, Shenzhen I/O) each sold in the hundreds of thousands; builder games (Stormworks, Besiege, Trailmakers) in the millions; robot-specific builders (Main Assembly, RoboCo) in the tens of thousands. A realistic v1 target is the Main Assembly/CRUMB band (50–100 k copies over the first two years) with a stronger education tail.
