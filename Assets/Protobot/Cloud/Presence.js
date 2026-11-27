// V2: the app's heartbeat (once a minute while Protobot runs, for players who agreed to the terms). It keeps:
// - presence: when each player was last seen (private custom data "presence", key = player id), from which the number
//   of players online now (seen in the last 150 s) is counted at most every 45 s and kept in "presence-stats"/"count";
// - usage: how long each player has used Protobot and on what (private custom data "usage", key = player id: name,
//   account, total seconds, seconds per area, seconds per day for the last 60 days, first and last seen), which only the
//   developer reads (Tools > Protobot > Players). Nothing here is readable by players.
// - the Discord server's "Players online" channel (V2, build 79): when the count is worked out again and it changed, the
//   channel is renamed to show it (Discord lets a channel be renamed twice in 10 minutes: at most once in 5 here). The
//   bot token and the channel's id are private custom data "secrets" ("discordBotToken", "discordOnlineChannel"), set
//   from Tools/AgentTesting/cs/t_discord_online.cs. A schedule runs this script every hour with no player (UGS Scheduler
//   "discord-online" -> trigger "discord-online"), so the channel goes back to 0 when everyone has left.
// - news (V2, build 85), private custom data "news"/"state", worked out again at most every 20 s by whichever heartbeat
//   comes first (a write lock keeps two from doing it at once):
//   - latest: the newest release's build number, read from GitHub's latest release (releases.win.json, at most once a
//     minute), so every app online hears of an update within about a minute of it being published;
//   - messages: what the developer sent the players online from the Discord channel #player-messages (admins only; its
//     id is "secrets"/"discordConsoleChannel", set by Tools/AgentTesting/cs/t_discord_console.cs): `!ask <question>`
//     (a card with a text box: answers come back through Feedback.js), `!notify <text>`, `!list`, `!stop <id|all>`,
//     `!help`, `!test ask|notify <text>` (only the developer's own players: "secrets"/"devPlayers"); an optional time after the command (`!ask 2h ...`: default 24 h, at most 7 days). The bot answers each
//     command as a reply. Only people (and the bot itself, for tests) give commands: other bots and webhooks are ignored.
// Params: areas = { area: seconds since the last heartbeat }, name = the name they play under, account = "1" when signed
// in to an account, leaving = "1" when the app is closing (they're no longer counted). Returns { online, latest, messages }
// (messages: [{ id, kind "ask"|"notify", text }] that are up now).
const axios = require("axios-1.6");
const { DataApi } = require("@unity-services/cloud-save-1.4");

const AREAS = ["Home menu", "Building", "Simulating", "Game", "Multiplayer", "Code", "Sketching", "Replay"];
const ONLINE_WINDOW = 150 * 1000;   // seen this recently: online
const RECOUNT = 45 * 1000;          // the count is worked out again at most this often
const RENAME_GAP = 5 * 60 * 1000;   // the Discord channel is renamed at most this often

function ChannelName(online) {
  return (online > 0 ? "🟢" : "⚫") + " Players online: " + online;
}

// Renames the Discord channel when the number on it isn't the one now. Never logs the token.
async function UpdateDiscord(data, project, online, now, logger) {
  try {
    const got = await data.getPrivateCustomItems(project, "presence-stats", ["discord"]);
    const found = got.data.results.find((r) => r.key === "discord");
    const last = found && found.value && typeof found.value === "object" ? found.value : null;
    if (last && last.online === online) return;
    if (last && now - Number(last.at || 0) < RENAME_GAP) return;
    const s = await data.getPrivateCustomItems(project, "secrets", ["discordBotToken", "discordOnlineChannel"]);
    const pick = (k) => { const r = s.data.results.find((x) => x.key === k); return r ? String(r.value || "") : ""; };
    const token = pick("discordBotToken"), channel = pick("discordOnlineChannel");
    if (!token || !/^[0-9]{5,25}$/.test(channel)) return;
    // The attempt is kept first, so a refusal (rate limit) waits RENAME_GAP before the next try.
    await data.setPrivateCustomItem(project, "presence-stats", { key: "discord", value: { online: last ? last.online : -1, at: now } });
    await axios.patch("https://discord.com/api/v10/channels/" + channel, { name: ChannelName(online) }, {
      headers: { Authorization: "Bot " + token, "Content-Type": "application/json", "X-Audit-Log-Reason": "Players online" },
      timeout: 8000,
    });
    await data.setPrivateCustomItem(project, "presence-stats", { key: "discord", value: { online, at: now } });
  } catch (e) {
    logger.warning("Presence discord: " + (e.response ? e.response.status + " " + JSON.stringify(e.response.data).slice(0, 200) : e.message));
  }
}

