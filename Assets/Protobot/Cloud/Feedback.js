// V2 (build 85): a player's answer to the developer's question (`!ask` in the Discord channel #player-messages, carried to
// the apps online by Presence.js). It's posted in that channel by the bot as a reply to the command, so each question's
// answers sit under it. The bot token and the channel's id are Cloud Save private custom data "secrets"
// ("discordBotToken", "discordConsoleChannel"); the app never has them. A player can answer each question at most 3
// times and send at most 10 answers an hour (protected "feedbackTimes": only Cloud Code writes it); nobody can be pinged.
// Params: id = the question's id, answer = what they wrote, name = the name they play under, build, area (what part of
// Protobot they were in). Returns { sent, reason }.
const axios = require("axios-1.6");
const { DataApi } = require("@unity-services/cloud-save-1.4");

const PER_QUESTION = 3;
const PER_HOUR = 10;
const MAX_ANSWER = 1000;
const LATE = 3600 * 1000;   // answers still count this long after a question is taken down (a card left open)

module.exports = async ({ params, context, logger }) => {
  const project = context.projectId, player = context.playerId, now = Date.now();
  const data = new DataApi({ accessToken: context.serviceToken });
  const id = String(params.id || "").replace(/[^a-z0-9]/g, "").slice(0, 12);
  const answer = String(params.answer || "").replace(/\r/g, "").trim().slice(0, MAX_ANSWER);
  if (!player) return { sent: false, reason: "not signed in" };
  if (!id || !answer) return { sent: false, reason: "write an answer first" };

  // The question: it must be one the developer asked (still up, or taken down within the hour).
  let question = null;
  try {
    const got = await data.getPrivateCustomItems(project, "news", ["state"]);
    const found = got.data.results.find((r) => r.key === "state");
    const state = found && found.value && typeof found.value === "object" ? found.value : {};
    question = (state.messages || []).concat(state.ended || []).find((m) => m.id === id && m.kind === "ask" && m.until + LATE > now) || null;
  } catch (e) { logger.warning("Feedback news: " + e.message); }
  if (!question) return { sent: false, reason: "that question has ended" };

  // How many this player sent lately.
  let times = [];
  try {
    const got = await data.getProtectedItems(project, player, ["feedbackTimes"]);
    const found = got.data.results.find((r) => r.key === "feedbackTimes");
    if (found && Array.isArray(found.value)) times = found.value.filter((t) => t && now - t.at < 8 * 86400000);
  } catch (e) { times = []; }
  if (times.filter((t) => t.id === id).length >= PER_QUESTION) return { sent: false, reason: "you've answered this one already" };
  if (times.filter((t) => now - t.at < 3600000).length >= PER_HOUR) return { sent: false, reason: "you've sent a lot of answers lately: try again later" };

  let token = "", channel = "";
  try {
    const s = await data.getPrivateCustomItems(project, "secrets", ["discordBotToken", "discordConsoleChannel"]);
    const pick = (k) => { const r = s.data.results.find((x) => x.key === k); return r ? String(r.value || "") : ""; };
    token = pick("discordBotToken");
    channel = pick("discordConsoleChannel");
  } catch (e) { token = ""; }
  if (!token || !/^[0-9]{5,25}$/.test(channel)) {
    logger.error("Feedback: no bot token or channel in secrets (Tools/AgentTesting/cs/t_discord_console.cs)");
    return { sent: false, reason: "answers can't be sent right now" };
  }

  const name = String(params.name || "Player").replace(/[\r\n`*_~|<>@#]/g, "").trim().slice(0, 40) || "Player";
  const area = String(params.area || "").replace(/[^A-Za-z ]/g, "").slice(0, 20);
  const build = String(params.build || "?").replace(/[^0-9.]/g, "").slice(0, 10);
  const short = question.text.length > 80 ? question.text.slice(0, 80) + "…" : question.text;
  try {
    await axios.post("https://discord.com/api/v10/channels/" + channel + "/messages", {
      embeds: [{
        author: { name: "💬 " + name },
        description: answer,
        color: 0x2885F4,
        footer: { text: "Answer to " + id + ": " + short + "\nbuild " + build + (area ? " · " + area : "") + " · player " + player.slice(0, 8) },
        timestamp: new Date(now).toISOString(),
      }],
      message_reference: question.msg ? { message_id: question.msg, fail_if_not_exists: false } : undefined,
      allowed_mentions: { parse: [] },
    }, { headers: { Authorization: "Bot " + token, "Content-Type": "application/json" }, timeout: 8000 });
  } catch (e) {
    logger.error("Feedback post: " + (e.response ? e.response.status + " " + JSON.stringify(e.response.data).slice(0, 200) : e.message));
    return { sent: false, reason: "answers can't be sent right now" };
  }

  times.push({ id, at: now });
  try { await data.setProtectedItem(project, player, { key: "feedbackTimes", value: times.slice(-60) }); }
  catch (e) { logger.warning("Feedback times: " + e.message); }
  return { sent: true };
};

module.exports.params = {
  id: { type: "STRING", required: true },
  answer: { type: "STRING", required: true },
  name: { type: "STRING", required: false },
  build: { type: "STRING", required: false },
  area: { type: "STRING", required: false },
};
