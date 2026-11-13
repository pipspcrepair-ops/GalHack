# Tech tree pacing: no empty level bands

Research note for the owner. Not committed. Reference only: nothing here is copied from Empyrion, Reforged Eden 2 or Star Salvage Reborn, and GalHack's design doc wins wherever it differs from them. Names in the proposal are GalHack's own.

How to read the confidence marks: **Measured** means I read it from a file on this machine. **Reported** means a web page, an in-game text mirror or a player said it, and I could not check it against files. Where I opened the page myself I say "read directly" and give the URL; where I only saw a search summary I say "search summary only". **Inferred** means my own conclusion.

Sources used are listed at the end.

---

## 1. Audit: GalHack's unlocks today

**Where the data lives (Measured).** There is no separate tree file. Each node sits on an item or block. `prototypes/slice/data/progress.json` holds the level curve and points. `shared/Items/data/blocks.json` holds block nodes (a node per grid: small and large). `shared/Items/data/items.json` and `prototypes/slice/data/items.json` hold item nodes (ammo, fuel, tools, the hover and capital cores). Deco sets in `shared/Items/data/deco.json` unlock by set, on a `deco` tab.

**Level curve (Measured).** The file defines levels 2 to 25, so the cap is 25. Points granted climb from 2 to 6 per level and add up to 104 by level 25. All vessel, tool, weapon and misc nodes together cost 133 points (deco sets cost more on top, not counted), so even today the whole tree is not open at the cap unless points keep flowing past it (1 point per 1,500 xp past the cap). The owner's "everything by max level" rule means the point total has to be re-tuned; section 3d does that.

**Tabs today (Measured).** The code has eight tabs: base, capital, small, hover, tools, weapons, deco, misc. The owner first decided on two vessel tabs, Base & Capital and Small & Hover, and this audit merges base+capital and small+hover to match. With the move to one 1 m grid (`docs/one-metre-grid.md` section 5) the two became one **Structures** tab, one tree from the core; the proposal in section 3 is redone for it. Weapons, tools, misc and deco stay separate tabs.

### 1a. What unlocks at each level

Nodes are counted once per grid. "Large" = Big Core grid (base and capital), "small" = Small Core grid (small and hover). Where a block appears on both grids at a level, it is listed once.

**Base & Capital (large grid, tabs base and capital)**

| Level | New nodes |
| --- | --- |
| 1 | Base core, steel, glass, concrete, generator, fuel tank, container, ladder, light (nine starter nodes, all free) |
| 2 | Door, small fabricator |
| 4 | CPU extender, deconstructor |
| 6 | Hardened armour |
| 7 | Capital set: chair, landing gear, gyro, thruster (plus the capital core, from the slice data) |
| 8 | Large fabricator |
| 10 | Factory (blueprint factory), large-grid hover block |
| 12 | Combat armour, warp drive, warp tank |
| 15 | Advanced fabricator, fold tank, hyperdrive 1 |
| 20 | Precursor armour (found-only alloy), hyperdrive 2 |

**Small & Hover (small grid, tabs small and hover)**

| Level | New nodes |
| --- | --- |
| 1 | Hover plate block, hover chair (plus the hover core, from the slice data) |
| 5 | Small core, steel, glass, ladder, light, generator, fuel tank, container, landing gear, gyro, thruster (eleven nodes at once) |
| 6 | Concrete, door, small fabricator |
| 7 | CPU extender, deconstructor |
| 8 | Hardened armour |
| 10 | Large fabricator |
| 12 | Factory, warp drive, warp tank |
| 14 | Combat armour |
| 16 | Advanced fabricator |
| 20 | Precursor armour |

**Other tabs (Measured)**

| Tab | Levels with something new |
| --- | --- |
| Tools | 1 (survival tool), 2 (hover bike), 4 (mining drill, paint), 8 (plasma drill). Nothing after 8. |
| Weapons | 2 (pistol rounds), 6 (small gatling, belt), 8 (large turret, shells), 9 (large gatling, small rocket pod, rocket), 10 (small turret), 11 (large rocket pod), 15 (artillery shell), 20 (plasma charge). Nothing after 20. |
| Misc | 1 (O2 bottle, energy cell, survival constructor), 2 (medkit, biofuel), 3 (burnstone pack, portable fabricator), 8 (large fuel pack), 9 (warp cell), 14 (fusion cell). Nothing after 14. |
| Deco | Sets at 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 15, 20. Cosmetic: it does not count towards filling the vessel gaps. |

### 1b. Empty bands, per tab

A band is a run of levels where that tab offers nothing new. The last row of each table is the biggest: levels 21 to 25 are empty everywhere because the data ends at level 20.

**Base & Capital**

| Empty levels | Length |
| --- | --- |
| 3 | 1 (the portable fabricator is on Misc) |
| 5 | 1 |
| 9 | 1 |
| 11 | 1 |
| 13 to 14 | 2 |
| 16 to 19 | 4 |
| 21 to 25 | 5 |

**Small & Hover**

| Empty levels | Length |
| --- | --- |
| 2 to 4 | 3 (a long wait for a player who built the hover vessel at level 1, only the hover bike on Tools and the portable fabricator on Misc arrive) |
| 9 | 1 |
| 11 | 1 |
| 13 | 1 |
| 15 | 1 |
| 17 to 19 | 3 |
| 21 to 25 | 5 |

**Across all non-cosmetic tabs combined (vessels, tools, weapons, misc)**

Only levels 13 and 17 to 19 are empty in every tab before the data ends (level 16 has the small advanced fabricator). Everything from 21 on is empty everywhere. So today's tree is not just patchy, it is over at level 20.

### 1c. Other problems the data shows (Measured, then Inferred)

- **Lumps (Measured).** Level 1 has nine large-grid nodes and level 5 has eleven small-grid nodes. A lump is one reason a tab then goes quiet: the whole set arrives together and the next tier is three levels away.
- **Tiers are thin (Inferred).** Most categories have only two or three tiers (armour has four, including a found-only one). Once the tiers are used up, the category cannot fill later levels. Thrusters, generators, shields and CPU extenders have one tier each today, so the late game has nothing to upgrade.
- **Late nodes cluster at the top of the old cap (Measured).** Hyperdrive, advanced fabricator, precursor armour and plasma ammo all sit between 15 and 20, so they need successors to fill levels 21 to 25.
- **The one-tab rule.** Merging every vessel tab into Structures is a UI and data change: one core (no Big Core or Small Core), each device one tier chain, and today's small and large parts become rungs of it.

---

## 2. How the references pace it

### 2a. Vanilla Empyrion (Measured, from the local install, read-only)

I counted nodes that carry an unlock level in the shipped block and item config files (`Content/Configuration/BlocksConfig.ecf` and `ItemsConfig.ecf`, build 1.19.6), skipping nodes marked hidden. Counts are approximate because the files mix commented-out lines and a few shared definitions.