// ------------------------------------------------------------------ news: updates and the developer's messages

const NEWS_GAP = 20 * 1000;                 // news is worked out again at most this often
const GITHUB_GAP = 60 * 1000;               // GitHub is asked about the latest release at most this often
const LATEST_URL = "https://github.com/Cranezz/Protobot-Rebuilt/releases/latest/download/releases.win.json";
const DAY = 24 * 3600 * 1000;
const MAX_TEXT = 600;

function Pick(results, key) {
  const r = results.find((x) => x.key === key);
  return r ? r.value : undefined;
}

// The newest release's build number: "2.0.0-beta.1.85" -> 85.
async function LatestBuild() {
  const r = await axios.get(LATEST_URL, { timeout: 6000, maxRedirects: 5, responseType: "json" });
  const assets = (r.data && r.data.Assets) || [];
  let best = 0;
  for (const a of assets) {
    const m = /\.(\d+)$/.exec(String(a.Version || ""));
    if (m) best = Math.max(best, Number(m[1]));
  }
  return best;
}

function Active(state, now) {
  return (state.messages || []).filter((m) => m.until > now);
}

function Duration(word) {
  const m = /^(\d{1,3})(m|h|d)$/i.exec(word || "");
  if (!m) return 0;
  const n = Number(m[1]), unit = m[2].toLowerCase();
  const ms = n * (unit === "m" ? 60000 : unit === "h" ? 3600000 : DAY);
  return Math.max(60000, Math.min(7 * DAY, ms));
}

function Ago(ms) {
  if (ms < 3600000) return Math.max(1, Math.round(ms / 60000)) + " min";
  if (ms < 2 * DAY) return Math.round(ms / 3600000) + " h";
  return Math.round(ms / DAY) + " days";
}

// Reads the new messages in #player-messages, carries out the commands and answers each one. `state` is changed in place.
async function Console(data, project, state, now, online, logger) {
  const s = await data.getPrivateCustomItems(project, "secrets", ["discordBotToken", "discordConsoleChannel"]);
  const token = String(Pick(s.data.results, "discordBotToken") || ""), channel = String(Pick(s.data.results, "discordConsoleChannel") || "");
  if (!token || !/^[0-9]{5,25}$/.test(channel)) return;
  const api = axios.create({ baseURL: "https://discord.com/api/v10", timeout: 8000,
    headers: { Authorization: "Bot " + token, "Content-Type": "application/json" } });
  if (!state.botId) state.botId = (await api.get("/users/@me")).data.id;
  if (state.channel !== channel) { state.channel = channel; state.lastMsg = undefined; }
  if (!state.lastMsg) {   // the first look: start after what's there now (old messages aren't commands to run again)
    const newest = (await api.get("/channels/" + channel + "/messages?limit=1")).data;
    state.lastMsg = newest.length ? newest[0].id : "0";
    return;
  }
  const got = (await api.get("/channels/" + channel + "/messages?limit=50&after=" + state.lastMsg)).data;
  got.sort((a, b) => (BigInt(a.id) < BigInt(b.id) ? -1 : 1));
  const reply = (m, content) => api.post("/channels/" + channel + "/messages", {
    content: content.slice(0, 1900), message_reference: { message_id: m.id, fail_if_not_exists: false }, allowed_mentions: { parse: [] },
  });
  for (const m of got) {
    state.lastMsg = m.id;
    const person = m.author && (!m.author.bot || m.author.id === state.botId) && !m.webhook_id;
    const text = String(m.content || "").trim();
    if (!person || !text.startsWith("!")) continue;
    let words = text.split(/\s+/), test = false;
    if (words[0].toLowerCase() === "!test" && words.length > 1) {   // "!test ask ...": only the developer's own players
      test = true;
      words = ["!" + words[1].replace(/^!/, "")].concat(words.slice(2));
    }
    const body = test ? text.replace(/^!test\s+!?\S+/i, "").trim() : text.slice(words[0].length).trim();
    const command = words[0].toLowerCase();
    try {
      if (command === "!ask" || command === "!notify") {
        let rest = body;
        const time = Duration(rest.split(/\s+/)[0]);
        if (time) rest = rest.slice(rest.split(/\s+/)[0].length).trim();
        if (!rest) { await reply(m, "Write the message after the command: `" + command + " <text>`."); continue; }
        if (rest.length > MAX_TEXT) { await reply(m, "That's " + rest.length + " characters: keep it under " + MAX_TEXT + "."); continue; }
        state.next = (state.next || 0) + 1;
        const id = state.next.toString(36);
        const kind = command === "!ask" ? "ask" : "notify";
        state.messages = Active(state, now).concat([{ id, kind, text: rest, at: now, until: now + (time || DAY), msg: m.id, test }]);
        await reply(m, test ? "🧪 Test: only your own Protobot (the players in \"devPlayers\") gets this, within a minute. `!stop " + id + "` takes it down."
          : (kind === "ask" ? "📣 Asked" : "📣 Sent to") + " the players online (" + online + " now) and anyone who opens Protobot in the next "
          + Ago(time || DAY) + ". " + (kind === "ask" ? "Answers come in as replies to your message. " : "") + "`!stop " + id + "` takes it down.");
      } else if (command === "!stop") {
        const which = (words[1] || "").toLowerCase();
        const before = Active(state, now);
        const left = which === "all" ? [] : before.filter((x) => x.id !== which);
        if (left.length === before.length) { await reply(m, which ? "Nothing up with the id `" + which + "` (`!list` shows them)." : "Which one? `!stop <id>` or `!stop all`."); continue; }
        state.messages = left;
        state.ended = (state.ended || []).concat(before.filter((x) => !left.includes(x)).map((x) => Object.assign({}, x, { until: now })));
        await reply(m, "Taken down: " + (which === "all" ? "everything (" + before.length + ")" : "`" + which + "`") + ". Players who haven't seen it won't.");
      } else if (command === "!list") {
        const up = Active(state, now);
        await reply(m, up.length ? up.map((x) => "`" + x.id + "` " + (x.test ? "🧪 " : "") + (x.kind === "ask" ? "❓ " : "📣 ") + x.text.slice(0, 120)
          + " (ends in " + Ago(x.until - now) + ")").join("\n") : "Nothing is up right now.");
      } else if (command === "!help") {
        await reply(m, "`!ask <question>`: a card with a text box (answers come back here). `!notify <text>`: just a message. "
          + "Add a time first to change how long it stays up (`!ask 2h ...`, default 24h). `!test ask ...` / `!test notify ...`: only your own "
          + "Protobot sees it (try one first). `!list`, `!stop <id>`, `!stop all`.");
      }
    } catch (e) {
      logger.warning("Presence console reply: " + (e.response ? e.response.status : e.message));
    }
  }
}

