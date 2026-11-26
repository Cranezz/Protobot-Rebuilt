// V2 (build 79): the Discord server's live skills leaderboard. One message in #skills-leaderboard (posted by
// Tools/Discord/setup_server.py leaderboard) shows the top 10 of the Driving and Programming Skills leaderboards. It runs
// with no player: a UGS Trigger on every leaderboard score posted ("discord-leaderboard",
// com.unity.services.leaderboards.score-submitted.v1) and one on the hourly schedule ("discord-leaderboard-hourly",
// after a score is removed by moderation). Players who deleted their online data (public custom "leaderboard"/"hidden")
// are left out, and names go through the same filter as in the app (NameFilter.cs: its lists are copied in below by
// Tools/AgentTesting/lb_filter.py). The bot token and the channel and message ids are private custom data "secrets"
// (discordBotToken, discordLeaderboardChannel, discordLeaderboardMessage), set by Tools/AgentTesting/cs/t_discord_lb.cs.
// Never logs the token.
const axios = require("axios-1.6");
const { DataApi } = require("@unity-services/cloud-save-1.4");
const { LeaderboardsApi } = require("@unity-services/leaderboards-1.1");

const BOARDS = [["skills-override", "🚗 Driving Skills"], ["programming-skills-override", "💻 Programming Skills"]];
const TOP = 10;

// ------------------------------------------------------------------ the name filter (NameFilter.cs, ROT13 lists)
const ANYWHERE_ROT = [/*ANYWHERE*/"shpx", "fuvg", "ovgpu", "phag", "avttre", "avttn", "snttbg", "zbgureshpx", "juber", "fyhg", "ergneq", "qvpxurnq", "nffubyr", "chffl", "onfgneq", "jnaxre", "gjng", "pbpxfhpx", "qvyqb", "anmv", "uvgyre", "zbyrfg", "crqbcuvy", "xxx", "puvax", "ornare", "jrgonpx", "genaal", "xlxr", "cbea", "uragnv", "pyvg", "frkl", "ahqr", "anxrq", "betnfz", "znfgheong", "rwnphyng", "cravf", "intvan", "intvan", "gvggl", "fcrez", "pbaqbz", "ohgguby", "erpghz", "fuvgr", "ovngpu", "shpxre", "sntt"];
const WORD_ROT = [/*WORDS*/"nff", "nffrf", "qvpx", "pbpx", "snt", "gvg", "gvgf", "pbba", "ubr", "ubrf", "wvmm", "phz", "frk", "encr", "encrq", "encvfg", "fcvp", "xvxr", "jnc", "cvff", "anqf", "obbo", "obbof", "nany"];
const FINE = [/*FINE*/"shiitake", "scunthorpe", "penistone", "cockburn", "dickinson", "hancock", "peacock", "shuttlecock"];
const rot13 = (s) => s.replace(/[a-z]/g, (c) => String.fromCharCode(97 + (c.charCodeAt(0) - 97 + 13) % 26));
const ANYWHERE = ANYWHERE_ROT.map(rot13);
const WORDS = new Set(WORD_ROT.map(rot13));
const LOOK = { "0": "o", "1": "i", "!": "i", "|": "i", "í": "i", "ì": "i", "3": "e", "é": "e", "è": "e", "ë": "e", "4": "a", "@": "a",
  "á": "a", "à": "a", "ä": "a", "5": "s", "$": "s", "7": "t", "+": "t", "8": "b", "9": "g", "ö": "o", "ó": "o", "ü": "u", "ú": "u" };

function letter(c) {
  c = c.toLowerCase();
  if (LOOK[c]) return LOOK[c];
  return c >= "a" && c <= "z" ? c : "";
}

const letters = (s) => [...s].map(letter).join("");

function squash(s) {
  let out = "";
  for (const raw of s) {
    const c = letter(raw);
    if (!c) continue;
    if (out.length && out[out.length - 1] === c) continue;
    out += c;
  }
  return out;
}

const doubled = (s) => { for (let i = 1; i < s.length; i++) if (s[i] === s[i - 1]) return true; return false; };

function isWord(part) {
  const l = letters(part);
  if (WORDS.has(l)) return true;
  for (const w of WORDS) if (!doubled(w) && squash(l) === w) return true;
  return false;
}

