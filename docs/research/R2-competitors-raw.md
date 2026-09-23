# R2 — Competitor research: raw notes

Researched 2026-09-22. The curated version is [02-market-research.md](../02-market-research.md). Method: Steam prices from the store API (`appdetails?cc=us`); review totals from the reviews API (`appreviews/<id>?language=all&purchase_type=all`). "bin" = executes compiled firmware binaries; "src" = source interpreter; "API" = models the Arduino API only. (?) = uncertain.

## Circuit / MCU simulators
- Wokwi — Web (+VS Code). Free; Hobby ≈ €5.6/mo; Hobby+ ≈ €8.1/mo (adds VS Code); Pro €20/seat/mo; classroom quote. avr8js MIT; platform proprietary. bin: avr8js (AVR), ESP32 real firmware binaries (10+ variants), RP2040, STM32. 2D canvas; no physics/bodies. Nets, logic analyser, Wi-Fi sim; no damage modelling. ≈1.3–1.8 M visits/month (Semrush estimate). https://wokwi.com/pricing · https://docs.wokwi.com/guides/esp32 · https://github.com/wokwi/avr8js
- Tinkercad Circuits — Web, free. Uno (+micro:bit, ATtiny via third parties); blocks/C++; binary-level unverified (?). 2D; LED "pops" on overcurrent; Autodesk: 100 M Tinkercad users (Nov 2025, whole product). https://adsknews.autodesk.com/en/news/150m-students-educators/ · https://www.tinkercad.com/help-center/circuits
- CRUMB Circuit Simulator — Steam Windows $8.99 (+iOS/Android/macOS). v1.2: one "experimental" programmable Arduino Nano; community (Dec 2024): "basic Arduino code and I/O work… don't expect low level C or assembly" → API-level (?). CRUMB 2.0 (ESP32/"Pi"/Wi-Fi-BT) announced 2024, not released on Steam as of 2026-09-22 (last news 2024-09-10; forum threads Sept 2026 still asking). 3D workbench, Godot planned; no world/bodies/missions. Steam: 814 reviews, 79 %; SteamSpy 50–100 k owners; dev: 100 000 copies by Sept 2024. https://store.steampowered.com/app/2198800/ · https://steamcommunity.com/app/2198800/discussions/0/4626981579416248266/ · https://www.crumbsim.com/development · https://steamspy.com/app/2198800
- Shortcuit (KoiJam) — Steam Windows "Coming soon", demo only; no news posts; forum "Has development stopped?" (Oct 2024) unanswered. bin: "emulates the Arduino AVR architecture… real compiled Arduino code"; user installs Arduino CLI. 3D sandbox; primitive shapes for frames; nodal analysis; breadboards, motors, drivers, distance sensors, LCDs. Successor to Short Circuit VR (free, 133 reviews). https://store.steampowered.com/app/2125820/Shortcuit/ · https://store.steampowered.com/app/970800/Short_Circuit_VR/
- Velxio — Web; AGPLv3; Free / Maker $5.75/mo / Pro $15.75/mo; edu $34/student/yr (5 min). bin: avr8js, rp2040js, QEMU (ESP32, RISC-V, Pi 3); 35 boards. 2D; ngspice in WASM ≈ 60 Hz. AI tutor. https://velxio.dev/ · https://velxio.dev/pricing · https://github.com/Kyrioslearn/velxio-circuit-simulator
- Tinkered.ai — Web, free; claims cycle-accurate AVR, 1 300+ boards, SPICE, "200K+ makers" (unverified). https://www.tinkered.ai/
- SimulIDE — Win/Linux/Mac; AGPLv3 (official), GPL-3 CE fork. bin via simavr; gpsim for PIC. 2D; "not an accurate simulator for circuit analysis". https://simulide.com/p/ · https://github.com/SimulIDE/SimulIDE
- PICSimLab — Win/Linux; GPL-2.0; simavr, qemu-stm32/esp32, picsim; gdb. https://github.com/lcgamboa/picsimlab
- Proteus VSM — Windows; commercial quote; bin MCU + mixed-mode SPICE. https://www.labcenter.com/whyvsm/ · https://www.labcenter.com/pricing/
- Virtual Breadboard — Windows/MS Store; core free; legacy AVR328P/PIC sims as subscriptions (end-of-life); third-party cites $49.99/yr (?). https://www.virtualbreadboard.com/
- UnoArduSim — Windows, free, v2.9.2; src interpreter; Uno/Mega; virtual DC/stepper/servo, I2C/SPI slaves. https://www.sites.google.com/site/unoardusim/home
- Fritzing — €8/€25 donation; GPL-3; v1.0.7 (Q4 2025); no code sim; DC-only simulator. https://en.wikipedia.org/wiki/Fritzing
- Cirkit Designer — Web; free + paid; Uno/Mega sketch simulation (level unverified). https://www.cirkitdesigner.com/arduino-simulator
- Simulator for Arduino (Virtronics) — Windows ≈ $50; src step-through. https://virtronics.com.au/Simulator-for-Arduino.html

