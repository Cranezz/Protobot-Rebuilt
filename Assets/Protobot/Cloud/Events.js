// V2 (build 88): in-game events and tournaments (Docs/Events concept.md). Everything about an event lives here, on the
// server, so nobody can start it early, change a score or skip a no-show: Cloud Save custom data "events", key = the
// event's code (six letters/digits), plus "index" (every event, for the public list and the host limit) and "champions"
// (finished events, newest first). Players can read custom data but only Cloud Code writes it.
//
// One script, `action` says what (params: action, code, data = a JSON object for the action):
//   create {name, desc, robots "any"|"build", buildMinutes, callSeconds, startIn (s) | startAt (UTC ms), listed,
//           autonomous, play (the host plays too), playerName}                         -> { code, event }
//   get                      the event, worked forward to now (start time, build timer, match calls, no-shows)
//   join {name}  leave       before the bracket is made; leaving later forfeits the match you're in
//   robot {name, locked}     your robot's name and whether it's locked in
//   here {match, session}    you're in the match's lobby with your robot (the higher seed also gives the session code)
//   timeout {match}          your one time-out of 3 minutes (VEX allows one per alliance in eliminations)
//   started {match}          the match has begun
//   report {match, mine, theirs}   the final score as your computer saw it (both players send it; they must agree)
//   host only: start, award {match, winner "a"|"b"}, replay {match}, remove {player}, message {text}, end
//   list                     the public events and the champions
// Every answer: { ok, reason?, now (the server's clock, so countdowns don't depend on the player's), event? }.
//
// Matches are today's online matches (1v1): the higher seed ("a", red) hosts an invite-only session and sends its code
// with `here`; the other ("b", blue) joins by it. A called match waits `callSeconds` (2 min by default); whoever isn't
// there by then is a no-show and loses (VEX's rule T5: no grace period). A tie is played again (elimination matches
// can't end tied). Scores that disagree go to the host (or are played again after 10 min).
const { DataApi } = require("@unity-services/cloud-save-1.4");

const STORE = "events";
const MAX_PLAYERS = 32;
const MAX_HOSTED = 3;              // events one player can have going at once
const PRESENT = 30 * 1000;         // a `here` this recent counts as being there
const TIMEOUT = 180 * 1000;        // one time-out per player per event
const LONE_REPORT = 120 * 1000;    // one player's score is taken alone after this (the other's app closed)
const STUCK = 9 * 60 * 1000;       // a match "playing" this long with no score is played again
const DISPUTE = 10 * 60 * 1000;    // scores that disagree are played again after this unless the host decides
const KEEP = 14 * 24 * 3600 * 1000;
const CODE_CHARS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

class Refuse extends Error {}

// ------------------------------------------------------------------ the name filter (NameFilter.cs, ROT13 lists;
// copied by Tools/AgentTesting/lb_filter.py)
const ANYWHERE_ROT = [/*ANYWHERE*/"shpx", "fuvg", "ovgpu", "phag", "avttre", "avttn", "snttbg", "zbgureshpx", "juber", "fyhg", "ergneq", "qvpxurnq", "nffubyr", "chffl", "onfgneq", "jnaxre", "gjng", "pbpxfhpx", "qvyqb", "anmv", "uvgyre", "zbyrfg", "crqbcuvy", "xxx", "puvax", "ornare", "jrgonpx", "genaal", "xlxr", "cbea", "uragnv", "pyvg", "frkl", "ahqr", "anxrq", "betnfz", "znfgheong", "rwnphyng", "cravf", "intvan", "intvan", "gvggl", "fcrez", "pbaqbz", "ohgguby", "erpghz", "fuvgr", "ovngpu", "shpxre", "sntt"];
const WORD_ROT = [/*WORDS*/"nff", "nffrf", "qvpx", "pbpx", "snt", "gvg", "gvgf", "pbba", "ubr", "ubrf", "wvmm", "phz", "frk", "encr", "encrq", "encvfg", "fcvp", "xvxr", "jnc", "cvff", "anqf", "obbo", "obbof", "nany"];
const FINE = [/*FINE*/"shiitake", "scunthorpe", "penistone", "cockburn", "dickinson", "hancock", "peacock", "shuttlecock"];
const rot13 = (s) => s.replace(/[a-z]/g, (c) => String.fromCharCode(97 + (c.charCodeAt(0) - 97 + 13) % 26));
const ANYWHERE = ANYWHERE_ROT.map(rot13);
const WORDS = new Set(WORD_ROT.map(rot13));
const LOOK = { "0": "o", "1": "i", "!": "i", "|": "i", "í": "i", "ì": "i", "3": "e", "é": "e", "è": "e", "ë": "e", "4": "a", "@": "a",
  "á": "a", "à": "a", "ä": "a", "5": "s", "$": "s", "7": "t", "+": "t", "8": "b", "9": "g", "ö": "o", "ó": "o", "ü": "u", "ú": "u" };
