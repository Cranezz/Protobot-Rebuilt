// V2: badges on a player (a guest or an account: an account made from a guest keeps them), shown beside their name in
// the server list and lobbies. Kept in the player's protected data ("badges", a list of ids): only Cloud Code writes it,
// so a player can't give themselves one. With award: adds that badge if it can be earned now. Always returns
// { badges: [...] }.
//
// Badges:
//   "beta"  Beta tester: played Protobot for 10 minutes during its beta (the app counts the time). Earnable while
//           BETA_OPEN is true: set it to false and redeploy when the beta ends; badges already given stay.
const { DataApi } = require("@unity-services/cloud-save-1.4");

const BETA_OPEN = true;
const EARNABLE = { beta: () => BETA_OPEN };

module.exports = async ({ params, context, logger }) => {
  const data = new DataApi({ accessToken: context.serviceToken });
  let badges = [];
  try {
    const got = await data.getProtectedItems(context.projectId, context.playerId, ["badges"]);
    const found = got.data.results.find((r) => r.key === "badges");
    if (found && Array.isArray(found.value)) badges = found.value.filter((b) => typeof b === "string");
  } catch (e) { badges = []; }
  const award = typeof params.award === "string" ? params.award.trim().toLowerCase() : "";
  if (award && !badges.includes(award)) {
    const can = EARNABLE[award];
    if (!can || !can()) return { badges, awarded: false, reason: "that badge can't be earned now" };
    badges.push(award);
    try {
      await data.setProtectedItem(context.projectId, context.playerId, { key: "badges", value: badges });
    } catch (e) {
      logger.warning("Badges: couldn't save: " + e.message);
      return { badges: badges.filter((b) => b !== award), awarded: false, reason: "couldn't save it" };
    }
    return { badges, awarded: true };
  }
  return { badges, awarded: false };
};

module.exports.params = {
  award: { type: "STRING", required: false },
};
