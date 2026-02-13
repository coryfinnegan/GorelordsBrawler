## 1. Game Overview

**Title:** Gorelords the Video Game

**Genre:** Party Brawler

**Platform(s):** PC, Console (Switch and XBox), Crossplay enabled with matchmaking

**Target Audience:** Casual players who want a fun gaming experience with friends.

**High Concept:** Gorelords the Video Game is a gory and frantic party brawler.  2-4 players will select from a roster of toys from the Gorelords toy series.  They will fight in a 2d plane with platforms and multiple levels. Gameplay will be frantic quick matches where the toys will use unique abilities to fight each other.  Abilities will be melee and ranged.  Think ultra violent Smash Bros with toys and weapons.

**Unique Selling Points:** The playable character sprites will be digitized photographs, or an equivalent, to achieve the look of the Gorelords toys (think Mortal Kombat with plastic toys) Backgrounds will be hand painted in the box art style of the Gorelords toys.  It will have harken to an era of 90s gaming that is fun and spilling over with over the top toy violence.

---

## 2. Core Gameplay

**Core Loop:** 2-4 players will pick their character and then fight in an arena.

**Primary Mechanics:** Players fight each other with a mix of melee and ranged weapons depending on the character they have chosen.  There are knock backs, defensive moves, jumping with varying degrees of mobility, and special attacks.   Characters will fit archetypes like glass cannon, heavy, ranger etc.

**Player Goals:**  Fight other Gorelords

**Win/Loss Conditions:** They will have a 3 lives.  If they are brought down to 0 health or knocked off the map they will lose a life.  When you are out of lives you are knocked out of the match.  The last player standing wins.

---

## 3. Story and Narrative

### Setting

The game is set on the Death Grid — a gladiator fighting arena on a moon of Saturn. There are 12 moons of Saturn, each with its own unique world. The game focuses on this one moon, which contains a handful of key locations: the rich district, the slums (where the Piss-ons live), the Death Grid arena complex, the mines, and the sewers.

### The Death Grid

The Death Grid is run by the evil **Total Master**. It is an ancient-style gladiator arena built for the entertainment of the wealthy. Events include death matches, chariot races (the Death Machine), versus-beast fights, and deadly obstacle courses where slaves compete for scraps of food. The arena contains hidden weapons and traps that pop out of the grid floor and walls — saw blades, flamethrowers, and caged beasts that can be activated by Total Master from his boardroom above.

The Death Grid complex includes:
- **The Arena** — the main fighting pit where crowds gather
- **The Boardroom / Owner's Box** — Total Master's perch overlooking the arena
- **The Dungeon** — cells where fighters are held between matches
- **The Dungeon Lab** — Madman's workshop where losers are reassembled
- **The Sewers** — where the Resistance hides

### The Participants

Fighters end up in the Death Grid for different reasons:
- **Glory seekers** — fighting for fame and riches, the top fighters live like kings
- **Criminals** — sentenced to the arena, fighting to earn their freedom
- **Slaves** — forced in because of their size or strength, fighting for a chance at freedom
- **Piss-ons** — the desperately poor, competing in brutal obstacle courses for small rations of food

### Plot Summary

The story follows **Future Axe**, an anti-hero who was abducted from Earth and forced into slavery in the mines. He now fights in the Death Grid death matches — partly because he's good at it, partly for the chance to one day earn his freedom. He wears a helmet at all times, never speaks, and lets his axes do the talking.

His cellmate is **Doc Marauder**, a wise-cracking intergalactic smuggler who got arrested and thrown into the arena. Doc tries to befriend the silent Future Axe, cracking jokes about his own modified crossbow hand (which accidentally goes off and shoots a random alien in the face).

The reigning champion is **The Suffer**, a brutal killing machine born of pain. When Future Axe defeats The Suffer in the main event — with an assist from a grid trap that decapitates the champion — **Total Master** is enraged. The crowd cheers for the new champion while Total Master screams "BRING ME FUTURE AXE!!!"

Meanwhile, a cloaked **Resistance Leader** watches from the crowd. After the fight she finds a mutilated Piss-on curled up with his meager winnings and throws him 20 times more food than he just won. The Resistance lives in the sewers and plots to end the death matches for good.