function letter(c) { c = c.toLowerCase(); if (LOOK[c]) return LOOK[c]; return c >= "a" && c <= "z" ? c : ""; }
const letters = (s) => [...s].map(letter).join("");
function squash(s) { let out = ""; for (const raw of s) { const c = letter(raw); if (!c) continue; if (out.length && out[out.length - 1] === c) continue; out += c; } return out; }
const doubled = (s) => { for (let i = 1; i < s.length; i++) if (s[i] === s[i - 1]) return true; return false; };
function isWord(part) { const l = letters(part); if (WORDS.has(l)) return true; for (const w of WORDS) if (!doubled(w) && squash(l) === w) return true; return false; }
function allowed(name) {
  if (!name || !name.trim()) return true;
  for (const f of FINE) name = name.replace(new RegExp(f, "gi"), " ");
  const whole = squash(name), raw = letters(name);
  for (const a of ANYWHERE) if (doubled(a) ? raw.includes(a) : whole.includes(a)) return false;
  const parts = []; let current = "", previous = " ";
  for (const c of name) {
    const l = letter(c), upper = c !== c.toLowerCase(), prevLower = previous !== previous.toUpperCase();
    if ((!l || (upper && prevLower)) && current.length) { parts.push(current); current = ""; }
    if (l) current += c;
    previous = c;
  }
  if (current.length) parts.push(current);
  for (const p of parts) if (isWord(p)) return false;
  if (parts.length > 1 && parts.every((p) => p.length === 1) && isWord(name)) return false;
  return true;
}

// ------------------------------------------------------------------ helpers

const Clean = (s, max) => String(s == null ? "" : s).replace(/[\r\n\t<>]/g, " ").replace(/\s+/g, " ").trim().slice(0, max);
const Code = (s) => String(s || "").toUpperCase().replace(/[^A-Z0-9]/g, "").slice(0, 8);
const Num = (v, lo, hi, d) => { const n = Number(v); return isFinite(n) ? Math.min(hi, Math.max(lo, n)) : d; };
const PlayerOf = (ev, id) => ev.players.find((p) => p.id === id);
const NameOf = (ev, id) => { const p = PlayerOf(ev, id); return p ? p.name : "?"; };
const AllMatches = (ev) => (ev.rounds || []).reduce((all, r) => all.concat(r), []);
const MatchOf = (ev, id) => AllMatches(ev).find((m) => m.id === id);
const Log = (ev, now, text) => { ev.log = (ev.log || []).concat([{ at: now, text }]).slice(-80); };

async function Load(data, project, key) {
  const got = await data.getCustomItems(project, STORE, [key]);
  const found = got.data.results.find((r) => r.key === key);
  return found ? { value: found.value, lock: found.writeLock } : { value: null, lock: undefined };
}

async function Save(data, project, key, value, lock) {
  const item = lock ? { key, value, writeLock: lock } : { key, value };
  await data.setCustomItem(project, STORE, item);
}

const Conflict = (e) => e && e.response && (e.response.status === 409 || e.response.status === 412);

// Load, change, work forward, save; again from the top when another call saved in between (write lock).
async function Change(data, project, code, now, change) {
  for (let attempt = 0; attempt < 5; attempt++) {
    const { value: ev, lock } = await Load(data, project, code);
    if (!ev) throw new Refuse("there's no event with that code");
    const result = change(ev);
    Process(ev, now);
    try {
      await Save(data, project, code, ev, lock);
      return { ev, result };
    } catch (e) {
      if (!Conflict(e)) throw e;
    }
  }
  throw new Refuse("the event is busy: try again");
}

