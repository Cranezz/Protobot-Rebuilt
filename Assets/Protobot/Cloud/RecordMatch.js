// V2: an online match's result for the player who played it (each player records their own): matches, wins, losses,
// ties and points in their stats (protected data: only Cloud Code writes it). At most one match a minute counts.
const { DataApi } = require("@unity-services/cloud-save-1.4");

module.exports = async ({ params, context, logger }) => {
  const mine = Math.round(Number(params.mine));
  const theirs = Math.round(Number(params.theirs));
  if (!(mine >= 0 && mine <= 1000) || !(theirs >= 0 && theirs <= 1000)) return { recorded: false, reason: "scores out of range" };

  const projectId = context.projectId;
  const playerId = context.playerId;
  const data = new DataApi(context);
  const stats = await readStats(data, projectId, playerId);
  const now = Date.now();
  if (now - stats.lastMatch < 60000) return { recorded: false, reason: "a match was recorded less than a minute ago", stats };

  stats.matches += 1;
  if (mine > theirs) stats.wins += 1;
  else if (mine < theirs) stats.losses += 1;
  else stats.ties += 1;
  stats.points += mine;
  stats.lastMatch = now;
  await data.setProtectedItem(projectId, playerId, { key: "stats", value: stats });
  logger.info("Match recorded", { "player": playerId, "mine": mine, "theirs": theirs });
  return { recorded: true, stats };
};

async function readStats(data, projectId, playerId) {
  const blank = { matches: 0, wins: 0, losses: 0, ties: 0, points: 0, skillsRuns: 0, skillsBest: 0, lastMatch: 0 };
  try {
    const response = await data.getProtectedItems(projectId, playerId, ["stats"]);
    const found = response.data.results.find((r) => r.key === "stats");
    return Object.assign(blank, found ? found.value : {});
  } catch (e) {
    return blank;
  }
}

module.exports.params = {
  mine: { type: "NUMERIC", required: true },
  theirs: { type: "NUMERIC", required: true },
};
