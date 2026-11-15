// V2: the player's own stats (matches, wins, losses, ties, points, skills runs and best, programming skills runs and best).
const { DataApi } = require("@unity-services/cloud-save-1.4");

module.exports = async ({ context }) => {
  const blank = { matches: 0, wins: 0, losses: 0, ties: 0, points: 0, skillsRuns: 0, skillsBest: 0, programmingRuns: 0, programmingBest: 0, lastMatch: 0 };
  try {
    const data = new DataApi({ accessToken: context.serviceToken });
    const response = await data.getProtectedItems(context.projectId, context.playerId, ["stats"]);
    const found = response.data.results.find((r) => r.key === "stats");
    return Object.assign(blank, found ? found.value : {});
  } catch (e) {
    return blank;
  }
};
