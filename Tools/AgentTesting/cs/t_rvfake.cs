var g = Protobot.Game.SkillsGame.Current;
var old = UnityEngine.Object.FindFirstObjectByType<Protobot.Game.ScoreReveal>();
if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
var s = g.score;
s.items.Clear();
int n = 0;
foreach (var goal in g.field.goals) {
  var q = goal.quadrant;
  string where = q == Protobot.Game.Quadrant.Midfield ? "Midfield Goal" : q + " Quadrant goal";
  for (int k = 0; k < 2; k++) {
    var it = new Protobot.Game.ScoredItem();
    it.point = g.field.ToWorld(goal.position, goal.top + 2f + k * 3f);
    it.points = k == 0 ? 5 : 10;
    it.kind = k == 0 ? "Red pin halves" : "Yellow pin halves";
    it.where = where;
    s.items.Add(it);
    n++;
  }
}
s.total = 0; foreach (var it in s.items) s.total += it.points;
Protobot.Game.ScoreReveal.Play(g, true);
return "items " + n + " total " + s.total;
