// V2: a Robot Skills run sent by the app. It only counts after these checks (players can't post scores themselves):
// the robot fit the 18" size limit at the start, the run took a skills run's time, every scored item is worth what
// Override's rules give it and they add up to the score, and there aren't more of them than the field holds.
// Then the score goes on the "skills-override" leaderboard (best kept) and into the player's stats (protected data:
// only Cloud Code writes it).
const { DataApi } = require("@unity-services/cloud-save-1.4");
const { LeaderboardsApi } = require("@unity-services/leaderboards-1.1");

const LEADERBOARD = "skills-override";
const VALUES = { "Red pin halves": 5, "Blue pin halves": 5, "Yellow pin halves": 10, "Robots in the Midfield": 8, "Robot in the Midfield": 8 };
const MOST = { "Red pin halves": 60, "Blue pin halves": 60, "Yellow pin halves": 30, "Robots in the Midfield": 1, "Robot in the Midfield": 1 };

module.exports = async ({ params, context, logger }) => {
  const run = params.run || {};
  const reject = (reason) => {
    logger.warning("Skills run refused: " + reason, { "player": context.playerId });
    return { accepted: false, reason };
  };

  const size = Array.isArray(run.footprint) ? run.footprint.map(Number) : [];
  if (size.length !== 3 || size.some((v) => !(v > 0))) return reject("no robot size was sent");
  if (size.some((v) => v > 18.05)) return reject("the robot was bigger than 18\" at the start");

  const seconds = Number(run.seconds);
  if (!(seconds >= 55 && seconds <= 75)) return reject("a skills run lasts 60 seconds");

  const counts = {};
  let total = 0;
  for (const item of Array.isArray(run.items) ? run.items : []) {
    const value = VALUES[item.kind];
    if (value === undefined || Number(item.points) !== value) return reject("an item's points don't match the rules");
    counts[item.kind] = (counts[item.kind] || 0) + 1;
    if (counts[item.kind] > MOST[item.kind]) return reject("more scored items than the field holds");
    total += value;
  }
  const score = Math.round(Number(run.score));
  if (score !== total) return reject("the items don't add up to the score");

  const projectId = context.projectId;
  const playerId = context.playerId;
  const leaderboards = new LeaderboardsApi(context);
  const posted = await leaderboards.addLeaderboardPlayerScore(projectId, LEADERBOARD, playerId, {
    score,
    metadata: { build: String(run.build || ""), robot: String(run.robot || "").slice(0, 40) },
  });

  const data = new DataApi(context);
  const stats = await readStats(data, projectId, playerId);
  stats.skillsRuns += 1;
  stats.skillsBest = Math.max(stats.skillsBest, score);
  await data.setProtectedItem(projectId, playerId, { key: "stats", value: stats });

  const entry = posted && posted.data ? posted.data : {};
  return { accepted: true, best: stats.skillsBest, rank: entry.rank, stats };
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

module.exports.params = { run: { type: "JSON", required: true } };