**Monitorr**, the floating referee head, presides over everything — announcing matches with cruel commentary, explaining the rules, and then devouring the remains of the losers with his laser eye before regurgitating what's left into barrels. The Death Guards carry these barrels to **Madman**, the blind cannibal surgeon in the dungeon lab, who hacks apart the dead and mixes and matches body parts to keep surviving fighters alive and make them stronger warriors — all while tasting his work along the way.

---

## 4. Characters

### Roster Status Key

- **Production Ready** — 3D model (OBJ/STL) exists and is ready for the sprite pipeline
- **In Game** — Currently implemented with prototype sprites (colored rectangles)
- **Concept Only** — Character designed but no 3D model yet

### Playable Fighters

#### Future Axe — `Production Ready`
*Model: `Future AXE.obj`*

Main character. Former Earth human, abducted and enslaved in the mines. Now fights in the Death Grid for fun and the chance to eventually gain his freedom. Wears a helmet at all times — no animated dialogue needed. Silent anti-hero.

- **Archetype:** Melee brawler
- **Melee:** Dual axes — hard-hitting, medium speed
- **Special:** Can pick up and use grid weapons (traps, saw blades) that spawn during matches
- **Traits:** Medium weight, solid all-rounder. Simple to learn, effective to play.

#### The Suffer (Tormentorr) — `Production Ready`
*Model: `Tormentorr.obj`*

Current reigning champion and main competitor of the Death Grid. A brutal killing machine born of pain. Sadomasochistic — loves to both receive and inflict punishment.

- **Archetype:** Heavy / berserker
- **Melee:** Mace that can stun (with diminishing returns)
- **Special:** Immune or resistant to knockbacks. When low on health he does more damage.
- **Traits:** Slow but devastating. The final boss energy of the roster.

#### Ichor (Astrarot) — `Production Ready`
*Model: `Astrarot_PrintReady.OBJ`*

Two-headed ogre. Speaks only in grunts. Participates in Death Grid fights purely for the fun of it. Massive, muscular, with furry legs and horns on both heads. Carries a spiked club and a bladed weapon.

- **Archetype:** Berserker / grappler
- **Melee:** Axe and spikey stick combo
- **Special:** Can run fast on hooved legs and leap well. Grabs and bites.
- **Traits:** Heavy but surprisingly mobile. Two heads means twice the anger.

#### Doc Marauder — `In Game`
*No 3D model yet*

Intergalactic smuggler who got arrested and placed in Death Grid fights. Wise-cracking comic relief who tries to befriend Future Axe. Has a modified hand that is now a crossbow (which tends to go off accidentally). Baby-faced with an eye patch.

- **Archetype:** Glass cannon / ranger
- **Melee:** Weak melee damage
- **Ranged:** Crossbow hand — quick shots, can fire while jumping
- **Traits:** Fast, fragile, jumpy. Keep your distance or lose your head.

#### Trollborg — `In Game`
*No 3D model yet*

Super dumb and slow but can kill you easily. The simplest character in the roster — pure melee, high armor, high damage. Simple for people who are simple.

- **Archetype:** Tank
- **Melee:** Heavy fists, slow but devastating
- **Special:** Highest armor in the game, barely flinches from hits
- **Traits:** Super heavy, slow, high armor, melee only. The beginner character.

#### Bloodozer — `Production Ready`
*Model: `Bloodozer_REV1.obj`*

A hulking brute built for destruction.

- **Archetype:** Charge / heavy
- **Melee:** Crushing melee attacks
- **Special:** Charge attack — builds momentum and plows through opponents
- **Traits:** WIP — needs moveset design

#### Treadkill — `Production Ready`
*Model: `Treadkill_PrintReady.OBJ`*

A treaded war machine fighter.

- **Archetype:** Charge / heavy
- **Melee:** Grinding treaded attacks
- **Special:** WIP — vehicle-themed movement abilities
- **Traits:** WIP — needs moveset design

#### Phaserbeast — `Production Ready`
*Model: `Phaserbeast_PrintReady.OBJ`*

An alien beast with built-in beam weapons.

- **Archetype:** Ranged / beam
- **Melee:** Claws and bites
- **Ranged:** Energy beams — sustained fire that can sweep across the arena
- **Traits:** WIP — needs moveset design

#### MaceFace — `Production Ready`
*Model: `MaceFace_PrintReady.OBJ`*

