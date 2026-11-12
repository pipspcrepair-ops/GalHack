#!/usr/bin/env python3
"""Throwaway checker for docs/world-layers.md. Reads the tables out of the file itself, so the two
cannot drift apart. Rolls worlds with a fixed seed, checks them, and can paste the results back in.

  python roll_worlds.py            print the report
  python roll_worlds.py --write    also replace the block between the roll markers in the file
"""
import collections
import itertools
import math
import pathlib
import random
import re
import sys

HERE = pathlib.Path(__file__).resolve().parent
DOC = HERE.parent / "world-layers.md"
THEMES = HERE.parent / "world-themes.md"
SEED = 20261107
ROLLS = 200

BEGIN = "<!-- roll results begin: written by world-layers-tools/roll_worlds.py -->"
END = "<!-- roll results end -->"

BAD = []
LIKE = re.compile(r"([a-z]+)\.([A-Za-z_0-9]+)\*([0-9.]+)")


class Rec:
    def __init__(self, kind, id_):
        self.kind, self.id = kind, id_
        self.f = {}
        self.line = ""

    def get(self, key, default=None):
        return self.f.get(key, default)

    @property
    def name(self):
        return " ".join(self.f.get("name", [self.id]))

    @property
    def weight(self):
        return float(self.f.get("weight", ["1"])[0])

    @property
    def tags(self):
        t = set(self.f.get("is", []))
        if "group" in self.f:
            t.add("group." + self.f["group"][0])
        return t

    def pairs(self, key):
        v = self.f.get(key, [])
        return {v[i]: float(v[i + 1]) for i in range(0, len(v) - 1, 2)}

    def reqs(self):
        return parse_req(self.f.get("req", []))


def parse_req(tokens):
    out = []
    for tok in tokens:
        m = re.fullmatch(r"([a-z_0-9]+)(!?=)([A-Za-z0-9_,]+)", tok)
        if not m:
            BAD.append(f"bad req token {tok}")
            continue
        out.append((m.group(1), m.group(2) == "=", set(m.group(3).split(","))))
    return out


def load():
    text = DOC.read_text(encoding="utf-8")
    recs = collections.defaultdict(dict)
    order = collections.defaultdict(list)
    for block in re.findall(r"```layers\n(.*?)```", text, re.S):
        for raw in block.splitlines():
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            parts = [p.strip() for p in line.split(" | ")]
            head = parts[0].split()
            r = Rec(head[0], head[1] if len(head) > 1 else "")
            if len(head) > 2:
                r.f["args"] = head[2:]
            r.line = line
            for p in parts[1:]:
                w = p.split()
                r.f[w[0]] = w[1:]
            if r.id in recs[r.kind] and r.kind != "clash":
                BAD.append(f"duplicate {r.kind} {r.id}")
            recs[r.kind][r.id] = r
            order[r.kind].append(r)
    return recs, order


REC, ORDER = load()
RULES = REC["rule"]
CLASHES = [(r.id, r.f["args"][0]) for r in ORDER["clash"]]

BODY = ["zone", "type", "air", "climate"]
PROFILE = ["pre", "post", "port", "people", "trade", "ran"]
HISTORY = ["phase", "age", "fall"]
LOOK = ["build", "infra", "running", "creed"]
SURFACE = ["flora", "fauna", "sky", "weather", "palette", "sound", "marks"]
FACT_LAYERS = {"zone", "type", "pre", "post", "port", "people", "phase", "age"}
SETTLED_ONLY = set(PROFILE) | set(HISTORY) | {"moment", "theme", "build", "infra", "running", "creed"}
LAYERS = ["zone", "type", "air", "climate", "land", "mod", "pre", "post", "port", "people", "trade", "ran",
          "phase", "age", "fall", "moment", "theme", "build", "infra", "running", "presence", "creed",
          "feature", "hazard", "flora", "fauna", "sky", "weather", "palette", "sound", "marks", "occ"]


