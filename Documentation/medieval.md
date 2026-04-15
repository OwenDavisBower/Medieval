# Medieval Sandbox Campaign (Game Idea)

## High concept
Single-player, top-down 3D medieval sandbox that plays like a cross of **Mount & Blade**, **Age of Empires**, and **Rimworld**: start as a lone adventurer, recruit and train troops, trade and fight across a large open world, and ultimately found and manage your own city and multiple armies through hired commanders—while a storyteller keeps the world fresh with dynamic events.

## Reference mix (what each game contributes)
- **Mount & Blade**: campaign travel, army recruitment/progression, sieges, politics, rising from nobody to warlord.
- **Age of Empires**: macro economy/production chains, build priorities/queues, territory value, “where do my resources come from?”
- **Rimworld**: storyteller pacing, emergent problems, character-driven consequences, and replayable event variety.

## Player fantasy
- Rise from nobody to warlord/kingmaker.
- Watch the world change without “scripted rails”: cities grow, fall, and reshape politics and trade.
- Win by strategy and logistics as much as combat.

## Pillars
- **Living world**: settlements and NPCs continuously simulate production, construction, defense, migration, and conflict.
- **Procedural but coherent**: cities, factions, characters, quests, and events are generated from rules + current world state.
- **Meaningful economy**: dynamic supply/demand tied to real production chains and disruption (war, raids, siege).
- **Army command at scale**: from personal squad → multiple armies via commanders (think RTS “control groups” and mission assignments, not constant micro).
- **Mobile-first**: aggressive simulation and rendering LOD so it runs smoothly on phones/tablets.

## Camera & feel
- **Top-down 3D**: readable strategy view on the world map, with optional closer zoom for battles/city scenes.
- **Map-first loop**: travel, decisions, logistics, and time progression are always “in motion.”
- **Grounded historical**: no magic; authenticity via equipment, tactics limits, and plausible politics/economy.

## Core gameplay loop
1. Travel on the world map (time advances).
2. Engage with locations (cities, villages, roads, ruins, camps).
3. Trade / recruit / accept procedurally generated quests.
4. Fight (skirmish, raid, siege, field battle).
5. Gain resources, reputation, and influence → unlock bigger opportunities.
6. Found/capture settlements → set build priorities, production focus, and defense posture.
7. Hire commanders → run parallel operations across the map (patrols, raids, escorts, sieges).
8. Respond to storyteller-driven events that force adaptation (shortages, feuds, invasions, opportunities).

## World map
- **Large overworld** with terrain, roads, rivers, choke points, and regions.
- **Time & seasons** affect travel, harvests, prices, and war.
- **Information is imperfect**: rumors/scouts influence what the player knows (optional difficulty layer).

## Settlements (cities, towns, villages)
### What “functions like a real city” means
- **Population**: households, migrants, births/deaths (simplified), and class roles.
- **Jobs/roles**: farmers, miners, loggers, builders, guards, artisans, traders, officials.
- **Daily routines**: work, rest, patrol, transport goods, react to threats.
- **Needs**: food, tools, shelter, security, wages, morale.
- **Growth/shrink**: driven by safety, prosperity, and available housing/jobs.

### Settlement lifecycle
- **Created**: new hamlets/encampments can appear (frontier expansion, refugees, player-founded).
- **Captured**: ownership changes shift laws/taxes/guards and can cause unrest.
- **Destroyed**: starvation, sacking, fire, plague (optional), or deliberate razing.
- **Rebuilt**: ruins can be reclaimed depending on region stability and resources.

### Procedural city generation
- **City plan**: walls, gates, districts, main streets, market, keep, workshops, docks (if river/coast).
- **Districts**: residential, market, crafts, military, noble, slums (emerge from inequality and crowding).
- **Buildings**: generated from district needs and local resources (wood/stone/iron availability).
- **Landmarks**: a few unique anchors per city (cathedral, arena, grand forge) for identity.

## NPCs & factions
- **Powerful characters** (lords, guildmasters, mayors, bandit kings, merc captains) are persistent agents with:
  - goals (expand, profit, revenge, protect, reform),
  - resources (wealth, troops, influence),
  - relationships (alliances, feuds, debts),
  - traits (honor, greed, caution, charisma).
- **Factions**: kingdoms, city-states, guilds, religions, outlaw networks; they wage war, trade, and negotiate.

## Economy & trade (dynamic)
- **Goods & chains**: raw → processed → finished (grain → flour → bread; ore → ingots → weapons).
- **Prices**: supply/demand + risk + taxes + distance + seasonality.
- **Caravans**: AI traders move goods; bandits/war disrupt routes; escorts matter.
- **Player trading**: profit via arbitrage, contracts, monopolies (e.g., control iron supply), and wartime shortages.
- **City budgets**: taxes fund guards/building; corruption/unrest can siphon or reduce efficiency (optional).

