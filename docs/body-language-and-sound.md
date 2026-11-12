# Body language and telltale sounds

Design pool for how GalHack's people, robots and creatures communicate without words, and how places give away what's in them through sound. Not a commitment and not code. Nothing here is copied from any game, film or book: other games are named only for the idea or role they showed, and sources are listed in section 11.

## 1. Summary

**The lens.** People in GalHack should look intelligent and communicative while being simple and reactive underneath. Every rule in this document is a short reaction to something a person can sense right now, plus a few numbers that change slowly (mood, nerve, memory). Nothing plans and nothing reasons in long chains. Moments that look clever come from two or three of these reactions meeting in front of the player: a woman sees you lift a crate from a stall, runs to the guard and points straight at you. Underneath that is "saw a theft, go to the nearest guard, point at the thief", which is three cheap rules. Halo's Grunts fleeing when their leader dies and F.E.A.R.'s soldiers narrating their moves read as smart for the same reason: a simple rule, shown loudly. Section 9 collects these rules and ranks them by how smart they look against how cheap they are.

**Why it matters here.** The game has no dialogue, written logs or subtitles. So people have to talk with their bodies and with wordless voices, and places have to give away what's inside them through sound. That suits the galaxy: a raised palm reads the same on every world, as a pictogram sign does.

**How it's built.** A gesture is not an animation clip. It's a parameterised motion on the person rig that already exists on `proto/bestiary` (`BodyAct` and `Gesture` in `shared/Bestiary/Rigs/HumanRig.cs`): a family of motion (wave, halt, cower and so on) with a size, tempo, height, tension, lean and target. The same family looks different on a nervous trader, a confident guard, a limping farmer, a child, or a squat heavy-world folk with a different greeting habit. Voices already come from the body and a world dialect (`shared/Bestiary/Audio/Voices.cs`), and small sounds inside places already exist (`shared/Bestiary/Sim/Quiet.cs`). This document extends those three pieces rather than adding new systems.

**What it covers.**
- The inputs that drive everything: who someone is, where they are, how they feel, what the player is doing, and who is near (section 2).
- Over 100 gestures and displays in 17 groups, how each one reads at 20 to 30 m, how crowds spread a mood, and how children and young behave and change the adults round them, including the occasional hardened child who acts like a grizzled adult (section 3).
- How anyone moves between states smoothly and readably, with no flicker, and how they remember you (section 4).
- Actions with consequences: people who drop their goods or weapon, offer something to make you leave, run and leave things behind, or call for help; what robbing does to your standing; and how people react to seeing someone die in front of them (section 5).
- Wordless voices for people, robots and creatures, including children and a wordless way of calling each other by "name" (section 6).
- What each kind of occupant gives away inside a place, how sound moves through rooms, walls and vents, and silence as a tool (section 7).
- How the player reads and uses all of it (section 8), the clever-looking rules ranked (section 9), and the first 12 to build plus three consequences (section 10).

**The hard rules kept.** Nothing is bespoke: every gesture and sound comes from a combination of state, role, occupancy, mood, species, culture, injury and what's happening. No real words, no text, no subtitles. Everything is generated in code: procedural motion and synthesised sound, no recordings and no external assets. No companions. The Hollow stays subtle and nothing labels it. People are humans or human-likes; robots and creatures share the bestiary. Everything has to stay cheap enough for crowds and many POIs, and gestures must read at 20 to 30 m.

## 2. The parts

Everything a body or a voice does is worked out from these inputs. Most already exist in the bestiary, the wildlife sim or the world layers. New ones are marked **new**.

### 2.1 Who they are (fixed at spawn)

| Input | Where it comes from | What it shapes |
| --- | --- | --- |
| Folk | `Figure`'s people: humans or a world's human-like folk | Proportions, so the same gesture fits a squat or a long-limbed body |
| Body | Height, build, sex, age (`LifeStage`: child, adult, elder), stoop, stagger | Amplitude, speed, balance, voice pitch and throat |
| Role | `PersonRole`: worker, trader, guard, farmer, medic, scavenger, elder, child, drunk, raider | Which gesture a situation picks, and how bold it is |
| Carried item | `Figure.Carry`: rifle, club, hoe, cane, bottle | Which gestures use the item (a guard's rifle to port arms, a farmer's hoe raised) |
| Mutation and Husk | Bestiary mutation layer, `Figure.Husk` | Asymmetry, slowness, twitch, a damaged voice |
| Personality (**new**, three numbers from the seed) | Nerve (bold to timid), warmth (cold to friendly), fuss (still to fidgety) | Thresholds, gesture size, idle fidgets, how fast they recover |
| Voice | `Voice.For(figure, dialect, seed)` | Pitch, throat, breath, rasp, pace; the same person always sounds the same |
| Call sign (**new**) | Two or three syllables from the voice seed | How others call this person (section 6.5) |

### 2.2 Where and among whom (from the world)

| Input | Where it comes from | What it shapes |
| --- | --- | --- |
| Occupancy | Bestiary `Occupancy` (friendly, raider, feral) and the world layers' codes (SQT, SCV, GNG, FAC, KPR, CLT, SLP, MIM, HSK, HOL and the rest) | Starting attitude, which gestures exist at all, what a place sounds like |
| Settlement mood | Peaceful, wary or angry, as the design says | Starting state for everyone in it, and how quickly guards warn |
| Gesture dialect (**new**) | Rolled from the world seed next to `Dialect.For(worldSeed)` | Greeting form, beckon form, personal distance, expressiveness, one signature gesture (section 3.3) |
| Faction manner (**new**, one small row per faction) | Section 17's factions | Crisp or loose, synchronised or ragged, how much they taunt |
| Creed | World layers' creeds | Ritual pauses and habits: horn pausers look up at the hour horn, ancestor tenders kneel at graves |
| Place and time | Town, road, POI interior, day or night, light | Indoors at night, people are quieter and startle more easily |
| Who's near (**new**, read each think) | Nearby people within about 15 m: their states, a leader, children, armed friends | Whether someone fights, flees or gives in, and how mood spreads |

### 2.3 How they feel now (changes every think)

Three slow numbers, smoothed so they never jump:

- **Ease**, from distressed (−1) to content (+1). Sets whether gestures are open and loose or closed and tight, and whether voices rise or fall at phrase ends.
- **Arousal**, from sleepy (0) to keyed up (1). Sets tempo, size and loudness.
- **Nerve**, from cowed (0) to dominant (1). Starts at the personality's value and moves with events. Sets whether arousal comes out as threat or as fear.

These three are a simple version of the pleasure, arousal and dominance axes used in emotion research, and they're enough to choose and colour every gesture and voice in this document.

Plus two fast values:

- **Awareness** of the player, from `Senses`: 0 to 1, with "curious" at 0.35 and "noticed" at 1, and a remembered last position held for a few seconds. Nothing reacts to what it can't sense.
- **Hurt**: where and how badly they're injured, from health and the last hit.

### 2.4 What the player is doing (the pressure on each person)

Pressure is one number per person, from 0 to 1, worked out from what that person can sense of the player. It's the main input for warning, fear and surrender. Each part adds; the total is capped.