// The index: one line per event (the public list, the host limit). Kept loosely: a lost update is fixed by the next.
async function Index(data, project, ev, now, logger) {
  for (let attempt = 0; attempt < 4; attempt++) {
    try {
      const { value, lock } = await Load(data, project, "index");
      const list = (value && Array.isArray(value.events) ? value.events : []).filter((e) => e.code !== ev.code && now - e.created < KEEP);
      if (!ev.deleted) list.push({ code: ev.code, name: ev.name, hostName: ev.hostName, host: ev.host, state: ev.state, listed: !!ev.settings.listed,
        startAt: ev.settings.startAt || 0, startedAt: ev.startedAt || 0, players: ev.players.length, robots: ev.settings.robots, created: ev.created });
      await Save(data, project, "index", { events: list.slice(-500) }, lock);
      return list;
    } catch (e) {
      if (!Conflict(e)) { logger.warning("Events index: " + e.message); return []; }
    }
  }
  return [];
}

async function AddChampion(data, project, ev, logger) {
  const final = ev.rounds[ev.rounds.length - 1][0];
  const loser = final.winner === final.a ? final.b : final.a;
  const winner = PlayerOf(ev, final.winner);
  const entry = { code: ev.code, name: ev.name, hostName: ev.hostName, date: ev.endedAt, players: ev.players.length,
    robots: ev.settings.robots, champion: winner ? winner.name : "?", championId: final.winner, robot: winner ? winner.robot : "",
    runnerUp: NameOf(ev, loser), score: final.note ? "" : Math.max(final.sa, final.sb) + "-" + Math.min(final.sa, final.sb), how: final.note || "" };
  for (let attempt = 0; attempt < 4; attempt++) {
    try {
      const { value, lock } = await Load(data, project, "champions");
      const list = (value && Array.isArray(value.list) ? value.list : []).filter((c) => c.code !== ev.code);
      list.unshift(entry);
      await Save(data, project, "champions", { list: list.slice(0, 200) }, lock);
      return;
    } catch (e) {
      if (!Conflict(e)) { logger.warning("Events champions: " + e.message); return; }
    }
  }
}

// ------------------------------------------------------------------ the bracket

// Seed order for a bracket of `size` (a power of two): 1 v size, then the halves, so the top seeds meet last and the
// byes go to them.
function SeedOrder(size) {
  let order = [1];
  while (order.length < size) {
    const n = order.length * 2;
    order = order.reduce((all, s) => all.concat([s, n + 1 - s]), []);
  }
  return order;
}

function MakeBracket(ev, now) {
  const players = ev.players.filter((p) => !p.out);
  for (let i = players.length - 1; i > 0; i--) {   // random seeding
    const j = Math.floor(Math.random() * (i + 1));
    [players[i], players[j]] = [players[j], players[i]];
  }
  players.forEach((p, i) => { p.seed = i + 1; });
  let size = 2;
  while (size < players.length) size *= 2;
  const order = SeedOrder(size);
  const rounds = [];
  const first = [];
  for (let i = 0; i < size / 2; i++) {
    const a = players[order[2 * i] - 1], b = players[order[2 * i + 1] - 1];
    first.push(NewMatch("r1m" + (i + 1), a ? a.id : null, b ? b.id : null));
  }
  rounds.push(first);
  for (let r = 2, n = size / 4; n >= 1; r++, n /= 2) {
    const round = [];
    for (let i = 0; i < n; i++) round.push(NewMatch("r" + r + "m" + (i + 1), null, null));
    rounds.push(round);
  }
  ev.rounds = rounds;
  ev.state = "playing";
  ev.playingAt = now;
  // Byes go straight through.
  for (const m of first) {
    if (m.a && !m.b) Finish(ev, m, m.a, 0, 0, "bye", now);
    else if (m.b && !m.a) Finish(ev, m, m.b, 0, 0, "bye", now);
  }
  Log(ev, now, "The bracket is made: " + players.length + " players");
}

function NewMatch(id, a, b) {
  return { id, a, b, state: a && b ? "ready" : "wait", winner: null, sa: 0, sb: 0, calledAt: 0, deadline: 0, session: "",
    present: {}, reports: {}, startedAt: 0, note: "", replays: 0, extends: 0 };
}