class World:
    def __init__(self, start=False):
        self.facts = {"start": "yes" if start else "no"}
        self.picks = {}
        self.rolled = []
        self.tags = set()
        self.only = []
        self.breaks = []
        self.break_left = 0

    def add(self, layer, rec):
        self.picks.setdefault(layer, []).append(rec)
        self.rolled.append(rec)
        self.tags |= rec.tags
        if layer in FACT_LAYERS:
            self.facts[layer] = rec.id
        if "speed" in rec.f:
            self.facts["speed"] = rec.f["speed"][0]
        for s in rec.f.get("sets", []):
            k, v = s.split("=")
            self.facts[k] = v
        for o in rec.f.get("only", []):
            self.only.append(tuple(o.split(".")))

    def one(self, layer):
        p = self.picks.get(layer)
        return p[0] if p else None

    def ids(self, layer):
        return [r.id for r in self.picks.get(layer, [])]


def fact_ok(world, req):
    key, want_in, vals = req
    have = world.facts.get(key)
    if have is None:
        return True  # not rolled yet, so nothing to check against
    return (have in vals) == want_in


def rule_state(world, rec):
    """Returns (ok under hard rules, ok under soft rules too)."""
    hard = soft = True
    for req in rec.reqs():
        if not fact_ok(world, req):
            hard = False
    for tag in rec.f.get("is", []):
        rule = RULES.get(tag)
        if rule is None:
            continue
        if not all(fact_ok(world, r) for r in rule.reqs()):
            if "soft" in rule.f:
                soft = False
            else:
                hard = False
    for a, b in CLASHES:
        for x, y in ((a, b), (b, a)):
            if x in rec.tags and y in world.tags:
                soft = False
    return hard, soft


def legal(world, layer, rec, filters=True):
    if not filters:
        return True, True
    for lay, tag in world.only:
        if lay == layer and tag not in rec.tags:
            return False, False
    t = world.one("type")
    if t is not None:
        if layer == "air" and rec.id not in t.f["air"]:
            return False, False
        if layer == "land" and rec.id not in t.f["land"]:
            return False, False
        if layer == "climate":
            s = dict(x.split("=") for x in rec.f["sets"])
            if s["temp"] not in t.f["temp"] or s["water"] not in t.f["water"]:
                return False, False
    if layer == "type" and world.facts["start"] == "yes" and rec.get("start") != ["yes"]:
        return False, False
    return rule_state(world, rec)


def weight(world, layer, rec):
    w = rec.weight
    for r in world.rolled:
        for like in r.f.get("like", []):
            m = LIKE.fullmatch(like)
            if m and m.group(1) == layer and m.group(2) == rec.id:
                w *= float(m.group(3))
    if layer == "theme":  # a theme fits by how well it suits what has already been rolled
        for like in rec.f.get("like", []):
            m = LIKE.fullmatch(like)
            if m and m.group(2) in world.ids(m.group(1)):
                w *= float(m.group(3))
    return w


def pick(rng, world, layer, filters=True, force=None, exclude=()):
    pool, hard_only = [], []
    for rec in ORDER[layer]:
        if rec.id in exclude:
            continue
        h, s = legal(world, layer, rec, filters)
        if h and s:
            pool.append(rec)
        elif h:
            hard_only.append(rec)
    if force is not None:
        rec = REC[layer][force]
        if rec in pool:
            return rec
        if rec in hard_only:
            world.breaks.append(f"{layer}.{rec.id}")
            return rec
        return None
    if world.break_left and hard_only and rng.random() < 0.25:
        world.break_left -= 1
        rec = rng.choice(hard_only)
        world.breaks.append(f"{layer}.{rec.id}")
        return rec
    if not pool:
        return None
    return rng.choices(pool, [max(weight(world, layer, r), 1e-6) for r in pool])[0]