## Robot / education simulators
- Webots — free, Apache-2.0; paid support CHF 500–2 500/yr; 4.6 k stars; controllers in C/C++/Python/Java/MATLAB/ROS; ODE; no MCU. https://cyberbotics.com/ · https://github.com/cyberbotics/webots
- CoppeliaSim — Edu free; Pro quote; Lua/Python/ROS; MuJoCo/Bullet/ODE/Newton/Vortex. https://www.coppeliarobotics.com/
- Gazebo — free, Apache; Classic EOL Jan 2025. https://en.wikipedia.org/wiki/Gazebo_(simulator)
- VEXcode VR — browser; free tier; Enhanced $199 / Premium $499 per educator/yr; blocks/Python; fixed robot. https://www.vexrobotics.com/vexcode-vr.html
- Robot Virtual Worlds — Windows; VEX $49/yr, $79 perpetual; LEGO classroom (30) $299; ROBOTC. https://www.robotvirtualworlds.com/
- Virtual Robotics Toolkit — $120/user/yr; EV3; import own 3D robot files; FLL/WRO mats. https://www.virtualroboticstoolkit.com/get_started
- CMU Virtual SPIKE Prime — browser via CS2N; 75+ environments. https://www.cmu.edu/roboticsacademy/roboticscurriculum/Lego%20Curriculum/spike-virtual.html
- Open Roberta Lab — web, free, Apache-2.0; NEPO blocks → real Uno/Nano/Mega; 2D sim. https://www.open-roberta.org/features/
- MakeCode micro:bit — JavaScript simulator; foundation reached 70 M young people (2025). https://makecode.microbit.org/device/simulator
- CoderZ — $660/class/yr (30 students); 3D sims, 50+ missions. https://stemfinity.com/products/coderz-cyber-robotics-101-class-license

## Steam games (US base price; API review totals, 2026-09-22)
Turing Complete $19.99 EA, 5 763 (96 %), SteamSpy 200–500 k · SHENZHEN I/O $14.99, 4 530 (95 %) · TIS-100 $6.99, 4 113 (97 %) · Logic World $25.00 EA, 369 (94 %) · Virtual Circuit Board $14.99, 322 (92 %) · Stormworks $24.99, 60 112 (90 %) · Scrap Mechanic $28.99 (1.0 2026-07-24), 122 920 (92 %) · Besiege $14.99, 53 275 (95 %) · Trailmakers $24.99, 39 284 (92 %) · Nimbatus $19.99, 2 172 (78 %) · Main Assembly $19.99, 1 356 (83 %), 50–100 k owners · RoboCo $9.99 (1.0 2026-02-06), 218 (88 %), 0–20 k owners · Robot Rumble 2 free, 295 (80 %) · Robocraft F2P delisted, 117 351 (73 %) · Autonauts $19.99, 4 968 (89 %) · LogicBots $19.99, 187 (79 %) · Craftomation 101 $14.99 EA, 694 (83 %) · MoSimulator free EA May 2026, 182 (98 %) · iRobot Factory free (Aug 2025), 11–21 (63–71 %) · God is a Cube $14.99 EA, 21 (71 %) · FutureKreate $89.99 EA dormant, 2 · Virtual Robots $9.99, 6 (0 %).

Not competitors: Circuit Canvas (diagram drawing) https://circuitcanvas.com/ · ElectroSim (iOS electrician trainer) · Roboteq RoboAGVSim (own controllers) · "SimplyIDE"/"ArduinoSim": no product found.

## Gap analysis and pricing (raw)
- Coverage: (1) binary MCU emulation: Wokwi, Velxio, SimulIDE, PICSimLab, Proteus, Shortcuit; (2) wiring with consequences: Tinkercad, CRUMB, Velxio/Proteus, Robot Rumble 2; (3) 3D physics: Webots/CoppeliaSim/Gazebo, RoboCo, Main Assembly, Stormworks, Robot Rumble 2, VEXcode VR, CoderZ; (4) body design: RoboCo, Main Assembly, Stormworks, Robot Rumble 2; imports: Webots, VRT, FutureKreate, MoSimulator; no Arduino simulator offers STL import/export; (5) missions: RoboCo, Main Assembly, VEXcode VR, CoderZ, Stormworks.
- No product combines all five. Closest: Shortcuit, Robot Rumble 2, RoboCo, Wokwi/Velxio, CRUMB.
- Price clusters: $14.99–24.99 for programming/building sims; $9.99 titles did not buy scale (CRUMB sold 100 k at ≈ $9). Edu SaaS per seat-year ($34–$499) or per class ($299–$660). Suggested: $19.99 base; EA $14.99–17.99; school site licence ≈ $300–600 per 30 seats/yr.