// What every heartbeat hands back: the newest build and the messages up now. Worked out again when the kept one is
// older than NEWS_GAP by the heartbeat that takes the write lock first.
async function News(data, project, now, online, logger, player) {
  let state = {}, lock;
  try {
    const got = await data.getPrivateCustomItems(project, "news", ["state"]);
    const found = got.data.results.find((r) => r.key === "state");
    if (found) { state = found.value && typeof found.value === "object" ? found.value : {}; lock = found.writeLock; }
  } catch (e) { logger.warning("Presence news: " + e.message); return { latest: 0, messages: [] }; }
  if (now - Number(state.checked || 0) > NEWS_GAP) {
    state.checked = now;
    let mine = false;
    try {
      const set = await data.setPrivateCustomItem(project, "news", lock ? { key: "state", value: state, writeLock: lock } : { key: "state", value: state });
      lock = set.data.writeLock;
      mine = true;
    } catch (e) { mine = false; }   // another heartbeat is doing it
    if (mine) {
      if (now - Number(state.githubAt || 0) > GITHUB_GAP) {
        state.githubAt = now;
        try { const b = await LatestBuild(); if (b > 0) state.latest = b; }
        catch (e) { logger.warning("Presence latest: " + e.message); }
      }
      try { await Console(data, project, state, now, online, logger); }
      catch (e) { logger.warning("Presence console: " + (e.response ? e.response.status + " " + JSON.stringify(e.response.data).slice(0, 200) : e.message)); }
      // Ended ones are kept an hour, so an answer typed into a card left open still counts (Feedback.js).
      state.ended = (state.ended || []).concat((state.messages || []).filter((m) => m.until <= now)).filter((m) => now - m.until < 3600 * 1000);
      state.messages = Active(state, now);
      try { await data.setPrivateCustomItem(project, "news", { key: "state", value: state, writeLock: lock }); }
      catch (e) { logger.warning("Presence news save: " + e.message); }
    }
  }
  let up = Active(state, now);
  if (up.some((m) => m.test)) {   // test messages go only to the developer's own players (secrets "devPlayers": ids, commas)
    let devs = [];
    try {
      const s = await data.getPrivateCustomItems(project, "secrets", ["devPlayers"]);
      devs = String(Pick(s.data.results, "devPlayers") || "").split(",").map((x) => x.trim()).filter((x) => x);
    } catch (e) { devs = []; }
    up = up.filter((m) => !m.test || (player && devs.includes(player)));
  }
  return { latest: Number(state.latest || 0), messages: up.map((m) => ({ id: m.id, kind: m.kind, text: m.text })) };
}