def roll(rng, start=False, filters=True, force=None):
    """Follows the chain in the doc. With force, a dict of layer to ids; returns None if they can't hold."""
    force = force or {}
    w = World(start)
    w.break_left = 1 if rng.random() < 0.04 else 0

    def one(layer):
        f = force.get(layer, [None])[0]
        rec = pick(rng, w, layer, filters, f)
        if rec is None:
            return False
        w.add(layer, rec)
        return True

    def many(layer, lo, hi, chance=1.0):
        n = rng.randint(lo, hi)
        if rng.random() > chance:
            n = lo
        got = set()
        for f in force.get(layer, []):
            rec = pick(rng, w, layer, filters, f)
            if rec is None:
                return False
            w.add(layer, rec)
            got.add(f)
        while len(got) < n:
            rec = pick(rng, w, layer, filters, None, got)
            if rec is None:
                break
            w.add(layer, rec)
            got.add(rec.id)
        return True

    for layer in BODY:
        if not one(layer):
            return None
    t = w.one("type")
    w.facts["life"] = force.get("life", [rng.choice(t.f["life"])])[0]
    if not many("land", 2, 2):
        return None
    settle = float(t.f["settle"][0]) * float(w.one("air").f.get("settle", ["1"])[0])
    wants_settled = any(k in force for k in SETTLED_ONLY)
    if start:
        settle = max(settle, 0.8)
    if "unsettled" in force:
        settled = False
    else:
        settled = wants_settled or rng.random() < settle
    w.facts["settled"] = "yes" if settled else "no"
    if settled:
        for layer in PROFILE + HISTORY:
            if not one(layer):
                return None
            if layer == "post":
                order = "LMH"
                d = order.index(w.facts["pre"]) - order.index(w.facts["post"])
                w.facts["drop"] = str(d) if d >= 0 else "up"
        if not many("mod", 0, 2, 0.6) or not one("moment"):
            return None
        if rng.random() < 0.33 or "scar" in force:
            f = force.get("scar", [None])[0]
            rec = pick(rng, w, "fall", filters, f, {w.one("fall").id})
            if rec is not None:
                w.picks["scar"] = [rec]
                w.rolled.append(rec)
        if not one("theme"):
            return None
        if rng.random() < 0.25:
            rec = pick(rng, w, "theme", filters, None, {w.one("theme").id, "plain"})
            if rec is not None:
                w.picks["theme2"] = [rec]
                w.rolled.append(rec)
        for layer in ("build", "infra", "running"):
            if not one(layer):
                return None
    else:
        w.facts.update(pre="none", post="none", port="0", people="none", age="none", drop="0")
        w.add("phase", REC["phase"]["untouched"])
        w.only.append(("theme", "wild"))
        if not one("theme"):
            return None
        if not many("mod", 0, 2, 0.6):
            return None
    if not many("feature", 1, 3) or not many("presence", 0, 2, 0.75):
        return None
    if settled and not one("creed"):
        return None
    if not many("hazard", 1, 3):
        return None
    for layer in SURFACE:
        if not one(layer):
            return None
    finish(w)
    return w


def finish(w):
    """Adds the tables together, the same way WorldRoll.Combine does."""
    mix = collections.Counter()
    tables = collections.defaultdict(collections.Counter)
    w.tagset, w.orbit, w.needs, w.style = [], [], [], {}
    for r in w.rolled:
        for key in ("region", "hub", "industry", "plant", "farm", "poi"):
            for k, v in r.pairs(key).items():
                tables[key][k] += v
        for k, v in r.pairs("occ").items():
            mix[k] += v
        w.tagset += [t for t in r.f.get("tags", []) if t not in w.tagset]
        w.orbit += [t for t in r.f.get("orbit", []) if t not in w.orbit]
        w.needs += [n for n in r.f.get("needs", []) if n not in w.needs]
        s = r.f.get("style", [])
        for i in range(0, len(s) - 1, 2):
            w.style[s[i]] = s[i + 1]
    w.tables = tables
    w.mix = {}
    for code, v in mix.items():
        rec = REC["occ"].get(code)
        if rec is None:
            BAD.append(f"unknown occupancy {code} in a roll")
            continue
        if v > 0 and all(rule_state(w, rec)):
            w.mix[code] = v
    total = sum(w.mix.values()) or 1.0
    w.mix = {k: round(100 * v / total) for k, v in sorted(w.mix.items(), key=lambda kv: -kv[1])}