A gladiator-style warrior wielding a massive mace. Armored and brutal.

- **Archetype:** Weapon specialist / melee
- **Melee:** Mace — slow wind-up, huge damage and knockback
- **Traits:** WIP — needs moveset design

#### Skab — `Production Ready`
*Model: `Skab_PrintReady.OBJ`*

A scrappy, scarred fighter covered in wounds and scabs.

- **Archetype:** Scrapper
- **Melee:** Dirty fighting — clawing, biting, improvised weapons
- **Traits:** WIP — needs moveset design

#### Pistain — `Production Ready`
*Model: `Pistain_PrintReady.OBJ`*

A stained, corrupted fighter.

- **Archetype:** TBD
- **Traits:** WIP — needs moveset design

#### Mutant Cop — `Production Ready`
*Model: `MutantCop_Printready02.OBJ`*

A former law enforcement officer who has mutated. Pre-melt and post-melt forms — once a cop, now a melting horror.

- **Archetype:** Fighter
- **Melee:** Beats you with his nightstick
- **Ranged:** Shoots you with his gun
- **Traits:** Intentionally the worst character in the game because fuck cops. (May be renamed from Psycops)

#### Murdroid — `Production Ready` `Concept Art Available`
*No confirmed model match — design sketches show front/back with drone head, laser cannon, receipt printer, and spike projectiles*