- **Level cap is 25 (Measured from the file range; Reported by players on the forum).** The distinct unlock levels used are 1, 3, 5, 7, 8, 10, 12, 15, 17, 20 and 25. Only eleven of the 25 levels hold anything.
- **How many nodes per band (Measured, rounded).** Blocks: about 90 at level 1 (most of them free starter blocks with no tab), about 27 at 3, 23 at 5, 28 at 7, 30 at 10, 21 at 12, 16 at 15, 3 at 17, 22 at 20, 6 at 25. Items (tools, weapons, consumables) are far smaller: a handful at each of 1, 3, 5, 7, 10, 12, 15, 20 and 25.
- **Spacing (Measured).** Gaps between unlock levels grow as the game goes on: every 2 levels at the start (1, 3, 5, 7), then 3, 2, 3, then 5 and 5 at the end (15 to 20 to 25). The vessel tabs hold their last big drops at 20 and 25, so levels 16 to 19 and 21 to 24 are long quiet stretches on the base and capital tab. Small vessels get a little extra at 17, which is the only mid-gap filler.
- **Early, middle, late (Inferred from the above).** Early (1 to 7): nearly everything a survivor needs, spread over four unlock steps, and the vessel classes (hover at 1, small at 5, capital at 7) arrive at odd levels two apart. Middle (10 to 15): the second tier of each device (more power, armour, constructors, turrets). Late (20 to 25): top-tier capital-ship systems, heavy weapons, the best armour.
- **What it does well (Inferred).** The odd-level cadence in the early game keeps a player always one level from something new. Lumping many nodes on one level is cheap to author and gives a "new chapter" feel.
- **What it does badly (Inferred).** At the end it goes silent for four to five levels at a time, and level 25 is a single large drop. Player reports say the cap leaves people short of points or short of ideas (Reported, forum and Steam threads, conflicting), which is why scenarios stretch it.
- **Other sources of progress (Reported).** Quest rewards give unlock points. The wiki lists points per tier; the base tree totals about 580 points (Reported, wiki). I did not verify this against the files.

### 2b. Reforged Eden 2 (read from pages this time; Reported)

Not installed here. Revised after a second research pass: the built-in browser opened the pages the first pass could not. Every item below says whether I read it on the page itself or only saw it in a search summary. Everything is player or developer talk or in-game text, not files, so the label is **Reported**, not Measured. Two things to know first: the Workshop item is called "Reforged Eden 2 Beta" (it ran as alpha, then beta builds; a data site I read lists build 46 on game version 1.19.2), and much of the best player talk is from 2021 to 2022 and is about the first Reforged Eden. I mark that where it matters.

**Level cap and what a level gives**
- **Cap is 25 (Reported, read directly).** The in-game encyclopedia text, as mirrored by a companion site, says you go from level 1 to 25, that each level grants a set number of unlock points (higher levels grant more), and that level changes nothing about the player except which tree items can be unlocked. https://empyrionbuddy.com/pda/player-level-eden_pda_0cIqKe8
- **XP stops at 25 in the first Reforged Eden (Reported, read directly, 2021).** A player there asked how to get points after 25 because XP no longer came in. https://steamcommunity.com/app/383120/discussions/0/3169946351455174551/ Whether that holds in the beta I did not see stated.

**How unlock points are earned (Reported, read directly)**
- The in-game text lists five sources: levelling, mission rewards, turning in items (undelivered cargo, insignias, Progenitor data chips, data pads), hacking, and a repeatable research mission that pays out every few hours. https://empyrionbuddy.com/pda/unlock-points-eden_pda_40O46Ga
- Players put numbers on these in 2021 and 2022 (data pads 100 or 50 each, lost cargo 25, research mission 250 every three hours, crafted research reports 100 once per research ship). The numbers disagree between posts, so treat them as rough. https://steamcommunity.com/app/383120/discussions/0/3169946351455174551/ and https://steamcommunity.com/app/383120/discussions/0/3482997257348124890/
- **Correction to my first pass:** I had said "research points" from encrypted data chips fund unlock points. A player on the beta's own discussion board says research points are a separate thing that opens the Progenitor Key, that data pads are what feed tree unlocks, and that no Progenitor space exists in the beta yet (December 2024). https://steamcommunity.com/workshop/filedetails/discussion/3143225812/4638240954399120287/ The in-game text above does list Progenitor data chips as a turn-in that pays unlock points, so the two accounts do not fully agree. I can't settle it.

**Is levelling enough to open the whole tree? (conflicting; the point matters to GalHack)**
- A beta thread (May 2025) asks whether there are enough points to unlock everything. A player answers "yes and no": not enough from levels alone, unlike vanilla, but plenty of other sources. Another says they never ran out in thousands of hours. The scenario's developer replied pointing at the in-game text. https://steamcommunity.com/workshop/filedetails/discussion/3143225812/813573650210881773/
- A March 2025 poster (necro on a vanilla thread) says they were at max level and still short of CV and base unlocks. https://steamcommunity.com/app/383120/discussions/0/3335371283869021230
- The in-game tree text says you cannot undo a choice but "can eventually get enough unlock points to unlock everything". https://empyrionbuddy.com/pda/tech-tree-eden_pda_4DCiOKY
- **Reading (Inferred):** in Reforged Eden 2 the whole tree is reachable, but only if you also do side activities. That is a different decision from GalHack's "everything by max level".
- Players in 2022 describe the price as steep: one new player found a single late item costing over a thousand points (Reported, 2022, first Reforged Eden). https://steamcommunity.com/app/383120/discussions/0/3482997257348124890/

**Two gates per item, not one (Reported, read directly)**
- Every item has a level requirement and a point cost, and you must open the parent node first. The tree has several tabs, and an item can appear in more than one tab but only needs opening once. Unlocking a weapon unlocks its ammo. https://empyrionbuddy.com/pda/tech-tree-eden_pda_4DCiOKY
- Some items also need a physical schematic: found in loot (often in Progenitor data caches), bought from traders, or handed in at a research vessel; you still need the level and points. https://empyrionbuddy.com/pda/schematics-eden_pda_8S5qYiO The changelog shows a "Schematic Unlocks" tab being added so players can see which they hold.
- **Inferred:** because points and levels both gate an item, the tree can be paced by cost as well as level. A player who is level-ready but short of points is waiting on activity, not on a level, so no band feels dead.

**How the spread looks by level (Reported)**
- One player's strategy guide (undated; written against builds 39 to 44) names six key levels: small vessels at 7, capital vessels at 10, warp drive for capital vessels at 12, a compact shield generator at 15, full shield generator, tool turret and medium turrets at 20, and the laser rifle at 25. https://steamcommunity.com/sharedfiles/filedetails/?id=3657911391 A 2021 post on the first Reforged Eden gives mechanical drills for small vessels at 7, laser drills for small vessels at 15, and for capital vessels at 20. https://steamcommunity.com/app/383120/discussions/0/3169946351455174551/
- So the headline gates sit at about the same levels as vanilla (7, 10, 12, 15, 20, 25) with gaps of 2, 3, 5 and 5. **This is not an every-level tree.** I did not count every node, so I can't say how many small items fill the gaps.
- The same guide says levelling needs no extra effort except at 10 to 12 and 15 to 20, where the author recommends deliberate XP mining. https://steamcommunity.com/sharedfiles/filedetails/?id=3657911391 (Reported, one player.) The felt slow stretches line up with the long gaps between headline gates.
- The same 2021 post says players tend to leave the start planet at levels 12 to 15.

