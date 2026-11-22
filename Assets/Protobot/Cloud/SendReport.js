// V2: a bug report, player report or appeal from the app, posted to the developer's Discord channel. The webhook lives
// here on the server (Cloud Save private custom data "secrets", key "discordWebhook": set it with Tools > Protobot >
// Set Report Webhook), never in the app, where anyone could pull it out and post anything with it. The report's zip was
// saved by the player as a Cloud Save file of their own; it's attached and then deleted. Each player can send at most a
// few reports in ten minutes and a day, and nobody can be pinged (@everyone, roles, users).
const axios = require("axios-1.6");
const { DataApi, FilesApi } = require("@unity-services/cloud-save-1.4");

const PER_TEN_MINUTES = 3;
const PER_DAY = 12;
const MAX_FILE = 9500000;   // Discord takes 10 MB

module.exports = async ({ params, context, logger }) => {
  const projectId = context.projectId;
  const playerId = context.playerId;
  const data = new DataApi({ accessToken: context.serviceToken });
  const files = new FilesApi({ accessToken: context.serviceToken });
  const key = String(params.file || "").replace(/[^A-Za-z0-9_-]/g, "").slice(0, 60);
  const dropFile = async () => {
    if (!key) return;
    try { await files.deleteFile(projectId, playerId, key); } catch (e) { /* already gone */ }
  };

  // How many this player sent lately (protected data: only Cloud Code writes it).
  const now = Date.now();
  let sent = [];
  try {
    const got = await data.getProtectedItems(projectId, playerId, ["reportTimes"]);
    const found = got.data.results.find((r) => r.key === "reportTimes");
    if (found && Array.isArray(found.value)) sent = found.value.filter((t) => now - t < 86400000);
  } catch (e) { sent = []; }
  if (sent.filter((t) => now - t < 600000).length >= PER_TEN_MINUTES || sent.length >= PER_DAY) {
    await dropFile();
    return { sent: false, reason: "you've sent a lot of reports lately: try again later" };
  }

  // Each kind has its own staff channel ("webhook_bug", "webhook_report", "webhook_appeal"); "discordWebhook" otherwise.
  const kindKey = "webhook_" + String(params.kind || "").replace(/[^a-z]/g, "");
  let webhook = "";
  try {
    const got = await data.getPrivateCustomItems(projectId, "secrets", [kindKey, "discordWebhook"]);
    const own = got.data.results.find((r) => r.key === kindKey);
    const any = got.data.results.find((r) => r.key === "discordWebhook");
    webhook = own ? String(own.value || "") : any ? String(any.value || "") : "";
  } catch (e) { webhook = ""; }
  if (!/^https:\/\/(discord\.com|discordapp\.com)\/api\/webhooks\//.test(webhook)) {
    await dropFile();
    logger.error("SendReport: no webhook set (Tools > Protobot > Set Report Webhook)");
    return { sent: false, reason: "reports can't be sent right now" };
  }

  const kind = String(params.kind || "report").replace(/[^a-z]/g, "").slice(0, 20);
  let content = String(params.content || "").slice(0, 1700);
  content += "\n-# " + kind + " · player `" + playerId + "` · build " + String(params.build || "?").slice(0, 10);

  let file = null;
  let fileName = String(params.fileName || "report.zip").replace(/[^A-Za-z0-9 ._()-]/g, "_").slice(0, 80);
  if (!fileName.toLowerCase().endsWith(".zip")) fileName += ".zip";
  if (key) {
    try {
      const meta = await files.getFileMetadata(projectId, playerId, key);
      if (meta.data && meta.data.size > MAX_FILE) {
        await dropFile();
        return { sent: false, reason: "the report is too big" };
      }
      const url = await files.getDownloadUrl(projectId, playerId, key);
      const download = await axios({
        url: url.data.signedUrl, method: url.data.httpMethod || "GET", headers: url.data.requiredHeaders || {},
        responseType: "arraybuffer", maxContentLength: MAX_FILE + 1000,
      });
      file = Buffer.from(download.data);
    } catch (e) {
      logger.warning("SendReport: couldn't read the report file: " + e.message);
    }
  }

  // multipart/form-data by hand: the message (no pings) and the zip.
  const boundary = "----protobot" + now.toString(16) + Math.floor(Math.random() * 1e9).toString(16);
  const payload = JSON.stringify({ content, allowed_mentions: { parse: [] } });
  const parts = [Buffer.from("--" + boundary + "\r\nContent-Disposition: form-data; name=\"payload_json\"\r\nContent-Type: application/json\r\n\r\n" + payload + "\r\n")];
  if (file) {
    parts.push(Buffer.from("--" + boundary + "\r\nContent-Disposition: form-data; name=\"files[0]\"; filename=\"" + fileName + "\"\r\nContent-Type: application/zip\r\n\r\n"));
    parts.push(file);
    parts.push(Buffer.from("\r\n"));
  }
  parts.push(Buffer.from("--" + boundary + "--\r\n"));
  try {
    await axios.post(webhook, Buffer.concat(parts), {
      headers: { "Content-Type": "multipart/form-data; boundary=" + boundary },
      maxBodyLength: 12000000, maxContentLength: 12000000,
    });
  } catch (e) {
    logger.warning("SendReport: Discord refused: " + (e.response ? e.response.status : e.message));
    await dropFile();
    return { sent: false, reason: "the moderators' channel didn't take it" };
  }
  await dropFile();
  sent.push(now);
  try { await data.setProtectedItem(projectId, playerId, { key: "reportTimes", value: sent }); } catch (e) { /* not counted */ }
  return { sent: true, withFile: !!file };
};

module.exports.params = {
  kind: { type: "String", required: false },
  content: { type: "String", required: true },
  file: { type: "String", required: false },
  fileName: { type: "String", required: false },
  build: { type: "NUMERIC", required: false },
};