def echoes(w):
    """The old themes a world echoes: those with at least two of their defining parts on this world."""
    have = {f"{lay}.{r.id}" for lay, recs in w.picks.items() for r in recs}
    have |= {f"fall.{r.id}" for r in w.picks.get("scar", [])}
    out = []
    for old in ORDER["old"]:
        parts = [x for x in old.f.get("parts", []) if x.split(".")[0] not in ("type", "pre", "post", "port", "zone", "air", "land", "people", "unsettled")]
        hit = sum(1 for x in parts if x in have)
        if parts and hit >= min(2, len(parts)):
            out.append((hit / len(parts), old.id))
    return [i for _, i in sorted(out, reverse=True)]


# ---- plain words --------------------------------------------------------------------------------

def phrase(w, layer, default=""):
    r = w.one(layer)
    return r.name if r else default


def describe(w, n):
    t = w.one("type")
    bits = [f"**{n}.** {t.name}, {phrase(w,'air')}, {phrase(w,'climate')}, {phrase(w,'zone')}."]
    land = " and ".join(r.name for r in w.picks.get("land", []))
    mods = ", ".join(r.name for r in w.picks.get("mod", []))
    bits.append(f"Land: {land}" + (f"; {mods}." if mods else "."))
    if w.facts["settled"] == "yes":
        bits.append(f"Once {phrase(w,'pre')}, now {phrase(w,'post')}, {phrase(w,'people')}, with {phrase(w,'port')}.")
        bits.append(f"It lived on {phrase(w,'trade')}, {phrase(w,'ran')}, built in {phrase(w,'build')}, joined by {phrase(w,'infra')}.")
        fall = f"It fell by {phrase(w,'fall')}, {phrase(w,'moment')}, {phrase(w,'age')}, and is {phrase(w,'phase')}"
        if w.picks.get("scar"):
            fall += f", with a scar of {w.picks['scar'][0].name} in one region"
        bits.append(fall + ".")
        th = phrase(w, "theme")
        if w.picks.get("theme2"):
            th += f"; in one region, {w.picks['theme2'][0].name}"
        bits.append(f"Mood: {th}. Still running: {phrase(w,'running')}. Belief: {phrase(w,'creed')}.")
    else:
        bits.append(f"Never settled. Mood: {phrase(w,'theme')}.")
    pres = ", ".join(r.name for r in w.picks.get("presence", [])) or "nobody new"
    feats = ", ".join(r.name for r in w.picks.get("feature", []))
    bits.append(f"Now: {pres}. Features: {feats}.")
    haz = ", ".join(r.name for r in w.picks.get("hazard", []))
    bits.append(f"Danger: {haz}. Life: {phrase(w,'flora')}, {phrase(w,'fauna')}.")
    bits.append(f"Sky: {phrase(w,'sky')}, {phrase(w,'weather')}, {phrase(w,'palette')}. Sound: {phrase(w,'sound')}. Marks: {phrase(w,'marks')}.")
    top = ", ".join(f"{k} {v}" for k, v in list(w.mix.items())[:4])
    if top:
        bits.append(f"Places: {top}.")
    ech = echoes(w)[:3]
    if w.orbit or ech:
        bits.append("Orbit reads: " + " ".join(w.orbit + [f"echo:{e}" for e in ech]) + ".")
    if w.breaks:
        bits.append(f"Rare break: {', '.join(w.breaks)}.")
    return " ".join(bits)


# ---- checks -------------------------------------------------------------------------------------

