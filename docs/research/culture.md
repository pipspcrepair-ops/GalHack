# Culture: research and plan

The owner's ask (15 Nov): a culture layer for worlds that sets social distance, individuality, gender norms and roles, castes, religion, clone labourers and political systems. It shows in how far apart people keep, who groups with whom, where the children are, what each sex wears, who carries weapons, who is aggressive, who wanders, and in places and flavour buildings. It also changes how people treat each other and the sleeper.

Phase 1 is built (section 5.3); phases 2 and 3 are plans. The owner's decisions on the research are in section 6. The hard rules hold throughout:
- no written text or dialogue: culture shows through behaviour, dress, arrangement and architecture;
- nothing bespoke: culture is data in rolled layers, and every effect is a rule over existing parts;
- no locked doors: segregation is shown by who is where and how they react, never by a lock;
- the player is a sleeper, a visitor who never frees, reforms or rescues anyone: a sleeper's means with people are gestures, trade, threats and violence, and in many games violence is what they end up using;
- works are named as inspiration only, and nothing is lifted from them.

## 1. Summary

- **15 independent axes**, each a small layer with 3 to 7 values, in a new `Culture` group of `world-layers.txt`. The ids are `space self sexes arms fierce young garb caste labour rite reign roam guest elders manner`, plus `cover` (the decency piece), a rolled fact `lead`, which says which sex is side A, and a world fact `desperation`. The political axis is `reign` in the data because `rule` is already the record kind for coherence rules.
- **The culture follows the community, never the body.** It rolls per world, drifts per town or region from the town seed that already exists (`FlavourRuins.CultureAt`), and is fixed per faction. Folk and appearance never set it, which keeps the design doc's rule that appearance is never tied to role, faction or behaviour.
- **Every sex-asymmetric value is symmetric in the data.** It names side A or side B, and each world rolls which sex is A, 50/50. Role groups are also assigned to sides by roll, so the generator holds no "men do X" rule.
- **What exists already and is reused:**
  - the `creed` layer (what survivors believe), which `rite` complements (how they practise it);
  - `ran` (who ran the old world), which leans the present `reign`;
  - `Temper` and `Stance`, which `guest` leans;
  - the planned gesture dialect (body-language doc 3.3, not built), which `manner` absorbs;
  - the per-town building-culture seed;
  - the signature `uniform` and `festive` clothes;
  - `RoleLook.Accent`;
  - `PackRules.Spacing`;
  - the `Brain.Folk` distances.
- **Phase 1 is constants and choices only, with no new meshes:**
  - spacing and grouping in towns and bands;
  - children's minders;
  - dress by sex, role or caste, plus a "decency piece";
  - who is armed;
  - which sex runs hot.

## 2. Tropes

Each trope is grouped under the axis it feeds. "Seen in" names the inspiring works only.

