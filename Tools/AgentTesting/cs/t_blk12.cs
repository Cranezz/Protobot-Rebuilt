// (Play mode, the 6-motor drive loaded) the code window with the My Block + list example (Blocks menu's 4th example),
// the My Blocks category shown; its Python compiles.
if (!UnityEngine.Application.isPlaying) return "not playing: the code window must not be opened in Edit mode";
Protobot.RobotCode.CodePanel.Open();
var T = typeof(Protobot.RobotCode.CodePanel);
var F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var panel = UnityEngine.Object.FindAnyObjectByType<Protobot.RobotCode.CodePanel>();
T.GetMethod("NewBlocks", F, null, new System.Type[] { typeof(int) }, null).Invoke(panel, new object[] { 3 });
var editor = (Protobot.RobotCode.Blocks.BlockEditor)T.GetField("blocks", F).GetValue(panel);
string compile;
try { Protobot.Scripting.Compiler.Compile(Protobot.RobotCode.CodeStore.Source); compile = "compiles"; } catch (System.Exception e) { compile = "ERROR " + e.Message; }
var comments = editor.GetComponentsInChildren<TMPro.TMP_InputField>(true).Count(f => f.transform.name == "Comment");
return compile + ", my blocks " + editor.program.myBlocks.Count + ", lists " + editor.program.lists.Count + ", comment boxes " + comments + "\n" + string.Join("\n", Protobot.RobotCode.CodeStore.Source.Split('\n').Skip(2).Take(60));
