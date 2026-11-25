# Culture: research and plan

The owner's ask (15 Nov): a culture layer for worlds that sets social distance, individuality, gender norms and roles, castes, religion, clone labourers and political systems. It shows in how far apart people keep, who groups with whom, where the children are, what each sex wears, who carries weapons, who is aggressive, who wanders, and in places and flavour buildings. It also changes how people treat each other and the sleeper.

Research and design only: nothing here is built. The hard rules hold throughout:
- no written text or dialogue: culture shows through behaviour, dress, arrangement and architecture;
- nothing bespoke: culture is data in rolled layers, and every effect is a rule over existing parts;
- no locked doors: segregation is shown by who is where and how they react, never by a lock;
- the player is a sleeper;
- works are named as inspiration only, and nothing is lifted from them.

## 1. Summary

- **15 independent axes**, each a small layer with 3 to 7 values, in a new `Culture` group of `world-layers.txt`. The ids are `space self sexes arms fierce young garb caste labour rite rule roam guest elders manner`, plus a rolled fact `lead`, which says which sex is side A.
- **The culture follows the community, never the body.** It rolls per world, drifts per town or region from the town seed that already exists (`FlavourRuins.CultureAt`), and is fixed per faction. Folk and appearance never set it, which keeps the design doc's rule that appearance is never tied to role, faction or behaviour.
- **Every sex-asymmetric value is symmetric in the data.** It names side A or side B, and each world rolls which sex is A, 50/50. Role groups are also assigned to sides by roll, so the generator holds no "men do X" rule.
- **What exists already and is reused:**
  - the `creed` layer (what survivors believe), which `rite` complements (how they practise it);
  - `ran` (who ran the old world), which leans the present `rule`;
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

**Children are never armed, on any value.** Today a young person rolled into an adult job is `Hardened` (`PeopleGenerator.cs:327`) and, as a guard, gets a weapon through `Fighter()`. That needs closing (question 3).

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
| bonded | 1 | None | 50% | Plain garb, no weapons; gangs of 4 to 8 with one armed overseer; barracks with bunks. They look to the overseer before reacting to the sleeper |
| rota | 1.5 | None | People | Everyone works shifts; a horn marks the change, and groups cross town at once |

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

### 3.11 `rule`: the present political system

`ran` is the old world's rulers; `rule` is the survivors'. It also decides the owner's "every group has a leader you trade with".

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

The body-language doc's greeting form, beckon form and signature gesture roll alongside `manner` (section 3.3 of that doc, not yet built). **Nod means yes and shake means no on every world**: they join the universal signals, because the player must read them. The owner's nod, shake and laugh job supplies the acts, and culture sets only their rates and triggers.

## 4. Rolling culture

### 4.1 Per world

Culture rolls in chain step 7, after `creed` (world-layers.md 8.2), and only on inhabited worlds. Each axis forks the seed by its own name, as layers do. Leans are `like` multipliers carried by values that already exist, so a culture follows from the world rather than a table:

| World fact | Leans |
| --- | --- |
| `presence` warlords, gangs, militias, bandit_riders; bias `strife` | arms everyone or side_a x2; fierce one_side or young x2; rule warband x3; guest proving or closed x2 |
| `fall` famine or drought; theme water_tax | self tight x2; caste masters x2; labour bonded x2; rule chief x2 |
| `build` sealed or pressure_dome, `air` bad | space close x2; self tight or herd x2; garb uniform x2; cover mask x2 |
| `post` H with `running` clerks, cleaners or carers | labour machines x3; space wide or apart x2; young kept x1.5 |
| `trade` vat_stock, `fall` cloning_mishap | labour clones x4 |
| `ran` great_houses, generals, faith, company, scholars, elected | caste masters or castes x2; garb uniform, arms fighters x2; rite public, rule priests x3; caste ranked, garb by_role x2; caste flat x2; rule council or assembly x2 |
| `trade` herding, pilgrimage, markets, military | roam herders x3; roam journey, rite public x2; guest trade_only or open x2; arms everyone x1.5 |
| `creed` not no_creed | rite private or public x2 |
| `people` survivors | caste flat x2; roam foragers x2; self loose x1.5 |
| `phase` falling; `zone` core | guest closed or hostile x1.5 |