| Axis | Trope | Seen in |
| --- | --- | --- |
| space | A society that cannot bear another's presence, living on lone estates served by robots and meeting only by projection | Asimov, *The Naked Sun* (Solaria) [1] |
| space | Crowded habitats breed closeness and touch, while clean, rich places keep their distance | *The Expanse* (Belt against Earth), *Snowpiercer* |
| self | The collective mind: one will, synchronised movement, no individual dress | Star Trek's Borg; *The Midwich Cuckoos* |
| self | Planet of Hats: a whole people shares one trait, which is the shorthand to avoid as a sole trait but use as an extreme | TV Tropes, "Planet of Hats" [2] |
| self | Anarchist commune: no property, communal dormitories, work by rota | Le Guin, *The Dispossessed* (Anarres) [3] |
| sexes | A single-sex society | Gilman, *Herland* [4] |
| sexes | No gender in the society's language, with one sex's word used for all | Leckie, *Ancillary Justice* (Radch) [5]; Le Guin, *The Left Hand of Darkness* [3] |
| sexes | Rigid sex roles and segregation enforced by a regime, with a colour per role | Atwood, *The Handmaid's Tale* [6] |
| sexes, arms | One sex barred from fighting; the warband is all one sex | *Fallout: New Vegas* (Caesar's Legion) [7]; *Mad Max: Fury Road* |
| arms, fierce | Proud warrior race: every adult armed, honour through combat, respect earned by display | TV Tropes, "Proud Warrior Race" [8]; Star Trek's Klingons; *The Mandalorian* |
| arms | Pacifist peoples with no weapons | Star Wars' Alderaanians [2]; Star Trek's Organians |
| young | Children raised apart in crèches and hatcheries, not by parents | Huxley, *Brave New World*; Solaria [1] |
| young | Children in their own packs, a society of the young | *Ender's Game* (Battle School); *Lord of the Flies* as a dark mirror |
| garb | A colour-coded dress code by role or caste | *The Handmaid's Tale* [6]; *Brave New World* (caste colours) |
| garb | A garment that decency demands, whose absence shocks | *Ancillary Justice* (gloves) [5]; *Dune* (stillsuits as everyday dress) |
| garb | Everyone in the same uniform | *Logan's Run*; *THX 1138*; the Star Trek uniform default; "why do all Klingons dress the same" [9] |
| caste | Engineered or hereditary castes with set roles | *Brave New World* (Alpha to Epsilon) [10]; Babylon 5's Minbari (worker, warrior, religious) [11] |
| caste | Hive castes, physically distinct by role | TV Tropes, "Hive Caste System" [10]; Starship Troopers' Arachnids |
| caste | Vertical class, with the rich above and the poor below | *Metropolis*; *High-Rise*; *Elysium* |
| caste | Class as a place: the front of the train against the back | *Snowpiercer* |
| labour | Clones as expendable labour or spare parts | TV Tropes, "Expendable Clone", "Walking Transplant" [12]; Ishiguro, *Never Let Me Go*; *Moon*; Star Wars' Kamino |
| labour | Machines do all the work while people idle | Solaria [1]; *WALL-E* |
| labour | Bonded workers under an overseer | *Fallout: New Vegas* [7]; *Total Recall* (Mars); *Elysium* |
| labour | Underclass and upper class as two peoples | Wells, *The Time Machine* (Eloi and Morlocks) |
| rite | A faith that structures the day, with pilgrims and holy roads | *Dune* (Fremen); *Hyperion* (the Shrike pilgrimage); the Minbari religious caste [11] |
| rite | Cargo cults of the old machines | Warhammer 40,000's Adeptus Mechanicus; *A Canticle for Leibowitz* |
| rule | A council of castes, three seats each | Minbari Grey Council [11] |
| rule | A warlord who rules by force, with trophies and arenas | *Mad Max*; Caesar's Legion [7] |
| rule | A corporate state whose officers are the government | *Alien* (Weyland-Yutani); *RoboCop* |
| roam | A coming-of-age journey in which the young leave alone and return with something of value | *Mass Effect* (the quarian Pilgrimage) [13] |
| roam | Whole peoples on the move: migrant fleets, caravans, nomads | *Mass Effect* (Migrant Fleet) [13]; *Dune* (Fremen) |
| guest | Hospitality code: a guest is untouchable once received | *Dune* (water bond); a common SF sacred-guest trope |
| guest | Trade without talk: goods left out, and the stranger steps back | Historical silent trade; fits our no-dialogue rule |
| manner | A gesture language born in suits with poor comms | *The Expanse* (Belter hand signs) [14] |
| manner | Logic over feeling: still, reserved, no laughter | Star Trek's Vulcans |

Two game systems that build culture from data are worth studying, for ideas only:
- RimWorld's Ideology builds a belief from 1 to 4 memes, which unlock precepts, apparel, roles and colours [15].
- Dwarf Fortress gives each civilisation `ETHIC` responses, whose clashes start wars in worldgen [16].

Both confirm the approach: a few axes with values, rolled, whose effects are rules.

Real-world proxemics give the numbers [17]. Hall's four zones are intimate (0 to 0.45 m), personal (0.45 to 1.2 m), social (1.2 to 3.6 m) and public (3.6 m and beyond). The zones recur across cultures, and their sizes vary by up to about 2x.

## 3. The axes, their values and their effects

Distances are between body centres. Weights are relative within an axis. **Bold** marks the blank or default value: today's behaviour. Side A is the sex the world's `lead` fact rolled; B is the other.

### 3.1 `space`: social distance

| Value | Weight | Chat pair, m | Touch | Band spacing, m (today 2.4) | Fire ring radius, m | Seats | Groups | Stranger distances (`Brain.Folk`) | Homes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| close | 3 | 0.75 | Often (shoulder, forearm) | 1.7 | 1.5 | All, shoulder to shoulder | 3 to 6 | x0.75 | Terraces, shared walls |
| **even** | 6 | 1.1 | Rare | 2.4 | 2.0 | All | 2 to 4 | x1 | Houses |
| wide | 3 | 1.6 | Never | 3.2 | 2.8 | Every other seat | 2 to 3 | x1.3 | Houses with gaps of 15 m or more |
| apart | 0.6 | 2.6 | Never | 5.0 | No gatherings | One per table | 1 to 2 | x1.8 | Homesteads 80 m or more apart, one person each; robots or hatches between |

The stranger-distance multiplier scales `Comfort` 2.6, `MeetAt` 4, `GreetIn` 14 and `KeepOff` 13.

### 3.2 `self`: individuality against collectivism

| Value | Weight | Schedule spread (σ, game min) | Reaction ripple, first to last (s) | Share of free time spent together | Work | Houses on a street | Individual deviants |
| --- | --- | --- | --- | --- | --- | --- | --- |
| herd | 1 | 2 | 0.4 | 60% | Gangs of 6 to 12 | One model repeated | 1% |
| tight | 3 | 10 | 0.8 | 45% | Crews of 3 to 5 | A few models | 3% |
| **mixed** | 5 | 25 | 1.2 | 30% | Pairs | Mixed (today) | 6% |
| loose | 3 | 45 | 1.6 | 20% | Alone | Every one different | 10% |
| lone | 1 | 90 | 2.0 | 5% | Alone | Odd one-offs common | 15% |

The ripple never reaches 0: only robots act in sync (design doc section 17). A deviant breaks the culture on one axis: a colour of their own, sitting alone, or carrying a weapon where nobody does.

### 3.3 `sexes`: roles, mixing and segregation

Roles are grouped as `fight` (guard, lawman, raider), `trade` (trader), `make` (worker, mechanic, farmer), `care` (medic, and minding children) and `lead` (leader, elder). In leaning, split, apart and secluded worlds, each group rolls a side, A or B.

| Value | Weight | Share of a group's roles from its side | Social groups outdoors | Places |
| --- | --- | --- | --- | --- |
| **same** | 6 | 50% | Mixed, as chance gives | None |
| leaning | 4 | 70% | Mostly mixed | None |
| split | 2 | 95% | Work crews of one sex; mixed off work | None |
| apart | 1 | 95% | One sex only outside homes; each sex has its own gathering spot | Two halls, two fires, separate quarters (phase 2) |
| secluded | 0.3 | 100% (B keeps only the `care` group) | B seen outdoors at 15% of A's rate, always in pairs or more, close to home | B's quarter is the inner court of each compound |

### 3.4 `arms`: who carries weapons

| Value | Weight | Share of adults visibly armed | What they carry | Stance |
| --- | --- | --- | --- | --- |
| everyone | 2 | 70 to 90% | A sidearm or blade on the belt; guards also have a rifle | A gun on them makes them draw (`Aimed` = Warn), not raise their hands |
| side_a | 2 | About 70% of side A, 0% of side B | As everyone, for A | B cowers behind A when threatened |
| **fighters** | 6 | 10 to 15%: guards, the law, raiders (today, `PeopleGenerator.Fighter`) | Era arms (`SignatureTables.Armed`) | As today |
| few | 2 | 3 to 5%: the leader and the law | A sidearm | Others flee to the armed |
| none | 1 | 0% in town; guards carry staves or tools; hunting arms hang on a rack at the gate | None | A drawn weapon is a grave offence: everyone's stance drops a step, and the near ones cower |

**Children can be armed** (owner, 15 Nov: children aren't treated as sacred in every culture). Whether they are depends on two things together:
- **the world's desperation**, a world fact from 0 to 1 rolled from facts the world already has (section 4.6): a falling world, famine and drought, hostile presences, Husks toward the core, raiders, bad air, a deep drop in tech;
- **the culture's values**: `arms` (everyone and side_a arm more of their young, none fewer) and `young` (working and packs children carry more often, kept children less).

A child's chance to carry a weapon is the culture's base plus 0.4 times desperation, scaled by `arms`: a desperate world arms its young often even under a mild culture, and a comfortable world rarely does unless its culture says so. A young person rolled into an adult job stays `Hardened` and, as a guard, is armed through `Fighter()` as before.

### 3.5 `fierce`: who is aggressive

| Value | Weight | Effect on `Temperament` and `Party.Lean` |
| --- | --- | --- |
| **even** | 6 | As rolled today |
| one_side | 3 | The hot side is weighted x3 toward Aggressive or Bold, and the other x3 toward Calm. Each hot member adds +0.15 lean. Hot members step forward and Warn or Taunt; calm members step Back first. Defaults to the armed side |
| young | 2 | Ages 16 to 25 are Bold or Aggressive x3. The young taunt; elders call them back (Point, then Back) |
| all | 1 | Everyone Bold, and Escalation +0.2 |
| calm | 1.5 | Everyone Calm or Skittish, and Escalation -0.2. They never start a fight, and run sooner |

### 3.6 `young`: children's place

| Value | Weight | Children are | Furthest from their minder | Share of time at play | Places |
| --- | --- | --- | --- | --- | --- |
| **anyone** | 5 | With the nearest adult (today, `Town.cs:893`) | 10 m | 50% | None |
| side_a / side_b | 2 / 2 | With an adult of that side | 6 m | 40% | None |
| packs | 2 | In packs of 3 to 6 of their own, roaming the town | Town edge | 70% | A play yard, and a den at the edge |
| kept | 1 | Indoors or in a walled yard; out by day only with a carer | 3 m | 30% | A crèche hall, and a children's dormitory |
| working | 1 | From age 10, at a work spot beside an adult; play only in the evening | 4 m | 15% | Small benches in workshops |

### 3.7 `garb`: dress

| Value | Weight | Colour clusters in a crowd | Rule |
| --- | --- | --- | --- |
| **free** | 5 | 6 or more | Today's look: role, climate and era |
| by_role | 3 | One per job | Each job wears its accent strongly (`RoleLook.Accent`), full cloth, not a trim |
| by_sex | 2 | 2 | Each side takes its own cut and colour pair, both drawn from existing garments (for example A in a long coat, B in a short jacket and wrap). The cut pair is rolled per world |
| by_caste | 1 | One per caste | One colour from the world paint per caste, strictly |
| uniform | 1.5 | 1 | One colour and one cut for everyone (the existing signature `uniform`) |

**`cover`**, the decency piece, is rolled separately: **none** 6, gloves 1, hood 1, mask 1, sash 1, face paint 1.
- Adults always wear it outdoors (or only side A, or B, 30% of the time).
- Air and climate always win: in vacuum everyone is suited, and culture shows as suit colour and markings.
- No real-world named garments are used.

### 3.8 `caste`: class roles and castes

| Value | Weight | Wealth (`HomeHints.Wealth`) | Districts | Grouping and manners |
| --- | --- | --- | --- | --- |
| flat | 5 | 0.4 to 0.6 | None | Mixed |
| **ranked** | 5 | 0 to 1, better off further out (today, `CityPlan.cs:463`) | Gradient | Mixed |
| castes | 1.5 | Bands, by caste | 3 castes, each a district with its own finish and colour, a lane or wall between (an open arch, never a gate) | Chat partners from the same caste 95% of the time. The lower caste gives way first (`PersonalSpace` give 1.0 against 0.3), steps off the path and dips its head |
| masters | 1 | 10% high, 90% low | One great compound, and workers' rows | As castes, 99% |
| vertical | 0.5 | By floor | Upper floors and towers for the high caste, ground and below for the low | As castes |

The castes are drawn from the role groups of 3.3: for example a fighting caste, a making caste and a keeping-and-faith caste.

### 3.9 `labour`: clone and bonded labour

| Value | Weight | Requires | Share of work spots | What shows |
| --- | --- | --- | --- | --- |
| **own** | 8 | None | People | Today |
| machines | 2 | post M or H | 60% robots (`Robots.cs`) | People sit, talk and stroll; the MCH share rises |
| clones | 0.8 | post H | 40% | One body for every worker, a plain coverall, colour bands for number, bunk-hall barracks, a vat hall near. They never trade, greet or laugh, and react on a 0.3 s ripple |
| bonded | 1 | None | 50% | Plain garb, no weapons; gangs of 4 to 8 with one armed overseer; barracks with bunks. They look to the overseer before reacting to the sleeper. As harsh as the culture runs it: no softening |
| rota | 1.5 | None | People | Everyone works shifts; a horn marks the change, and groups cross town at once |

Nobody is freed. Sleepers are visitors who don't interfere with systems: kill an overseer or a master and the workers either run, or just stand there and look at the sleeper. They never thank, follow or join the sleeper, and the system goes on with the next overseer. There is nothing to free, reform or rescue.

The owner's clone world (9 Nov) stays the rare extreme in `presence.duplicates` and occupant `MIM`, where everyone is one person. `labour.clones` is its milder neighbour: a clone workforce among varied people.

### 3.10 `rite`: how faith is practised

`creed` says what they believe, and `rite` says how it shows.

| Value | Weight | Shrines in homes | Street shrines per 100 m | Temple | Pauses |
| --- | --- | --- | --- | --- | --- |
| **none** | 4 | 0 | 0 | None | None |
| private | 4 | 60% | 0 | None | None |
| public | 3 | 30% | 0.5 | One per town, at the centre; a gathering point at set hours | Twice a day, everyone in sight stops for 20 s and faces it |
| pervasive | 1 | 100% | 2 | The largest building; streets and doors face it; a robed clergy caste | Five a day |

A creed other than `no_creed` doubles the odds of private and public.

### 3.11 `reign`: the present political system

`ran` is the old world's rulers; `reign` is the survivors'. It also decides the owner's "every group has a leader you trade with".

| Value | Weight | Seat | Who steps forward to the sleeper | Leader's mark |
| --- | --- | --- | --- | --- |
| **council** | 4 | A meeting hall, or a circle of seats | The eldest of 3 to 5 | A staff |
| chief | 4 | The biggest house, at the centre | The chief, with two guards at 3 m | A mark piece (hat or sash) and the accent |
| priests | 1.5 | The temple | A robed priest | The clergy colour |
| assembly | 2 | A common hall; no leader's house | Whoever able is nearest | None; the role passes person to person |
| warband | 1.5 | A fighting ring, and trophy walls | The best armed | Trophies and the heaviest weapon |
| officers | Factions only | The faction core room | The officer | Insignia accent |

### 3.12 `roam`: who wanders or stays

| Value | Weight | Share of adults away at once | Who | On the roads |
| --- | --- | --- | --- | --- |
| **settled** | 6 | 5% | Scavengers (today) | Few |
| foragers | 3 | 20% | One side, by `sexes` | Foraging bands |
| herders | 2 | 25% | One side | Herders with stock |
| traders | 2 | 15% | Mixed | Caravans with pack animals |
| seasonal | 1 | The whole town | Everyone | Camps struck and pitched; 1 camp in 3 empty |
| journey | 1 | 5% | Young adults, 17 to 20, alone | Lone wanderers with packs: wary, not hostile; they come home with a find |

These feed the wanderers job (playtest list, 13 Nov) and the signature `Always` hunting parties.

### 3.13 `guest`: how outsiders, the sleeper included, are taken

| Value | Weight | Effect on `Temper.Roll` | What shows |
| --- | --- | --- | --- |
| open | 3 | Openness +0.3 | Greet and Beckon |
| guest_right | 2 | Welcoming x2 | Once greeted, never attacked in town unless they are shot first; a seat offered (Beckon to a sit spot) |
| **trade_only** | 3 | Bargaining x3, the rest Indifferent | Today, more or less |
| proving | 1.5 | Starts Guarded | Warmed x3 by kills of predators near town (the existing "glad of it" hook) |
| closed | 2 | Wary | Trade only at a hatch or a table at the gate: goods set down, and they step back 6 m (silent trade) |
| hostile | 1 | Lean +0.4 | As Hostile |

### 3.14 `elders`

| Value | Weight | Effect |
| --- | --- | --- |
| revered | 3 | Central seats; everyone gives way (give 0.1); they lead councils and are approached first |
| **equal** | 5 | Today |
| set_apart | 1 | Their own quarter or hamlet at the edge; few are seen in town |

### 3.15 `manner`: expressiveness, and the gesture dialect

| Value | Weight | Gesture size | Laughs an hour of social time | Listener nods per 10 s of chat | Voice |
| --- | --- | --- | --- | --- | --- |
| reserved | 3 | x0.75 | 0.5 | 0.5 | -4 dB |
| **even** | 5 | x1 | 2 | 1 | 0 dB |
| expressive | 3 | x1.3 | 6 | 2 | +4 dB |

Gestures are each culture's own manner (owner, 15 Nov). The body-language doc's greeting form, beckon form and signature gesture roll alongside `manner` (section 3.3 of that doc, not yet built). **Nod means yes and shake means no on every world**: they join the universal signals, because the player must read them.

The hook for the gestures job (nod, shake, laugh, which comes later): the `manner` value's traits `gesture`, `laughs` and `nods` are on every person's culture (`Culture.Trait`), so that job reads its dialect's size and rates from there and adds no culture logic of its own.

## 4. Rolling culture

### 4.1 Per world

Culture rolls in chain step 7, after `creed` (world-layers.md 8.2), and only on inhabited worlds. Each axis forks the seed by its own name, as layers do. Leans are `like` multipliers carried by values that already exist, so a culture follows from the world rather than a table:

| World fact | Leans |
| --- | --- |
| `presence` warlords, gangs, militias, bandit_riders; bias `strife` | arms everyone or side_a x2; fierce one_side or young x2; reign warband x3; guest proving or closed x2 |
| `fall` famine or drought; theme water_tax | self tight x2; caste masters x2; labour bonded x2; reign chief x2 |
| `build` sealed or pressure_dome, `air` bad | space close x2; self tight or herd x2; garb uniform x2; cover mask x2 |
| `post` H with `running` clerks, cleaners or carers | labour machines x3; space wide or apart x2; young kept x1.5 |
| `trade` vat_stock, `fall` cloning_mishap | labour clones x4 |
| `ran` great_houses, generals, faith, company, scholars, elected | caste masters or castes x2; garb uniform, arms fighters x2; rite public, reign priests x3; caste ranked, garb by_role x2; caste flat x2; reign council or assembly x2 |
| `trade` herding, pilgrimage, markets, military | roam herders x3; roam journey, rite public x2; guest trade_only or open x2; arms everyone x1.5 |
| `creed` not no_creed | rite private or public x2 |
| `people` survivors | caste flat x2; roam foragers x2; self loose x1.5 |
| `phase` falling; `zone` core | guest closed or hostile x1.5 |

**Desperation leans arms:** arms everyone and side_a x(1 + desperation), and arms none x(1 - desperation).

**Tech never leans sexes, caste or young.** The SF canon puts its most rigid castes and sex codes on high-tech worlds, and "low tech means backward" is a stereotype to avoid.

**Coherence rules** (`rule` records in the same file):
- `sexes same` forbids side-sided values elsewhere;
- `self herd` forbids `garb free`;
- `garb by_caste` needs `caste castes`, `masters` or `vertical`;
- `labour clones` needs post H;
- `space apart` forbids `reign assembly`;
- `young kept` forbids `space close`.

### 4.2 Strength and rarity

The owner's aim (decision 9): about 3 in 10 settled worlds near today's default, and about 1 in 8 with an extreme. Fifteen axes rolled on their own would put 8 or so off default on every world, so the roll goes in three steps:
1. **Strength:** 30% of settled worlds are near default (0 to 2 axes off it); the rest have 4 to 6 axes off.
2. **Which axes:** picked at random, each weighted by how much the world leans it (an axis whose off-default values the world's facts like twice as much is twice as likely to come off).
3. **Each axis off default:** a value by its weight and the world's leans, among the values that aren't the default and aren't extreme. Every other axis takes its default.

**Extremes** are marked in the data (`trait extreme 1`): space apart; self herd and lone; sexes apart and secluded; arms none; caste masters and vertical; labour clones; rite pervasive. A world rolls how many it has, apart from strength: one on 1 world in 8, two on 1 in 60, none otherwise. Each extreme takes the place of one of the off-default axes (a near-default world with an extreme has it as its one axis off). Secluded is weighted so it falls on at most 1 settled world in 30.

### 4.3 Per place

A town takes the world culture and drifts. On each axis, with the town seed from `FlavourRuins.CultureAt` (the same seed its houses use), 80% keep the world's value and 20% step one value along. Trade hubs and spaceports take extra drift, and their outsiders (`Founders.Outsiders`) keep their own home culture, so a hub's crowd is mixed. A world with two folk still has one culture per town, never per folk.

### 4.4 Per town in practice

The town seed is the one its houses use (`FlavourRuins.CultureAt`), so a town's culture and its building style change together. Drift steps one value along the axis's table order, never onto a value the coherence rules forbid there; an extreme stays extreme only where the world rolled it.

### 4.5 Per faction

A faction's places use its own culture whatever the world rolled, with drift 0.1:

| Faction | space | self | sexes | arms | garb | caste | labour | rite | rule | guest | manner |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Meridian | wide | tight | same | fighters | by_role | ranked | machines | none | officers | trade_only | reserved |
| Ledger | close | mixed | same | few | free | ranked | own | private | council | guest_right | expressive |
| Reavers | close | loose | rolled | everyone | free, with trophies | flat | bonded | none | warband | hostile | expressive |
| Freeholders | the world's | the world's | the world's | the world's | the world's | flat | own | the world's | council or assembly | open | even |
| Kessari | even | herd | none (machines) | fighters | by_caste (chassis colour by job) | castes | own | the world's `creed` machine_kin | council | guest_right | reserved (lights, body language doc 3.4.16) |
| Vharn | wide | herd | rolled per world's Vharn | the soldier caste | by_caste (carapace marks) | castes (soldier, worker, keeper) | bonded | public | officers | hostile | reserved |

The other three axes (fierce, young, roam) take the world's values for every faction. Kessari have no sexes or children, so `sexes`, `young` and `fierce one_side` don't apply to them.

### 4.6 Desperation

A world fact (`desperation`, 0 to 1), rolled just before culture from what the world already rolled, with a little noise of its own (±0.1). Each value below carries `trait despair` in the data, and they add up:

| Fact | Adds |
| --- | --- |
| `phase` falling | 0.3 |
| `phase` fallen (no mending yet) | 0.1 |
| `fall` of the war group, or famine, drought, plague, long_winter, air_loss or supply_cutoff | 0.15 |
| `theme` water_tax | 0.15 |
| each hostile `presence` (warlords, gangs, militias, bandit_riders, water_lords, reaver_capital, reaver_salvage, vharn_garrison, vharn_patrols, frenzied, husk_nights) | 0.15 |
| `zone` inner (Husks near) | 0.1 |
| `zone` core | 0.2 |
| `air` foul or none | 0.1 |
| `people` survivors | 0.1 |

It feeds the children's arms (3.4) and leans `arms`. Comfortable worlds sit near 0, and the worst near 0.8 to 1.

### 4.7 Three rolled examples

| World | Culture |
| --- | --- |
| Dry; theme water_tax; warlords; post L | split (A guards, B herds and trades); arms side_a; fierce one_side A; young side_b; garb by_sex; cover mask; caste masters; labour bonded; rite public; reign warband; roam herders (B); guest proving |
| Sealed domes; post H; ran company; running clerks | space close; self herd; sexes same; arms few; garb uniform; caste vertical; labour machines; rite none; reign council; guest trade_only; manner reserved |
| Temperate; post H; running carers; creed the_unmoving | space apart; self lone; young kept; labour machines; arms none; guest closed (a hatch at each homestead); manner reserved; rite private |

## 5. Data and code

### 5.1 Schema

The schema adds layer tables to `prototypes/poi-generator/data/world-layers.txt`, in the existing format. It needs one new keyword, `trait`, a set of `key value` pairs where the last value wins (unlike `bias`, which adds up), and one new record kind, `culture`, for a faction's fixed set:

```text
space close | name close, touching | weight 3 | trait chat 0.75 band 1.7 ring 1.5 seats all touch 1 stranger 0.75 group 3-6
space apart | name apart, alone | weight 0.6 | trait chat 2.6 band 5 ring 0 seats one touch 0 stranger 1.8 group 1-2 homes scattered | like self.lone*3 labour.machines*2
sexes split | name roles split by sex | weight 2 | trait share 0.95 mix work_apart | sides yes
arms side_a | name only side A armed | weight 2 | trait armed_a 0.7 armed_b 0 | req sexes!=same
garb uniform | name one cut and colour for all | weight 1.5 | trait clusters 1 deviant 0.01 | like cover.sash*1.5
labour clones | name clone workers | weight 0.8 | req post=H | trait share 0.4 | poi vat_hall 1 | tags bunk_halls
reign warband | name ruled by the strongest | weight 1.5 | trait leader armed seat ring | tags trophy_walls
culture vharn | space wide | self herd | caste castes | garb by_caste | labour bonded | rite public | rule officers | guest hostile | manner reserved | drift 0.1
```

`lead` is a fact (`female` or `male`), rolled 50/50 when any value with `sides yes` is rolled. Role groups to sides are rolled the same way, and kept as facts (`side.fight=a`).

In code:
- a `CultureReading` record (one enum per axis, plus the merged traits) is built once per world from `WorldRoll`;
- `Culture.For(place)` applies the faction set or the town drift;
- the result rides on `WorldConditions` into the bestiary and on `Townsfolk` into towns.

Nothing in the generators names a value: they read the traits.

### 5.2 What it touches

| System | File | What changes |
| --- | --- | --- |
| Layer kinds and roll | `prototypes/poi-generator/src/Gen/WorldLayers.cs:10-14` (`LayerKind`), `:497` (after `Creed` in `Roll`); `LayerParser` | 15 kinds, `trait`, `culture` records |
| Layer data and doc | `prototypes/poi-generator/data/world-layers.txt`; `docs/world-layers.md` 2, 4.4, 8.2; `docs/world-layers-tools/roll_worlds.py` | Tables, chain step, rarity checks |
| World to bestiary | `prototypes/slice/src/Surface/WildlifePatch.Signature.cs:25` (`Story`) | Carry `CultureReading` onto `WorldConditions` |
| Signature kinds | `shared/Bestiary/Core/Signature.cs` (clothes `uniform` and `festive`, `Armed`, `Always`) | Garb modes reuse these; roam feeds `Always` |
| Person roll | `shared/Bestiary/People/PeopleGenerator.cs:324` (sex, 50/50 today), `:287` `Fighter`, `:296` `Arm`, `:420` flavour prop, `:257` `FoundersOf`, `:619` `HardenedChance` | Sex by role and side; armed by `arms`; garb and cover; children never armed |
| Wanderers | `PeopleGenerator.cs:450` (`GroupMax`), `shared/Bestiary/People/Party.cs` (`Lean`) | Band make-up by `sexes` and `roam`; lean from `fierce` |
| Temper | `shared/Bestiary/People/Temper.cs` (`Roll` lean, caution, odds) | `guest` multiplies the stance odds |
| Band distances | `shared/Bestiary/Sim/Brain.Folk.cs:8-13`; `shared/Bestiary/Core/WildlifeData.cs:449` (`Spacing` 2.4) | Scaled by `space` |
| Personal space | `shared/Bestiary/Sim/PersonalSpace.cs`, `prototypes/slice/src/Places/Town.Space.cs` (give way) | Comfort radius; lower caste and young give way more |
| Town routine | `prototypes/slice/src/Places/Town.cs:783` (`Next`), `:893` (children's minder), `:899` (chat partners), `:907` (`Idle` facing) | Chat distance, partner by sex and caste, minder rule, packs, schedule spread, pauses for rite |
| Gathering | `prototypes/slice/src/Places/Town.Placement.cs:121` (`Gather`) | Ring radius; a spot per sex or caste; temple hours |
| Reactions and gestures | `Town.Reactions.cs`, `Town.Gestures.cs`, `Town.Mood.cs`, `Town.Standing.cs`; `shared/Bestiary/People/Gestures.cs`; `shared/Bestiary/Rigs/HumanRig.cs:7` (`BodyAct`) | Who steps forward, quarter wave-offs, `manner` rates; Nod, Shake and Laugh acts come from the owner's gestures job |
| Town spawns | `prototypes/poi-generator/src/Gen/Dressing.cs:56-138` (`Townsfolk`), `Features.cs:213` | Roles by labour and rule (overseer, clergy, leader); spots per quarter |
| Copies | `prototypes/poi-generator/src/Gen/Occupancy.cs:380` (`Copies`) | Clone workforce reuses the one-look path for a subset |
| Rooms and programs | `prototypes/poi-generator/data/rooms.txt` (chapel, quarters, mess, washroom...), `programs.txt` (barracks...), `depths.txt` | New rooms: crèche, dorm hall, shrine niche, overseer post, gate rack |
| Builders' habit | `prototypes/poi-generator/src/Gen/Arch/Depths.cs:99` (`CultureOf`: orderly, radial, linear, warren) | Lean by self, caste and rite (pervasive leans radial round the temple) |
| Houses | `prototypes/poi-generator/src/Gen/Arch/HomeStyles.cs:17` (`HomeHints`), `prototypes/planet-surface/src/World/Infra/FlavourRuins.cs` (`CultureAt`, street salts), `CityPlan.cs:463` | Model repetition by `self`; spacing by `space`; compounds; caste districts and rings |
| Furniture | `shared/Deco/Core/Arrange/DecoPlanner.cs` | Seat gaps, long communal tables against small ones, altar facing, gate weapon racks, cover racks |
| Signs and marks | `docs/signs/`; `marks` layer | Frame colours by caste or rule (design doc: "a world's culture and tech level can vary the frames and colours") |

### 5.3 Phases

Each line is its own small job, per the owner's rule not to bury asks.

**Phase 1: cheap and visible.** Data, constants and choices only; no new meshes or rooms.

| Job | Scope | Check that proves it |
| --- | --- | --- |
| 1a Roll | Tables, `trait`, `CultureReading`, strength, extremes, desperation, faction sets, town drift, debug readout | `CultureTest`: 1,000 fresh random worlds (never pinned); each value's share within ±30% of what its weight and the world's leans give it; no coherence rule broken; near-default worlds 30% (±8 points) and 4 to 6 off on the rest; extremes on 1 in 8 worlds (±40%), two on about 1 in 60; secluded on at most 1 in 30; factions keep their set; towns keep 80% (±8 points) of the world's values |
| 1b Spacing and grouping | Chat distance, band spacing, stranger scale, seats, group size, partners by sex and caste, single-sex bands, children's minder and packs, schedule spread | Slice staging per `space` and `sexes` value forced on fresh worlds: median chat distance within 15% of the trait; mixed groups 35 to 65% under same and under 5% (social, outdoors) under apart; 90% of children within their minder distance and of the right side; frame time inside 16.7 ms on the walk route (`PlacePerf`) |
| 1c Dress | Garb modes and cover piece over climate, era and role | Hue clusters in a town crowd: uniform 1, by_sex 2, by_caste equal to the caste count; cover on 100% of adults outdoors; vacuum and bad air still suited (existing checks); screenshot pairs of two cultures, viewed before sending |
| 1d Arms and temper | `arms` in `Fighter` and `Arm`; children's arms by culture and desperation; `fierce` in `Temperament` and `Party.Lean` | Armed share per side and role within ±10 points; zero weapons in town under none; armed children's share rising with desperation (the top third of worlds at least 3x the bottom third) and under everyone above fighters; hot side's Aggressive share 2x or more the other's; the ripple-order checks still pass |

**Phase 2: places.**

| Job | Scope | Check |
| --- | --- | --- |
| 2a Housing kinds | Communal dorm halls, family houses, compounds with an inner court, scattered homesteads, by `self`, `space`, `sexes` and `caste` | PoiTest and SurfaceTest on fresh worlds: housing kind matches the culture; every room is reachable and every door opens by hand (existing checks) |
| 2b Quarters | Two halls and two gathering spots under apart; an inner court under secluded; an elders' quarter | 90% of people spawn and sleep in their own quarter; no lock anywhere |
| 2c Faith places | Home shrines, street shrines, the temple at the centre, facing rules | Shrine counts per home and per 100 m within ±25%; pervasive towns' doors face the temple more often than chance |
| 2d Castes and class | Caste districts with distinct finishes and colours, vertical class in towers | Finish and colour differ between districts; the high caste's share on upper floors over 80% |
| 2e Labour places | Clone barracks and vat hall, bonded barracks and overseer posts, robot bays | Present whenever the labour value is; worker counts match the share |
| 2f Seats of rule | Meeting circle, chief's house, fighting ring, common hall | One per town, matching `reign` |
| 2g Furniture | Seat gaps, table forms, altar facing, gate weapon rack, cover racks | `DecoPlanner` tests: walkways stay clear; seat gaps match `space` |

**Phase 3: how peoples treat sleepers.** Sleepers are visitors with terrible tools (owner, 15 Nov): a combine harvester that can maim and consume people as well as trees, a sleeper tool that makes it easy to zap someone carelessly, and late-game weapons that can bomb a village. A people treats a sleeper as what that sleeper has shown it can be. A sleeper's means with people stay gestures, trade, threats and violence: no dialogue, and nothing a sleeper does frees or reforms anyone.

**3.1 The deeds a people remembers.** `Deed` and `PlaceMemory.Standing` already exist (`Town.Standing.cs`: help, trade, thanks, threat, harm, kill, rob, word). Phase 3 adds the sleeper's atrocities and a second number beside standing, **dread** (0 to 100): standing is how much they like you, dread how much they fear what you can do.

| Deed | What counts | Standing | Dread |
| --- | --- | --- | --- |
| Help, Trade, Thanks | As today | +6, +2, +1 | 0 |
| Threat | A weapon or the tool aimed at someone, a warning shot | -4 | +3 |
| Zap | A person hit by the sleeper tool or a stray shot, alive after | -10 | +8 |
| Harm, Kill, Rob | As today | -15, -40, -25 | +5, +15, +4 |
| Massacre | 3 or more of a people killed within 60 s | -60 | +35 |
| Harvest | A person cut or consumed by the harvester or a tool, living or dead | -70 | +45 |
| Bomb | An explosive or a strike hitting a town: a building wrecked, or 2 or more killed at once | -90 | +60 |
| Desecration | Any of the above inside a shrine or temple (`rite` public or pervasive) | x2 | x2 |

A deed counts only when someone sees it (in sight within 60 m) or finds what it left (a body, a crater, a stripped field) within a game day: an unseen zap of a lone person is found half the time. Standing drifts back toward 0 by 2 a game day. Dread halves every 10 game days, and an atrocity's (massacre, harvest, bomb) dread never falls below 20 at the town it happened to.

**3.2 How word travels.** Inside a town, word goes person to person as robbery's word does now. Between places:

| Reach | How fast | Strength |
| --- | --- | --- |
| Towns within 2 km | 1 game hour | 100% |
| Towns within 8 km | 1 game day | 60% |
| The rest of its region (same town seed) | 3 game days | 40% |
| Every place of the same faction | 1 game day with radio (post M or H), 3 days by runners (post L) | 50% |
| Further | Only when a trader or wanderer carries it (each one met carries 25%) | |

Atrocities travel twice as far at the same speeds. Culture bends it: `self` herd and tight x1.5 reach, loose and lone x0.6; `space` apart x0.5; `roam` traders and herders x1.5; `guest` closed x0.7 (they talk less with strangers, so less reaches them). Strength scales both the standing and the dread a town takes from the news.

**3.3 The response ladder.** Each town (and each band of its people met on the road) reads a **threat** from what it knows: `threat = dread / 100 - standing / 200`. Culture then moves the thresholds. The steps, mildest first:

| Step | Default threshold | What shows |
| --- | --- | --- |
| Guest-right or welcome | threat below 0, and `guest` open or guest_right | As 3.13 |
| Wary | 0.15 | The checkpoint (today's wary place) |
| Hiding | 0.35 | Children and the frail indoors, doors shut (never locked), streets empty but for the armed |
| Arming | 0.5 | Everyone the culture lets carry draws; the armed share rises to arms everyone's |
| Flight | 0.6 | The town empties away from the sleeper and stands empty for 1 to 3 game days |
| Attack on sight | 0.75 | Hostile at first sight; bands sent to look for the sleeper |

Each culture leans differently, so some cower, some band together and some hunt:

| Value | Thresholds | What shows |
| --- | --- | --- |
| `fierce` calm | Hiding and flight -0.15; never attack below 0.95 | They cower and run |
| `fierce` all | Arming and attack -0.25; flight +0.3 | They hunt: bands go after the sleeper |
| `fierce` one_side | The hot side arms and attacks at -0.2; the other side hides at -0.2 | One side goes out, the other in |
| `fierce` young | Ages 16 to 25 attack at -0.2 | The young go out alone; elders call them back |
| `self` herd, tight | One response for the whole town | They band together: each step is the town's at once |
| `self` loose, lone | Each person rolls their own step, the town's ±0.15 | Some flee, some fight |
| `arms` none | No arming step; flight -0.15 | Tools at most |
| `arms` everyone | Arming -0.2 | Everyone draws at once |
| `space` apart | Hiding -0.15 | Each homestead shuts itself in |
| `roam` foragers, herders, seasonal | Flight -0.2 | Camp struck and gone within a game day |
| `guest` guest_right | A received guest's truce holds until a harvest, bomb or massacre against them, then breaks for good | |
| `guest` hostile | Attack -0.3 | |
| `guest` proving | Each predator kill near town lowers threat by 0.05, to -0.2 at most | |
| `reign` warband | Attack -0.15; hunting bands sent | |
| `reign` council | Each step comes a game hour later | |
| `caste` masters, `labour` bonded | Overseers arm; workers never arm and never join anyone: they run, or stand and stare | |
| `elders` revered | Every threshold +0.05 | Elders hold them back |

**3.4 Jobs and their checks.**

| Job | Scope | Check |
| --- | --- | --- |
| 3a Who meets you | The leader by `reign` steps forward and trades (the owner's leader rule); clones and bonded never trade | Scripted approach on fresh worlds: the first to meet the sleeper is the reign's leader in 90% or more of towns; stop distance matches the stranger trait ±20% |
| 3b Guest codes | `guest` in `Temper`; guest_right truce; proving's standing from kills; closed hatch trade (goods down, step back 6 m) | The truce holds unless shot first; hatch trades complete with no approach closer than 5 m; standing moves the right way |
| 3c Quarters and offences | Entering the other sex's or caste's quarter: wave-off within 3 s, Warn by 10 s, a small standing loss after 20 s; never blocked, never locked. A drawn weapon in a shrine, or anywhere under `arms none`, drops stance a step | Bot walk-ins: the timings hold; never a door that won't open |
| 3d Deeds and dread | The deeds of 3.1, seen or found; dread beside standing | Scripted deeds on fresh worlds: each moves standing and dread by its numbers ±10%; an unseen zap is found 50% ±15 points of the time |
| 3e Word | Word between towns and to the faction, as 3.2 | After a bomb, towns within 2 km reach dread 54 or more within a game hour; towns at 5 km get 60% ±10 points within a day; same-faction places 50% within their day; a town 30 km off with no trader passing hears nothing |
| 3f Response | The ladder of 3.3, with each culture's lean | After a harvest seen in town: calm cultures hide or flee in 80% or more of towns; fierce all ones attack or send bands in 80% or more; herd towns all take one step, lone towns spread over 3 or more; bonded workers never arm |
| 3g Manner | Nod, shake and laugh rates and triggers by `manner`, from the gestures job's acts | Counts per hour within ±30% of the trait; nod and shake keep their meaning everywhere |

## 6. The owner's decisions (15 Nov)

| # | Question | Decision |
| --- | --- | --- |
| 1 | Whose culture? | Per world, and per town or faction: each world rolls its own, its towns drift from it (by the town seed its houses use), and each faction keeps its own fixed set wherever it lives. Culture belongs to the community, never to an alien people's looks |
| 2 | One-sided extremes? | Kept, with the side swapped at random per world (`lead`), and seclusion on at most 1 settled world in 30 |
| 3 | Children and weapons | Children can be armed: children aren't treated as sacred in every culture. Whether they are depends on the world's desperation and its culture together (3.4, 4.6). The Hardened to Fighter path stays, and no check forbids armed children |
| 4 | How dark does bonded labour go? | It can be dark: bonded labour is shown as harshly as the culture runs it. Sleepers are visitors who never free anyone and don't interfere with systems. Their means with people are gestures, trade, threats and violence, and in many games violence is what players end up using. Shooting the masters frees no one: the workers run, or just stand there and look at the sleeper. This isn't a Hollywood film. Nothing to free, reform or rescue |
| 5 | Does a culture react to the sleeper's own sex? | Never: sleepers always get the outsider rules |
| 6 | A sleeper in a forbidden quarter | A wave-off, then a warning, then a small standing loss for lingering. Never blocked, never locked |
| 7 | Gestures | Each culture's manner. A separate gestures job (nod yes, shake no, laugh) comes later and takes its dialect from `manner`; phase 1 leaves the hook (3.15) |
| 8 | Merge `creed`? | No: creed stays as belief, and `rite` is practice |
| 9 | How strong? | About 3 in 10 settled worlds near today's default, and about 1 in 8 with an extreme (4.2) |
| 10 | What sleepers can do (later the same day) | Peoples treat sleepers as what they can be: they remember harvesting people, careless zaps and bombing, word spreads to nearby towns and the faction, and each culture's response shifts with it (phase 3) |

## Sources

1. The Naked Sun (Solaria): https://en.wikipedia.org/wiki/The_Naked_Sun ; https://interestingliterature.com/2021/03/isaac-asimov-the-naked-sun-review-analysis/
2. Planet of Hats: https://www.tvtropes.org/pmwiki/pmwiki.php/Main/RaceOfHats ; https://tvtropes.org/pmwiki/pmwiki.php/PlanetOfHats/StarWars ; https://tvtropes.org/pmwiki/pmwiki.php/PlanetOfHats/StarTrek
3. Le Guin, gender and anarchism: https://theanarchistlibrary.org/library/lewis-call-postmodern-anarchism-in-the-novels-of-ursula-k-le-guin ; https://seanguynes.com/2015/11/03/rethinking-the-dispossessed/
4. Herland: https://www.Wikipedia.org/wiki/Herland_(novel)
5. Ancillary Justice (pronouns, gloves): https://www.seattlereviewofbooks.com/reviews/pronouns-in-space/ ; https://reactormag.com/post-binary-gender-in-sf-ancillary-justice-by-ann-leckie/
6. The Handmaid's Tale colour code: https://speculativefictions.web.unc.edu/color-code/ ; https://www.shmoop.com/study-guides/handmaids-tale/characterization.html
7. Caesar's Legion: https://fallout.fandom.com/wiki/Hierarchy_of_Caesar%27s_Legion ; https://www.tvtropes.org/pmwiki/pmwiki.php/Characters/FalloutNewVegasCaesarsLegion
8. Proud Warrior Race: https://Tvtropes.org/pmwiki/pmwiki.php/Main/ProudWarriorRace
9. Klingon dress: https://www.slashfilm.com/2161475/why-klingons-dress-same-star-trek-explained/
10. Castes: https://TVTropes.org/pmwiki/pmwiki.php/Main/HiveCasteSystem ; https://tvtropes.org/pmwiki/pmwiki.php/Literature/BraveNewWorld ; https://shmoop.com/study-guides/brave-new-world/society-class-theme.html
11. Minbari castes and Grey Council: https://babylon5.fandom.com/wiki/Minbari ; https://en.wikipedia.org/wiki/Delenn
12. Clones: https://tvtropes.org/pmwiki/pmwiki.php/Main/ExpendableClone ; https://www.tvtropes.org/pmwiki/pmwiki.php/Main/WalkingTransplant
13. Quarian Pilgrimage and Migrant Fleet: https://masseffect.fandom.com/wiki/Pilgrimage ; https://www.cbr.com/mass-effect-quarian-pilgrimage-explained/
14. Belter gestures: https://www.syfy.com/the-expanse/photos/the-science-of-the-expanse-season-1-episode-3 ; https://dspace.ut.ee/items/eca3cc96-fea6-47b9-b348-2b8faf6b97f9
15. RimWorld Ideology: https://rimworldwiki.com/wiki/Meme ; https://www.rimworldwiki.com/wiki/Precepts
16. Dwarf Fortress ethics: https://dwarffortresswiki.org/index.php/Ethic
17. Proxemics: https://www.ebsco.com/research-starters/social-sciences-and-humanities/proxemics ; https://www.ebsco.com/research-starters/communication-and-mass-media/personal-space