def nonsense(w):
    """The worst nonsense, written from the rolled facts and ids, independent of the filter's rules."""
    f, out = w.facts, []

    def has(layer, *tags):
        return any(set(tags) & r.tags for r in w.picks.get(layer, []))
    settled = f.get("settled") == "yes"
    if settled and f.get("air0") == "none" and has("build", "open"):
        out.append("open streets built on an airless world")
    if f.get("air") == "none" and has("fauna", "beasts"):
        out.append("breathing animals on the surface of an airless world")
    if f.get("air") == "none" and has("flora", "plants"):
        out.append("plants in the open on an airless world")
    if f.get("air") == "none" and has("weather", "air_weather"):
        out.append("weather without air")
    if settled and f.get("air0") == "none" and has("infra", "open"):
        out.append("open roads across vacuum")
    if settled and f.get("life") == "none" and has("trade", "crops"):
        out.append("open farming on lifeless ground")
    if settled and f.get("water") != "sea" and has("trade", "sea"):
        out.append("a sea trade with no sea")
    if settled and f.get("pre") == "L" and has("fall", "mid", "high"):
        out.append("a modern or stellar fall on a low-tech world")
    if settled and f.get("pre") == "M" and has("fall", "high"):
        out.append("a stellar fall on a mid-tech world")
    if settled and f.get("pre") != "H" and has("build", "high"):
        out.append("high-tech architecture on a lower world")
    if settled and f.get("post") == "L" and f.get("air") == "none":
        out.append("low-tech survivors living in vacuum")
    if settled and f.get("phase") == "falling" and f.get("age") != "now":
        out.append("a mid-collapse world with an old fall")
    if settled and f.get("phase") == "recovered" and f.get("post") == "L":
        out.append("a recovered world at low tech")
    if settled and f.get("people") == "none" and has("creed", "living"):
        out.append("a creed with no one to hold it")
    if settled and f.get("port") == "0" and has("fall", "port"):
        out.append("a port disaster with no port")
    if f.get("start") == "yes" and any("faction" in r.tags for r in w.picks.get("presence", [])):
        out.append("a faction on a start world")
    if f.get("start") == "yes" and f.get("air") not in ("open", "thin"):
        out.append("a start world you can't breathe on")
    if f.get("temp") in ("scorch", "hot") and has("weather", "snowy"):
        out.append("snow on a hot world")
    if settled and has("mod", "high") and f.get("pre") != "H":
        out.append("terraforming by a people who couldn't")
    if settled and f.get("water") == "sea" and has("infra", "overland"):
        out.append("a continental road or rail web on an ocean world")
    return out


def histogram(worlds):
    h = collections.defaultdict(collections.Counter)
    for w in worlds:
        for layer, recs in w.picks.items():
            for r in recs:
                h[layer][r.id] += 1
    return h


SPINE = ["type", "air", "climate", "pre", "post", "trade", "ran", "phase", "age", "fall", "moment", "theme", "build",
         "infra", "running", "creed", "flora", "fauna", "sky", "weather", "palette", "sound", "marks"]


def shared(a, b):
    n = 0
    for lay in SPINE:
        x, y = a.one(lay), b.one(lay)
        if x is not None and x is y:
            n += 1
    return n


def legal_at(facts, layer, rec, only):
    w = World(False)
    w.facts = dict(facts)
    w.only = list(only)
    return all(legal(w, layer, rec))