**How it fills a long tree (Reported, read directly from the changelog)**
- **New devices get chained under existing nodes.** Many changelog lines say a new device is "unlocked in the tech tree after" some other device. https://steamcommunity.com/sharedfiles/filedetails/changelog/3143225812 (several pages)
- **Merging nodes to cut cost and clutter.** An alpha build merged small-vessel jet thrusters into their parent blocks so fewer points are needed and "the tech tree looks like less of a disaster", and adjusted the hydroponics bay costs. https://steamcommunity.com/sharedfiles/filedetails/changelog/3143225812?p=7
- **Items can show up on several vessel tabs** (matter transceivers, flares and ventilators were moved from Misc into the vessel tabs). The same changelog, page 4.

**Praise and complaints about pacing (Reported)**
- Praise (January 2023, first Reforged Eden): a player says vanilla's research tree is too small for how quickly you reach its end, while Reforged Eden extended it and ties further research to endgame missions that feel earned. https://empyriononline.com/threads/a-call-from-from-reforged-eden-to-egs.101076/
- Praise (May 2025, beta): a player says getting points by doing varied activities "gives you enough reason" to do them. https://steamcommunity.com/workshop/filedetails/discussion/3143225812/813573650210881773/
- Complaint (August 2025, beta): a player asks for an endgame goal because they struggle to stay invested; the developer lists several tasks and says more boss encounters are planned. https://steamcommunity.com/workshop/discussions/18446744073709551615/84031737849493636/?appid=383120 So even with a long tree and extra sources, the top of the game still reads as thin to some.
- Vanilla complaint context (forum, 2016): two players said progression felt fast, with spare points once the useful choices were made. https://empyriononline.com/threads/experience-points-player-level-progression-and-tech-trees.3082/page-20

**Not found:** a per-level count of unlocks, any statement of a "dead" level band, or the beta's point total. I did not see level numbers beyond 25.

### 2c. Star Salvage: Reborn (read from pages; Reported)

"Star Salvage: Reborn" is a Workshop scenario that is a maintained revival of the original Star Salvage by KnowItAllDM (version 1.13 or later). https://steamcommunity.com/sharedfiles/filedetails/?id=3409399573 Its Nexus Mods mirror lists version 1.13.1 and a last update of 31 August 2025. https://www.nexusmods.com/empyriongalacticsurvival/mods/155 Not installed here, so I have no config data. The original scenario's own Workshop page was rate-limited when I tried it, so I did not read it.

**How progress is built (Reported, read directly)**
- The tree is organised by four specialisations (Combat, Engineering, Exploration, Medical). Missions drive it; you "cross-specialise" for more. https://steamcommunity.com/sharedfiles/filedetails/?id=3409399573 and the Nexus page above.
- A player's explanation in the pinned difficulty thread (February 2025; a player, not the author): you follow starter missions and pick one specialisation. At rank 3 you can take advanced training that pays 500 unlock points or opens a second specialisation; the same choice returns at rank 5, and picking another specialisation repeats the cycle from that branch, so all trees can be opened "but it takes time". On the non-story starter planets, specialisation choices come automatically at set levels instead. https://steamcommunity.com/workshop/filedetails/discussion/3409399573/592887031601832968
- A bug report in the pinned bug thread mentions Exploration rank 9, so ranks run to at least 9 in one line. https://steamcommunity.com/workshop/filedetails/discussion/3409399573/592887031601896921
- A player in the same thread complains that in the original version, "whatever tech you start with that's it, no matter how many points you have" (the specialisation locks looked like a bug). They were told it is by design. Same thread. **Reading (Inferred):** a branch lock can read as "my points do nothing" if the player does not know the unlock path.
- **Level cap and per-level unlocks: not found.** Nothing I read states them. The tree is framed by missions and ranks, not levels, in every page I read.