**Tech never leans sexes, caste or young.** The SF canon puts its most rigid castes and sex codes on high-tech worlds, and "low tech means backward" is a stereotype to avoid.

**Coherence rules** (`rule` records in the same file):
- `sexes same` forbids side-sided values elsewhere;
- `self herd` forbids `garb free`;
- `garb by_caste` needs `caste castes`, `masters` or `vertical`;
- `labour clones` needs post H;
- `space apart` forbids `rule assembly`;
- `young kept` forbids `space close`.

### 4.2 Rarity

Rarity aims:
- On a settled world, 4 to 6 axes come off their default.
- About 3 in 10 settled worlds read as near default, matching owner decision 4 for moods.
- About 1 in 8 has one extreme value (weight under 1); about 1 in 60 has two.

The roll script (`docs/world-layers-tools/roll_worlds.py`) gets these as checks.

### 4.3 Per place

A town takes the world culture and drifts. On each axis, with the town seed from `FlavourRuins.CultureAt` (the same seed its houses use), 80% keep the world's value and 20% step one value along. Trade hubs and spaceports take extra drift, and their outsiders (`Founders.Outsiders`) keep their own home culture, so a hub's crowd is mixed. A world with two folk still has one culture per town, never per folk.

### 4.4 Per faction

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

### 4.5 Three rolled examples

| World | Culture |
| --- | --- |
| Dry; theme water_tax; warlords; post L | split (A guards, B herds and trades); arms side_a; fierce one_side A; young side_b; garb by_sex; cover mask; caste masters; labour bonded; rite public; rule warband; roam herders (B); guest proving |
| Sealed domes; post H; ran company; running clerks | space close; self herd; sexes same; arms few; garb uniform; caste vertical; labour machines; rite none; rule council; guest trade_only; manner reserved |
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
rule warband | name ruled by the strongest | weight 1.5 | trait leader armed seat ring | tags trophy_walls
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
| 1a Roll | Tables, `trait`, `CultureReading`, faction sets, town drift, debug readout | `CultureTest`: 1,000 fresh random worlds (never pinned); each value's share within ±30% of its weight; no coherence rule broken; extremes on 1 in 8 worlds (±40%); factions keep their set; the roll script agrees |
| 1b Spacing and grouping | Chat distance, band spacing, stranger scale, seats, group size, partners by sex and caste, single-sex bands, children's minder and packs, schedule spread | Slice staging per `space` and `sexes` value forced on fresh worlds: median chat distance within 15% of the trait; mixed groups 35 to 65% under same and under 5% (social, outdoors) under apart; 90% of children within their minder distance and of the right side; frame time inside 16.7 ms on the walk route (`PlacePerf`) |
| 1c Dress | Garb modes and cover piece over climate, era and role | Hue clusters in a town crowd: uniform 1, by_sex 2, by_caste equal to the caste count; cover on 100% of adults outdoors; vacuum and bad air still suited (existing checks); screenshot pairs of two cultures, viewed before sending |
| 1d Arms and temper | `arms` in `Fighter` and `Arm`; `fierce` in `Temperament` and `Party.Lean`; children never armed | Armed share per side and role within ±10 points; zero props under none; zero armed children over every rolled world; hot side's Aggressive share 2x or more the other's; the ripple-order checks still pass |

**Phase 2: places.**