def count_combos():
    """Exact count of legal rolls, by fact state, memoising how many values of a layer fit that state."""
    memo = {}

    def legal_count(layer, facts, only):
        key = (layer, tuple(sorted(facts.items())), only)
        if key not in memo:
            memo[key] = sum(1 for r in ORDER[layer] if legal_at(facts, layer, r, only))
        return memo[key]

    core = full = 0
    for t in ORDER["type"]:
        tw = World(False)
        tw.add("type", t)
        only_t = tuple(tw.only)
        for z, a, c, lf in itertools.product(ORDER["zone"], ORDER["air"], ORDER["climate"], t.f["life"]):
            if a.id not in t.f["air"]:
                continue
            s = dict(x.split("=") for x in c.f["sets"])
            if s["temp"] not in t.f["temp"] or s["water"] not in t.f["water"]:
                continue
            base = {"zone": z.id, "air0": a.id, "air": a.id, "temp": s["temp"], "water": s["water"], "life": lf,
                    "start": "no", "settled": "yes", "type": t.id}
            for pre in ORDER["pre"]:
                if not legal_at(base, "pre", pre, only_t):
                    continue
                f1 = dict(base, pre=pre.id)
                for post in ORDER["post"]:
                    if not legal_at(f1, "post", post, only_t):
                        continue
                    order = "LMH"
                    d = order.index(pre.id) - order.index(post.id)
                    f2 = dict(f1, post=post.id, drop=str(d) if d >= 0 else "up")
                    for phase in ORDER["phase"]:
                        if not legal_at(f2, "phase", phase, only_t):
                            continue
                        f3 = dict(f2, phase=phase.id)
                        only3 = only_t + tuple(tuple(o.split(".")) for o in phase.f.get("only", []))
                        sp = 1
                        for lay in ("age", "port", "people", "fall", "trade", "ran", "moment", "build", "infra", "running"):
                            sp *= legal_count(lay, f3, only3)
                        core += sp
                        fu = sp
                        for lay in ("theme", "creed", "flora", "fauna", "sky", "weather", "palette", "sound", "marks"):
                            fu *= max(1, legal_count(lay, f3, only3))
                        for lay, k, lo in (("presence", 2, 0), ("feature", 3, 1), ("hazard", 3, 1), ("mod", 2, 0)):
                            n = legal_count(lay, f3, only3)
                            fu *= sum(math.comb(n, i) for i in range(lo, k + 1))
                        full += fu
    return core, full


def check_references():
    errs = list(BAD)
    known = set()
    for r in ORDER["fam"]:
        known |= set(r.f.get("list", []))
    text = THEMES.read_text(encoding="utf-8")
    new_table = set(re.findall(r"^\| ([a-z_]+) \| \d+ \|$", text, re.M))
    known |= new_table
    used_fams, used_tags = set(), set()
    for kind, recs in ORDER.items():
        if kind in ("rule", "clash", "fam", "old", "oldtype", "dress"):
            continue
        for r in recs:
            for key in ("hub", "industry", "plant", "farm", "poi"):
                used_fams |= set(r.pairs(key))
            used_fams |= set(r.f.get("needs", []))
            used_tags |= set(r.f.get("tags", []))
            for like in r.f.get("like", []):
                m = LIKE.fullmatch(like)
                if not m or m.group(2) not in REC[m.group(1)]:
                    errs.append(f"{kind} {r.id}: bad like {like}")
            for code in r.pairs("occ"):
                if code not in REC["occ"]:
                    errs.append(f"{kind} {r.id}: unknown occupancy {code}")
            for tag in r.f.get("is", []):
                if tag not in RULES and tag not in KNOWN_TAGS:
                    errs.append(f"{kind} {r.id}: tag {tag} has no rule")
            for o in r.f.get("only", []):
                lay, _, tag = o.partition(".")
                if not any(tag in x.tags for x in ORDER[lay]):
                    errs.append(f"{kind} {r.id}: only {o} matches nothing")
    for f in sorted(used_fams - known):
        errs.append(f"family {f} is neither existing nor wanted")
    for f in sorted(new_table - used_fams):
        errs.append(f"new family {f} has no home in any layer")
    for t in sorted(used_tags - set(REC["dress"])):
        errs.append(f"dressing set {t} is not in the dress table")
    for t in sorted(set(REC["dress"]) - used_tags):
        errs.append(f"dressing set {t} is never used")
    old_ids = re.findall(r"^\*\*(\d+)\. .*?\*\* \(`([a-z_]+)`\)", text, re.M)
    themes = [(int(n), i) for n, i in old_ids if int(n) <= 112]
    ledger = {r.id: r for r in ORDER["old"]}
    for n, i in themes:
        if i not in ledger:
            errs.append(f"theme {n} {i} is not in the ledger")
    for i in sorted(set(ledger) - {i for _, i in themes}):
        errs.append(f"ledger {i} is not an old theme")
    for r in ORDER["oldtype"]:
        for part in r.f["to"]:
            lay, _, val = part.partition(".")
            if val not in REC[lay]:
                errs.append(f"old type {r.id}: unknown home {part}")
    falls = set(re.findall(r"^\*\*Z\d+\. .*?\*\* \(`([a-z_]+)`\)", text, re.M))
    for f in sorted(falls - set(REC["fall"])):
        errs.append(f"apocalypse {f} is missing")
    return errs, len(themes), len(falls), len(used_fams), len(new_table)