function Call(ev, m, now) {
  m.state = "called";
  m.calledAt = now;
  m.deadline = now + ev.settings.callSeconds * 1000;
  m.session = "";
  m.present = {};
  m.reports = {};
  m.startedAt = 0;
  m.extends = 0;
}

function Replay(ev, m, now, why) {
  m.replays = (m.replays || 0) + 1;
  Call(ev, m, now);
  m.note = why;
  Log(ev, now, NameOf(ev, m.a) + " vs " + NameOf(ev, m.b) + " is played again: " + why);
}

function Finish(ev, m, winner, sa, sb, note, now) {
  m.state = "done";
  m.winner = winner;
  m.sa = sa;
  m.sb = sb;
  m.note = note || "";
  m.endedAt = now;
  const r = ev.rounds.findIndex((round) => round.includes(m));
  const i = ev.rounds[r].indexOf(m);
  if (note !== "bye") Log(ev, now, NameOf(ev, winner) + " beats " + NameOf(ev, winner === m.a ? m.b : m.a) + (note ? " (" + note + ")" : " " + Math.max(sa, sb) + "-" + Math.min(sa, sb)));
  if (r === ev.rounds.length - 1) {
    ev.state = "done";
    ev.endedAt = now;
    ev.champion = winner;
    Log(ev, now, NameOf(ev, winner) + " wins the event!");
    return;
  }
  const next = ev.rounds[r + 1][Math.floor(i / 2)];
  if (i % 2 === 0) next.a = winner; else next.b = winner;
  if (next.a && next.b && next.state === "wait") next.state = "ready";
  // The player who would have come from the other side left the event: straight through.
  else if (next.forfeit && next.state === "wait") Finish(ev, next, winner, 0, 0, "the other player left", now);
}

// The match a player is in now (called or being played), if any.
const Busy = (ev, id) => AllMatches(ev).some((m) => (m.state === "called" || m.state === "playing" || m.state === "disputed") && (m.a === id || m.b === id));

// Works the event forward to `now`: the start time, the build timer, calling matches, no-shows, lone scores, stuck
// matches. Everything time-based happens here, on whichever call comes first after the time passes.
function Process(ev, now) {
  if (ev.state === "waiting" && ev.settings.startAt > 0 && now >= ev.settings.startAt && ev.players.filter((p) => !p.out).length >= 2) Start(ev, now);
  if (ev.state === "building") {
    const everyone = ev.players.filter((p) => !p.out);
    if (now >= ev.buildEndsAt || (everyone.length >= 2 && everyone.every((p) => p.locked))) {
      for (const p of everyone) p.locked = true;
      Log(ev, now, now >= ev.buildEndsAt ? "Time's up: robots are locked in" : "Everyone is done building");
      MakeBracket(ev, now);
    }
  }
  if (ev.state !== "playing") return;
  for (let pass = 0; pass < 3; pass++) {
    for (const m of AllMatches(ev)) {
      if (m.state === "ready" && !Busy(ev, m.a) && !Busy(ev, m.b)) {
        Call(ev, m, now);
        Log(ev, now, NameOf(ev, m.a) + " vs " + NameOf(ev, m.b) + " is called");
      }
      if (m.state === "called" && now > m.deadline) {
        const here = (id) => m.present[id] && now - m.present[id] < PRESENT;
        if (here(m.a) && here(m.b)) {
          // Both are there but it hasn't begun (a connection problem): wait on, with a fresh session every 3 minutes.
          m.extends = (m.extends || 0) + 1;
          m.deadline = now + 60 * 1000;
          if (m.extends % 3 === 0) m.session = "";
        } else if (here(m.a)) Finish(ev, m, m.a, 0, 0, NameOf(ev, m.b) + " was a no-show", now);
        else if (here(m.b)) Finish(ev, m, m.b, 0, 0, NameOf(ev, m.a) + " was a no-show", now);
        else Finish(ev, m, m.a, 0, 0, "both were no-shows: the higher seed goes through", now);
      }
      if (m.state === "playing" || m.state === "called") Settle(ev, m, now);
      if (m.state === "playing" && m.startedAt && now - m.startedAt > STUCK && Object.keys(m.reports).length === 0) Replay(ev, m, now, "the match didn't finish");
      if (m.state === "disputed" && now - (m.disputedAt || now) > DISPUTE) Replay(ev, m, now, "the scores didn't agree");
    }
    if (ev.state !== "playing") break;
  }
}