| Job | Scope | Check |
| --- | --- | --- |
| 2a Housing kinds | Communal dorm halls, family houses, compounds with an inner court, scattered homesteads, by `self`, `space`, `sexes` and `caste` | PoiTest and SurfaceTest on fresh worlds: housing kind matches the culture; every room is reachable and every door opens by hand (existing checks) |
| 2b Quarters | Two halls and two gathering spots under apart; an inner court under secluded; an elders' quarter | 90% of people spawn and sleep in their own quarter; no lock anywhere |
| 2c Faith places | Home shrines, street shrines, the temple at the centre, facing rules | Shrine counts per home and per 100 m within ±25%; pervasive towns' doors face the temple more often than chance |
| 2d Castes and class | Caste districts with distinct finishes and colours, vertical class in towers | Finish and colour differ between districts; the high caste's share on upper floors over 80% |
| 2e Labour places | Clone barracks and vat hall, bonded barracks and overseer posts, robot bays | Present whenever the labour value is; worker counts match the share |
| 2f Seats of rule | Meeting circle, chief's house, fighting ring, common hall | One per town, matching `rule` |
| 2g Furniture | Seat gaps, table forms, altar facing, gate weapon rack, cover racks | `DecoPlanner` tests: walkways stay clear; seat gaps match `space` |

**Phase 3: the sleeper.**

| Job | Scope | Check |
| --- | --- | --- |
| 3a Who meets you | The leader by `rule` steps forward and trades (the owner's leader rule); clones and bonded never trade | Scripted approach on fresh worlds: the first to meet the sleeper is the rule's leader in 90% or more of towns; stop distance matches the stranger trait ±20% |
| 3b Guest codes | `guest` in `Temper`; guest_right truce; proving's standing from kills; closed hatch trade (goods down, step back 6 m) | The truce holds unless shot first; hatch trades complete with no approach closer than 5 m; standing moves the right way |
| 3c Quarters and offences | Entering the other sex's or caste's quarter: wave-off within 3 s, Warn by 10 s, a small standing loss after 20 s; a drawn weapon in a shrine, or anywhere under `arms none`, drops stance a step | Bot walk-ins: the timings hold; never a door that won't open |
| 3d Manner | Nod, shake and laugh rates and triggers by `manner`, using the owner's gestures job's acts | Counts per hour within ±30% of the trait; nod and shake keep their meaning everywhere |

## 6. Open questions for the owner

| # | Question | Recommendation |
| --- | --- | --- |
| 1 | Does culture follow the community (world, town, faction) or each folk (every alien people its own ways)? | The community. Folk-tied culture would tie behaviour to looks, which the design doc rules out; the Vharn get theirs as a faction |
| 2 | Keep the sex-asymmetric extremes (segregated quarters, one sex armed, one sex secluded)? | Keep them, always symmetric (which sex is rolled 50/50 per world), rare (secluded at most 1 world in 30), and never tied to folk or tech |
| 3 | Children and weapons: today a young person in an adult job can be a guard and carry a gun | Children are never armed on any culture; hardened children only take unarmed jobs |
| 4 | How dark do bonded labour and slavery go? | Show the system (gangs, overseers, barracks, plain garb, eyes on the overseer), with no chains or beating animations. Freeing them (kill the overseer and they scatter, or join a town) waits for phase 3 or later |
| 5 | Does a culture react to the sleeper's own sex? | No. Sleepers are outsiders in suits and get the outsider rules only; that avoids gating a player by their body |
| 6 | What happens when a sleeper walks into a forbidden quarter? | Wave-off, then a warning, then a small standing loss for lingering; never blocked, never a locked door, never an attack on its own |
| 7 | Fold the body-language doc's gesture dialect (greeting form, beckon form, personal distance, signature gesture) into `manner` and `space`? | Yes: one source of truth, and the dialect isn't built yet |
| 8 | Merge `creed` into culture? | No. Keep `creed` as what they believe and `rite` as how it shows; creed leans rite |
| 9 | How strong should culture be on most worlds? | About 3 in 10 settled worlds near default, 4 to 6 axes off default on the rest, an extreme on about 1 in 8, as in 4.2 |

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