**Is "not much after the first ten hours" a pattern? (Reported, read directly; the answer is yes, with a cause)**
- **The comment you flagged (9 October 2025).** One player says they enjoyed it but "there's not a whole lot of meat to it after the first 10ish hours". A maintainer answers that the scenario is being kept alive as is, and the original author says he will return at some point. https://steamcommunity.com/sharedfiles/filedetails/?id=3409399573 (comments)
- **A second signal (March 2025).** A player reached a story dead end: a governor with blank dialogue, asking if this is the end or a bug. https://steamcommunity.com/workshop/filedetails/discussion/3409399573/604150365670092752/ A bug thread also records a blank governor line at Exploration rank 9. See the pinned bug thread above.
- **A third, from the maintainers (2025).** Asked if the whole galaxy is salvage, a maintainer says the scenario "is only developed until a certain stage", with a plan to close "chapter 1 of at least 4". https://steamcommunity.com/workshop/filedetails/discussion/3409399573/595158831570616067/
- **What it tells us (Inferred):** the late pacing problem here is mission content running out, not tech-tree spacing. Because unlocks are tied to missions and ranks, the tree is exactly as long as the authored missions are. If the missions stop, so does progress.
- **Not pacing, but related:** an early-game complaint that waves of drones hit too hard right at the start (author acknowledges it, https://steamcommunity.com/workshop/filedetails/discussion/3409399573/592887273544050363/) and a player who quit over junkyard defence drones (https://steamcommunity.com/workshop/filedetails/discussion/3409399573/604150170601415046/). Those are difficulty, not unlock pacing.
- I did not read the Reborn change notes, the capital vessels thread, or the original scenario's comments, so I can't say whether more complaints exist.

### 2d. Summary

| | Vanilla Empyrion | Reforged Eden 2 | Star Salvage Reborn |
| --- | --- | --- | --- |
| Basis | Measured (local files) | Reported, read directly (in-game text mirror, Workshop threads, changelog, one player guide) | Reported, read directly (Workshop page, pinned thread, comments, discussion list) |
| Level cap | 25 | 25 | Not found |
| Unlock-point sources | Levels, quests | Levels (more at higher levels), missions, turn-ins, hacking, a repeatable research mission | Levels or mission ranks; 500-point advanced-training choice at rank 3 and 5 |
| Gap pattern between headline gates | 2, 3, 2, 3, then 5, 5 | Same headline levels as vanilla (7, 10, 12, 15, 20, 25) per one guide; item-level counts not seen | Not level-based; rank-based |
| Does levelling alone open everything? | Yes (players say so) | No, but the in-game text says everything is reachable with other sources | Not stated; choice of specialisation shapes it |
| Fills gaps with | Early lumps, quest points | Extra point sources, point cost on top of level, schematics, chained and merged nodes | Specialisations and missions |
| Late-game complaint | Few levels left (older threads) | Wants endgame goals (2025); some short of points | Little content after the first hours; story ends early |
| Confidence | High | Medium (many pages read; no per-level counts) | Medium-low (one pinned thread, one comment run, no config) |

**What this changes for GalHack (Inferred):**
1. Neither reference spreads unlocks across every level. They use a handful of headline gates and cover the gaps with point cost and side sources. GalHack's "something new at every level" rule is stricter than anything I found, and that is fine: the owner's goal is no empty bands, and nothing in the evidence argues for thinning it.
2. The late-game risk is not the tree, it is the top of the game having nothing to do. Both scenarios show players stalling at the end for content reasons. A level map that reaches the cap should have capstone unlocks tied to something to do, not a number only.
3. Do not borrow the "not enough points from levels" choice. It conflicts with the owner's decision. Where the references add side sources, GalHack can add them as accelerants without making them required.

---

## 3. Proposal

### 3a. Spacing rule (Inferred; a design suggestion, not from the references)

1. **No level is empty on Structures or Weapons.** Each of the two has at least one new node on every level from 1 to 25. The map needs no exceptions, so none are allowed. Hard rule, checkable by a test.
2. **Tools and Misc may rest, but only for two levels in a row.** They're small tabs and can't fill every level without padding. No level is quiet across the whole tree anyway, because rule 1 holds on two tabs.
3. **About ten nodes a tab a level.** With 25 levels, each level has to carry more, so the limit is ten new nodes on one tab at one level. Level 1 and the first level of a new vessel class may go over, because a starter set is cheap and most of it is free. Only one level does: Structures has 23 at level 1, and 14 of those cost nothing.
4. **Every level offers more than the points can buy.** At each level from 2 to 24, the cost of everything open to you and not yet bought is more than the points you have, even if you've spent every point so far. So each level-up is a choice of what to take first. Level 25 closes the gap. A rule that each level's own new nodes must cost more than its own grant can't hold on every level, because those grants would then never add up to the tree's cost. So the test uses the running total, and the own-level version holds through most of the climb (section 3d).
5. **The whole tree opens by level 25 from levels alone.** Total points granted by level 25 must be at least the total cost of the tree, deco included. Finds, missions and past-cap points only speed the player up.
6. **Every level is a real step.** A level always has at least one node that changes what you can do, not only a bigger number, on Structures or Weapons. A tier ladder never waits more than 8 levels for its next step.
7. **Slower early progress is about xp, not about node spacing.** The cadence stays one or more new nodes a level, so the longer waits feel purposeful.
8. **In the last five levels a tier can be a variant, not a new block:** an upgraded output, a bigger size, a quieter drive. That is cheap to author and still counts.
9. **The top of the game needs goals, not only levels.** Players of both reference scenarios stall at the top for lack of things to do, and Star Salvage Reborn's tree is exactly as long as its authored missions (Reported, sections 2b and 2c). The design doc keeps every tree node open by level and points alone, so this rule adds no extra gate to the tree. Cyber modules help here without being a late-game item: they sit outside the tree, unique alien equipment gained by discovering and consuming a module. They're hard to get rather than level-gated, so a bold player can reach one early and there's still something to chase at the top.

How this relates to the evidence: the references do not space unlocks level by level (they use a few headline gates 2 to 5 levels apart and fill the gaps with point cost and side activities), so rule 1 is stricter than the references. That is the owner's aim, and no evidence argued for loosening it. Rule 4 borrows the point-cost idea from Reforged Eden 2: a player who is level-ready but short of points has a reason to keep going, and no band feels dead. Rule 7 is backed by the one player guide I read, which says the slow stretches in Reforged Eden 2 are the long gaps (levels 10 to 12 and 15 to 20), where players had to chase XP on purpose (Reported, one player).

### 3b. Level map, 1 to 25

Marks: **E** is in the data at that level (`shared/Items/data/blocks.json` for Structures), **N** new. Nothing that exists moves. Names for new entries are descriptive placeholders for the owner to rename; nothing here is taken from any reference game. Weapon codes are the entry codes in `docs/weapon-ideas.md`, and section 9 of that file holds the same list with each node's cost and the recipes it opens.

| Lv | Structures | Tools | Misc | Weapons |
| --- | --- | --- | --- | --- |
| 1 | Core, Base block, Window block, Stairs, Iron plated, Concrete plated, Glass plated, Command chair, Cockpit T1 and T2, Generator T1 to T3, Fuel tank T1 to T3, Container T1 to T4, Light, Ladder, Hover plate T1 (E) | Survival tool, Hover bike, Trail ration, Patch kit, Air cell (E) | Survival constructor, O2 bottle, Energy cell (E) | BW1 Cord Sling, ME1 Work Knife, PS1 Crash Ward (N) |
| 2 | Door, Fabricator T1 (E); Spotlight (N) | Chainsaw (N) | Medkit, Biofuel (E) | Pistol rounds (E); SA2 Tack Pistol, GR1 Stick Bomb, FL1 Fire Bottle, TE3 Bola Thrower, JK2 Saw Thrower (N) |
| 3 | Portable fabricator (E); Replicator T1, Harvester module (N) | Multi-tool T1 (N) | Burnstone pack (E) | MG1 Hand Organ, BW2 Hunter's Bow, RK1 Fire Rack, MN1 Powder Keg, LT1 Dynamo Snapper, CH3 Reek Pot, LV1 Wasp Jar (N) |
| 4 | Deconstructor (E) | Mining drill, Paint (E) | Survival tent, Light suit (N) | SM1 Stamp Gun, SG1 Scatterbuss, FL4 Tallow Sprayer, ME4 Maul (N) |
| 5 | Thruster T1, Gyro T1, Landing pad T1 (E); Medical bay, Solar panel (N) | Hand detector (N) | Insulation booster (N) | RF1 Long Arm, BW4 Rack Bow, GL1 Stovepipe, AR1 Iron Bombard, DF1 Bull Shield, HV9 Ram Spike (N) |
| 6 | Hardened plated (E); Scanner (N) |  | Weapon bench, Portable heater and cooler (N) | MG6 Chatter Gun, small mount, 12 mm belt (E); SG2 Pump Scattergun, ME6 Shock Baton, PS2 Mesh Ward (N) |
| 7 | Thruster T2 and T3, Gyro T2, Landing pad T2 to T4 (E) |  | Medium suit, Emergency O2 and water unit (N) | SA3 Six-Chamber, RF2 Line Rifle, GR2 Frag Ball, MN3 Trip Line, AC1 Scrap Coil, FL5 Pressure Thrower, SU1 Patch Gun (N) |
| 8 | Fabricator T2 (E) | Plasma drill (E) | Large burnstone pack (E); Armour locker (N) | HV3 Deck Gun, large mount, 40 mm shells (E); SM2 Corridor Gun, BW6 Pump Gun, GL2 Break Launcher, TE1 Harpoon Rig, MI1 Dread Horn, BT1 Breaching Pipe, PS10 Ward Cell (N) |
| 9 | Shield generator T1, Capacitor T1, Vessel drill T1 (N) |  | Warp cell (E); Jetpack booster (N) | MG6L Chatter Gun, large mount, RK10 Rocket Pod, small mount, Rockets (E); SA4 Glint Pistol, RK3 Shoulder Tube, LS1 Cutter Beam, CH1 Etch Sprayer, LV10 Bile Pod, DF4 Flare and Chaff Pod, PS6 Quick Ward (N) |
| 10 | Hover plate T2, Gyro T3, Blueprint factory (E); Cloning bay (N) | Hijack Tool T1 (N) | Oxygen booster (N) | HV3S Deck Gun, small mount (E); LG2 Stalking Rifle, GR4 Clinger, AR2 Knee Mortar, MN13 Prox Mine, LT2 Wire Biter, ME8 Piston Fist, MA1 Sentry Stalk (N) |
| 11 | Replicator T2 (N) | Multi-tool T2 (N) | EVA booster, Multi-charge (N) | RK10L Rocket Pod, large mount (E); SG4 Drum Sweeper, MG2 Squad Belt, FL10 Drag Flame, LV2 Hornet Hive, HV1 Drum Cannon, EW1 Paint Lamp, JK1 Junk Cannon, PS3 Field Ward (N) |
| 12 | Combat plated, Warp drive, Warp tank (E) | Suit scanner (N) | Heavy suit (N) | RF4 Long Glint, GL4 Gum Launcher, SN1 Basslance, CD2 Frost Lance, SK5 Heat Dart, SU3 Weld Beam (N) |
| 13 | Capacitor T2 (N) |  | Armour booster (N) | SM4 Pingback Carbine, GR7 Flash Cracker, RK4 Lock Tube, LS2 Stutter Lamp, LT6 Skycall Rod, CH5 Tangle Gun, HV2 Chain Cannon, HV4 Flak Burst, PS7 Bulk Ward (N) |
| 14 | Comms array T1 (N) |  | Fusion cell (E); Radiation booster (N) | LG3 Bench Gun, MN6 Remote Satchel, AC3 Stage Rifle, FL6 Jelly Thrower, LV6 Wormspitter, MA3 Crawl Mine, PS12 Ward Post (N) |
| 15 | Generator T4, Fuel tank T4, Thruster T4, Fabricator T3, Hyperdrive T1, Fold tank (E); Hover plate T3 (N) | Drill T3 (N) |  | Artillery shells (E); MG4 Spin Gun, AR5 Fortress Howitzer, GV1 Shove Cannon, DF3 Pop Dome, HV5 Torpedo Tube, BT2 Quake Spike (N) |
| 16 | Teleporter, Shield generator T2 (N) |  | Mobility booster (N) | PB3 Static Cannon, LT3 Fork Gun, ME10 Hum Blade, SK1 Bend Rounds, EW2 Jam Pod, PS4 Line Ward (N) |
| 17 | Vessel drill T2 (N) | Hijack Tool T2 (N) |  | SG5 Shardcaster, GL7 Hop Launcher, RK11 Sky Battery, AC6 Fighter Rail, FL12 Gate Jet, DF6 Needle Curtain, NU1 Hand Bomb (N) |
| 18 | Warp drive T2 (N) |  | Large medkit (N) | GR8 Pulse Ball, LS3 Scorch Line, LT4 Storm Rope, PS8 Snap Ward (N) |
| 19 | Auto-miner, Replicator T3 (N) |  | Multi booster (N) | SA6 Three-Step Pistol, AC4 Rail Lance, SN3 Resonator, LV7 Sand Swimmer, GV3 Well Launcher, BT7 Demolition Bore (N) |
| 20 | Thruster T5, Hyperdrive T2, Precursor plated (E); Comms array T2, Hover plate T4 (N) | Drill T4 (N) |  | Plasma charges (E); RK8 Flock Launcher, MN9 Picky Mine, PL2 Ember Rifle, PL5 Strip Battery, ME11 Light Blade, SK3 Needle Swarm, HV6 Overload Torpedo (N) |
| 21 | Capacitor T3 (N) |  |  | MG5 Lamp Rotary, LS9 Lance Array, LT9 Coil Tower, CD4 Still Beam, TM1 Slow Bubble, DF7 Thread Laser, SU2 Mender Dart (N) |
| 22 | Charged plating (N) |  | Booster set T2 (N) | RF5 Dial Rifle, LS6 Cook Box, PB2 Marrow Beam, FL8 Star Gel, PH1 Swap Rifle, MI5 Confusion Lens, DS1 Ash Ray, PS5 Lattice Ward (N) |
| 23 | Shield generator T3, Fold gate, Warp drive T3 (N) | Hijack Tool T3 (N) |  | AC7 Line Battery, PL7 Bottled Sun, LV3 Cinder Moths, GV4 Lift Beam, NU2 Sun Shell (N) |
| 24 | Matter assembler (N) |  | Booster set T3 (N) | RK13 Long Reach, NA1 Mote Cloud, TM3 Delay Pistol, AM0 Bottle Bay, PS9 Sink Ward (N) |
| 25 | Hyperdrive T3, Hyperspace gate: capstone (N) |  |  | AC9 Spinal Lance, AM1 Speck Rifle, SW1 Bow Gun (N) |

Node totals by tab (Structures counts each block once: there is one grid):

| Tab | Nodes today | New | Total nodes | Points today | Points new | Total points | Most on one level |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Structures | 53 | 30 | 83 | 79 | 102 | 181 | 23 (level 1) |
| Tools | 8 | 10 | 18 | 6 | 28 | 34 | 4 (level 1) |
| Misc | 9 | 20 | 29 | 10 | 54 | 64 | 3 (level 1) |
| Weapons | 12 | 150 | 162 | 25 | 456 | 481 | 10 (level 9) |
| All four | 82 | 210 | **292** | 120 | 640 | **760** | |

Deco stays a separate cosmetic tab: 13 paid sets costing 18 points, at levels 3 to 20, counted in the points total in section 3d but not in the spacing rules.

Notes on the map:

- **Size.** 292 nodes is about 11.7 a level across the four tabs, about six or seven of them weapons, and no tab goes over ten except the Structures starter set at level 1. The tree in the data has 82 nodes. The two old vessel columns held 132 nodes; one Structures tree holds 83. Most of the 49 went in the merge itself (a block on both grids is one node, one core replaces four, and the small, hover and capital tiers of one device are one ladder); the rest are things the grid plan drops (below), less three new rungs that fill levels the merge left empty.
- **Gates.** The reference gates become: hover at 1 (Hover plate T1), portable fabricator at 3, the first one-cell thruster at 5, and the first multi-block thrusters (Thruster T2 and T3, today's large thruster) at 7.
- **Tiers.** In the real list a tier should change something a player can feel (capacity, output, mass, footprint), not only a bigger number.
- **Finds.** The late tiers lean on finds as well as points. Precursor alloy and echo shards are found only (lattice cores went with the CPU). The tree node opens by level and points; the parts come from exploration.
- **Hover plates.** Hover plates are a prerequisite for thrusters (owner decision): Thruster T1 hangs under Hover plate T1, so hover and flight run as parallel branches from the core. Plate tiers sit at 1, 10, 15 and 20; thruster tiers at 5, 7, 7, 15 and 20.
- **Items dropped from the old map.** The design doc has air handled by the core, with no refill stations to place, so the oxygen station is gone. It lists replicators, not hydroponic bays, so food is on replicator tiers.
- **Dropped or folded in the merge** (`docs/one-metre-grid.md` sections 3, 5 and 10). The four cores are one Core. The capital and hover chairs are the command chair. CPU extenders T1 to T4 go with the CPU. Repair bays T1 and T2 and the field repair station go: repair is by the plating gun. Shutters and ramps go: one door family, no ramps. The hover cargo bay and the medium and large containers are Container T1 to T4. The hover armour skirt and the canopy sets are base-block shapes and the cockpit tiers. Landing gear and the docking pad are Landing pad T1 to T4; the large hover block is Hover plate T2. The small, hover and base shields are one Shield generator ladder, and the shield extender goes (bubbles may not touch). Jet and retro thrusters go (Empyrion's jet family is not carried over; one thruster ladder). The vehicle detector is the scanner, the long-range teleporter is the teleporter (no range limit), the remote-control console is the comms array, and the salvager is the tool's dismantling. Warp drive T4 goes, leaving Hyperdrive T3 and the hyperspace gate as the capstones.
- **New rungs where the merge left a level empty:** Capacitor T2 (13) and T3 (21) and Comms array T1 (14); Hover plate T3 and T4 take 15 and 20, after Hover plate T2 moved to 10.
- **Two ladders wait too long.** Generator and Fuel tank T3 are today's large parts at level 1 and T4 is at 15 (figures doc), a 14-level wait that breaks rule 6. A rung between (for example at 8) or T4 earlier would fix it; the owner's call.
- **Ranks or separate nodes.** Reforged Eden 2 merged some tier nodes into their parent to cut points and clutter (Reported, changelog read directly). If the owner wants fewer nodes, the tier rows can become ranks of one node, as long as each rank-up still shows as something new on its level.
- **The level-1 lump.** Starting each T1 at the lower of today's two levels and today's large part at its own level puts 23 nodes at level 1: today's large generator, fuel tank and container were level-1 base parts, so Generator T1 to T3, Fuel tank T1 to T3 and Container T1 to T4 all open there. It could be split (T2 at 3, T3 at 5) if the owner wants a gentler start. That is a data change only.
- **Capstones.** The hyperspace gate, Hyperdrive T3, the Spinal Lance, the Speck Rifle and the Bow Gun sit at 25. Per rule 9, each should point at something to do, such as a found part to build it with. Which part is a world-design decision for the owner, and none of this adds a gate to the tree node.

### 3c. Categories and their tiers (Inferred)

Each ladder has as many tiers as 25 levels can use, with no thin, numbered filler at the top.

| Category | Tiers today | Levels in the map |
| --- | --- | --- |
| Plating | 6 (iron, concrete, glass, hardened, combat, precursor) | Iron, concrete and glass 1, hardened 6, combat 12, precursor 20, charged 22 |
| Thrusters | 5 | 5, 7, 7, 15, 20 |
| Generators | 4 | 1, 1, 1, 15 |
| Shields | 0 | 9, 16, 23 |
| Capacitors | 0 | 9, 13, 21 |
| Fuel tanks | 4 | 1, 1, 1, 15 |
| Containers | 4 | 1, 1, 1, 1 |
| Gyros | 3 | 5, 7, 10 |
| Landing pads | 4 | 5, 7, 7, 7 |
| Cockpits | 2, under the command chair | 1, 1 |
| Replicators | 0 | 3, 11, 19 |
| Warp and jump | warp 1, hyperdrive 2 | Warp 12, 18, 23. Hyperdrive 15, 20, 25. Fold gate 23, hyperspace gate 25 |
| Constructors | Survival, portable, Fabricator T1 to T3 | Portable 3, Fabricator T1 to T3 at 2, 8, 15, deconstructor 4. Adds the matter assembler at 24 |
| Hover plates | 2 | 1, 10, 15, 20 |
| Medical and clone | 0 | Medical bay 5, cloning bay 10 |
| Comms array | 0 | 14, 20 |
| Vessel drills | 0 | 9, 17 |
| Teleporters | 0 | 16 |
| Drills, detectors | 3 | Drills 4, 8, 15, 20. Hand detector 5, suit scanner 12 |
| Multi-tool, Hijack Tool | 0 | Multi-tool 3, 11. Hijack Tool 10, 17, 23 |
| Suits and boosters | 0 | Suits 4, 7, 12. Boosters 5, 9, 10, 11, 13, 14, 16, 19, then sets at 22 and 24 |
| Personal shields | 0 | Tiers 1, 6, 11, 16, 22. Tunings 9, 13, 18, 24 |

### 3d. Points curve

**Today.** The tree costs 133 points and the curve grants 104 by level 25, so it breaks the rule that levels alone open the tree.

**Node costs.** Existing nodes keep their costs. New nodes follow today's pattern by level: 1 point at levels 1 to 4, 2 at 5 to 10, 3 at 11 to 16, 4 at 17 to 21, 5 at 22 to 24 and 6 at 25. Starter nodes are free. Five nodes cost more: the Bottle Bay 6, and the hyperspace gate, Spinal Lance, Speck Rifle and Bow Gun 8 each.

**New curve.** A starting grant of 3 points, then one grant per level. For `progress.json`:

```
"startPoints": 3,
"points": [8, 10, 12, 15, 17, 19, 21, 23, 26, 27, 30, 32, 34, 36, 38, 40, 41, 44, 46, 48, 50, 52, 54, 55]
```

The curve is the two-tab curve scaled to the smaller tree (about 0.83 of each grant). `startPoints` is in the data now: the player would otherwise start with no points, and Hover plate T1 costs 1, which leaves the design's "hover at level 1" gate out of reach until level 2.

**The sum.**

- Cost of the tree: Structures 181 + Tools 34 + Misc 64 + Weapons 481 = 760, plus 18 for deco = **778**.
- Points by level 25: 3 + 8 + 10 + 12 + 15 + 17 + 19 + 21 + 23 + 26 + 27 + 30 + 32 + 34 + 36 + 38 + 40 + 41 + 44 + 46 + 48 + 50 + 52 + 54 + 55 = **781**.
- Levels alone leave 3 points spare. Past-cap points still come on top, at 1 per 1,500 xp.

Why the numbers rise so far: the tree goes from 82 nodes to 292, on the same 25 levels, and most new nodes cost more than today's many free starter nodes. 778 points over 24 level-ups is about 32 a level.

Level by level. "Still unaffordable" is the cost so far minus the points so far: what a player who spends everything still can't buy.

| Lv | New nodes: Structures, Tools, Misc, Weapons | Cost of new nodes (with deco) | Grant | Points so far | Cost so far | Still unaffordable |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 23, 4, 3, 3 | 10 | 3 | 3 | 10 | 7 |
| 2 | 3, 2, 2, 6 | 12 | 8 | 11 | 22 | 11 |
| 3 | 3, 1, 1, 7 | 14 | 10 | 21 | 36 | 15 |
| 4 | 1, 2, 2, 4 | 13 | 12 | 33 | 49 | 16 |
| 5 | 5, 1, 1, 6 | 25 | 15 | 48 | 74 | 26 |
| 6 | 2, 0, 2, 5 | 19 | 17 | 65 | 93 | 28 |
| 7 | 6, 0, 2, 7 | 27 | 19 | 84 | 120 | 36 |
| 8 | 1, 1, 2, 9 | 28 | 21 | 105 | 148 | 43 |
| 9 | 3, 0, 2, 10 | 29 | 23 | 128 | 177 | 49 |
| 10 | 4, 1, 1, 8 | 32 | 26 | 154 | 209 | 55 |
| 11 | 1, 1, 2, 9 | 39 | 27 | 181 | 248 | 67 |
| 12 | 3, 1, 1, 6 | 32 | 30 | 211 | 280 | 69 |
| 13 | 1, 0, 1, 9 | 33 | 32 | 243 | 313 | 70 |
| 14 | 1, 0, 2, 7 | 31 | 34 | 277 | 344 | 67 |
| 15 | 7, 1, 0, 7 | 48 | 36 | 313 | 392 | 79 |
| 16 | 2, 0, 1, 6 | 27 | 38 | 351 | 419 | 68 |
| 17 | 1, 1, 0, 7 | 36 | 40 | 391 | 455 | 64 |
| 18 | 1, 0, 1, 4 | 24 | 41 | 432 | 479 | 47 |
| 19 | 2, 0, 1, 6 | 36 | 44 | 476 | 515 | 39 |
| 20 | 5, 1, 0, 8 | 62 | 46 | 522 | 577 | 55 |
| 21 | 1, 0, 0, 7 | 32 | 48 | 570 | 609 | 39 |
| 22 | 1, 0, 1, 8 | 50 | 50 | 620 | 659 | 39 |
| 23 | 3, 1, 0, 5 | 45 | 52 | 672 | 704 | 32 |
| 24 | 1, 0, 1, 5 | 36 | 54 | 726 | 740 | 14 |
| 25 | 2, 0, 0, 3 | 38 | 55 | 781 | 778 | -3 |

What the table shows:

- At every level from 2 to 24 something stays out of reach. The gap peaks at 79 points at level 15, about two levels' grant, and closes at 25. Level 1 already leaves 7 points of starter tiers to choose between.
- A level's own new nodes cost more than its grant at levels 2 to 13 and at 15 and 20. Later levels pay down the backlog, so the last levels feel like catching up on what you passed by.
- Players who skip whole tabs will have points to spare near the top, which suits the design's "specialise, then fill in".

### 3e. Checks a test can make

A test can read the data, with no game running, and list any level or tab that breaks these. The map above passes checks 1 to 5, 7, 9 and 10 (counted by hand for the Structures column). Check 8 fails on two ladders, Generator and Fuel tank (section 3b). Check 6 holds in the data: the slice's BuildTest walks every structure block's parents to the core and every tier's parent to the tier below.

1. For each level 1 to 25, Structures and Weapons each have at least one new node.
2. Tools and Misc never go more than 2 levels in a row without a new node.
3. No tab has more than 10 new nodes on a level, except a listed starter level (Structures at 1).
4. Running cost is more than running points at every level from 2 to 24.
5. Total points by level 25 are at least the total tree cost, deco included. The curve has one entry per level.
6. Every node's parent exists, has a level no higher than the node's own, and no chain loops.
7. The reference gates hold: Hover plate T1 at 1, portable fabricator at 3, Thruster T1 at 5, the multi-block thrusters at 7.
8. Thruster T1 comes after Hover plate T1, its parent, and no tier ladder waits more than 8 levels for its next step. A tier's id names its ladder (`thruster-t3`).
9. Personal weapons appear on every level. Melee, placed, living and support weapons never go more than 5 levels without a new node, and personal shields never more than 4.
10. Every weapon entry reaches the player one way only: as a node, a recipe under one node, or a listed find, reward, relic, cyber module or salvage.

---

## 4. Weapon slots

The Weapons column in section 3b is the whole tab, taken from the tree fit in `docs/weapon-ideas.md` section 9. This is the same list by slot. Hover and small-vessel mounts share a row because they share a grid and their nodes, and the old table's anti-air, ammo and vehicle-gun rows now sit inside these. Personal, placed, melee, living and support weapons, which the old table was too thin on, each have real slots.

| Slot | Nodes | Levels (a repeat means two nodes) | Longest gap with none |
| --- | --- | --- | --- |
| Personal: hand guns, launchers, thrown | 81 | 1, 2, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 5, 5, 5, 6, 7, 7, 7, 7, 7, 8, 8, 8, 8, 8, 9, 9, 9, 10, 10, 10, 10, 11, 11, 11, 12, 12, 12, 12, 12, 13, 13, 13, 13, 13, 14, 14, 14, 15, 15, 16, 16, 17, 17, 17, 18, 18, 18, 19, 19, 19, 19, 20, 20, 20, 21, 21, 21, 22, 22, 22, 22, 22, 22, 23, 23, 24, 24, 25 | 0 |
| Melee | 6 | 1, 4, 6, 10, 16, 20 | 5 |
| Placed: mines, charges, placed machines | 10 | 3, 3, 7, 8, 10, 10, 14, 14, 15, 20 | 5 |
| Living | 6 | 3, 9, 11, 14, 19, 23 | 5 |
| Support and hand defence | 6 | 5, 7, 11, 12, 15, 21 | 5 |
| Personal shields | 11 | 1, 6, 8, 9, 11, 13, 14, 16, 18, 22, 24 | 4 |
| Hover and small-vessel mounts | 15 | 5, 6, 9, 9, 9, 10, 11, 11, 13, 13, 16, 16, 17, 19, 22 | 3 |
| Base and capital turrets | 9 | 5, 8, 9, 11, 15, 17, 20, 21, 23 | 3 |
| Capital heavy | 8 | 15, 20, 21, 23, 24, 24, 25, 25 | 4 |
| Anti-air and point defence | 4 | 13, 17, 17, 21 | 4 |
| Ammo (the six existing nodes) | 6 | 2, 6, 8, 9, 15, 20 | 5 |

The tab holds 145 weapon nodes that open groups, 11 personal shield nodes and the 6 existing ammo nodes: 162 in all.

**Does it break the per-tab limit? No.** The old tree-fit table came to 151 nodes, and adding the shield line and the existing mounts took a first draft to 170. I merged 14 groups, mostly second-rank tiers and capital variants that read better as recipes under one node, which gave 156. Level 9 peaked at 12 and two other levels at 10, so I moved nine nodes to quieter levels. The tab now averages about six and a half nodes a level and peaks at 10, at level 9, where the existing gatling, rocket and pod nodes sit.

Notes:

- Vehicle guns come through the tree (owner decision), so mounts have their own rows.
- Existing weapon nodes keep their levels and costs. In weapon-ideas terms, the gatlings are the Chatter Gun, the turrets are the Deck Gun and the pods are the Rocket Pod.
- The old rule "never two big weapon categories on the same level" is gone: it can't hold at 25 levels.
- Cyber modules sit outside the tree, as the design doc says: finding and consuming one unlocks unique alien equipment for building, for the whole crew. They take no level or point slots here.
- Comic weapons and relics are finds, and take no slots here.

---

## 5. Questions for the owner

Settled, so not questions:

- **Points:** the whole tree opens by max level, and the curve in section 3d does that from levels alone, with 25 as the cap.
- **Point sources:** skill points come from levelling up. Levels alone must cover everything. If finds or missions ever pay points, they only speed the player up.
- **Cyber modules:** outside the tree, found and consumed, for the whole crew.
- **Cyber modules are hard to get, not late-game (owner's decision):** dangerous places hold them, at any level. Enough other late goals still need planning so the top of the game doesn't go quiet.

- **Starting points:** the player starts with 3 points, so the hover gate opens at level 1. This needs a small code change.

---

## Sources

Measured (local files, read-only):

- `G:\GalHack-wt\batch\prototypes\slice\data\progress.json`
- `G:\GalHack-wt\batch\prototypes\slice\data\items.json`
- `G:\GalHack-wt\batch\shared\Items\data\blocks.json`, `items.json`, `deco.json`
- `G:\GalHack-wt\batch\prototypes\slice\src\Items\Progress.cs` (tab list)
- `G:\GalHack\docs\DESIGN.md` (progression, tech tree, CPU sections), `G:\GalHack\docs\playtest-list.md` and `G:\GalHack\docs\weapon-ideas.md`
- `Z:\Steam\steamapps\common\Empyrion - Galactic Survival\Content\Configuration\BlocksConfig.ecf` and `ItemsConfig.ecf`, build 1.19.6 (counts only; nothing copied)
- `Z:\Steam\steamapps\workshop\content\383120\2550354956` (a Playfields folder only; no tree config; identity unconfirmed)

Reported, read directly in the built-in browser (second pass):

- Reforged Eden 2 in-game encyclopedia text, as mirrored by a companion site: player level https://empyrionbuddy.com/pda/player-level-eden_pda_0cIqKe8 , unlock points https://empyrionbuddy.com/pda/unlock-points-eden_pda_40O46Ga , tech tree https://empyrionbuddy.com/pda/tech-tree-eden_pda_4DCiOKY , schematics https://empyrionbuddy.com/pda/schematics-eden_pda_8S5qYiO
- Reforged Eden 2 Beta discussions: skill points https://steamcommunity.com/workshop/filedetails/discussion/3143225812/813573650210881773/ and research points https://steamcommunity.com/workshop/filedetails/discussion/3143225812/4638240954399120287/
- Reforged Eden 2 Beta changelog, pages 1 to 9 (searched for tech tree, unlock points, merged nodes): https://steamcommunity.com/sharedfiles/filedetails/changelog/3143225812
- A player's Reforged Eden 2 Beta strategy guide (key levels, XP stretches): https://steamcommunity.com/sharedfiles/filedetails/?id=3657911391
- Steam threads: Reforged Eden points after level 25 (2021) https://steamcommunity.com/app/383120/discussions/0/3169946351455174551/ ; more skill points (2022) https://steamcommunity.com/app/383120/discussions/0/3482997257348124890/ ; a vanilla tech tree thread with a 2025 reply https://steamcommunity.com/app/383120/discussions/0/3335371283869021230 ; "endgame question" (2025) https://steamcommunity.com/workshop/discussions/18446744073709551615/84031737849493636/?appid=383120 ; Progenitor data chips (2020 to 2021) https://steamcommunity.com/app/383120/discussions/0/2918850377430194759
- Empyrion Online forum: "A call from Reforged Eden to EGS" https://empyriononline.com/threads/a-call-from-from-reforged-eden-to-egs.101076/ ; "Experience Points, Player Level Progression and Tech Trees", page 20 https://empyriononline.com/threads/experience-points-player-level-progression-and-tech-trees.3082/page-20 ; "How to Modify the Tech Tree Guide" (field names only) https://empyriononline.com/threads/how-to-modify-the-tech-tree-guide.95887/ ; the first-pass Reforged Eden thread https://empyriononline.com/threads/reforged-eden-scenario.94773/ (page 1 only)
- Star Salvage: Reborn Workshop page and comments https://steamcommunity.com/sharedfiles/filedetails/?id=3409399573 ; discussion list https://steamcommunity.com/sharedfiles/filedetails/discussions/3409399573 ; threads: pinned difficulty https://steamcommunity.com/workshop/filedetails/discussion/3409399573/592887031601832968 , bug reporting .../592887031601896921 , "Beyond Orion?" .../604150365670092752 , "Giving up" .../604150170601415046 , galaxy question .../595158831570616067 , drones and thrusters thread .../592887273544050363 ; Nexus Mods page https://www.nexusmods.com/empyriongalacticsurvival/mods/155
- Empyrion Buddy home and forum thread (build and scenario support only): https://empyrionbuddy.com/ and https://empyriononline.com/threads/tool-empyrion-buddy.102560/

Reported, search summary only (not opened, or the page failed when opened):

- Empyrion wiki, Tech Tree: https://empyrion.fandom.com/wiki/Tech_Tree
- Steam discussion on the level cap: https://steamcommunity.com/app/383120/discussions/0/3203652426715508521
- Original Star Salvage Workshop page (rate-limited, not opened): https://steamcommunity.com/workshop/filedetails/?id=2827006530
- Search summaries said a "Star Salvage Light" version uses the base tree and that the author promised 500 points per specialisation mission. The second part matches what a player wrote in the pinned thread; the first I did not see on a page.
- A Russian mod catalogue entry for the original scenario sat behind a bot check; I did not try to get past it.

## What I could not verify

- Reforged Eden 2's actual unlock levels per item, how many items sit at each level, or its total point cost. I read the cap (25), the point sources, the two-gate rule and a player's list of key levels, but no per-level counts, and no page stating a dead band. The companion site lists build 46 as its data version; I did not confirm that is the current build.
- Whether XP stops at level 25 in the Reforged Eden 2 beta. The statement I read is from 2021 about the first Reforged Eden.
- The size of today's point rewards (data pad, research mission, reports). Posts from 2021 and 2022 disagree, and a search summary mentioned a patch that lowered data pad rewards that I did not find on a page.
- Whether Progenitor data chips pay unlock points or only research points. The in-game text and a player disagree.
- Star Salvage Reborn's level cap and per-level unlocks. Nothing I read states them. The original Star Salvage's Workshop page, the Reborn change notes, the capital vessels thread and the 35-comment "fixes for 1.12" thread were not read (Steam rate-limited the browser for long stretches). Only the newest slice of the roughly 630 comments on the Reborn page was readable; the "first ten hours" comment is read directly, and I cannot say how many similar comments are deeper in.
- Whether the "not much after ten hours" remark is about tech pacing or about missions ending. Three signals point to content running out (the comment, a story dead end, and the maintainers saying only chapter one of at least four exists), but none speaks to unlock spacing directly.
- Whether Workshop item 2550354956 on this machine is the first Reforged Eden. It shows up in search next to Reforged Eden threads, but I did not confirm.
- Whether the vanilla "no tab" level-1 entries are all free starter blocks (likely, not checked).
- The vanilla wiki's 580-point total.