KNOWN_TAGS = {"now", "mild", "flood", "hightech", "falling", "spared", "wild"}


def check_ledger(rng):
    fails = []
    done = collections.Counter()
    for r in ORDER["old"]:
        force = collections.defaultdict(list)
        bad = False
        for part in r.f.get("parts", []):
            lay, _, val = part.partition(".")
            if lay == "unsettled":
                force["unsettled"] = [val]
                continue
            if val not in REC["fall" if lay == "scar" else lay]:
                fails.append(f"{r.id}: unknown part {part}")
                bad = True
            force[lay].append(val)
        to = r.f["to"][0]
        done[to] += 1
        if to != "dissolved" and to not in REC["theme"]:
            fails.append(f"{r.id}: unknown home {to}")
        if to != "dissolved" and "theme" not in force:
            force["theme"].append(to)
        if bad:
            continue
        ok = False
        why = collections.Counter()
        for _ in range(500):
            w = roll(rng, force=force)
            if w is None:
                continue
            n = nonsense(w)
            if not n and not w.breaks:
                ok = True
                break
            why.update(n)
            why.update("break " + b for b in w.breaks)
        if not ok:
            fails.append(f"{r.id}: its parts never hold together {why.most_common(2)}")
    return fails, done


def main():
    write = "--write" in sys.argv
    out = []
    errs, n_themes, n_falls, n_fams, n_new = check_references()
    counts = {k: len(ORDER[k]) for k in LAYERS}
    total = sum(counts.values())
    out.append("Values by layer: " + ", ".join(f"{k} {v}" for k, v in counts.items()))
    out.append(f"Total layer values: {total}, plus {len(ORDER['dress'])} dressing sets, {len(ORDER['rule'])} rules and {len(ORDER['clash'])} clashes.")
    out.append(f"Old lists found and homed: {n_themes} themes, {n_falls} apocalypses, {n_new} new POI families; {n_fams} families referenced in all.")

    rng = random.Random(SEED)
    worlds, tries = [], 0
    while len(worlds) < ROLLS:
        tries += 1
        w = roll(rng)
        if w is not None:
            worlds.append(w)
    starts = []
    while len(starts) < 60:
        w = roll(rng, start=True)
        if w is not None:
            starts.append(w)
    control = []
    rng2 = random.Random(SEED)
    while len(control) < ROLLS:
        w = roll(rng2, filters=False)
        if w is not None:
            control.append(w)

    settled = [w for w in worlds if w.facts["settled"] == "yes"]
    out.append(f"Rolled {ROLLS} worlds in {tries} tries with seed {SEED}: {len(settled)} settled, {ROLLS-len(settled)} never settled, plus 60 start worlds.")

    h = histogram(worlds)
    out.append("")
    out.append("Most common value in each layer, as a share of the worlds that rolled it:")
    flags = []
    for lay in LAYERS + ["scar"]:
        if lay not in h:
            continue
        rollers = len(settled) if (lay in SETTLED_ONLY and lay not in ("theme", "phase")) or lay == "scar" else ROLLS
        top, n = h[lay].most_common(1)[0]
        share = 100 * n / rollers
        size = len(ORDER["fall" if lay == "scar" else lay])
        out.append(f"  {lay:9s} {top:22s} {share:4.0f}%   {len(h[lay])} of {size} values seen")
        limit = 70 if size <= 6 else 24
        if share > limit and "blank" not in REC["fall" if lay == "scar" else lay][top].f:
            flags.append(f"{lay}.{top} {share:.0f}%")
    out.append("Dominance flags: " + (", ".join(flags) if flags else "none"))

    pairs = [(shared(a, b), a, b) for a, b in itertools.combinations(settled, 2)]
    mean = sum(p[0] for p in pairs) / len(pairs)
    closest = max(pairs, key=lambda p: p[0])
    out.append("")
    out.append(f"Of {len(SPINE)} single-value layers, two settled worlds share {mean:.1f} on average; the closest pair shares {closest[0]}.")
    keyed = collections.Counter((w.one('type').id, w.one('fall').id, w.one('trade').id) for w in settled)
    out.append(f"Settled worlds with the same type, fall and trade as another: {sum(n for n in keyed.values() if n > 1)} of {len(settled)}.")
    empt = [w.mix.get('EMP', 0) for w in settled]
    out.append(f"Places left empty: average {sum(empt)/len(empt):.0f}%, range {min(empt)} to {max(empt)}.")
    tops = collections.Counter(next(iter(w.mix)) for w in settled if w.mix)
    out.append("Biggest occupant by world: " + ", ".join(f"{k} {v}" for k, v in tops.most_common()))
    themes = collections.Counter(w.one('theme').id for w in settled)
    out.append(f"Themes seen: {len(themes)} of {len(ORDER['theme'])}; plain on {themes.get('plain', 0)} worlds.")

    bad_control = sum(1 for w in control if nonsense(w))
    bad_kept = [n for w in worlds + starts for n in nonsense(w)]
    tally = collections.Counter(n for w in control for n in nonsense(w))
    out.append("")
    out.append(f"Worst nonsense: with the filters off, {bad_control} of {ROLLS} worlds fail at least one check; with them on, {len(set(id(w) for w in worlds + starts if nonsense(w)))} of {ROLLS + 60}.")
    for k, v in tally.most_common(8):
        out.append(f"  filters off: {v:3d} {k}")
    for k, v in collections.Counter(bad_kept).most_common():
        out.append(f"  FILTERS ON:  {v:3d} {k}")
    br = sum(1 for w in worlds if w.breaks)
    out.append(f"Rare breaks kept on purpose: {br} of {ROLLS} worlds.")

    echoed = sum(1 for w in settled if echoes(w))
    out.append(f"Settled worlds that echo at least one old theme, for the orbit tables: {echoed} of {len(settled)}.")

    start_types = collections.Counter(w.one('type').id for w in starts)
    out.append(f"Start worlds: {len(start_types)} types seen; most common {start_types.most_common(3)}.")
    plan = [w for w in settled if not (w.tables['hub'] and w.tables['region'] and w.needs and w.style)]
    out.append(f"Settled worlds with an empty region plan table: {len(plan)}.")

    fails, done = check_ledger(random.Random(SEED + 1))
    dissolved = done.pop('dissolved', 0)
    kept = sum(done.values())
    out.append("")
    out.append(f"Ledger: {dissolved} old themes dissolve into parts; {kept} live on in {len(done)} themes; every recipe rolled legal: {'yes' if not fails else 'NO'}.")
    errs += fails

    core, full = count_combos()
    out.append(f"Distinct legal combinations: core layers {core:.3g}; all layers {full:.3g}.")

    if errs:
        out.append("")
        out.append(f"PROBLEMS ({len(errs)}):")
        out += ["  " + e for e in errs[:80]]

    report = "\n".join(out)
    print(report)
    print()
    samples = [describe(w, i + 1) for i, w in enumerate(worlds)][:20]
    print("\n\n".join(samples))
    if write:
        text = DOC.read_text(encoding="utf-8")
        block = BEGIN + "\n\n```text\n" + report + "\n```\n\nTwenty sample worlds, the first twenty of the roll, unedited:\n\n" + "\n\n".join(samples) + "\n\n" + END
        text = re.sub(re.escape(BEGIN) + r".*?" + re.escape(END), lambda m: block, text, flags=re.S)
        DOC.write_bytes(text.encode("utf-8"))


if __name__ == "__main__":
    main()
