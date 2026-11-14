var g = Protobot.Game.SkillsGame.Current;
var a = g.CornerGaps();
g.SetSlide(-200f);
var b = g.CornerGaps();
return "default " + a.minus.ToString("0.00") + " / " + a.plus.ToString("0.00") + " | slid to the end " + b.minus.ToString("0.00") + " / " + b.plus.ToString("0.00");
