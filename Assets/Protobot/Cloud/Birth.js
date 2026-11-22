// V2: an account's birth month and year (children's privacy law: players under 13 play without voice chat, and it
// switches on by itself when they turn 13). Only the month and year are kept, in the player's protected data (only Cloud
// Code writes it), and once set it can't be changed by the player (so it can't be moved to get round the rule).
// With month and year: sets it when there's none yet. Always returns { birth: "YYYY-MM" or "" }.
const { DataApi } = require("@unity-services/cloud-save-1.4");

module.exports = async ({ params, context, logger }) => {
  const data = new DataApi({ accessToken: context.serviceToken });
  let birth = "";
  try {
    const got = await data.getProtectedItems(context.projectId, context.playerId, ["birth"]);
    const found = got.data.results.find((r) => r.key === "birth");
    if (found && typeof found.value === "string") birth = found.value;
  } catch (e) { birth = ""; }
  const month = Math.round(Number(params.month));
  const year = Math.round(Number(params.year));
  if (!birth && month >= 1 && month <= 12) {
    const now = new Date().getUTCFullYear();
    if (!(year >= now - 120 && year <= now)) return { birth, set: false, reason: "that year isn't possible" };
    birth = year + "-" + (month < 10 ? "0" : "") + month;
    try {
      await data.setProtectedItem(context.projectId, context.playerId, { key: "birth", value: birth });
    } catch (e) {
      logger.warning("Birth: couldn't save: " + e.message);
      return { birth: "", set: false, reason: "couldn't save it" };
    }
    return { birth, set: true };
  }
  return { birth, set: false };
};

module.exports.params = {
  month: { type: "NUMERIC", required: false },
  year: { type: "NUMERIC", required: false },
};