| What the player does | Pressure added (reference) |
| --- | --- |
| Weapon put away | 0 |
| Weapon carried, lowered | 0.15 |
| Weapon raised, not at them | 0.35 |
| Weapon aimed at someone within 5 m of them | 0.55 |
| Weapon aimed at them | 0.9 |
| Inside their personal distance (about 1.2 m, scaled by the world's gesture dialect) | 0.2 |
| Coming at them fast | 0.15 |
| Following them for more than about 15 s within 10 m | 0.2, growing |
| Standing in the only doorway out of the room they're in | 0.25, and they count as cornered |
| A shot within about 10 m in the last 10 s | 0.4, fading |
| Torch in their face at night | 0.1 |
| Crouched and creeping inside a settlement | 0.1 |
| Inside their home, behind their stall or in a guarded area | 0.2 |
| Standing still with the weapon put away for a while | Takes off up to 0.1 as they get used to you |

Standing and memory then scale the total: a town that knows you as a good trader takes your raised rifle as less of a threat, and one you robbed last week takes it as more (section 4.7).

### 2.5 What's happening (events)

Events are short pulses that every nearby body hears or sees: a shot, a scream, an explosion, a body falling, a body found, a creature in town, a siren, the hour horn, a trade completed, a gift given, a theft seen, a door forced. Each one nudges ease, arousal and nerve, and some trigger a gesture straight away (a startle, a point). This is the same pattern as the wildlife sim's `WorldNoise`: a position, a radius, a time.

## 3. Gestures

### 3.1 How a gesture is built

A gesture is a motion family plus parameters. The rig already has the families (`BodyAct`: `HandsUp`, `Cower`, `WaveOff`, `Beckon`, `Point`, `Shrug`, `Warn`, `Celebrate`, `Grieve`, `Panic`, `Fidget`, `Taunt`, `Present`, `Greet`, `Startle`) and the posture channels (`Sit`, `Work`, `Aim`, `Crouch`, `Reach`, `Retch`, `Down`, `Look`, `ActAmount`, `ActAt`). This document adds a handful of families and these parameters on every family:

| Parameter | Range | What it does |
| --- | --- | --- |
| Size | 0.4 to 1.4 | How far the hands travel. Arousal and expressiveness raise it; injury and fear of being noticed lower it |
| Tempo | 0.5 to 2 | Speed of the repeating part (a wave's sweep). Arousal raises it |
| Height | Low, chest, head, overhead | Where the hands work. Overhead reads furthest |
| Tension | Loose to stiff | Stiff is less overshoot and a fine tremor; loose is swing and follow-through |
| Lean | Back to forward | Forward with high nerve is threat; forward with low nerve is pleading; back is fear or refusal |
| Stance | Narrow to wide | Wide and square reads as standing ground |
| Chest and head | Sunk to lifted | Lifted is pride or threat; sunk is shame, grief or submission |
| Arms | One or both | Both reads further and more urgently |
| Repeats and hold | Count and seconds | A key pose is held at least 0.6 s so it can be read |
| Target | `ActAt`: a point, a person, an exit | Where a point, a present or a wave-off goes |

The same rule governs all of them, which Darwin called antithesis: a threat is big, tall, square, forward and still-headed; its opposite, appeasement, is small, low, rounded, backing off, open-handed and glancing away. Every species and machine gets that one axis, so a player who has learned it on people can read it on a creature or a robot.

### 3.2 Reading at 20 to 30 m

At 25 m a 1.75 m person is roughly 60 pixels tall on a 1080p screen at a typical field of view. Arms are about ten pixels long, hands a few pixels, fingers nothing. So:

- **Silhouette first.** Every important gesture changes the outline: arms above the head, arms out wide, the body low, the body turned away. Palm direction and fingers are only a bonus up close (under about 8 m).
- **Motion over pose.** A big repeating sweep reads when a still pose doesn't. Waves, wave-offs, brandishing and beckoning all repeat.
- **The carried item amplifies.** A rifle swinging from slung to port arms is a long, high-contrast line that reads at 40 m or more. So does a club raised overhead, or a lamp swung.
- **Whole-body cues.** Running, freezing mid-step, dropping to a crouch and turning to face you read further than any hand gesture.
- **Voice backs it up.** At distance the body says what and the voice says how much: a shout carries the urgency that a small figure can't.
- **Exaggerate a little.** Halo's designers found subtle hierarchy didn't read in a fight, and they had to exaggerate the reactions. Gestures that matter (warnings, surrender, panic) start at a size of 1.1 or more.

**Cost.** A gesture is target positions for two hands, a torso bend and pitch, and a head turn, fed to the rig's existing two-bone IK. That costs next to nothing. Distance sets how often it updates: every frame within 15 m, every few frames to 40 m, and beyond 60 m only the posture layer (lean, crouch, arms up or down) on a slow tick. Beyond about 120 m a person is a walking figure with no gesture at all.

### 3.3 Universal signals and world habits

Some signals keep a person alive, and they never change between worlds: hands up, halt, wave-off, point, cower, taunt, flee, and the threat and appeasement axis. The player learns them once.

The friendly, social gestures vary by world, which is where dialect in the body comes from. Each world rolls a small **gesture dialect** next to its voice dialect:

- **Greeting form:** a raised open hand; a raised forearm with a closed fist; both hands lifted briefly; a hand to the chest then out; a dip of the head and shoulders; or a quick lift of the chin and chest (a body-sized version of the eyebrow flash that ethologists found across many cultures, which reads only close up).
- **Beckon form:** palm up with a curl, palm down with a scoop, or a whole-arm sweep. Real cultures disagree on palm up and palm down, so this is exactly the kind of thing a world can vary.
- **Personal distance:** 0.8 to 1.4 times the base. Hall's distance zones vary in size between cultures, though the zones themselves recur.
- **Expressiveness:** 0.7 to 1.3 times the size of all social gestures.
- **Touch:** whether pairs touch shoulders and forearms when talking, greeting or comforting.
- **One signature gesture:** a small motion only this world's folk use, such as knuckles to the forehead, a palm pressed flat over the heart, or two taps on the chest. It appears in greeting, in grief and in celebration, so it reads as this people's own.

Faction manner sits on top. Meridian is crisp and minimal, with gestures small and precise. Reavers are big, crude and showy. Freeholders are loose and unhurried. Ledger traders are open-handed and quick to present. Kessari machines use posture and lights rather than arms (section 3.4.16). Vharn soldiers, where they're human-like, move in step, with gestures copied down the line from an officer.

### 3.4 The set

Each table gives when the gesture fires, how it varies, and how it reads at distance. "Role" means `PersonRole`. Unless a row says otherwise, size and tempo follow arousal, openness follows ease, lean follows nerve, and injury shrinks the hurt side and slows everything.

#### 3.4.1 Greeting

| Gesture | When | How it varies | At distance |
| --- | --- | --- | --- |
| Wave (`Greet`) | A peaceful settlement's people when the player is first noticed at 10 to 40 m with the weapon put away | World greeting form. Traders and children wave bigger and sooner; guards give a short raised hand and keep watching; elders lift a hand from the cane. Shy personalities wave smaller and look away after | Reads to 40 m when overhead and repeating |
| Hail | Someone far off who wants to be seen: a lookout, a caravan leader, a person in trouble | Both arms up and still, or one arm swept side to side overhead. A hurt person hails from the ground | The longest-reading friendly signal, 60 m or more |
| Nod-lift | Passing close in a street, at 2 to 8 m | Chin and chest lift or a head dip, by world. Warm people add a hand lift | Close only; it's colour, not a signal |
| Welcome (open arms) | A friendly elder or trader when the player arrives with good standing or after a gift | Arms out wide and low, palms up, a step forward | Reads to 25 m as a wide outline |
| Farewell | The player walks away after a trade or a gift | A smaller wave or the signature gesture; children follow a few steps | 20 m |
| Double take (**new**) | Someone who remembers the player (section 4.7) notices them | Looks, looks away, snaps back and stops. Then a warm or a wary gesture, by the memory | Reads to 25 m as a sharp head and body turn, and it says "they know me" |

#### 3.4.2 Trade

| Gesture | When | How it varies | At distance |
| --- | --- | --- | --- |
| Beckon | A trader with a stall or a pack notices a neutral or friendly player at 5 to 30 m | World beckon form. Bigger when trade is slow, smaller once the player is close. Ledger traders beckon readily; Reaver black-market dealers beckon low and only once, glancing about | Whole-arm form reads to 30 m |
| Present | Player in the social zone (about 2 to 4 m) at a stall | A hand sweeps along the wares (`ActAt` the goods), or lifts one item up to show it. Proud traders show the best item; nervous ones show and step back | 15 m |
| Weigh | During a trade, while the player browses | Two hands see-saw, head tilted: "maybe" | 10 m |
| Accept | Trade completed | A short nod and a hand to the chest, or a clap, by world; a pleased trader adds a small celebration | 15 m |
| Refuse | A trade they won't make: low standing, or someone who robbed them | A hand pushed out flat and low toward the player, head turned aside; the goods pulled back toward the body | 20 m: the pull-back reads |
| Closed | Trader leaving, or the stall closing at night | Arms folded, or a cloth thrown over the goods | 25 m |

#### 3.4.3 Warning (guards, wary people)

| Gesture | When | How it varies | At distance |
| --- | --- | --- | --- |
| Watch | First step of any guard's attention | The whole body turns to face the player and stays square; the weapon hand moves to the weapon | 30 m: a figure that stops and faces you |
| Halt (`Warn`) | Player inside the guard's warning ring (about 15 to 25 m, by settlement mood) or moving toward a guarded door | One arm straight out, palm toward the player. Two arms if the player keeps coming. A nervous guard's arm shakes; a confident one holds still | 30 m |
| Wave-off (`WaveOff`) | After halt, or from any wary citizen at an armed stranger | A big sweep of the arm away from the body: go away. Toward the way out (`ActAt` the exit), which also tells the player where they're allowed | 30 m |
| Point to the line | A guard holding a boundary | Points at the ground between them and the player, then at the player: no further | 20 m |
| Weapon show | Escalation from halt | Rifle from slung to port arms, or a club shown across the body. Not aimed | 40 m: the weapon's line |
| Ready | Last step before threat | Weapon raised, not aimed, a step forward, wide stance | 40 m |
| Warning shot | Low-tech wary towns and Freeholders, after ready, if the player keeps coming | A shot into the air or the ground near the player's feet | The sound says it all |

#### 3.4.4 Threat (raiders, hostile people)

| Gesture | When | How it varies | At distance |
| --- | --- | --- | --- |
| Taunt (`Taunt`) | Raiders and gangs who've noticed the player but aren't yet in range, or who outnumber them | Arms flung wide, chest out, chin up, a beat on the chest or the weapon. Bigger with numbers and with a leader near; groups taunt in ragged unison | 40 m: the widest outline in the set |
| Brandish | Raiders with melee weapons, or as a group rallies | Weapon shaken overhead, repeated | 40 m |
| Beat the ground | Club and spear carriers, low-tech | Weapon hammered on the ground or on metal; the clang carries | Sound to 80 m |
| Advance | Raiders closing in | A head-forward stalk, shoulders up, spread out from each other | 40 m |
| Cut-off | A raider moving to block the player's way out | Steps sideways into a gap with arms out | 25 m |
| Aim | Last step before shooting | `Aim` channel, after at least one of the above | Section 15 of the design: first shots are wild, accuracy ramps |

#### 3.4.5 Surrender and appeasement

| Gesture | When | How it varies | At distance |
| --- | --- | --- | --- |
| Hands up (`HandsUp`) | Aimed at, unarmed or outmatched | Height falls as fear grows and nerve fails: shoulders, head, overhead. Palms out. A wounded person raises one hand. Bold types raise them slowly and keep their chin up; children cover their heads instead | 40 m |
| Hands out low | Pressure is high but no weapon is aimed at them: an armed stranger closing in | Palms open at hip height, shoulders raised, a small step back | 20 m |
| Back away | Hands up or hands out, and the player isn't closing in | Slow steps backwards, hands kept visible, eyes on the player; they turn and walk once 15 m or more away | 30 m |
| Kneel | Surrender with nowhere to run: cornered, or the group has given up | Drops to the knees, hands up or behind the head. Elders sit instead. Raiders kneel only when their leader is down or most of the group has fallen | 30 m: a lowered outline |
| Drop it (**new**) | A guard or raider surrendering, or a citizen told to by aiming | The weapon is held out to the side, then let fall. Spawns the item (section 5) | 20 m |
| Offer (**new**) | A trader or anyone carrying goods, under heavy pressure | Holds out a bundle or an item at arm's length, head turned away; may set it down and step back (section 5) | 20 m |
| Cower (`Cower`) | Fear high and nerve low, nowhere to go | Crouched, arms over the head, turned half away | 25 m |
| Show the back | Some worlds' submission habit (a dialect choice) | Turns away, head bowed | 20 m |

#### 3.4.6 Fear

| Gesture | When | How it varies | At distance |
| --- | --- | --- | --- |
| Startle (`Startle`) | Any sudden event close by: a shot, a fall, the player appearing within 3 m unseen | A jump, arms flinch up, a half-step back, then a look toward the cause. Big for children and timid people; a guard's startle turns straight into ready | 30 m |
| Freeze | Prey-style response: the threat is far, or hiding seems possible | Stops mid-step, holds still; only the head tracks the player | 25 m, mostly because everything around them is moving |
| Flee | Fear past the flee threshold with a way out | Runs away from the cause toward home, cover or a group, with glances back over the shoulder. Drops what they're carrying when panicked (section 5). A limp makes it a hobble | 60 m |
| Hide | Fear high, cover near | Into a doorway, behind a wall or a cart; peeks out every few seconds | 20 m: the peek reads |
| Huddle | Several frightened people near each other | They close in on each other against a wall, facing outward or burying faces | 30 m as a clump |
| Panic (`Panic`) | Fear at the top: explosions, creatures in town, the leader killed | Arms up and flailing as they run, no fixed direction, collisions | 50 m |
| Tremble | Close-up colour on any fear state | Fine shake in the hands | Under 8 m |

#### 3.4.7 Pain and injury

| Gesture | When | How it varies | At distance |
| --- | --- | --- | --- |
| Clutch | Just hit, and for a while after | A hand presses on the wound (arm, side, leg), the body curls toward it | 20 m |
| Limp | Leg hurt | The rig's stride shortens on that side and the body dips; speed drops | 40 m: gait reads far |
| Doubled over | A heavy body hit | Bent at the waist, one hand on the knee | 30 m |
| Stagger | Dazed, or a blast close by | The existing stagger value, raised for a few seconds | 30 m |
| Down and calling | Downed but alive | On the ground, one arm raised to the group, a cry (section 6) | 30 m: the raised arm |
| Crawl | Downed, trying to reach cover or friends | Drags along the ground toward cover or friends | 25 m |
| Tend | A medic, or a friend, near someone hurt | Kneels by them, hands on the wound, looks up and about | 25 m |

#### 3.4.8 Grief

| Gesture | When | How it varies | At distance |
| --- | --- | --- | --- |
| Kneel by the body (`Grieve`) | Someone finds a dead member of their group, family or settlement, or sees them die (sections 5.6 and 9) | Kneels, a hand on the body, head down. Partners stay longer. Raiders grieve briefly and then rise angry | 30 m |
| Call the group over | Right after finding the body, if others are near | Stands, waves the others in with big sweeps, then points at the body | 40 m |
| Head in hands | Grief while standing | Both hands over the face, shoulders shaking | 20 m |
| Rock | Long grief, sitting | Slow forward and back | 15 m |
| Hold | Grieving over a body | Lifts the shoulders of the body into the lap | 20 m |
| Keen | Grief in company | The group gathers round in a loose ring, heads down, the world's signature gesture | 30 m |

#### 3.4.9 Celebration and joy

| Gesture | When | How it varies | At distance |
| --- | --- | --- | --- |
| Victory (`Celebrate`) | A raider band after a kill, guards after driving off a threat, townsfolk when a predator in town is killed | Arms up, chest out, chin up; Tracy and Matsumoto found this pride display even in athletes blind from birth, so it reads as universal | 40 m |
| Clap or slap | Friendly crowds after a gift, a rescue, or the end of an invasion | Hands together, or slaps on each other's shoulders where the world touches | 20 m |
| Dance | Festival-keeper creeds, celebration crowds, a drunk | Rhythmic sway and bounce at the world's dialect tempo, in loose sync | 30 m |
| Embrace | Reunions: a rescued person meets their own; a child found | Two bodies together, rocking | 20 m |
| Raise a drink | Evenings at bars and fires | A cup lifted to someone | 15 m |

#### 3.4.10 Work

These are the steady loops that make a town look busy. The rig's `Work` channel and `Clock` drive them, and each takes the carried item.

| Gesture | Who | At distance |
| --- | --- | --- |
| Hammer or strike | Workers at benches, smiths, breakers | 30 m, with the tap sound |
| Dig or hoe | Farmers in fields | 50 m |
| Carry and haul | Workers between places, with a box or sack; it slows them | 50 m |
| Bench work | Workers and medics at a bench: bent, small hand motions | 20 m |
| Sweep | Keepers and cleaners | 25 m |
| Tend stall | Traders arranging goods | 20 m |
| Repair, crouched | At a machine or a vehicle | 25 m |
| Stand watch | Guards walking a beat or leaning on a post, looking about | 40 m |

#### 3.4.11 Idle and social

| Gesture | When | At distance |
| --- | --- | --- |
| Weight shift, fidget (`Fidget`) | Standing about; the fuss value sets how often | 20 m |
| Look about | Every few seconds when idle | 25 m |
| Lean, sit, squat | Against walls, on steps, at fires | 30 m |
| Stretch, yawn | Morning and night | 20 m |
| Drink, smoke, eat | The `Clock` channel's slow loops | 15 m |
| Chat in pairs | Two people facing each other; the one "speaking" makes small beat gestures in time with their voice, the other nods and makes small backchannel sounds; they swap every few seconds | 25 m: two figures turned to each other, one moving |
| Laugh | A pair or group; head back, shoulders shake, a hand slaps a knee or a shoulder | 25 m |
| Shrug (`Shrug`) | Uncertainty, a failed trade, losing sight of something | 20 m |

#### 3.4.12 Curiosity and attention

| Gesture | When | How it varies | At distance |
| --- | --- | --- | --- |
| Head turn, then body turn | Awareness past curious (0.35) | The head goes first, then the body if it holds; this two-step is the cheapest "they've noticed" | 30 m |
| Peer | Something far off, bright light or a strange sound | A hand shades the eyes, the body leans forward | 25 m |
| Lean in | Something interesting close by: the player's odd vehicle, a strange item | Torso forward, hands behind back or on knees | 15 m |
| Point it out (`Point`) | One person shows another something: the player, a ship overhead, a creature | Points, then looks at the other person, then back | 40 m |
| Approach to look | Children, curious personalities, a crowd forming | Comes closer to the social zone and no further; stops when the player turns to them | 30 m |
| Tilt | Puzzled by something | Head tilted, one hand to the chin | 10 m |

#### 3.4.13 Disgust and disapproval

| Gesture | When | At distance |
| --- | --- | --- |
| Recoil | Close to a corpse, rot, a Bloom growth, a Husk | 20 m |
| Turn away | A rude act: the player looting a body in town, walking into a home | 20 m |
| Waft | Bad smell or smoke: a hand waved in front of the face | 10 m |
| Retch (`Retch`) | Gore, strong rot | 20 m |
| Spit | A low-tech world's contempt, toward the player after a robbery | 10 m |

#### 3.4.14 Status and confidence

| Gesture | When | At distance |
| --- | --- | --- |
| Hands on hips, chin up | Leaders, bosses, confident guards at rest | 30 m |
| Arms folded | Waiting, doubtful, refusing | 25 m |
| Block | Standing square in a doorway or a gap | 25 m |
| Step aside | Low status making room: workers for guards, everyone for a gang boss, people for the player if they fear them | 25 m |

#### 3.4.15 Group signals (coordination)

These give the illusion of a team talking. The rule behind each is "the first to see it does this", and the others react to the gesture as if it were an event.

| Gesture | When | At distance |
| --- | --- | --- |
| Rally | A leader or guard calling others in: big arm sweeps toward themselves | 40 m |
| Point there | Telling others where the threat is; the others turn to look where they point | 40 m |
| Hush | Someone hiding or sneaking signals others to be quiet: palm pressed down, or a finger to the lips up close | 15 m |
| Split | A leader signals left and right with each arm; the group spreads (the raider version of flanking) | 30 m |
| Alarm | Both arms waving overhead while running toward the group: danger | 50 m |
| Gather the children | An adult's arm sweeps behind them toward a door or behind a wall (section 3.6) | 30 m |

#### 3.4.16 Robots

Robots share the bestiary's bodies and rigs, so they use the same antithesis axis without human gestures. Kessari and tidy machines speak through posture and lights; feral robots through broken motion.

| Body signal | Meaning | At distance |
| --- | --- | --- |
| Rise and extend: body raised to full height, sensor head up | Alert, warning | 40 m |
| Retract and fold: low, limbs in, sensor turned away | Yielding, retreat, surrender | 30 m |
| Status lights shifting along a row | A state change; the change itself is the tell (weapon doc, section 7) | 50 m at night |
| A slow sweep of the sensor head | Searching | 30 m |
| A tool raised and held | The robot version of halt | 30 m |
| A jerky, repeated half-gesture | Feral robots: an old routine stuck in a loop (a clerk machine "stamping" at the air) | 25 m |
| Pausing and resuming | A service machine yielding the way | 20 m |

#### 3.4.17 Creatures

Creatures already have pose intentions: `WantCrouch`, `WantHidden`, `WantGraze`, `Rear`, `Lunge`, `Alert`, `Brace` and `LookAt`. This adds parameters, not new states.

| Display | When | How it varies |
| --- | --- | --- |
| Head up and still (`Alert`) | Awareness past curious | Prey go stiller; the whole herd lifts heads in a wave (section 3.5) |
| Rear and roar (`Rear`, the existing `Warn` state) | Territorial giants and nest guards, before any charge | Bigger animals rear higher; injured ones don't rear |
| Puff up | Small creatures threatened: frills, spines, inflated throat sacs from the body parts | From swappable parts |
| Brace and paw (`Brace`) | Charge wind-up | The existing telegraph |
| Crouch and tuck | Appeasement and fear: lower, tail down, head low | Pack members to their leader, beaten animals |
| Play bow | Young animals, and some calm adults with their young | Front low, rear up, a hop |
| Stamp | Herd alarm, and a giant's warning | The sound carries |

### 3.5 Crowds and how a mood spreads

A crowd is cheap because it's just people reading each other. Each person, when they think, looks at the nearest few others they can see or hear within about 15 m and pulls their own arousal and ease a little toward theirs. Fear pulls faster than calm, so panic spreads quickly and settles slowly, which is how real crowds behave. Crowd simulation research finds the same pattern: density and nearness drive how fast fear spreads.

Four small rules make it look right:

- **Delay by distance and sight.** A person reacts to a neighbour's startle after 0.1 to 0.6 s, plus more if they only heard it. So a shot doesn't flip a square at once: heads turn in a visible wave rolling out from the cause. This is the single most convincing crowd effect and it's free.
- **Personal thresholds.** Each person's nerve shifts their thresholds a little, so some run at once, some freeze, some stay and stare.
- **Anchors.** Calm guards and elders pull hard toward calm. A square with a steady guard in it settles in seconds; one whose guard ran stays panicked. Shoot the guard and the square breaks, the same idea as Halo's Grunts breaking when their Elite dies.
- **Density amplifies.** Packed crowds spread fear faster; scattered people barely affect each other.

The crowd shapes that come out of this:

- **Gawking ring.** Something interesting (a landed ship, a dead predator, the player doing something odd) draws curious people to a loose ring at about the social distance, which grows as pressure rises.
- **Parting.** An armed player walking through a market gets a moving bubble round them, wider the higher the pressure; people step aside early and close in behind.
- **Scatter.** Away from the cause, toward homes and doors, children first (section 3.6).
- **Huddle.** People who can't get away bunch against walls and indoors.
- **Mob.** Rare, angry settlements only. When the player has wronged the town and its armed people outnumber them, they gather into a line before they advance. The line forming is the warning.
- **Mourners.** A ring round the dead, heads down.
- **Celebration.** A cluster that bounces in loose sync.

**Crowd cost.** Far crowd members share their cluster's posture layer, with each person given a phase offset so they don't move in lockstep. Only the nearest 10 to 20 people run their own gestures at full rate. Mood reads are limited to a few neighbours per think, on the brain's existing think timer.

### 3.6 Children and young

Children and young animals do more than add variety: they change how every adult nearby behaves, and that makes a place feel inhabited.

**People's children.**
- **Tethered.** A child stays within a tether of an adult of their household: about 6 m for small children and 20 m for older ones, longer when calm and much shorter when anything's wrong. The tether is the whole rule; following, wandering back and clinging all come out of it.
- **Play when calm.** At high ease and moderate arousal, children near each other play: chase loops, circling, hopping, a ball of junk kicked about. The loops are the herd and pack movement verbs with no target, at a child's speed.
- **Curious.** Children notice the player before adults and approach to look, stopping at a distance and running back if the player turns to them quickly. A parent who sees this calls the child back with a call and a beckon (section 6.5). The call-back is a small, smart-looking moment for free.
- **Hide when in danger.** Children react first and fastest: a lower fear threshold, bigger startles, and they go to an adult, behind an adult's legs, through a doorway or under a cart, then peek. They recover faster too, and drift back to play once things are calm.
- **Cry.** A frightened or lost child cries, and that pulls every nearby adult's arousal up and makes the nearest one come.

**Child roles.** Children never take adult roles. Their `PersonRole` is always child, with a few child jobs underneath it as a second value: playing, fetching water or messages, minding younger ones, herding stock, helping at a parent's stall or bench, and scavenging with the family. Those jobs use the adult work gestures at a smaller size and with more breaks for play.

**Hardened children.** Sometimes you meet a child who behaves like a grizzled adult, out of necessity. It's not a separate role: it's the child role with one value, **hardness** (0 to 1), turned up by the child's circumstances. Hardness is rolled at spawn from things the world already knows:
- **No adults:** a child in a household with no adult left, or a group of children living alone in a ruin.
- **Hard places:** scavenger bands, refugee camps, raider wastelands, worlds whose fall was war or famine, warlord-held towns, water lords' towns.
- **Hard creeds and duties:** keeper households where the duty passed down early; road menders and lamp keepers whose last adult is old or gone.
- **Age:** only older children (about 10 and up); small children are never hardened.

Hardness changes the child in three ways.
- **Body language.** Fewer startles and smaller ones. No play. The stance is wider and the chin level, they watch the player instead of approaching to look, and they keep their distance at the adult social zone rather than coming close. They copy adult gestures: a short raised hand instead of a big wave, the guard's wave-off, a trader's weigh. They don't hide behind adults; they stand in front of younger children instead, with the interpose gesture.
- **Gear.** Adult kit cut down or worn too big: a coat to the knees, a scarf and goggles, a pack, a tool belt, a lamp, a sling or a short bow, sometimes an old rifle carried slung and never quite at ease. Patched clothes. On scavenger worlds, the same scavenger gear the adults wear, smaller.
- **Voice.** The child's pitch and throat stay, but the delivery is adult: lower in their range, flat, slower and clipped, with no squeals, laughter or babble. They whisper rather than cry when afraid, and they cry rarely, but when they do it's sudden and then cut off.

**How others react to them.** Adults treat a hardened child with a mix of respect and pity: a nod rather than a pat on the head, and a step aside at a stall. Kind adults kneel to them and get a level stare back. Other children follow them like a leader. Guards let them pass where they'd stop other children, and raiders sometimes taunt them, which they meet without flinching. Traders deal with them as with adults, but they're harder to frighten into an offer, and they'll stand their ground with a warning gesture over a younger child or the family's goods.

Under the suggestion in decision 1 (section 12), hardened children still can't be harmed or targeted, and they never fight: they warn, guard, trade, scavenge and lead other children, and when a fight starts they take the younger ones and go. Their weapon is part of the look and their warning, never something they fire at the player. If the owner chooses differently, that's where the line moves.

**How adults change with children near.** These are modifiers on the gestures above, not new gestures:
- **Interpose.** An adult steps between the child and the cause, facing it, and sweeps an arm back to keep the child behind them. Nerve rises when children are near, so a timid farmer who would flee alone stands their ground.
- **Usher.** An adult with a hand on the child's back walks them indoors, glancing at the player. When a town goes wary, the first sign is children being taken inside, which tells the player the mood has changed before any guard moves.
- **Pick up.** A small child is lifted and carried while fleeing (the rig's carry pose with a small body).
- **Lower down.** Kneeling to a child's height to comfort them or point something out.
- **Fight harder when cornered.** A cornered adult with a child behind them won't surrender: they hold the halt pose and stay put.
- **Fewer threats.** Guards and raiders with children near don't taunt.

**Animal young.** Each species rolls a young strategy:
- **Followers** stay at heel and run with the herd. When threatened the herd closes round them: the young go into the middle and adults face outward, as musk oxen do against wolves. Against a single threat the herd forms a line in front of the young instead.
- **Hiders** lie still in cover while the mother grazes some way off. They freeze rather than run, and the mother comes back and calls them out, as deer do with fawns. So the player can find a fawn-thing lying still in the grass, with its mother watching from 40 m away.
- **Young play** when the herd is calm: play bows, chases and mock fights.
- **A mother interposes** like a human parent and charges sooner. A grazer that would flee from the player alone may stand and stamp when its young is near.

**Children's voices** are covered in section 6.4. Whether children and young can be harmed is a decision for the owner (section 12).

## 4. Moving between states

People and creatures need to switch between states properly: no pops, no snaps, no flicker, and a change of state you can see before it acts.

### 4.1 The states

States are what the brain is doing; gestures are how the body shows it. Each state draws on the gestures in section 3.

| State | What it is |
| --- | --- |
| Calm | Idle, work, social, routine |
| Attentive | Noticed something; looking (awareness past curious) |
| Wary | Watching the player, ready to act; hands near the weapon or near the goods |
| Warning | Telling the player to stop or go (halt, wave-off, weapon show) |
| Threatening | Taunt, brandish, ready, advance |
| Attacking | Fighting, using the existing attack and telegraph rules |
| Searching | Lost track of something it knows about; goes to the last known place (`Senses`' `LastSeen`) |
| Freezing | Holding still in fear |
| Fleeing | Running away with a destination |
| Hiding | In cover, peeking |
| Surrendering | Hands up, kneeling, dropping things |
| Cowering | Fear with nowhere to go |
| Panicking | Fear at the top, running without a destination |
| Hurt | Pain overriding everything |
| Grieving | At a body |
| Celebrating | After a win, a festival, a gift |
| Shaken | Recovering: calm again but jumpy (section 4.6) |

### 4.2 Sensible orders

There are three ladders, and a person climbs them one rung at a time.

- **Stand-ground ladder:** calm, attentive, wary, warning, threatening, attacking.
- **Fear ladder:** calm, attentive, wary, freezing, then fleeing or hiding; if cornered, surrendering or cowering; if cornered with high nerve, attacking.
- **Loss ladder:** calm, startled, then grieving, then searching (if the cause is unknown) or threatening (if they know who did it).

Rules:
- **One rung per step,** and each rung is shown for at least its minimum hold (section 4.4), so the player always sees attentive before wary and warning before threatening.
- **Hard triggers jump at most two rungs:** being hit, a shot within 3 m, a body falling next to them. But a startle beat of 0.3 to 0.6 s always plays first, and the startle is the tell.
- **Attacks never start below threatening,** except for creatures whose attack already opens with a telegraph (`AttackPhase.Windup`) and ambushers, which have their tell kinds (`TellKind`). This matches the design's "warned, never ambushed".
- **Which ladder** is chosen by nerve, role and who's near: guards and raiders take the stand-ground ladder; farmers, traders, children and elders the fear ladder. Nerve and company can swap them: a lone raider facing three armed players takes the fear ladder, and a farmer with a child behind them takes the stand-ground ladder as far as warning.

### 4.3 What triggers each change

Each state has an **entry** level and a lower **exit** level on its driving number, so a value sitting near the line can't flip it back and forth. This is hysteresis, the standard fix for flickering state machines.

| Change | Driven by | Enter at | Leave below |
| --- | --- | --- | --- |
| Calm to attentive | Awareness | 0.35 (the existing curious mark) | 0.2 |
| Attentive to wary | Pressure | 0.25 | 0.12 |
| Wary to warning | Pressure, for a guard or a wary town | 0.4 | 0.25 |
| Warning to threatening | Pressure, or the player ignoring a warning for 4 s | 0.6 | 0.4 |
| Threatening to attacking | Pressure 0.8, or being hit, or a hostile occupancy at range | 0.8 | 0.55 |
| Wary to freezing or fleeing | Fear, which is arousal times one minus nerve | 0.45 | 0.3 |
| Fleeing to surrendering | Cornered, or aimed at while fleeing, with fear above 0.6 | On the event | Pressure below 0.3 for 3 s |
| Any to hurt | A hit | On the event | Pain fades (a few seconds) |
| Any to grieving | A dead member of their group found | On the event | Hold time ends (section 4.4) |
| Any to celebrating | A win event | On the event | Hold time ends |

The reference numbers are on a 0 to 1 scale, tuned per role. The gap between entry and exit is about 0.15.

### 4.4 How long each state holds

| State | Minimum hold | Usual length | Then |
| --- | --- | --- | --- |
| Startle | 0.3 s | 0.5 to 1 s | Attentive, wary, freezing or fleeing |
| Attentive | 1 s | Until awareness falls | Calm, or wary |
| Wary | 1.5 s | While pressure stays | Calm via shaken, or warning |
| Warning | 2 s | Escalates after about 4 s of being ignored | Threatening, or back to wary when the player backs off |
| Threatening | 1.5 s | Until the player leaves range or attacks | Attacking, or wary |
| Freezing | 1 s | Up to 6 s | Fleeing, or attentive |
| Fleeing | Until it reaches its destination | 5 to 20 s | Hiding or shaken; reconsiders only on arrival, unless hit or cornered |
| Surrendering | 3 s | While aimed at, plus 3 to 5 s after | Backing away, then shaken |
| Hurt | 0.5 s | By the hit | The previous state, or fleeing |
| Grieving | 8 s | 20 s to minutes, longer for close bonds | Searching or threatening if the killer is known; mourning at the place otherwise |
| Celebrating | 3 s | 5 to 15 s | Calm |
| Shaken | 10 s | 30 s to a few minutes | Calm |

### 4.5 Blending motion and voice

**Motion.** The body is layers, each eased toward its target by a damped spring that never overshoots wildly. Each layer has its own speed, so nothing changes all at once:
- head and eyes, fastest (about 0.1 s);
- arms and gesture, quick (0.15 to 0.3 s);
- posture (lean, crouch, chest, stance), slower (0.25 to 0.5 s);
- walk speed, limited by acceleration, as the rig already does.

Then four rules:
- **Head first.** Every change starts with the head turning toward the cause, then the body, then the hands. That order is the visible "something has changed" that comes before any action.
- **Anticipation.** A big gesture starts with a small counter-move (the hand dips before it rises, the body sinks before it jumps), the oldest trick in animation. It costs one extra key in the motion and makes everything read as intended.
- **Phases.** Every gesture has in, hold and out. A new gesture waits for the current one to reach hold or out unless its priority is higher (section 4.6). If the hand paths would cross awkwardly (hands up straight to a beckon), they pass through a neutral ready pose at chest height.
- **Interruptions are fast, never instant.** A startle or a hit cuts in with a blend of about 0.1 s, still on the spring, so it looks like a flinch and not a teleport.

**Voice.** Voices change the same way. Instead of four separate moods (`VoiceMood`: murmur, shout, whisper, cry), the voice takes continuous values (loudness, pitch, pace, breath, tension), and the mood names become points on that space. Pitch and loudness glide over 150 to 300 ms. A phrase in progress finishes its current syllable before changing. A change upward starts with a sharp intake of breath, which is the voice's own tell. A change downward ends with a sigh or a long exhale, which the player learns as "they've stood down".

### 4.6 Interruptions and priority

Higher interrupts lower; equal waits.

1. Hurt (a hit, a fall)
2. Startle
3. Attacking, fleeing, panicking
4. Surrendering, cowering
5. Warning, threatening
6. Grieving
7. Searching, attentive, wary
8. Celebrating
9. Calm: work, social, idle

Two special cases. Grief can be broken by a new threat, but the person returns to the body afterwards if it's safe, which looks like loyalty and costs one remembered position. Fleeing commits to its destination: it only re-decides on arrival, when hit, or when cornered. That stops the runner zigzagging as the threat moves.

### 4.7 Recovery, and memory of the player

**Shaken.** After fear or a fight, a person goes to shaken rather than straight to calm. Shaken is calm with lower thresholds (they re-alarm faster), more look-abouts, staying near cover and doors, whispering rather than murmuring, and children kept close. It fades over 30 s to a few minutes. Biologists call this sensitisation.

**Getting used to you.** The opposite also happens. If the player keeps coming back to a town, weapon put away, and nothing bad happens, each visit lowers how much pressure the player makes there, down to a floor. Locals stop staring and children start to play near you. This is habituation, and it makes being a known, peaceful visitor feel earned.

**Memory, kept small.**
- **Each settlement keeps a ledger per player** of what they did there: threatened, robbed, hurt, killed, traded, gave, helped (killed a predator near town, rescued someone). Each is a number that decays with a half-life of days, and bad deeds decay slower than good ones. It's one small record per settlement, saved with the POI's state, which fits the design's "lasting facts".
- **Up to a few people per settlement** also remember the player personally: the ones who were robbed, threatened, helped or saw it happen. They react before the rest of the town, with a double take (section 3.4.1).
- **On arrival,** the ledger sets where everyone starts: a town that loves you starts calm with a few waves; a town you robbed starts wary, with guards at warning, children inside and traders' goods pulled back.
- **News travels only by contact.** A witness carries "saw the theft" until they reach a guard, an elder or their household, and only then does the settlement's ledger change (section 5.5). No information appears from nowhere, which gives the player a chance to stop the runner.

### 4.8 Groups shifting together

A group (a patrol, a household, a gang, a herd) has a shared mood that its members pull toward, as the crowd rules do (section 3.5), plus a few group rules:
- **The leader leads.** The group's state follows the leader's with a short delay. If the leader goes down, the group checks its numbers: outnumbered, it breaks (flee or surrender); not outnumbered, it turns vengeful (threatening, nerve up).
- **The first to see it signals.** They point; the others turn to where they point, then take their own state from what they see.
- **Members don't move in lockstep.** Each member's delay and threshold differ slightly, so a patrol going on alert is a ripple, not a switch.
- **The last to calm is the guard.** Groups calm from the timid outward to the bold, and the guard who watches you leave is the final beat.

### 4.9 No flicker

Five rules, all cheap:
1. Separate entry and exit levels (section 4.3).
2. Minimum holds (section 4.4).
3. Smoothed inputs: pressure rises fast (about 0.3 s) and falls slowly (about 4 s), like a sound envelope, so a weapon flicked up and down doesn't flip anyone back and forth.
4. A cooldown before re-entering a state just left: 2 to 5 s for warning, 10 s for celebrating.
5. Committed actions: fleeing goes to a destination, surrender holds while aimed at, and grief has a minimum length.

### 4.10 Visible before it acts

The player should be able to watch every escalation coming:
- Every step up starts with a head turn, then a body turn, then a gesture held for at least 0.6 s.
- No one attacks without first being seen in threatening or ready for at least 1 s, unless they're a creature or ambusher with its own telegraph.
- Stepping down shows too: the weapon lowered, shoulders dropping, the sigh. The player always knows when danger has passed.

## 5. Actions and consequences

Gestures say things; this section makes them do things. A threatened trader holding out a bundle is a gesture, and taking the bundle is a robbery the town will remember. All of it runs on the pressure, nerve, role, company and memory from section 2, with no dialogue.

### 5.1 What the player does and what comes back

| Player does | Typical response (alone, neutral town) | Changes with |
| --- | --- | --- |
| Approaches with the weapon put away | Attentive, then a greeting or a beckon | Wary towns watch; raiders taunt |
| Approaches with the weapon out | Wary; guards watch; citizens step aside | Habituation lowers it; robbed towns warn at once |
| Aims at a citizen | Hands up; if carrying goods, an offer; if near a door, they back through it | Guards near: the citizen ducks behind them and the guard warns |
| Aims at a guard | Warning, then ready; aimed at long enough, the guard attacks | A guard alone against several armed players may drop their weapon instead |
| Aims at a raider | Threatening; aimed at when hurt or outnumbered, surrender | Leader alive: they fight |
| Shoots near someone without hitting | Startle, then flee or surrender; guards attack | A warning shot into the air in a hostile camp starts the fight |
| Shoots someone | Fight or flight by role; witnesses react (section 5.5) | |
| Follows someone | They glance back, speed up, then head for company, home or a guard; if followed into a dead end, they turn and surrender or offer | Traders and children go to a guard; raiders turn and taunt |
| Stands in the doorway of a room with someone inside | They count as cornered: hands up or cower; a guard or a raider attacks sooner | |
| Enters a home or goes behind a stall | The owner warns (wave-off, point to the door); neighbours watch | Good standing makes it a refuse instead |
| Takes from a container or stall without trading | Theft (section 5.4) | |
| Puts the weapon away | Pressure drops; after a beat people step down, with the sigh | Shaken people stay wary a while longer |
| Drops an item near someone or gives a gift through the trade menu | Accept, then a warm gesture; the ledger notes a gift; children come closer | Hostile people don't take it; wary ones pick it up and back off |
| Crouches still for a while in town | Curious people come to look; a guard watches | |
| Torch in someone's face at night | They shield their eyes, turn away and step back | |

### 5.2 The responses

Each response is a short sequence of gestures plus one world effect. None needs planning.

- **Drop the weapon.** The weapon held out to the side, let fall, then hands up. Spawns the real weapon item on the ground.
- **Drop the goods.** A carried sack or crate falls as the person runs. Spawns a small pickup from their role's carried-goods roll.
- **Offer something to make you leave.** A trader holds out a bundle or sets it on the ground and steps back, gesturing at it and then at the way out. The bundle is a small roll from their stock, sized by how frightened they are, never the whole shop.
- **Flee and leave things behind.** Panic drops whatever is carried; a stall left mid-trade stays open and unattended, which is a temptation the town will remember if you take from it.
- **Call for help.** A hail, a shout and a point toward the player, running toward the nearest guard or group. Guards who hear it come.
- **Lock the door.** Someone who reaches home pulls the door shut; the bang and the lock clack carry. The house stays shut until they've calmed down.
- **Ring the alarm.** In towns with a bell, a horn or a siren, the first guard to go to attacking, or a citizen reaching one, rings it. Every citizen then goes to the fear ladder and every guard to wary or higher.
- **Pay off.** Wary towns, after you've hurt someone, may set goods out at the edge of town when you arrive: an offering to make you leave. It's one rule (high fear of the player, standing not hostile, the player approaching), and it reads as a town that has made a decision together.

### 5.3 What decides the response

Read in this order, each cheap:
1. **Role:** guards hold ground; traders appease and offer; farmers and workers flee; elders stand still or plead; children hide; drunks shrug, taunt or wander into trouble; raiders taunt then fight.
2. **Nerve and mood:** high nerve turns fear into warning; low nerve turns warning into surrender.
3. **Occupancy and settlement mood:** a wary town warns quicker; an angry one threatens quicker; friendly folk forgive one raised weapon.
4. **Company:** alone means appease; with armed friends, hold ground; with children near, protect (section 3.6); a group whose leader is down breaks.
5. **Standing and memory:** good standing raises every threshold; a bad ledger lowers them.
6. **Cornered or not:** cornered people never flee, and those with high nerve fight.

### 5.4 Robbing versus trading

Trading is the menu, as the design says. Robbing is taking what a frightened person drops, offers or leaves, or taking from a stall or container without the menu.

- **Seen or not.** A theft only counts if someone saw it: the victim or a witness who sensed it (`Senses` sight, or hearing up close). An unseen theft from an empty house is just loot.
- **The victim reacts** by role: flees and calls for help, or, if a guard is close, points.
- **The ledger** records robbed. In a settlement: prices up, then refusal to trade, then guards warning on sight, then hostile if it keeps happening. Repeat robberies escalate faster.
- **Standing.** In a faction's place, standing with that faction falls. Reference losses on the design's scale (bands are 6,000 wide): threatening a non-hostile person about 100; robbing about 1,500; hurting about 3,000; killing about 6,000, a whole band. Killing someone who surrendered, if seen, counts double. Gifts and help give back a little, capped per day. Factionless settlements need a standing of their own (section 12).
- **Hostiles are fair game.** Robbing raiders, Reavers or anyone already hostile costs nothing with them, and may please their rivals by the design's rivalry rules.
- **Letting someone go.** A surrendered raider who's allowed to back away isn't a standing gain in itself, but the act is seen. A gang that's been spared once is less likely to attack on sight next time: a ledger entry, nothing more.
- **No farming.** Each person drops goods at most once a day; a settlement's offering happens at most once per visit; dropped goods come from a small roll, not a shop's stock. The design's rule that the player's loot is never at risk is unchanged; this is only what NPCs give up.

### 5.5 What witnesses do

A witness is anyone who sensed the act. Each one runs a short rule set by role:
- **Citizens:** startle, then point at the player for a beat, then flee toward a guard, an elder or home while carrying the news. They cry out if it was violent.
- **Guards:** go to warning on a theft or a threat, and straight to threatening or attacking on violence.
- **Children:** hide, then run to a parent.
- **Traders:** pull their goods back and close the stall.
- **Raiders:** a theft from someone else is no concern of theirs; they laugh and taunt.

A witness who reaches a guard or an elder hands on the news: the ledger updates and the guard looks where the witness points. If the player catches up first, by aiming at the witness, the witness surrenders, and whether they still report it later depends on their nerve and on whether the player let them go. That's the whole "stop the witness" idea without a menu.

### 5.6 Seeing someone die

Finding a body later (rule 4 in section 9) is one trigger; watching someone die in front of you is a stronger one. Every death is an event (section 2.5) with three facts attached: who died, who did it, and how. Everyone who sensed it reacts in two beats.

**Beat one: shock (0.3 to 1 s, everyone).** A startle much bigger than usual: the body jerks back, hands fly up toward the face, the breath catches with a sharp gasp, then a cry. Close bonds cry out loudest. That beat is the same for everyone, and it's the tell that something terrible just happened, readable at 40 m.

**Beat two: what they do next.** One choice, from four inputs:

| Input | Pushes toward |
| --- | --- |
| Who died: their kin, partner or child | Rushing to the body, whatever the danger |
| Who died: their group or town | Rushing if brave, recoiling if not |
| Who died: a stranger, or an enemy | Recoil, freeze or flee; raiders may cheer the death of an enemy |
| Who died: their leader | The group check from rule 1: break or turn vengeful |
| Witness's role | Guards turn on the killer; medics rush to help; children freeze then hide; traders and farmers flee; raiders turn on the killer or run, by numbers |
| Mood and courage | High nerve turns grief into anger; low nerve into fleeing or freezing |
| Who did it: the player | The player becomes the cause, and the ledger takes the killing (section 5.4) |
| Who did it: a creature, another faction, an accident | The anger goes elsewhere, and the player is a bystander or an ally |

So the possible second beats are:
- **Rush to the body:** runs in, drops to their knees, a hand on the body, and the grief keen (section 3.4.8). They stay there through danger unless hit, which reads as love and costs nothing.
- **Recoil:** steps back, a hand over the mouth, turns away, sometimes retches.
- **Freeze:** stares, can't move for a few seconds, then flees or kneels.
- **Flee:** runs, crying out, toward home or a group, carrying the news.
- **Turn on the killer:** a cry of grief that turns into a shout, the body squaring up, then straight to threatening with no warning rung skipped. Even a timid farmer with a hoe may do this if the dead was their partner.
- **Call for help:** a medic or a partner waves others in and calls the dead person's call sign.

**How it spreads.** The shock pulses through the crowd at a much higher strength than a shot, with the head-turn delay (section 3.5). People who didn't see it but heard the cry turn toward it and come to look, or run, by their own rules. A death in a square leaves a ring of mourners, a few who stare from a distance, and empty streets beyond. Animals do a version of it too: a herd that sees one of its own pulled down bolts, and a mother whose young is killed stamps and charges or stands calling over it.

**What it does to their view of the player, if the player did it.**
- Every witness becomes a person who remembers you personally (section 4.7), with killed noted, and starts shaken or worse.
- A death seen is worth more in the ledger than a death reported. A killing in front of children is worth more again, and those children hide from you on every later visit.
- A killing the town sees as fair (a raider attacking the town, a predator, someone who fired first at you) counts as help, not harm: the shock still plays, but the second beat is relief and, after a while, thanks.
- Killing someone who surrendered, in front of witnesses, is the worst entry there is: the town turns hostile, and the faction, if any, loses a whole band.
- If the player is the one who dies in front of townsfolk (in co-op, or a respawn in town), the same shock plays: the friendly ones rush in, and a medic kneels.

### 5.7 Cost

Every response is a short scripted sequence of existing gestures, one item spawn or door event, and a few numbers in a per-settlement record. Witness checks run only on the rare events (theft, threat, violence), only for people within about 30 m, and use the senses that already run. None of it plans.

## 6. Wordless voice

### 6.1 The building blocks

The bestiary's `Voice` already renders syllables with a small formant synthesiser: a glottal pulse at the person's pitch, shaped by two formants scaled by their throat length, with breath and rasp, at their pace. Here is the full set of building blocks, most of them additions:

| Block | What it is | Already there |
| --- | --- | --- |
| Source | Pulse train with pitch, jitter (pitch wobble), shimmer (loudness wobble), breath noise and rasp | Pitch, breath, rasp |
| Filter | Formants from throat length and the dialect's vowels; a brighter or darker tilt for tension | Two formants, throat scale |
| Syllable | Consonant plus vowel shapes: stops, hisses, nasals, liquids; open or closed syllables | Hiss share |
| Prosody | Each phrase's pitch contour (gently falling, a rise or fall at the end), stress pattern, rhythm | Lilt, rate |
| Phrase | 1 to 12 syllables with a pause pattern; questions rise, statements fall | Takes |
| Turn-taking | Pairs alternate; the listener makes short "mm" and "ah" backchannels | New |
| Non-speech | Laughs, sighs, gasps, grunts, screams, sobs, hums, whistles, the hiss of "shh", a sharp breath in | New |
| Calls | Short fixed shapes for a purpose: greeting hail, warning bark, rally, alarm, taunt chant, pain cry, grief keen, call-back, call sign | New |

The aim is the one Simlish and Animalese found: no meaning in the syllables, all of it in the delivery. The Sims' designers kept emotion and intonation and dropped words, and Animal Crossing's voices come from fast syllables with pitch shaped by mood. GalHack synthesises everything, so it can go further: every person is a distinct throat, every world a distinct dialect.

### 6.2 Dialect and personality

**A world's dialect** already sets favourite vowels, rate, lilt and hiss. Add:
- **Syllable shape:** mostly open (ba-li-mo) or mostly closed (tak-ver-osh).
- **Consonant colour:** favouring stops, hisses or nasals.
- **Stress position:** first syllable or last.
- **Pitch range:** sing-song or flat.
- **A hail motif:** each world's long-distance greeting call has its own melody, so a valley's hails sound like that valley.
- **Whistled calls** on open, windy worlds, where people call across distances. Real whistled languages grew up in exactly that kind of country.
- **Faction register:** Meridian clipped and level; Reavers loud and rough with harsh consonants; Freeholders slow; Ledger quick and bright.

**Personality** shifts a person off their world's average:
- pace, pitch range (expressive or flat) and loudness;
- how often they laugh and how much they talk;
- **one verbal tic:** a favourite syllable or a little rising "hm" they repeat, so the same person is recognisable across visits;
- age adds rasp and wobble; an elder's breath runs out in long phrases.

### 6.3 Emotion and situation

Vocal emotion research (Banse and Scherer's acoustic profiles, for example) finds that intensity shows in loudness, pitch and pace, and that the quality of an emotion shows in voice texture and contour. Mapped onto the three mood numbers:

| Mood | Voice |
| --- | --- |
| High arousal | Higher pitch, louder, faster, wider range, brighter |
| Low arousal | Lower, quieter, slower, narrower |
| Positive ease | Rising, smooth contours, laughs |
| Negative ease | Falling contours, harsher, more breath or more strain |
| High nerve | Lower pitch, slower, phrases ending down, fewer pauses |
| Low nerve | Higher, hesitant pauses, phrases ending up |

So the existing moods fall out as points: **murmur** is calm and neutral; **shout** is high arousal with high nerve; **whisper** is fear with low noise wanted; **cry** is fear or grief with no restraint. Grief adds sobs and long falling keens; anger adds pressed, clipped bursts; joy adds laughter.

**Situation** changes delivery on top of mood:
- **Distance to the listener:** people raise their voice for someone far off, and shout over noise (the Lombard effect). Calls become hails.
- **Hiding:** whisper, or silence, with "shh" to others.
- **In pairs:** turn-taking, backchannels, and laughter that spreads.
- **Crowds:** beyond the nearest few speakers, a crowd is a synthesised murmur bed whose loudness, pitch and roughness follow the crowd's average mood, rather than dozens of voices.
- **Indoors:** room reverb by room size (section 7.3).
- **Suits and helmets:** a band-limited radio colour with a click at each phrase start, for vacuum workers and soldiers.

### 6.4 Children's and young voices

Children's voices are higher in pitch and formants than adults' and much more variable, settling to adult values in the teenage years, according to acoustic studies of children's speech. In the synthesiser:
- **Pitch** about one and a half to two and a half times an adult's, from the body's age and height; throat scale from the shorter vocal tract.
- **More wobble:** more pitch and timing variation; rhythm less regular than the dialect's.
- **Small children babble:** repeated syllables (ba-ba, da-da) and sing-song contours, with fewer of the world's consonants. Older children use the dialect, exaggerated.
- **Play sounds:** squeals, laughing bursts, the rising "wheee" of a chase, calls to each other.
- **Crying** comes quickly and stops quickly.
- **Animal young** make a higher, thinner version of their species' calls: peeps, bleats and contact calls that the mother answers. A hider calf answers only its mother's call.

### 6.5 Calls between people, and wordless names

The Last of Us Part II made enemies feel like people by having them call each other by name. GalHack can't use names, but it can use **call signs**: each person gets a two or three syllable motif from their voice seed, with its own melody. When others call that person, they use that motif in their own voice. Bottlenose dolphins do something similar with signature whistles, copying a partner's whistle to call them when they're apart.

- A parent calls a child back with the child's call sign and a beckon; the child answers with a short rising call and comes.
- A patrol pair checks in on a rhythm: one calls the other's sign, the other answers. If the answer doesn't come (the player has dealt with the partner quietly), the caller calls again louder and higher, then goes to look (section 9).
- A guard who finds a body cries out, then calls the dead person's sign, rising and falling with no answer. The player who listens learns that this group had a "ka-REN" and a "to-mi", and that one of them is gone.
- The player never sees a name or a label. They just hear the same little motif used for the same person.

### 6.6 Robots

- **Kessari and tidy machines** speak in melodic tone strings, modem-like and pleasant, each clan with its own scale and rhythm (a machine dialect). Status lights flicker in time with them. They don't whisper: they lower the volume and switch to short clicks.
- **Service machines** (clerks, cleaners, carers) still chime their old service tones: a polite two-note "next", a soft acknowledgement. It's eerie when no one is there to hear.
- **Sentries** use rising warning tones, matching the lock tone the weapon doc already gives seekers.
- **Feral robots** stutter: old chimes broken off and looped, grinding servos, a speaker crackling at the wrong pitch.
- **Cyborgs** have a human voice with machine edges: steady pitch, too regular rhythm, a click before each phrase.

### 6.7 Creatures

Creature voices come from the same mass that already sets loudness in `Heft`: bigger bodies sound lower and louder, throat length sets the formants, and the body parts colour the sound (a throat sac booms, a beak clicks).

| Call type | When |
| --- | --- |
| Contact | Calm herds and pairs, now and then; mothers and young |
| Alarm | The first to notice; a sharp, short call that sets the herd moving |
| Threat | Growl, hiss or roar with the rear and brace displays (`TellKind.Growl`, `TellKind.Hiss`) |
| Distress | Hurt or caught; pulls kin toward it, or away |
| Begging | Young wanting food or their mother |
| Hunting | Pack members' short calls while surrounding prey |
| Echo clicks | Blind hunters (`TellKind.Click`) |

**Husks** make the dry rattle the weapon doc gives them, and nothing else. **The Hollow** has no voice of its own. At most, something with almost the cadence of a murmur, from a room with nobody in it, which stops when you stand still to listen.

### 6.8 Cost

Voices are rendered off the main thread and cached by take, as `Voice.Render` already allows. Each person keeps a small pool of takes per mood region, so most utterances are cache hits. Only the nearest few speakers render as separate voices; everyone else feeds the crowd bed. Calls are short, and dozens of people can share a few dozen cached clips per world.

## 7. Telltale sounds in places

### 7.1 The rule

Inside places, things keep quiet so they can surprise you, and give themselves away only through the small sounds of what they're doing out of sight. `Quiet.cs` already does this: footsteps, skitter, clatter, murmur and servo; nothing in plain sight makes one; a wall cuts the sound to a short range at 10 dB less. This section fills in what each kind of occupant gives away, and how sound moves.

**Everything is truthful.** A creature sound always has a creature behind it. Non-living sounds (the building settling, a drip, wind in a broken window, a cooling pipe ticking) form the noise floor that makes the living ones mean something, and the player learns to tell them apart by rhythm: living things move in a gait, buildings are random or regular. The Hollow is the one place where sounds are wrong, and even there something real is the cause.

### 7.2 What each occupant gives away

| Occupancy | At rest | Moving or working | Alarmed |
| --- | --- | --- | --- |
| Empty (EMP) | Only the building: settling creaks, drips, wind. The absence of everything else is the message | — | — |
| Nesters (NST) | Soft rustle in the nest, young chirping, wet sounds | Skitter of many legs, claws on metal, dragged food | The skitter stops all at once, then a hiss (`TellKind.Hiss`) |
| Husks (HSK) | Nothing for a long time, then a dry rattle | Slow shuffle, feet dragging, a body bumping a wall | Rattles answering each other from different rooms |
| Scavengers passing (SCV) | Whispered pairs, a can opened | Careful rummaging, debris shifted, then stillness while they listen | Quick footsteps away; a dropped item they don't stop for |
| Squatters and survivors (SQT) | Cooking clink, a child's voice, a murmur, snoring at night | Tool taps, a door, a bucket | Shushing, a door shut and barred, then whispers |
| Gang-held (GNG) | Loud: laughter, banging music, bottles, arguments | An engine idling, metal clatter, boots | A shout taken up by others, a gun cocked, boots running toward |
| Faction-held (FAC) | Regular patrol footsteps, wordless radio chatter in bursts, equipment rattle | Doors cycling on the patrol's rhythm | Rally calls, a siren, synchronised running |
| Machine-held (MCH) | A steady hum, a periodic chime | Servo whine, wheels, the tick of a routine | Tone strings rising, lights changing |
| Cyborgs (CYB) | Silence and a faint hum | Even, too-regular footsteps, servo clicks | A click before the arc (weapon doc) |
| Feral robots (BOT) | Grinding, a speaker crackling | Dragging metal, a limping clank | Stuttering chimes speeding up |
| Cultists and pilgrims (CLT) | A hummed drone, a bell | Drums, chant-hum | The drone stops; then the horn |
| Mutated predators (MUT) | Heavy breathing in the dark (`TellKind.Breath`) | Heavy footfalls, a low growl, claws on floor | A roar, then nothing |
| Keepers (KPR) | A broom, a hummed tune, a routine bell at its hour | The same footsteps on the same route each time | They stop what they're doing; a lamp is lit |
| Sleepers (SLP) | Many people breathing slowly in unison; pod hum | A single pair of feet walking an old routine | — |
| Guardians (GRD) | Deep resonance, almost below hearing | A grinding as something heavy wakes | The resonance rises |
| Mimics (MIM) | The same voice from several rooms | Footsteps with exactly the same rhythm, in two places | They all stop at once |
| Something wrong (HOL) | Sound dies off; your own footsteps sound dull | A murmur with no source that stops when you stop | Nothing. The silence is the tell |

### 7.3 Situations

Whatever the occupant, what it's doing changes the sound:
- **Asleep:** slow breathing, a snore, a shift and settle. The thing that wakes gives a single sharp sound, then quiet.
- **Eating:** crunching, tearing, clinks; it stops when awareness passes curious.
- **Working:** regular taps and tool noise, a rhythm the player can time.
- **Patrolling:** footsteps with a regular rhythm and a route. The player can count the loop.
- **Searching:** irregular footsteps, doors opened, things moved, pauses.
- **Hiding:** silence, then a held breath let go, or a shuffle as the player passes.
- **Wounded:** groans, dragging, laboured breathing.
- **Fighting elsewhere:** distant shots and cries, as in the STALKER zone's far-off gunfights. Two occupants fighting each other is a gift to the player.
- **Something knocked over:** a moving body near real deco has a chance to knock it: a can rolls, a bottle breaks, a chair scrapes, a tray clatters. The sound is chosen from the deco actually in that room, and the item is moved or broken afterwards, so a curious player who goes to look finds the fallen thing. That's `QuietKind.Clatter`, tied to real props.

### 7.4 How sound travels through rooms and walls

The POI generator already knows its rooms and doors, so the sound graph comes free: rooms are nodes and openings are links. Wwise, Hitman and other games model sound this way, with rooms and portals.

- **Through openings:** sound goes along the shortest path through open doors and gaps, losing level with path length, and it seems to come from the last opening it passed through. A sound two rooms away comes "from the doorway on the left", which is the truth the player can follow.
- **Closed doors:** a link with extra loss (the existing 10 dB) and the highs cut, so it's muffled.
- **Walls:** the existing short through-wall range, muffled and quiet. Thin partitions let more through than bulkheads.
- **Vents and ducts:** long links with little loss and a hollow metal colour. A vent can carry a skitter from far away and put it overhead, which is exactly what ambushers in vents need.
- **Floors and ceilings:** heavy footsteps carry up and down as low thumps only.
- **Pipes:** knocks carry along pipe runs. The Knockers creed, who talk through the pipes, is this rule used by people.
- **Room size:** a cheap reverb from the room's size and material: a tight dry closet, a booming hall, a ringing tank.
- **Surfaces:** metal floors clang, grates rattle, water splashes, broken glass crunches. A POI's decay state puts glass and debris on floors.

**Fairness.** The same graph carries the player's sounds to the occupants (`Senses`' hearing and the weapon noise radii). If you can hear them through that door, they can hear you, and the player learns both sides at once.

### 7.5 Building tension without explaining

A good approach to an occupied place has a shape: rumour, evidence, silence, tell, act.
1. **Rumour:** a faint, far sound through two rooms, hard to place.
2. **Evidence:** closer and clearer: footsteps with a rhythm, a murmur, a skitter.
3. **Silence:** the sound stops because it noticed you. Awareness passing curious turns off the occupant's quiet sounds. This is the strongest tell in the set, and it's free.
4. **Tell:** the existing ambush tells: scratching in the walls, a vent rattle, grit from the ceiling, a shadow.
5. **Act:** the telegraphed attack, or the occupant fleeing, or a door slammed and barred.

A small **quiet director** per POI keeps the pacing right without inventing anything. It never makes a sound with no source. When several true sources are due, it voices the ones that build the shape and holds back the rest. It spaces big tells apart by at least several seconds. And it now and then lets a whole floor go quiet. Alien: Isolation used a director that knew where the player was but let the creature find them on its own, varying the tension so it never stayed at the top; this is the same idea scaled down to choosing between true sounds.

### 7.6 Silence as a tool

- **The stop:** sounds that cut off when you're noticed (section 7.5).
- **The dead place:** empty POIs have only the building. Players learn that a place with no settling sounds at all is wrong.
- **Before the ambush:** the subsisters go quiet when a predator is near. Small creatures freezing tells you a big one is close, inside and outdoors alike: birdsong thinning in a valley means a large hunter is lying up nearby.
- **The Hollow:** sound dies off, your own footsteps go dull, the ambience thins. It builds gradually as you approach, several effects together, so it reads as deliberate (design section 4).
- **After a fight:** everything stops for a few seconds, then the place's life comes back one sound at a time. That's the player's signal that it's over.

### 7.7 Cost

`Quiet.Step` already loops over a place's creatures for one listener and skips anything still and silent. Add a cap of about six live quiet sounds at once, chosen by loudness and the quiet director. The room graph is built once per POI. Paths are short searches over a few dozen rooms, cached per source room, and re-run only when a door opens or closes. Reverb is one value per room. Outdoors stays as it is: `Quiet` doesn't run there.

## 8. How the player reads and uses all this

**Stealth.**
- Listen before entering: the occupancy table (section 7.2) tells you who's home.
- Time patrols by footstep rhythm, and pass when the steps go away.
- Choose soft surfaces and avoid glass and grates; crouch where they would hear you.
- Watch for the stop: if the skitter stops, you've been heard, so back off or get ready.
- Quietly dealing with one of a pair starts the partner's check-in calls (section 6.5). That gives you a few seconds of warning, and a way to draw the partner to a place of your choosing.
- Noise tools in the weapon doc (Chatterbox, Lure Beacon) feed the same system, so people go and look where the noise is.

**Trade.**
- A trader's beckon, present and weigh say they're open; refuse and closed say not today.
- Put the weapon away and the bubble round you shrinks, the beckons start, children come out.
- Gifts and help shift the ledger, and a town that knows you greets you with waves and double takes.

**Avoiding fights.**
- A guard's halt and wave-off draw a line and point at the way out. Respect it and nothing happens.
- Every escalation shows a step before it acts, so you can always back off in time: lower the weapon, step back, wait for the sigh.
- A raider taunting is a raider not yet attacking. That's your window to leave, or to strike first.
- If the player has emotes (section 12), raising their own hands or waving lowers pressure in the same way.

**Surrender.**
- Aim at an outmatched enemy and they may give up: hands up, weapon dropped, a kneel when cornered.
- Kill the leader and a group's nerve breaks: fleeing or surrender all round.
- Letting people go is remembered; killing the surrendered is remembered more.

**Intimidation.**
- Aiming makes people drop and offer things. Taking them is robbery, and the town keeps count.
- Standing in a doorway corners whoever's inside; following someone wears down their nerve.
- Witnesses are real people who have to reach a guard. Stop them and the news doesn't spread, which is a choice with its own cost if anyone else saw.

## 9. Cheap rules that look clever

Each rule is a short reaction to something sensed now. None plans more than one step. They look smart because they happen at the right moment and are shown loudly, which is how the referenced games got the most out of simple AI:
- Halo's Grunts fled when their Elite died, an over-the-top reaction that told players about the hierarchy. Bungie found few players noticed until they added a loud audio cue.
- F.E.A.R.'s soldiers called out what they were doing. Players read flanking and planning into what was mostly moving between cover.
- The Last of Us Part II's enemies mourned found bodies and called fallen friends by name, and that made each one feel like a person.
- Thief's guards had a few readable alert levels with barks and sounds that told the player where they stood.
- The Sims got believable lives by having objects advertise what they offered, so simple agents seemed to make choices.
- STALKER's distant gunfights made the zone feel alive beyond the player's sight.
- Rain World's creatures pursue their own needs whether or not the player is watching.

Rank: **Looks smart** and **Cost** are each from 1 (least) to 5 (most); the list runs from best value down.

| # | Rule | Example in play | Looks smart | Cost |
| --- | --- | --- | --- | --- |
| 1 | **Leader down, group breaks.** When a group's leader dies, members check numbers: fewer than the player's side, flee or surrender; otherwise turn vengeful | You drop the gang boss and three raiders throw down their weapons and run, one kneeling in a doorway | 5 | 1 |
| 2 | **Witness points.** Anyone who saw a theft or a threat points at the culprit for a beat, then runs to the nearest guard and points again | A woman sees you lift a crate, runs to the guard and points straight at you; the guard turns | 5 | 1 |
| 3 | **The stop.** Occupants' quiet sounds stop the moment their awareness passes curious | The skittering in the next room stops dead as you step on glass | 5 | 1 |
| 4 | **Body found.** Anyone who finds a dead member of their group startles, cries out, kneels, then waves the others over and points at the body; the group goes to searching, or threatening if they know who did it | A guard walking his beat finds his partner, kneels, wails, waves the others in, and they spread out to search | 5 | 2 |
| 5 | **Check-in calls.** Pairs on patrol call each other's sign on a rhythm; a missed answer brings a second, sharper call and then a walk to the partner's last known place | After you quietly deal with one sentry, his partner calls "to-MI?", again, louder, then walks over to look | 5 | 2 |
| 6 | **Children inside first.** When a town's mood drops to wary, adults with children near usher them indoors before anything else happens | You walk in with your rifle out and every child vanishes through doors before a single guard moves | 5 | 1 |
| 7 | **Head-turn wave.** People react to a neighbour's startle after a delay set by distance and sight | A shot at the edge of the market, and heads turn in a ripple across the square | 4 | 1 |
| 8 | **Point there.** The first to see the player points; others look where they point, then react to what they see | A lookout points at your ridge, and the camp turns as one to stare at you | 4 | 1 |
| 9 | **Cornered fights.** Anyone with no path away from the player counts as cornered: low nerve surrenders, high nerve attacks | The farmer who ran from you turns at the dead end, hoe raised, his child behind him | 4 | 1 |
| 10 | **Call-back.** A parent who sees their child near a stranger calls the child's sign and beckons; the child runs back | A small child edges toward your ship until a woman calls and the child scampers back, looking over its shoulder | 4 | 1 |
| 11 | **Grudge double take.** People who remember the player personally react before the town does | The trader you robbed last week spots you at 30 m, looks away, snaps back and pulls her goods in | 4 | 2 |
| 12 | **Paying you off.** A frightened, not-yet-hostile town with you approaching sets goods out at the edge of town | You come back to a town you shot up, and there's a crate left on the road with an old man waving you away from it | 5 | 2 |
| 13 | **Guard as anchor.** Calm guards pull a crowd toward calm, and a missing guard lets panic run | A predator wanders into town; where the guard stands firm, people huddle behind him; where he fell, they scatter | 4 | 1 |
| 14 | **Return to the body.** A grieving person who was interrupted goes back to the body once it's safe | After the fight, the woman who fled walks back to the fallen man and kneels again | 4 | 1 |
| 15 | **Habituation.** Repeated harmless visits lower the pressure the player makes in that place | By your fifth visit the stall owners wave first and children play round your boots | 3 | 1 |
| 16 | **Show the way out.** A wave-off is aimed at the actual exit | The guard doesn't just shoo you: he points down the one alley that leaves town | 3 | 1 |
| 17 | **Echo the find.** Someone who finds something (a body, a stash, the player's dropped item) points it out to the nearest companion, who comes to look | A scavenger finds the crate you hid, points, and his partner jogs over | 3 | 1 |
| 18 | **Sound-led look.** People go and look at the last place they heard something, not where the player is | You throw a can; the guard walks to the can, looks round it, shrugs, and walks back | 3 | 1 |
| 19 | **Victim remembers the hand.** Someone who gave you goods under threat won't trade with you, but gives the guard a point when you return | You come back friendly, and she points you out to the guard before you reach the stall | 4 | 2 |
| 20 | **Mob line.** Armed townspeople who outnumber a player they hate gather into a line before advancing | Six farmers with old rifles fall into a line across the road and stand there | 4 | 2 |
| 21 | **Seeing a death.** Everyone who saw it gets a shock beat, then one second beat chosen by who died, their role, their nerve and who did it (section 5.6) | A raider shoots a farmer in the street; his wife runs to him and kneels, his neighbour picks up a hoe and walks at the raider, and the children vanish | 5 | 2 |
| 22 | **The hardened child.** One hardness value turns down a child's play and startles and turns up adult gestures (section 3.6) | A girl of twelve in an oversized coat watches you from a doorway, holds up a flat hand when you come near her little brothers, and doesn't flinch when you draw | 4 | 1 |

Rules 1 to 3 and 6 to 10 are a few lines each on top of what's already there. None needs a planner: each one fires on a sensed event and runs a fixed little sequence of gestures and calls.

## 10. The first 12 to build, plus three consequences

Chosen for being cheap, readable at distance, and built on the rig, voices and quiet sounds already on `proto/bestiary`.

| # | What | Why first |
| --- | --- | --- |
| 1 | **Pressure and the stand-ground and fear ladders, with entry and exit levels and minimum holds** (sections 2.4, 4.2 to 4.4, 4.9) | Everything else hangs on it, and it fixes flicker from day one |
| 2 | **Layered blending with head-first turns and anticipation** (section 4.5) | Makes every existing `BodyAct` read as intended, at no extra cost per gesture |
| 3 | **Hands up when aimed at, back away when the weapon is lowered** (`HandsUp` exists) | The owner's own example, and the most important signal in the game |
| 4 | **Guard watch, halt, wave-off to the exit and weapon show** (`Warn`, `WaveOff` exist) | Gives every wary place a readable line and an exit |
| 5 | **Greeting wave and trader beckon, with the world's gesture dialect** (`Greet`, `Beckon`, `Present` exist) | The friendly half of every town, reading to 30 to 40 m |
| 6 | **Startle and flee with glances back, plus the head-turn wave across a crowd** (`Startle`, `Panic` exist) | The cheapest big crowd effect (rule 7) |
| 7 | **Raider taunt and brandish before attacking** (`Taunt` exists) | Delivers "warned, never ambushed" for people |
| 8 | **Children: tether, hide, play, and adults ushering them inside** (rules 6 and 10) | Makes towns feel alive and gives the earliest mood tell |
| 9 | **Grief at a found body or a death seen, and calling the group over** (`Grieve` exists; rules 4 and 21) | The owner's Last of Us reference, from one event and three gestures; a death seen adds only the shock beat and a choice of second beat |
| 10 | **Voice moods as a continuous blend, with breath-in and sigh, plus hail, warn, alarm and call-sign calls** (extends `Voices.cs`) | Gives every gesture its urgency at distance, and makes the check-in calls possible |
| 11 | **The stop, and knocked-over sounds from real deco** (extends `Quiet.cs`; rule 3) | The strongest telltale sound, for almost nothing |
| 12 | **Rooms and portals for quiet sounds: doors, walls, vents** (section 7.4) | Makes sounds point the right way and keeps hearing fair both ways |
| +1 | **Drop the weapon under aim** (spawns the real item) | The surrender loop's payoff, one item spawn |
| +2 | **Drop goods and flee, with the settlement ledger's "robbed" entry** | The owner's dropped-belongings idea, with a consequence that lasts |
| +3 | **Witness runs to a guard and points** (rule 2) | The best-looking rule in the set, and it makes robbery risky |

## 11. Sources

Used for concepts and roles only. Pages were read as data.

**Nonverbal communication**
- Darwin, *The Expression of the Emotions in Man and Animals* (1872), principle of antithesis: https://brocku.ca/MeadProject/Darwin/Darwin_1872_02.html
- Eyebrow flash as a cross-cultural greeting (Eibl-Eibesfeldt; Grammer et al.): https://en.wikipedia.org/wiki/Eyebrow_flash
- Proxemics, Hall's distance zones and cultural variation: https://www.ebsco.com/research-starters/social-sciences-and-humanities/proxemics and https://thereader.mitpress.mit.edu/?p=1696
- Palm-up and palm-down gestures (deference and assertion): https://center-for-nonverbal-studies.org/htdocs/57912.html
- Pride and shame displays in blind athletes (Tracy and Matsumoto, 2008): https://www.nationalgeographic.com/science/phenomena/2008/08/13/blind-olympic-athletes-show-the-universal-nature-of-pride-and-shame
- Vocal emotion acoustic profiles (Banse and Scherer, 1996): https://archive-ouverte.unige.ch/unige:102055
- Children's speech acoustics (Lee, Potamianos and Narayanan, 1999): https://pubmed.ncbi.nlm.nih.gov/10089598/
- Dolphin signature whistles used to call partners (King and Janik, 2013): https://news.st-andrews.ac.uk/?p=14508 and https://www.nbcnews.com/news/all/dolphins-call-each-other-name-flna1c8451952

**Animals and crowds**
- Musk ox circle and line defence of calves: https://www.nps.gov/gaar/learn/nature/muskox-circle-defense.htm
- Hider and follower strategies in ungulate young: https://www.deer.psu.edu/hide-and-seek/ and https://library.giraffeconservation.org/document-tag/cluster-analysis/
- Fear contagion in crowd models: https://www.southampton.ac.uk/~eg/AAMAS2023/pdfs/p1895.pdf and https://datalearner.com/academic/journal-papers/1387-2532/volumes-and-issues/663/paper-detail/4610

**Games**
- Halo, "The Illusion of Intelligence" (Butcher and Griesemer, GDC 2002): https://www.jmeiners.com/shamans/papers/ai/the_illusion_of_intelligence.pdf and https://www.halo.bungie.org/misc/gdc.2002.haloai/talk.html?page=19
- Halo 2 and 3, exaggerating AI to make it readable (Isla): https://gamedeveloper.com/programming/abandoning-subtlety-to-create-immersive-ai-encounters
- F.E.A.R., squad dialogue narrating simple AI (Orkin): https://www.oreilly.com/library/view/game-ai-pro/9781482254792/K23980_C002.xhtml and https://gdcvault.com/play/1013282/Three-States-and-a-Plan
- The Last of Us Part II, named enemies and mourning: https://www.gamespot.com/articles/the-last-of-us-2s-upgraded-enemy-ai-will-make-for-/1100-6470119/ and https://blog.hyperx.com/article/4462/enemies-in-the-last-of-us-part-ii-will-know-each-other-by-name
- Thief, sensory system and alert states (Leonard, GDC 2003, via summaries): https://intelligent-artifice.com/2003/03/making_sense and https://bosnan.vercel.app/essays/stealth-game-design
- Alien: Isolation, director and creature AI, menace and pacing: https://www.gamedeveloper.com/design/the-perfect-organism-the-ai-of-alien-isolation
- Rain World, creature ecosystem and procedural animation: https://gamedeveloper.com/design/crafting-the-complex-chaotic-ecosystem-of-i-rain-world-i- and https://unity.com/blog/exploring-procedural-design-rain-world
- Journey, wordless chirp communication: https://www.gamedeveloper.com/game-platforms/in-depth-hunicke-and-chen-talk-tgc-s-intriguing-new-i-journey-i-
- Shadow of the Colossus, procedural and keyed motion combined for Agro: https://www.gameanim.com/?p=15474 and https://glitterberri.com/?p=3489
- The Sims, Simlish and emotion over words: https://www.vice.com/en/article/learning-simlish-sims-language-game and https://store.epicgames.com/news/the-sims-history-of-conversations-simlish
- The Sims, objects advertising to simple agents: https://www.pcgamer.com/games/the-sims/will-wright-says-the-original-sims-ai-was-actually-too-good-almost-anything-the-player-did-was-worse-than-the-sims-running-on-autopilot
- Animal Crossing, Animalese: https://nookipedia.com/wiki/Animalese
- Banjo-Kazooie, mumbling voices: https://en.wikipedia.org/wiki/Banjo_%26_Kazooie
- Half-Life, scientists fleeing when attacked or witnessing violence: https://developer.valvesoftware.com/wiki/Monster_barney
- STALKER, distant fights and ambience: https://bit-tech.net/reviews/gaming/ten-years-on-stalker/2/
- SWAT 4, shouting for compliance: https://strategywiki.org/wiki/SWAT_4/Gameplay
- Red Dead Redemption 2, greet, antagonise, defuse and witnesses: https://www.rdr2.org/wiki/nonplayable-characters-npcs/ and https://kotaku.com/being-naughty-and-nice-in-red-dead-redemption-2-1830044669

**Techniques**
- Procedural animation from few keys (Rosen, GDC 2014): https://gdcvault.com/play/1020049/Animation-Bootcamp-An-Indie-Approach
- Hysteresis against state flicker: https://www.shawnhargreaves.com/blog/hysteresis.html and https://research.ncl.ac.uk/game/mastersdegree/gametechnologies/aitutorials/1state-basedai/AI%20-%20State%20Machines.pdf
- Rooms and portals sound propagation: https://audiokinetic.com/en/blog/rooms-and-portals-with-wwise-spatial-audio and Hitman's sound propagation (GDC Europe 2015): https://twvideo01.ubm-us.net/o1/vault/gdceurope2015/Boev_Stepan_Sound%20Propagation%20in.pdf

**Project files read**
- `docs/DESIGN.md` (sections 4, 5 and 17), `docs/world-layers.md` (4.2 and 4.4), `docs/weapon-ideas.md` (5.9 and 7), `docs/sci-fi-inspirations.md`
- On `proto/bestiary`: `shared/Bestiary/Rigs/HumanRig.cs`, `shared/Bestiary/People/Figure.cs`, `shared/Bestiary/Audio/Voices.cs`, `shared/Bestiary/Sim/Quiet.cs`
- On `proto/batch`: `prototypes/wildlife/src/Sim/Senses.cs`, `Brain.cs`, `Creature.cs`, `prototypes/wildlife/src/Core/DeepData.cs`

## 12. Decisions for the owner

**Decided already:** children can be harmed like anyone else, with no special case, and follow normal occupancy rules. Young have child roles; a few hardened children act like adults out of necessity. Most children run and play, while adults walk. Wherever this doc says otherwise, the owner's decision wins.


1. **Can children and animal young be harmed?** Most games make children unharmable or leave them out. My suggestion: people's children can't be damaged or targeted, they hide early so they're rarely near a fight, and raiders and gangs have none in combat. Animal young are ordinary wildlife and can be hunted, as in nature, with their mothers defending them. Hardened children (section 3.6) carry weapons for the look and the warning but never fire them. The alternative is full realism, which would change the game's tone and how it's rated.
2. **Should the player have gestures of their own?** A small quick wheel (wave, hands up, beckon, halt, point, hush) that NPCs read like any other body, and that co-op players see on each other. It's cheap because the rig already exists, it makes peaceful play deliberate, and it gives co-op wordless talk in the spirit of Journey. Without it, the player's only signals are the weapon's position and movement.
3. **Do factionless settlements get their own standing number?** The design gives each settlement a mood that shooting changes, and robbing needs somewhere to land. My suggestion: each settlement gets a local standing on the same bands, stored in its ledger, and the envoy consoles don't reach it, so you have to make amends in person with gifts and help.
4. **How harsh is robbery?** My reference losses are in section 5.4 (about 1,500 for a seen robbery, double for killing someone who surrendered). Is threatening and robbing friendly people meant to be a real, costly way to get goods, or mostly flavour that's quickly punished?
5. **Should there be an accessibility option to show sound direction?** The UI is quiet and the Hollow is never labelled, but deaf and hard-of-hearing players lose most of section 7 without one. My suggestion: an option, off by default, that shows direction-only marks for heard sounds with no labels, and nothing at all for Hollow effects.
6. **Should only the friendly gestures vary by world?** My suggestion keeps surrender, warning and threat the same everywhere for safety, and varies greetings, beckons, distance and a signature gesture per world. The other way gives more alien worlds more alien bodies, at the cost of the player misreading a guard.