function allowed(name) {
  if (!name || !name.trim()) return true;
  for (const f of FINE) name = name.replace(new RegExp(f, "gi"), " ");
  const whole = squash(name);
  const raw = letters(name);
  for (const a of ANYWHERE) if (doubled(a) ? raw.includes(a) : whole.includes(a)) return false;
  const parts = [];
  let current = "";
  let previous = " ";
  for (const c of name) {
    const l = letter(c);
    const upper = c !== c.toLowerCase();
    const prevLower = previous !== previous.toUpperCase();
    if ((!l || (upper && prevLower)) && current.length) { parts.push(current); current = ""; }
    if (l) current += c;
    previous = c;
  }
  if (current.length) parts.push(current);
  for (const p of parts) if (isWord(p)) return false;
  if (parts.length > 1 && parts.every((p) => p.length === 1) && isWord(name)) return false;
  return true;
}

const shown = (name) => (allowed(name) ? name : "Player");

// ------------------------------------------------------------------ the post

const escape = (s) => s.replace(/([\\*_~`|>#[\]()-])/g, "\\$1");
const MEDALS = ["🥇", "🥈", "🥉"];

module.exports = async ({ params, context, logger }) => {
  if (context.playerId) return { updated: false };   // only the triggers run it
  const project = context.projectId;
  const data = new DataApi({ accessToken: context.serviceToken });
  const boards = new LeaderboardsApi({ accessToken: context.serviceToken });
  let token = "";
  let channel = "";
  let message = "";
  try {
    const s = await data.getPrivateCustomItems(project, "secrets", ["discordBotToken", "discordLeaderboardChannel", "discordLeaderboardMessage"]);
    const pick = (k) => { const r = s.data.results.find((x) => x.key === k); return r ? String(r.value || "") : ""; };
    token = pick("discordBotToken");
    channel = pick("discordLeaderboardChannel");
    message = pick("discordLeaderboardMessage");
  } catch (e) {
    logger.warning("Leaderboard post: secrets: " + e.message);
    return { updated: false };
  }
  if (!token || !/^[0-9]{5,25}$/.test(channel) || !/^[0-9]{5,25}$/.test(message)) return { updated: false };

  let hidden = new Set();
  try {
    const h = await data.getCustomItems(project, "leaderboard", ["hidden"]);
    const r = h.data.results.find((x) => x.key === "hidden");
    if (r && Array.isArray(r.value)) hidden = new Set(r.value);
  } catch (e) { /* none hidden */ }

  const fields = [];
  for (const [id, title] of BOARDS) {
    let lines = [];
    try {
      const got = await boards.getLeaderboardScores(project, id, 0, 40);
      const rows = (got.data.results || []).filter((r) => !hidden.has(r.playerId)).slice(0, TOP);
      lines = rows.map((r, i) => {
        const name = escape(shown(String(r.playerName || "Player").split("#")[0].replace(/_/g, " ")).slice(0, 24));
        const score = Math.round(Number(r.score) || 0);
        return (i < 3 ? MEDALS[i] + " **" + name + "**" : "`" + String(i + 1).padStart(2, " ") + ".` " + name) + " · " + score;
      });
    } catch (e) {
      logger.warning("Leaderboard post: " + id + ": " + e.message);
      lines = ["(couldn't be read just now)"];
    }
    fields.push({ name: title, value: lines.length ? lines.join("\n") : "No runs yet: be the first!", inline: true });
  }
  const now = Math.floor(Date.now() / 1000);
  const embed = {
    title: "🏆 Protobot Skills Leaderboard · V5RC Override",
    color: 0xF5A524,
    description: "The best Robot Skills runs in Protobot: Play game > Robot Skills (driving) or Programming Skills, signed in to an account. Updated <t:" + now + ":R>.",
    fields,
    footer: { text: "Every run and its replay: Protobot > Multiplayer > Skills leaderboard" },
  };
  try {
    await axios.patch("https://discord.com/api/v10/channels/" + channel + "/messages/" + message,
      { content: "", embeds: [embed], allowed_mentions: { parse: [] } },
      { headers: { Authorization: "Bot " + token, "Content-Type": "application/json" }, timeout: 8000 });
  } catch (e) {
    logger.warning("Leaderboard post: " + (e.response ? e.response.status + " " + JSON.stringify(e.response.data).slice(0, 200) : e.message));
    return { updated: false };
  }
  return { updated: true };
};
