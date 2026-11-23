// V2: the app's heartbeat (once a minute while Protobot runs, for players who agreed to the terms). It keeps:
// - presence: when each player was last seen (private custom data "presence", key = player id), from which the number
//   of players online now (seen in the last 150 s) is counted at most every 45 s and kept in "presence-stats"/"count";
// - usage: how long each player has used Protobot and on what (private custom data "usage", key = player id: name,
//   account, total seconds, seconds per area, seconds per day for the last 60 days, first and last seen), which only the
//   developer reads (Tools > Protobot > Players). Nothing here is readable by players.
// Params: areas = { area: seconds since the last heartbeat }, name = the name they play under, account = "1" when signed
// in to an account. Returns { online }.
const { DataApi } = require("@unity-services/cloud-save-1.4");

const AREAS = ["Home menu", "Building", "Simulating", "Game", "Multiplayer", "Code", "Sketching", "Replay"];
const ONLINE_WINDOW = 150 * 1000;   // seen this recently: online
const RECOUNT = 45 * 1000;          // the count is worked out again at most this often

module.exports = async ({ params, context, logger }) => {
  const data = new DataApi({ accessToken: context.serviceToken });
  const project = context.projectId, player = context.playerId, now = Date.now();
  const today = new Date(now).toISOString().slice(0, 10);

  // What this heartbeat adds (at most 7 minutes: a minute's beat, or a few missed ones).
  let added = {}, sum = 0;
  let areas = params.areas;
  if (typeof areas === "string") { try { areas = JSON.parse(areas); } catch (e) { areas = {}; } }
  for (const area of AREAS) {
    const s = Math.max(0, Math.min(420, Number((areas || {})[area]) || 0));
    if (s > 0) { added[area] = s; sum += s; }
  }
  if (sum > 420) { for (const a in added) added[a] = added[a] * 420 / sum; sum = 420; }

  try { await data.setPrivateCustomItem(project, "presence", { key: player, value: now }); }
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
  let online = 0;
  try {
    const kept = await data.getPrivateCustomItems(project, "presence-stats", ["count"]);
    const c = kept.data.results.find((r) => r.key === "count");
    if (c && c.value && now - c.value.at < RECOUNT) return { online: Math.max(1, c.value.online) };
    let after, count = 0, pages = 0;
    do {
      const page = await data.getPrivateCustomItems(project, "presence", undefined, after);
      for (const r of page.data.results) if (now - Number(r.value) < ONLINE_WINDOW) count++;
      const next = page.data.links && page.data.links.next;
      after = next ? new URL(next, "https://x").searchParams.get("after") : undefined;
      pages++;
    } while (after && pages < 50);
    online = Math.max(1, count);
    await data.setPrivateCustomItem(project, "presence-stats", { key: "count", value: { at: now, online } });
  } catch (e) { logger.warning("Presence count: " + e.message); online = 1; }
  return { online };
};

module.exports.params = {
  areas: { type: "ANY", required: false },
  name: { type: "STRING", required: false },
  account: { type: "STRING", required: false },
};