// The scores sent in: both agree -> done (a tie is played again); one alone after LONE_REPORT -> taken; they disagree ->
// the host decides.
function Settle(ev, m, now) {
  const ra = m.reports[m.a], rb = m.reports[m.b];
  let sa, sb;
  if (ra && rb) {
    if (ra.mine !== rb.theirs || ra.theirs !== rb.mine) {
      m.state = "disputed";
      m.disputedAt = now;
      Log(ev, now, NameOf(ev, m.a) + " and " + NameOf(ev, m.b) + " sent different scores: the host decides");
      return;
    }
    sa = ra.mine; sb = ra.theirs;
  } else if (ra && now - ra.at > LONE_REPORT) { sa = ra.mine; sb = ra.theirs; }
  else if (rb && now - rb.at > LONE_REPORT) { sa = rb.theirs; sb = rb.mine; }
  else return;
  if (sa === sb) Replay(ev, m, now, "it was a tie " + sa + "-" + sb);
  else Finish(ev, m, sa > sb ? m.a : m.b, sa, sb, "", now);
}

function Start(ev, now) {
  ev.startedAt = now;
  if (ev.settings.robots === "build") {
    ev.state = "building";
    ev.buildEndsAt = now + ev.settings.buildMinutes * 60 * 1000;
    for (const p of ev.players) { p.locked = false; p.robot = ""; }
    Log(ev, now, "Building starts: " + ev.settings.buildMinutes + " minutes");
  } else {
    Log(ev, now, "The event starts");
    MakeBracket(ev, now);
  }
}

// Leaving or being removed after the start: the match you're in (or next) goes to the other player.
function Forfeit(ev, id, now, why) {
  const p = PlayerOf(ev, id);
  if (p) p.out = true;
  if (ev.state !== "playing") return;
  for (const m of AllMatches(ev)) {
    if (m.state === "done" || (m.a !== id && m.b !== id)) continue;
    const other = m.a === id ? m.b : m.a;
    if (other) Finish(ev, m, other, 0, 0, why, now);
    else {   // the other player isn't known yet: whoever comes goes through
      if (m.a === id) m.a = null; else m.b = null;
      m.forfeit = true;
    }
    break;
  }
}

// ------------------------------------------------------------------ the actions