async function Count(data, project, now) {
  let after, count = 0, pages = 0;
  do {
    const page = await data.getPrivateCustomItems(project, "presence", undefined, after);
    for (const r of page.data.results) if (now - Number(r.value) < ONLINE_WINDOW) count++;
    const next = page.data.links && page.data.links.next;
    after = next ? new URL(next, "https://x").searchParams.get("after") : undefined;
    pages++;
  } while (after && pages < 50);
  return count;
}

module.exports = async ({ params, context, logger }) => {
  const data = new DataApi({ accessToken: context.serviceToken });
  const project = context.projectId, player = context.playerId, now = Date.now();
  const today = new Date(now).toISOString().slice(0, 10);

  // Run by the hourly schedule (no player): count again and update the Discord channel.
  if (!player) {
    let online = 0;
    try {
      online = await Count(data, project, now);
      await data.setPrivateCustomItem(project, "presence-stats", { key: "count", value: { at: now, online } });
      await data.setPrivateCustomItem(project, "presence-stats", { key: "scheduled", value: { at: now, online } });   // the last hourly run
    } catch (e) { logger.warning("Presence count: " + e.message); return { online: 0 }; }
    await UpdateDiscord(data, project, online, now, logger);
    await News(data, project, now, online, logger);   // commands sent while nobody was online get their answer
    return { online };
  }
  const leaving = params.leaving === "1" || params.leaving === true;

  // What this heartbeat adds (at most 7 minutes: a minute's beat, or a few missed ones).
  let added = {}, sum = 0;
  let areas = params.areas;
  if (typeof areas === "string") { try { areas = JSON.parse(areas); } catch (e) { areas = {}; } }
  for (const area of AREAS) {
    const s = Math.max(0, Math.min(420, Number((areas || {})[area]) || 0));
    if (s > 0) { added[area] = s; sum += s; }
  }
  if (sum > 420) { for (const a in added) added[a] = added[a] * 420 / sum; sum = 420; }

  try { await data.setPrivateCustomItem(project, "presence", { key: player, value: leaving ? 0 : now }); }
  catch (e) { logger.warning("Presence: " + e.message); }

  if (sum > 0) {
    try {
      const got = await data.getPrivateCustomItems(project, "usage", [player]);
      const found = got.data.results.find((r) => r.key === player);
      const u = found && found.value && typeof found.value === "object" ? found.value : { total: 0, areas: {}, days: {}, first: now };
      u.total = (u.total || 0) + sum;
      u.areas = u.areas || {};
      for (const a in added) u.areas[a] = (u.areas[a] || 0) + added[a];
      u.days = u.days || {};
      u.days[today] = (u.days[today] || 0) + sum;
      const keep = Object.keys(u.days).sort().slice(-60);
      for (const d of Object.keys(u.days)) if (!keep.includes(d)) delete u.days[d];
      u.name = String(params.name || u.name || "").slice(0, 40);
      u.account = params.account === "1" || params.account === true;
      u.last = now;
      u.first = u.first || now;
      await data.setPrivateCustomItem(project, "usage", { key: player, value: u });
    } catch (e) { logger.warning("Presence usage: " + e.message); }
  }

  // The number online, worked out again when the kept one is old.
  let online = 0, counted = false;
  try {
    const kept = await data.getPrivateCustomItems(project, "presence-stats", ["count"]);
    const c = kept.data.results.find((r) => r.key === "count");
    if (!leaving && c && c.value && now - c.value.at < RECOUNT) online = Math.max(1, c.value.online);
    else {
      const count = await Count(data, project, now);
      online = leaving ? count : Math.max(1, count);
      counted = true;
      await data.setPrivateCustomItem(project, "presence-stats", { key: "count", value: { at: now, online } });
    }
  } catch (e) { logger.warning("Presence count: " + e.message); online = leaving ? 0 : 1; }
  if (counted) await UpdateDiscord(data, project, online, now, logger);
  if (leaving) return { online };
  const news = await News(data, project, now, online, logger, player);
  return { online, latest: news.latest, messages: news.messages };
};

module.exports.params = {
  areas: { type: "ANY", required: false },
  name: { type: "STRING", required: false },
  account: { type: "STRING", required: false },
  leaving: { type: "STRING", required: false },
};