Murder bot with a heart of gold. Mostly chrome with matte black, glowing red and teal lights. Head pops off and flies like a drone. Has a receipt printer (he's also an auditor — knocks on your door, and when you answer, your head explodes).

- **Archetype:** Hybrid ranged/melee
- **Melee:** Chainsaw arm
- **Ranged:** Laser from head, spike projectiles from back
- **Special:** Rocket boosters — can drop down on you with spikey back but gets stuck for a second. Drone head detachment.
- **Traits:** Unique silhouette, lots of personality. A robotic Swiss army knife of death.

#### Madman — `Production Ready`
*Model: `MadMan_PrintReady02.OBJ`*

The blind cannibal surgeon of the Death Grid. Long stringy hair covering his face (front view) and a bloody apron with tools. Carries a cleaver/saw. Has a collection of random parts from dead fighters and enjoys tasting his work while operating. Mixes and matches body parts to keep fighters alive and make them better warriors.

- **Archetype:** Melee / debuffer
- **Melee:** Cleaver and surgical saw
- **Special:** WIP — could involve stealing opponent health/limbs, poison/infection mechanics
- **Traits:** Creepy, unsettling. Blind but deadly. The Frankenstein of the Death Grid.

#### Bobee — `Production Ready`
*Model: `Bobee_PrintReady.OBJ`*

- **Archetype:** TBD
- **Traits:** WIP — needs character design and moveset

#### Lil Flex — `Production Ready`
*Model: `lil flex.obj`*

- **Archetype:** TBD
- **Traits:** WIP — needs character design and moveset

### GDD-Original Characters (No Models Yet)

These characters are from the original GDD and remain in the design. They need 3D models or physical toys to enter the sprite pipeline.

#### Maggotgagger — `Concept Only`
Smells bad and is rotting, covered in slime.
- **Melee:** Slime fists that cover the enemy in damage-over-time stink
- **Ranged:** CC that slows by throwing slime-covered maggots

#### Time-Gore — `Concept Only`
Two-headed guy who can time travel.
- **Melee:** Standard melee damage
- **Special:** Time warp — set a checkpoint, then warp back to it (restoring health to checkpoint state). Time-charge dash that does damage.

#### Gumongous — `Concept Only`
Four-handed mutant with swords.
- **Archetype:** Melee grabber. Hits with swords, grabs and bites. Slow but if he gets you it hurts.

#### Arthur — `Concept Only`
Is the Zodiac Killer. Has a shotgun hand and noose hand.
- **Archetype:** Butcher — grabs and pulls you close, then shoots you
- **Special:** Can disappear for a few seconds but loses health while invisible

#### Quantum Wolf — `Concept Only`
Demon cyborg who jump scares everyone.
- **Archetype:** Feral rushdown — runs on all fours, leaps on victims, bites and drills with robot hand. High jump.

#### Snotpile — `Concept Only`
Made of snot, teeth, and shards of glass.
- **Archetype:** Pest — can jump around and bite you. Only bites but is fast and hard to hit.

### Non-Playable Characters

#### Total Master — `Production Ready`
*Model: `TotalMaster_Printready.OBJ`*

Boss of the Death Grid and warden of the entire operation. Smooth-talking villain who wears a suit and is humanoid in features. Watches fights from his boardroom above the arena. Controls the grid traps — activates saw blades, flamethrowers, and caged beasts when his champion starts losing. Sits in an egg-shaped chair surrounded by alien skull trophies, sipping cocktails.

- **Role:** Main antagonist, controls arena hazards during matches
- **In-Game:** Could trigger random arena traps, appear in cutscenes/intros. Potential unlockable boss character.

#### Right Hand Man — `Production Ready`
*Model: `RightHandMan_Printready.OBJ`*

Total Master's personal guard. Large brute with a small head and an enlarged right arm. Stands silently by the doorway of the boardroom.

- **Role:** Enforcer, potential sub-boss or unlockable character

#### Monitorr — `Concept Only`
*Design sketches available (see "Kills Split Face" sketch sheet — Monitorr beaming body parts)*

Referee and commentator of the Death Grid. A large floating head that presides over every match. Announces fighters, explains the rules, provides cruel running commentary ("flaccid and weak display of male inferiority"). After a match ends, he uses his laser eye to telekinetically lift the remains of the loser into his mouth, chews them up, and regurgitates what's left into barrels for Madman. Speaks electronically — mouth only moves when consuming parts or breathing. His catchphrase: **"The only way out is death."**

- **Role:** Announcer, match referee, lore narrator
- **In-Game:** Floating head that appears during match intros, KOs, and victories. Announces countdown, delivers "FIGHT!" and "K.O.!" calls. Could replace the current text-based announcement system with a character-driven one. Devours the loser's stock icon when a life is lost.

#### Kevin in Accounting — `Concept Only`

Personal assistant and accountant to Total Master. Goes over his schedule and upcoming events. A regular human who somehow ended up working for an intergalactic death arena — wears a polo shirt and khaki pants.

- **Role:** Comic relief, exposition. Appears in boardroom cutscenes.

#### Death Guards (TV Heads) — `Production Ready`
*Models: `Death Guard 1.obj`, `DeathGuard_2_PrintReady.OBJ`*

Cyborg guards of the Death Grid. Large and muscular with no mind of their own. Their heads are TV sets that display commands via prompter lettering or broadcast the Death Grid fights. Carry electrified staffs and wear heavy armor. "Screen Prompts Commands" written on the design.

- **Role:** Escort fighters, enforce order, carry barrels of remains. Potential stage hazards.

#### Resistance Leader — `Concept Only`

A cloaked woman who is trying to end the death matches. Hides in the crowd during fights, observes, and slips away. Shows compassion for the Piss-ons. Leads a group of ~10 resistance fighters in the sewers who are arming up with guns and knives.

- **Role:** Potential story mode ally, could unlock future content

#### Whipmaster General — `Concept Only`
*Design sketch available*

Overseer of the mines. Four-eyed beast with double whips and a mushroom pouch — the mushrooms make him aggressive, high, and abusive. Forces miners to harvest his mushrooms. Runs fast and has awesome double whipping skills.

- **Role:** Mine stage boss or stage hazard. Potential unlockable.

### Arena / World Characters (Non-Combatant)

These characters populate the world and could appear as background elements, stage hazards, or crowd members:

- **Piss-ons** — The desperately poor inhabitants. Compete in obstacle courses for food. Sad, deformed, missing limbs.
- **Mine Slaves** — Helmeted slaves with pickaxes in the mines. Various alien species — humanoid, tentacled, insectoid, fat, skeletal. All wearing the same cage-like mining helmets.
- **Battle Slaves** — Poorly armored fighters thrown against champions like The Suffer. Expendable cannon fodder with improvised weapons.
- **Rich Fans** — Wealthy alien spectators in the swanky viewing areas. Dressed in finery, sipping cocktails, wearing hats and jewelry. Alien species with multiple eyes, horns, tentacle fingers.
- **Poor Fans** — The slum-dwelling spectators. Covered in sores, missing teeth, injecting drugs, holding signs, cheering desperately.
- **Space Thugs / Biker Gang** — Three members: an entry-level crystal runner with a knife and space booze, Percy & Gonad (a two-headed biker duo, high on space crystals, the little one is in charge, uses laser to propel), and CrotchRot the Enforcer (all fingers cut off but middle, crotch rotting from riding long trips around Saturn's rings).
- **Samantha** — A blindfolded warrior woman with skull accessories, platform heels, and a spiked tail.
- **Chain Fighter** — 80s-style fighter with mohawk, visor, and dual chains. Very retro.
- **Death Car Runner** — Terrified, crying victims being chased by the Death Machine chariot.

---

## 5. Game Mechanics

### 5.1 Player Mechanics

- Movement
	- All characters can move horizontally in a 2d plane by walking forward and backward.
	- Players can jump, but their jump height depends on their character.
- Actions/Abilities
	- Each character has 2-3 attacks as detailed in the character roster above.
- Interaction systems
	- They fight each other
- Progression systems
	- None

### 5.2 Game Systems

- Combat
	- Party brawler style combat mixed with hero shooter style abilities.  Each character has 2-3 attacks.
- AI behavior
	- First MVP will not have AI but we will eventually have the option for 1 player with bots
- Physics
	- Exaggerated physics, lots of bonking with knock backs and stuff.
- Arena Hazards
	- Total Master can activate grid traps during matches: saw blades from walls, flamethrowers from the floor, and caged beasts released from grid cells. These hazards add chaos and can turn the tide of a fight.
- Monitorr System
	- Monitorr serves as the in-game announcer. He appears as a floating head during match intros, countdowns, KOs, and victory screens. He delivers commentary and devours the loser's remains (stock icon consumed on death).

### 5.3 Progression
- Unlockables
	- Unlock different variants of the toys.
	- Unlock non-playable characters as fighters (Total Master, Right Hand Man, Whipmaster General)
- Difficulty curve
	- Certain characters are harder to play than others. Guiding light is easy to learn - very hard to master.

---

## 6. Levels and Environments

**Level Design Philosophy:** The levels will be hand crafted painted scenes.  They will feel like the card stock of a toy. Background reference art inspirations include Scott Wills paintings (Ren & Stimpy backgrounds) — moody, detailed, painterly environments.

**World Structure:** Just arenas, but menus will also be the back of toy boxes that you navigate.

**Key Locations:**

- **The Death Grid Arena** — The main fighting stage. A gladiator pit with cheering crowds, trap doors in the floor, and Total Master's boardroom looming overhead. The grid floor has hidden panels that can spring open to reveal saw blades, flamethrowers, and caged beasts.
- **The Mines** — Underground tunnels where slaves harvest crystals and resources. Dimly lit, dangerous. Mine carts, pickaxes, and cave-ins as hazards.
- **The Sewers** — Where the Resistance hides. Dark, wet, infested with giant rats. Pipes and tunnels as platforms.
- **The Slums** — Where the Piss-ons live. Ramshackle buildings, garbage, drug dens. A desperate place.
- **The Boardroom** — Total Master's luxury viewing box. Could serve as a story mode location or menu backdrop.

**Environmental Challenges:** Arenas will have platforms and pits you can die in. Arena hazards (Total Master's traps) add danger. Giant rats in the sewer stage. Cave-ins in the mine stage.

**Death Grid Playset:** A full 3D playset model exists (`DG_base_rev1.OBJ`, `DG_door_rev1.OBJ`, `DG_Top_rev1.STL`) that could be rendered as a background or used as reference for stage layout.

---

## 7. Art and Audio

**Visual Style:** Characters will look like soft vinyl toys.  Backgrounds will look hand painted.  Menus will look like the back of a cardstock of the gorelords toy.

**Character Design:** The characters will look like the soft vinyl Gorelords toys. We have 3D sculpt models (OBJ/STL) for 17 characters that will be rendered in Blender to create digitized sprites — giving the retro Mortal Kombat "photographed figure" aesthetic. See `docs/sprite-pipeline-proposal.md` for the full asset pipeline.

**UI/UX Design:** Things should feel real and handmade. Monitorr serves as the announcer/narrator for match events.

**Audio Design:** Synthwave music, over the top sound effects, when players hit each other it sounds like plastic crunching. Monitorr speaks electronically (synthesized voice).

**Branding:** Violence Toys logo assets available in `reference/wetransfer_logo_2026-02-12_0235/logo/` — circle and rectangle variants, PSD source files, .ico file. Can be used for splash screen, title cards, loading screens.

---

## 8. Technical Requirements

**Engine:** MonoGame 3.8 DesktopGL + Nez Framework (ECS)

**Minimum Specs:** Potato

**Key Technical Features:** Digitized sprite pipeline from 3D models via Blender rendering. See `docs/sprite-pipeline-proposal.md`.

**Performance Targets:**
- This is a low end game
---

## 9. Monetization (if applicable)

**Business Model:** Pay once model. No DRM.

**In-game Purchases:** Nothing, everything is unlockable in the game.

**Ethical Considerations:** The spirit of the 90s is alive.

---

## 10. Development Timeline

### Phase 1: Foundation (Months 1-2)

**Goals:** Set up project, prove core tech, establish asset pipeline

- Weeks 1-2: Project setup — MonoGame + Nez, folder structure, basic game loop, input handling
- Weeks 3-4: Character controller — movement, variable jump heights, gravity, platform collision
- Weeks 5-6: Combat foundation — hitbox/hurtbox system, basic attacks, health, knockback physics, life stocks
- Weeks 7-8: Asset pipeline — render first toy model in Blender, process sprites, get the digitized look working, basic animation system

**Milestone:** One character with placeholder moveset fighting a clone on a flat test stage.

---

### Phase 2: Netcode & Combat Systems (Months 3-5)

**Goals:** Multiplayer functional, combat feels right

- Weeks 9-12: Netcode implementation with LiteNetLib (or your preferred library) — host/join, sync player positions, handle latency
- Weeks 13-16: Character state machine — idle, walk, jump, attack startup/active/recovery, hitstun, knockback, death states
- Weeks 17-20: Combat polish — input buffering, knockback scaling, frame data, hit confirmation feedback

**Milestone:** Two players fighting online, one character, combat feels good.

---

### Phase 3: Content Build (Months 6-8)

**Goals:** Roster, arenas, full match flow

- Weeks 21-24: Characters 2-4 — render toy models, implement unique movesets, balance pass
- Weeks 25-28: Arenas — 4 hand-painted backgrounds, platform layouts, death pits, spawn points, arena hazards
- Weeks 29-32: UI — main menu, character select, Monitorr announcer system, health bars, life stocks, win/lose screens, toy box aesthetic

**Milestone:** 4 characters, 4 arenas, playable from menu to match end. Alpha build.

---

### Phase 4: Polish & Playtesting (Months 9-10)

**Goals:** Feels finished, plays well

- Weeks 33-36: Audio — synthwave music, plastic crunch hits, Monitorr voice lines, UI sounds, death effects
- Weeks 37-40: Visual polish — hit particles, screen shake, knockback trails, death animations, Monitorr eating losers
- Ongoing: Playtesting with friends, balance tuning, netcode stress testing

**Milestone:** Beta build ready for outside eyes.

---

### Phase 5: Launch Prep (Months 11-12)

**Goals:** Ship on PC

- Weeks 41-44: Bug fixes, optimization, Steam page, trailer, store assets
- Weeks 45-48: Final QA, launch build, release

**Milestone:** PC launch.

---

### Post-Launch

- Patch based on player feedback
- Expand roster — many production-ready models waiting in the pipeline
- Console submissions once PC is stable
- Story mode featuring the Future Axe narrative arc

---

## 11. Team and Resources

**Team Structure:**
- Developer - Cory Finnegan
- Art - Zach Taylor

**Budget:**
- Just our time

---

## 12. Reference Materials

All reference materials are stored in `reference/` (not tracked in git):

- `reference/wetransfer_death-grid-playset_2026-02-04_2223/Gorelords/` — 17 character OBJ/STL models + playset pieces
- `reference/wetransfer_animation_2026-02-12_0313/Animation/` — Character sketches (30+ sheets), storyboards, background reference art, text documents (treatment, character list, shot breakdowns), GIFs and photos (mood board)
- `reference/wetransfer_logo_2026-02-12_0235/logo/` — Violence Toys branding (PSD, JPG, AI, ICO formats)