module.exports = async ({ params, context, logger }) => {
  const project = context.projectId, me = context.playerId, now = Date.now();
  const data = new DataApi({ accessToken: context.serviceToken });
  const action = String(params.action || "");
  const code = Code(params.code);
  const p = params.data && typeof params.data === "object" ? params.data : {};
  if (!me) return { ok: false, now, reason: "not signed in" };
  try {
    if (action === "list") {
      const { value } = await Load(data, project, "index");
      const events = (value && Array.isArray(value.events) ? value.events : [])
        .filter((e) => (e.listed || e.host === me) && e.state !== "done" && !e.deleted && now - e.created < KEEP);
      const champs = await Load(data, project, "champions");
      return { ok: true, now, events, champions: (champs.value && champs.value.list) || [] };
    }

    if (action === "create") {
      const name = Clean(p.name, 40), desc = Clean(p.desc, 200), playerName = Clean(p.playerName, 24) || "Player";
      if (!name) return { ok: false, now, reason: "give the event a name" };
      if (!allowed(name) || !allowed(desc)) return { ok: false, now, reason: "that name isn't allowed: choose another" };
      const { value: index } = await Load(data, project, "index");
      const mine = (index && Array.isArray(index.events) ? index.events : []).filter((e) => e.host === me && e.state !== "done" && !e.deleted && now - e.created < KEEP);
      if (mine.length >= MAX_HOSTED) return { ok: false, now, reason: "you have " + MAX_HOSTED + " events going already: end one first" };
      const settings = {
        robots: p.robots === "build" ? "build" : "any",
        buildMinutes: Math.round(Num(p.buildMinutes, 15, 240, 60)),
        callSeconds: Math.round(Num(p.callSeconds, 60, 600, 120)),
        startAt: 0,
        listed: !!p.listed,
        autonomous: p.autonomous !== false,
      };
      if (Number(p.startAt) > 0) settings.startAt = Num(p.startAt, now, now + 30 * 24 * 3600 * 1000, 0);
      else if (Number(p.startIn) > 0) settings.startAt = now + Num(p.startIn, 60, 30 * 24 * 3600, 600) * 1000;
      let made = "";
      for (let attempt = 0; attempt < 8 && !made; attempt++) {
        let c = "";
        for (let i = 0; i < 6; i++) c += CODE_CHARS[Math.floor(Math.random() * CODE_CHARS.length)];
        const { value } = await Load(data, project, c);
        if (!value) made = c;
      }
      if (!made) return { ok: false, now, reason: "couldn't make a code: try again" };
      const ev = { v: 1, code: made, name, desc, host: me, hostName: playerName, created: now, settings, state: "waiting",
        players: [], rounds: [], log: [], messages: [] };
      if (p.play !== false) ev.players.push({ id: me, name: playerName, robot: "", locked: false, joined: now, timeout: false, out: false });
      Log(ev, now, "Event made by " + playerName);
      await Save(data, project, made, ev);
      await Index(data, project, ev, now, logger);
      return { ok: true, now, code: made, event: ev };
    }

    if (!code) return { ok: false, now, reason: "type the event's code" };

    if (action === "get") {
      for (let attempt = 0; attempt < 3; attempt++) {
        const { value: ev, lock } = await Load(data, project, code);
        if (!ev || ev.deleted) return { ok: false, now, reason: "there's no event with that code" };
        const before = JSON.stringify(ev);
        Process(ev, now);
        if (JSON.stringify(ev) === before) return { ok: true, now, event: ev };
        const champion = ev.state === "done" && ev.champion && JSON.parse(before).state !== "done";
        try {
          await Save(data, project, code, ev, lock);
          if (ev.state !== JSON.parse(before).state) await Index(data, project, ev, now, logger);
          if (champion) await AddChampion(data, project, ev, logger);
          return { ok: true, now, event: ev };
        } catch (e) { if (!Conflict(e)) throw e; }
      }
      const { value: ev } = await Load(data, project, code);
      return { ok: true, now, event: ev };
    }

    let stateBefore = "";
    const { ev, result } = await Change(data, project, code, now, (ev) => {
      if (ev.deleted) throw new Refuse("there's no event with that code");
      stateBefore = ev.state;
      const mine = PlayerOf(ev, me);
      const isHost = ev.host === me;
      const match = () => {
        const m = MatchOf(ev, String(p.match || ""));
        if (!m) throw new Refuse("there's no such match");
        return m;
      };
      const inMatch = (m) => { if (m.a !== me && m.b !== me) throw new Refuse("that isn't your match"); };
      switch (action) {
        case "join": {
          if (mine && !mine.out) return "already";
          if (ev.state !== "waiting" && ev.state !== "building") throw new Refuse("the event has started: joining is closed");
          if (ev.players.filter((x) => !x.out).length >= MAX_PLAYERS) throw new Refuse("the event is full (" + MAX_PLAYERS + " players)");
          const name = Clean(p.name, 24) || "Player";
          if (mine) { mine.out = false; mine.name = name; }
          else ev.players.push({ id: me, name, robot: "", locked: false, joined: now, timeout: false, out: false });
          Log(ev, now, name + " joined");
          return "joined";
        }
        case "leave": {
          if (!mine) return "not in";
          if (ev.state === "waiting" || ev.state === "building") ev.players = ev.players.filter((x) => x.id !== me);
          else Forfeit(ev, me, now, mine.name + " left the event");
          Log(ev, now, mine.name + " left");
          return "left";
        }
        case "robot": {
          if (!mine || mine.out) throw new Refuse("you aren't in this event");
          if (ev.state === "done") throw new Refuse("the event is over");
          if (mine.locked && ev.state !== "waiting" && !(ev.state === "building" && p.locked === false && now < ev.buildEndsAt))
            throw new Refuse("your robot is locked in");
          if (ev.state === "playing" && Busy(ev, me) && mine.locked) throw new Refuse("your match is on: the robot can't change now");
          mine.robot = Clean(p.name, 40);
          mine.locked = !!p.locked && mine.robot.length > 0;
          return "set";
        }
        case "here": {
          const m = match();
          inMatch(m);
          if (m.state !== "called" && m.state !== "playing") return "not called";
          m.present[me] = now;
          if (me === m.a && typeof p.session === "string" && /^[A-Za-z0-9]{0,12}$/.test(p.session)) m.session = p.session;
          return "here";
        }
        case "timeout": {
          const m = match();
          inMatch(m);
          if (m.state !== "called") throw new Refuse("a time-out can only be taken before the match begins");
          if (mine.timeout) throw new Refuse("you've had your time-out");
          mine.timeout = true;
          m.deadline += TIMEOUT;
          Log(ev, now, mine.name + " took a time-out (3 min)");
          return "timeout";
        }
        case "started": {
          const m = match();
          inMatch(m);
          if (m.state === "called") { m.state = "playing"; m.startedAt = now; }
          return "started";
        }
        case "report": {
          const m = match();
          inMatch(m);
          if (m.state !== "playing" && m.state !== "called") return "not playing";
          const mineScore = Math.round(Num(p.mine, 0, 999, -1)), theirs = Math.round(Num(p.theirs, 0, 999, -1));
          if (mineScore < 0 || theirs < 0) throw new Refuse("scores out of range");
          m.reports[me] = { mine: mineScore, theirs, at: now };
          if (m.state === "called") { m.state = "playing"; m.startedAt = m.startedAt || now; }
          Settle(ev, m, now);
          return "reported";
        }
        case "start": {
          if (!isHost) throw new Refuse("only the host can start the event");
          if (ev.state !== "waiting") throw new Refuse("it has started already");
          if (ev.players.filter((x) => !x.out).length < 2) throw new Refuse("it needs 2 players at least");
          Start(ev, now);
          return "started";
        }
        case "award": {
          if (!isHost) throw new Refuse("only the host can do that");
          const m = match();
          if (m.state === "done" || !m.a || !m.b) throw new Refuse("that match can't be awarded");
          Finish(ev, m, p.winner === "b" ? m.b : m.a, 0, 0, "awarded by the host", now);
          return "awarded";
        }
        case "replay": {
          if (!isHost) throw new Refuse("only the host can do that");
          const m = match();
          if (m.state === "done" || m.state === "wait" || m.state === "ready") throw new Refuse("that match isn't on");
          Replay(ev, m, now, "the host asked for it");
          return "replay";
        }
        case "remove": {
          if (!isHost) throw new Refuse("only the host can do that");
          const who = PlayerOf(ev, String(p.player || ""));
          if (!who) throw new Refuse("they aren't in this event");
          if (ev.state === "waiting" || ev.state === "building") ev.players = ev.players.filter((x) => x.id !== who.id);
          else Forfeit(ev, who.id, now, who.name + " was removed");
          Log(ev, now, who.name + " was removed by the host");
          return "removed";
        }
        case "message": {
          if (!isHost) throw new Refuse("only the host can do that");
          const text = Clean(p.text, 300);
          if (!text) throw new Refuse("write a message first");
          if (!allowed(text)) throw new Refuse("that message isn't allowed");
          ev.messages = (ev.messages || []).concat([{ at: now, text, id: String(now) }]).slice(-20);
          return "sent";
        }
        case "end": {
          if (!isHost) throw new Refuse("only the host can do that");
          if (ev.state === "done") ev.deleted = true;
          for (const m of AllMatches(ev)) if (m.state !== "done") { m.state = "done"; m.note = "the event was ended"; }
          ev.state = "done";
          ev.endedAt = ev.endedAt || now;
          ev.cancelled = !ev.champion;
          Log(ev, now, "The host ended the event");
          return "ended";
        }
        default:
          throw new Refuse("unknown action " + action);
      }
    });
    if (stateBefore !== "done" && ev.state === "done" && ev.champion) await AddChampion(data, project, ev, logger);
    if (ev.state !== stateBefore || action === "join" || action === "leave" || action === "remove" || action === "end") await Index(data, project, ev, now, logger);
    return { ok: true, now, result, event: ev };
  } catch (e) {
    if (e instanceof Refuse) return { ok: false, now, reason: e.message };
    logger.error("Events " + action + ": " + (e.response ? e.response.status + " " + JSON.stringify(e.response.data).slice(0, 300) : e.stack || e.message));
    return { ok: false, now, reason: "the events server had a problem: try again" };
  }
};

module.exports.params = {
  action: { type: "STRING", required: true },
  code: { type: "STRING", required: false },
  data: { type: "JSON", required: false },
};
