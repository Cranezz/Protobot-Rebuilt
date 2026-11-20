// Blocks: My Blocks (number and boolean inputs, a label), lists and 2D lists, comments: the Python they make runs on
// the interpreter with the expected output (44, 3, 2, 3), the saved text reads back the same, then the My Block is
// edited (input renamed, a new input added) and its call keeps its value. No Play mode needed.
var D = Protobot.RobotCode.Blocks.BlockDefs.ById;
System.Func<string, Protobot.RobotCode.Blocks.Block> B = id => Protobot.RobotCode.Blocks.Block.Make(D[id]);
var p = new Protobot.RobotCode.Blocks.BlockProgram { config = "# no robot" };
var m = new Protobot.RobotCode.Blocks.MyBlock { key = "t0000001", name = "add to total", parts = {
    new Protobot.RobotCode.Blocks.MyBlockPart { kind = "number", text = "n", id = "a" },
    new Protobot.RobotCode.Blocks.MyBlockPart { kind = "label", text = "twice" },
    new Protobot.RobotCode.Blocks.MyBlockPart { kind = "boolean", text = "twice", id = "b" } } };
p.myBlocks.Add(m);
p.numbers.Add("total");
p.lists.Add(new Protobot.RobotCode.Blocks.ListVar { name = "nums", size = 3 });
p.lists2.Add(new Protobot.RobotCode.Blocks.ListVar { name = "grid", rows = 2, cols = 2 });
p.RegisterDefs();
p.SyncMyBlocks();
var define = p.stacks[0];
define.comment = "adds n (twice when asked)";
var ifb = B("if_else");
var tw = B("argbool"); tw.fields[0] = "twice"; ifb.inputs[0] = tw;
var ch2 = B("change_var"); ch2.fields[0] = "total";
var mul = B("math"); mul.fields[1] = "*"; mul.fields[2] = "2"; var a1 = B("arg"); a1.fields[0] = "n"; mul.inputs[0] = a1; ch2.inputs[1] = mul;
ifb.mouths[0] = ch2;
var ch1 = B("change_var"); ch1.fields[0] = "total"; var a2 = B("arg"); a2.fields[0] = "n"; ch1.inputs[1] = a2;
ifb.mouths[1] = ch1;
define.next = ifb;

var start = B("when_started"); start.x = 300;
var setn = D["setlist:nums"]; var sn = Protobot.RobotCode.Blocks.Block.Make(setn); sn.fields[0] = "5"; sn.fields[1] = "6"; sn.fields[2] = "7";
sn.comment = "three numbers\nsecond line";
var rp = B("list_replace"); rp.fields[0] = "2"; rp.fields[1] = "nums"; rp.fields[2] = "10";
var sg = Protobot.RobotCode.Blocks.Block.Make(D["setlist:grid"]); sg.fields[0] = "1"; sg.fields[1] = "2"; sg.fields[2] = "3"; sg.fields[3] = "4";
var setv = B("set_var"); setv.fields[0] = "myVariable"; setv.fields[1] = "1";
var rep = B("repeat"); rep.fields[0] = "3";
var call = B("call:" + m.key);
var it = B("list_item"); it.fields[1] = "nums"; var cv = B("var"); cv.fields[0] = "myVariable"; it.inputs[0] = cv; call.inputs[0] = it;
var tf = B("true_false"); tf.fields[0] = "True"; call.inputs[1] = tf;
var inc = B("change_var"); inc.fields[0] = "myVariable";
call.next = inc; rep.mouths[0] = call;
System.Func<Protobot.RobotCode.Blocks.Block, Protobot.RobotCode.Blocks.Block> Print = value => { var pr = B("console_print"); pr.inputs[0] = value; return pr; };
var tv = B("var"); tv.fields[0] = "total";
var gi = B("list2_item"); gi.fields[0] = "2"; gi.fields[1] = "1"; gi.fields[2] = "grid";
var gl = B("list2_length"); gl.fields[0] = "grid"; gl.fields[1] = "len(#[0])";
var ll = B("list_length"); ll.fields[0] = "nums";
var chain = new[] { start, sn, rp, sg, setv, rep, Print(tv), Print(gi), Print(gl), Print(ll) };
for (var i = 0; i + 1 < chain.Length; i++) chain[i].next = chain[i + 1];
p.stacks.Add(start);
p.notes.Add(new Protobot.RobotCode.Blocks.Note { text = "a canvas note", x = 10, y = 400 });

var source = p.ToSource(out var code);
var back = Protobot.RobotCode.Blocks.BlockProgram.FromSource(source);
var same = back != null && back.ToSource(out _) == source;
System.Func<string, string> Run = src => {
    var output = new System.Text.StringBuilder();
    try {
        var vm = new Protobot.Scripting.Interpreter { Output = s => output.Append(s) };
        vm.Load("class Event:\n    pass\ndef wait(a, b):\n    pass\nMSEC = 0\n" + src);
        for (var i = 0; i < 1000 && vm.Running; i++) vm.Step();
        return vm.Failed != null ? output + "ERROR line " + vm.Failed.line + ": " + vm.Failed.Describe : output.ToString();
    } catch (System.Exception e) { return "EXCEPTION " + e.Message; }
};
var result = Run(source).Replace("\n", " ").Trim();

// Edit: rename n -> amount, add an input before it; the call keeps its list item in "amount".
var parts = new System.Collections.Generic.List<Protobot.RobotCode.Blocks.MyBlockPart> {
    new Protobot.RobotCode.Blocks.MyBlockPart { kind = "number", text = "extra", id = "c" },
    new Protobot.RobotCode.Blocks.MyBlockPart { kind = "number", text = "amount", id = "a" },
    new Protobot.RobotCode.Blocks.MyBlockPart { kind = "boolean", text = "twice", id = "b" } };
back.EditMyBlock(back.myBlocks[0], "add to total", parts);
var call2 = back.Everything().First(b => b.type == "call:" + m.key);
var defn = back.stacks.First(s => s.type == "define:" + m.key);
var argNames = string.Join(",", defn.All().Where(b => b.type == "arg").Select(b => b.fields[0]));
var src2 = back.ToSource(out _);
var result2 = Run(src2).Replace("\n", " ").Trim();
return "round trip " + same + " | output: " + result + " (want 44 3 2 3) | after edit: call inputs " + call2.inputs.Count + " [" + (call2.inputs[1]?.type ?? "null") + "], args " + argNames + ", output " + result2
    + "\n" + string.Join("\n", source.Split('\n').Skip(2));