## Army building & progression
- Start with **one character** (player).
- Recruit from cities/villages/merc camps; morale and wages matter.
- Troops **level up** through training and combat; injuries and desertion are possible.
- **Logistics**: food, horses, ammo/supplies; overextending weakens armies.

## Combat (high-level direction)
- **Encounters** initiated from world map: ambush, skirmish, raid defense, field battle, siege.
- **Battle objectives**: break morale, hold points, protect wagons, breach walls, capture leader.
- **Simulation detail**: individuals fire arrows and swing weapons; hits resolve as character-vs-character damage (with armor/shields/morale).
- **Low micro**: choose army layout/formation doctrine pre-battle (lines, skirmishers, cavalry wings, reserve), then minimal control during the fight (pause + a few coarse commands optional).
- **Commanders matter**: tactics are mostly expressed through commander traits/doctrine rather than constant player input.

## City founding & management (player)
- **Found** a settlement: pick site, lay out districts, establish laws/tax, recruit settlers.
- **Build**: housing, farms, workshops, walls, garrison, storage.
- **Defend**: patrol radius, watchtowers, militia training, diplomacy/tribute.
- **Govern**: resolve disputes, manage shortages, handle unrest, respond to events.

## Commanders & multi-army play
- Hire/promote commanders with traits and skills (tactics, logistics, diplomacy, scouting).
- Assign **missions**:
  - patrol region, escort caravan, raid target, besiege city, defend ally, explore, recruit, train.
- **Autonomy**: commanders interpret orders based on doctrine and personality; outcomes feed back into world state.
- **Reporting**: periodic updates, map pings, resource transfers, and requests for help.

## Quests & story (procedural sandbox)
- **Quest seeds** come from world state + powerful characters’ goals:
  - succession crisis, trade dispute, bandit suppression, border war, escort, sabotage, hostage, debt collection.
- **Quest arcs**: multi-step chains that adapt if cities change hands or key NPCs die.
- **“Story”** is the emergent narrative of factions and the player’s rise; curated templates keep it dramatic.

## Systems that make the world feel alive
- **Storyteller (Rimworld-like)**: a pacing/variety director that schedules random events/encounters based on tension, recent wins/losses, region danger, and faction agendas.
- **Event engine**: generates conflicts, festivals, famines, coups, feuds, marriages, treaties, ambushes, caravan opportunities, and sudden political shifts.
- **Simulation ticks**: daily/weekly updates for production, construction, population, AI movement.
- **Stability model**: prosperity vs unrest; safety vs threat; legitimacy vs rebellion.

## Mobile performance targets (design constraints)
- **LOD simulation**: off-screen NPCs become aggregated “households/companies”; distant battles become coarse simulations; full agents only near the player or in high-impact locations.
- **Limited active agents**: cap concurrent fully simulated characters; use pooling and deterministic ticks.
- **Battery-friendly**: configurable tick rate, simplified physics, and conservative VFX.
- **UI-first**: large touch targets, quick panels for trade/recruit/assign missions, and readable map layers.

## Win/lose & long-term goals (sandbox-friendly)
- No hard “end”; optional milestones:
  - become a renowned mercenary captain,
  - control a trade empire,
  - unite regions under one banner,
  - build the greatest city in the world.
- Fail states: death/permadeath (optional), bankruptcy, total loss of forces/settlements (recoverable if desired).

## Minimal viable version (MVP)
- World map travel + time progression.
- 3–5 settlements with basic production + dynamic prices.
- Recruit troops, simple battles, bandits + one rival faction.
- One player-founded settlement with basic build/defend loop.
- Basic storyteller that triggers a few encounter/event templates (ambush, shortage, rival demand, caravan chance).

## Big risks (design/tech)
- **Simulation cost**: many NPCs/settlements can get expensive; needs level-of-detail (LOD) simulation.
- **Procedural city believability**: requires strong constraints and “identity” hooks.
- **Economy exploits**: needs sinks, friction, and AI competition.
- **AI commander reliability**: autonomy must be understandable; good UI for intent and outcomes is critical.
- **Mobile constraints**: memory, thermal throttling, and battery can force hard limits on unit counts, effects, and update frequency.

## Open questions (to decide later)
- Battle mode: fully tactical real-time vs abstracted/auto-resolve with intervention?
- How detailed is “city interior” vs a strategic management view?
- Death/legacy: single character only, or dynasty/heirs?
