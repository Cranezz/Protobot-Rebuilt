// V2: a player's "Delete my online data" (GDPR / CCPA erasure). Deletes everything Cloud Save keeps for the player:
// stats and report counts (protected), replays (public), any other items, and their files (reports not sent). Their
// leaderboard scores can't be deleted from a script (the Leaderboards API here has no delete), so the player goes on
// a public "hidden" list the app leaves out of the leaderboard at once, and on the moderators' private list of
// deletions to finish: Tools > Protobot > Skills Leaderboard Moderation > Finish deletions removes the scores (admin
// API) and takes them off both lists. The app then deletes the player itself (Authentication).
const { DataApi, FilesApi } = require("@unity-services/cloud-save-1.4");

module.exports = async ({ context, logger }) => {
  const projectId = context.projectId;
  const playerId = context.playerId;
  const data = new DataApi({ accessToken: context.serviceToken });
  const files = new FilesApi({ accessToken: context.serviceToken });
  const failed = [];
  const attempt = async (what, fn) => {
    try { await fn(); } catch (e) {
      if (!(e.response && e.response.status === 404)) failed.push(what + ": " + (e.response ? e.response.status : e.message));
    }
  };

  await attempt("protected", () => data.deleteProtectedItems(projectId, playerId));
  await attempt("public", () => data.deletePublicItems(projectId, playerId));
  await attempt("default", () => data.deleteItems(projectId, playerId));
  await attempt("files", async () => {
    let after;
    for (let page = 0; page < 20; page++) {
      const list = await files.listPlayerFiles(projectId, playerId, after);
      const results = (list.data && list.data.results) || [];
      for (const f of results) await files.deleteFile(projectId, playerId, f.key);
      after = list.data && list.data.links && list.data.links.next ? results.length && results[results.length - 1].key : undefined;
      if (!after) break;
    }
  });

  // The leaderboard: hidden now, deleted by the moderators' tool.
  const add = async (read, write, customId, key) => {
    let list = [];
    try {
      const got = await read(projectId, customId, [key]);
      const found = got.data.results.find((r) => r.key === key);
      if (found && Array.isArray(found.value)) list = found.value;
    } catch (e) { list = []; }
    if (!list.includes(playerId)) list.push(playerId);
    await write(projectId, customId, { key, value: list });
  };
  await attempt("hidden", () => add(data.getCustomItems.bind(data), data.setCustomItem.bind(data), "leaderboard", "hidden"));
  await attempt("queue", () => add(data.getPrivateCustomItems.bind(data), data.setPrivateCustomItem.bind(data), "moderation", "deletions"));

  if (failed.length) logger.warning("DeleteMyData: " + failed.join("; "), { player: playerId });
  return { deleted: failed.length === 0, failed };
};
