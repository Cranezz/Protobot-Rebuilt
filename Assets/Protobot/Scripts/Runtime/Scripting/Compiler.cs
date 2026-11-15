using System;
using System.Collections.Generic;
using System.Linq;

namespace Protobot.Scripting {
    /// <summary>V2 (robot code): statements -> code objects for the <see cref="Interpreter"/>.</summary>
    public sealed class Compiler {
        public static readonly string[] BinOps = { "+", "-", "*", "/", "//", "%", "**", "<<", ">>", "&", "|", "^", "@" };
        public static readonly string[] CmpOps = { "<", "<=", "==", "!=", ">", ">=", "in", "not in", "is", "is not" };
        public static readonly string[] UnOps = { "-", "+", "~", "not" };

        /// <summary>How names are found in a code object: module (globals), class body (its dict) or function (slots).</summary>
        private sealed class ScopeInfo {
            public enum Kind { Module, Class, Function }
            public Kind kind;
            public readonly List<string> slots = new();
            public readonly HashSet<string> globals = new(), nonlocals = new();
            public ScopeInfo parent;
            public int Slot(string name) => slots.IndexOf(name);
        }

        private enum BlockKind { Loop, ForLoop, Try, Finally, Handler }

        private sealed class Block {
            public BlockKind kind;
            public int continueTarget;
            public readonly List<int> breaks = new();
            public List<Stmt> final;
        }

        private CodeObject code;
        private ScopeInfo scope;
        private readonly List<Block> blocks = new();
        private int line = 1;
        private int lineOverride;

        public static CodeObject CompileModule(List<Stmt> body) {
            var c = new Compiler();
            c.code = new CodeObject { name = "<module>", isModule = true };
            c.scope = new ScopeInfo { kind = ScopeInfo.Kind.Module };
            c.Body(body);
            c.Emit(Op.LoadNone);
            c.Emit(Op.Return);
            return c.code;
        }

        public static CodeObject Compile(string source) => CompileModule(Parser.Parse(source));

        // ------------------------------------------------------------------ emitting

        private int Emit(Op op, int arg = 0) {
            code.code.Add(new Instr { op = op, arg = arg, line = lineOverride > 0 ? lineOverride : line });
            return code.code.Count - 1;
        }

        private int Here => code.code.Count;

        private void Patch(int at, int target) {
            var i = code.code[at];
            i.arg = target;
            code.code[at] = i;
        }

        private void At(Node n) {
            if (n != null && n.line > 0) line = n.line;
        }

        private SyntaxErr Error(Node n, string message) => new(message, n?.line ?? line, n?.column ?? 1);

        // ------------------------------------------------------------------ names

        private static void Assigned(List<Stmt> body, ScopeInfo s) {
            foreach (var st in body) AssignedIn(st, s);
        }

        private static void AddTarget(Expr target, ScopeInfo s) {
            switch (target) {
                case NameExpr n:
                    if (!s.slots.Contains(n.name)) s.slots.Add(n.name);
                    break;
                case TupleExpr t:
                    foreach (var i in t.items) AddTarget(i, s);
                    break;
                case ListExpr l:
                    foreach (var i in l.items) AddTarget(i, s);
                    break;
                case StarExpr st:
                    AddTarget(st.value, s);
                    break;
            }
        }

        private static void AssignedIn(Stmt st, ScopeInfo s) {
            switch (st) {
                case AssignStmt a:
                    foreach (var t in a.targets) AddTarget(t, s);
                    break;
                case AugAssignStmt a:
                    AddTarget(a.target, s);
                    break;
                case ForStmt f:
                    AddTarget(f.target, s);
                    Assigned(f.body, s);
                    if (f.orElse != null) Assigned(f.orElse, s);
                    break;
                case WhileStmt w:
                    Assigned(w.body, s);
                    if (w.orElse != null) Assigned(w.orElse, s);
                    break;
                case IfStmt i:
                    Assigned(i.body, s);
                    if (i.orElse != null) Assigned(i.orElse, s);
                    break;
                case TryStmt t:
                    Assigned(t.body, s);
                    foreach (var h in t.handlers) {
                        if (h.name != null && !s.slots.Contains(h.name)) s.slots.Add(h.name);
                        Assigned(h.body, s);
                    }
                    if (t.orElse != null) Assigned(t.orElse, s);
                    if (t.final != null) Assigned(t.final, s);
                    break;
                case WithStmt w:
                    if (w.target != null) AddTarget(w.target, s);
                    Assigned(w.body, s);
                    break;
                case FuncDef d:
                    if (!s.slots.Contains(d.name)) s.slots.Add(d.name);
                    break;
                case ClassDef c:
                    if (!s.slots.Contains(c.name)) s.slots.Add(c.name);
                    break;
                case ImportStmt im:
                    if (im.names == null) {
                        var n = im.alias ?? im.module.Split('.')[0];
                        if (!s.slots.Contains(n)) s.slots.Add(n);
                    }
                    else
                        foreach (var (name, alias) in im.names)
                            if (!s.slots.Contains(alias ?? name)) s.slots.Add(alias ?? name);
                    break;
                case GlobalStmt g:
                    foreach (var n in g.names) (g.nonlocal ? s.nonlocals : s.globals).Add(n);
                    break;
                case DelStmt d:
                    foreach (var t in d.targets) AddTarget(t, s);
                    break;
            }
        }

        private enum Where { Fast, Deref, Global, Name }

        private (Where where, int arg) Resolve(string name) {
            switch (scope.kind) {
                case ScopeInfo.Kind.Module:
                    return (Where.Global, code.Name(name));
                case ScopeInfo.Kind.Class:
                    return (Where.Name, code.Name(name));
            }
            if (scope.globals.Contains(name)) return (Where.Global, code.Name(name));
            if (!scope.nonlocals.Contains(name)) {
                var slot = scope.Slot(name);
                if (slot >= 0) return (Where.Fast, slot);
            }
            // An enclosing function's variable (class bodies are skipped, but count as a level).
            var depth = 0;
            for (var s = scope.parent; s != null && s.kind != ScopeInfo.Kind.Module; s = s.parent) {
                depth++;
                if (s.kind != ScopeInfo.Kind.Function) continue;
                if (s.globals.Contains(name)) break;
                var slot = s.Slot(name);
                if (slot >= 0 && !s.nonlocals.Contains(name)) return (Where.Deref, (depth << 16) | slot);
            }
            return (Where.Global, code.Name(name));
        }

        private void Load(string name) {
            var (where, arg) = Resolve(name);
            Emit(where switch { Where.Fast => Op.LoadFast, Where.Deref => Op.LoadDeref, Where.Name => Op.LoadName, _ => Op.LoadGlobal }, arg);
        }

        private void Store(string name) {
            var (where, arg) = Resolve(name);
            Emit(where switch { Where.Fast => Op.StoreFast, Where.Deref => Op.StoreDeref, Where.Name => Op.StoreName, _ => Op.StoreGlobal }, arg);
        }

        // ------------------------------------------------------------------ statements

        private void Body(List<Stmt> body) {
            foreach (var s in body) Statement(s);
        }

        private void Statement(Stmt s) {
            At(s);
            switch (s) {
                case ExprStmt e:
                    Expression(e.expr);
                    Emit(Op.Pop);
                    break;
                case AssignStmt a:
                    Expression(a.value);
                    for (var i = 0; i < a.targets.Count; i++) {
                        if (i < a.targets.Count - 1) Emit(Op.Dup);
                        StoreTarget(a.targets[i]);
                    }
                    break;
                case AugAssignStmt a:
                    AugAssign(a);
                    break;
                case IfStmt i: {
                    if (i.test is ConstExpr { value: true }) {   // if True:
                        Body(i.body);
                        break;
                    }
                    Expression(i.test);
                    var jf = Emit(Op.JumpIfFalse);
                    Body(i.body);
                    if (i.orElse != null) {
                        var je = Emit(Op.Jump);
                        Patch(jf, Here);
                        Body(i.orElse);
                        Patch(je, Here);
                    }
                    else Patch(jf, Here);
                    break;
                }
                case WhileStmt w: {
                    var top = Here;
                    var block = new Block { kind = BlockKind.Loop, continueTarget = top };
                    var infinite = w.test is ConstExpr { value: true } || w.test is ConstExpr { value: long l1 } && l1 != 0;
                    var jf = -1;
                    if (!infinite) {
                        Expression(w.test);
                        jf = Emit(Op.JumpIfFalse);
                    }
                    blocks.Add(block);
                    Body(w.body);
                    blocks.RemoveAt(blocks.Count - 1);
                    At(w);
                    Emit(Op.Jump, top);
                    if (jf >= 0) Patch(jf, Here);
                    if (w.orElse != null) Body(w.orElse);
                    foreach (var b in block.breaks) Patch(b, Here);
                    break;
                }
                case ForStmt f: {
                    Expression(f.iter);
                    At(f);
                    Emit(Op.GetIter);
                    var top = Here;
                    var fi = Emit(Op.ForIter);
                    StoreTarget(f.target);
                    var block = new Block { kind = BlockKind.ForLoop, continueTarget = top };
                    blocks.Add(block);
                    Body(f.body);
                    blocks.RemoveAt(blocks.Count - 1);
                    At(f);
                    Emit(Op.Jump, top);
                    Patch(fi, Here);
                    if (f.orElse != null) Body(f.orElse);
                    foreach (var b in block.breaks) Patch(b, Here);
                    break;
                }
                case BreakStmt:
                case ContinueStmt: {
                    var isBreak = s is BreakStmt;
                    var loop = -1;
                    for (var i = blocks.Count - 1; i >= 0; i--)
                        if (blocks[i].kind is BlockKind.Loop or BlockKind.ForLoop) {
                            loop = i;
                            break;
                        }
                    if (loop < 0) throw Error(s, "'" + (isBreak ? "break" : "continue") + "' is only allowed inside a loop");
                    Unwind(loop + 1);
                    At(s);
                    var lb = blocks[loop];
                    if (isBreak) {
                        if (lb.kind == BlockKind.ForLoop) Emit(Op.Pop);   // the iterator
                        lb.breaks.Add(Emit(Op.Jump));
                    }
                    else Emit(Op.Jump, lb.continueTarget);
                    break;
                }
                case PassStmt:
                    break;
                case ReturnStmt r:
                    if (scope.kind != ScopeInfo.Kind.Function) throw Error(s, "'return' is only allowed inside a function");
                    if (r.value != null) Expression(r.value);
                    else Emit(Op.LoadNone);
                    Unwind(0);
                    At(s);
                    Emit(Op.Return);
                    break;
                case GlobalStmt:
                    break;
                case DelStmt d:
                    foreach (var t in d.targets) Delete(t);
                    break;
                case AssertStmt a: {
                    Expression(a.test);
                    var jt = Emit(Op.JumpIfTrue);
                    if (a.message != null) Expression(a.message);
                    else Emit(Op.LoadNone);
                    Emit(Op.Assert);
                    Patch(jt, Here);
                    break;
                }
                case RaiseStmt r:
                    if (r.exc != null) {
                        Expression(r.exc);
                        Emit(Op.Raise, 1);
                    }
                    else Emit(Op.Raise, 0);
                    break;
                case ImportStmt im:
                    Import(im);
                    break;
                case TryStmt t:
                    Try(t);
                    break;
                case FuncDef d:
                    foreach (var dec in d.decorators) Expression(dec);
                    MakeFunction(d);
                    for (var i = 0; i < d.decorators.Count; i++) Emit(Op.Call, 1);
                    Store(d.name);
                    break;
                case ClassDef c:
                    MakeClass(c);
                    Store(c.name);
                    break;
                case WithStmt w:
                    throw Error(w, "'with' isn't supported in robot code");
                default:
                    throw Error(s, "this statement isn't supported");
            }
        }

        /// <summary>Leaves the blocks inside blocks[keep..] (a break, continue or return): pops try blocks, ends
        /// handlers and runs finally bodies.</summary>
        private void Unwind(int keep) {
            for (var i = blocks.Count - 1; i >= keep; i--) {
                var b = blocks[i];
                switch (b.kind) {
                    case BlockKind.Try:
                        Emit(Op.PopBlock);
                        break;
                    case BlockKind.Handler:
                        Emit(Op.PopExcept);
                        break;
                    case BlockKind.Finally: {
                        Emit(Op.PopBlock);
                        // The finally body runs outside this block (and the ones inside it).
                        var saved = blocks.GetRange(i, blocks.Count - i);
                        blocks.RemoveRange(i, blocks.Count - i);
                        Body(b.final);
                        blocks.AddRange(saved);
                        break;
                    }
                }
            }
        }

        private void StoreTarget(Expr target) {
            At(target);
            switch (target) {
                case NameExpr n:
                    Store(n.name);
                    break;
                case AttrExpr a:
                    Expression(a.target);
                    Emit(Op.StoreAttr, code.Name(a.name));
                    break;
                case IndexExpr ix:
                    Expression(ix.target);
                    Expression(ix.index);
                    Emit(Op.StoreIndex);
                    break;
                case TupleExpr or ListExpr: {
                    var items = target is TupleExpr te ? te.items : ((ListExpr)target).items;
                    var star = items.FindIndex(e => e is StarExpr);
                    if (items.Count(e => e is StarExpr) > 1) throw Error(target, "only one '*' target is allowed");
                    Emit(Op.Unpack, items.Count | ((star + 1) << 16));
                    foreach (var item in items) StoreTarget(item is StarExpr se ? se.value : item);
                    break;
                }
                default:
                    throw Error(target, "can't assign to this");
            }
        }

        private void Delete(Expr target) {
            At(target);
            switch (target) {
                case NameExpr n: {
                    var (where, arg) = Resolve(n.name);
                    if (where == Where.Fast) Emit(Op.DeleteFast, arg);
                    else if (where == Where.Global || where == Where.Name) Emit(Op.DeleteGlobal, code.Name(n.name));
                    else throw Error(target, "can't delete an outer function's variable");
                    break;
                }
                case IndexExpr ix:
                    Expression(ix.target);
                    Expression(ix.index);
                    Emit(Op.DeleteIndex);
                    break;
                case TupleExpr t:
                    foreach (var i in t.items) Delete(i);
                    break;
                default:
                    throw Error(target, "can't delete this");
            }
        }

        private void AugAssign(AugAssignStmt a) {
            var op = Array.IndexOf(BinOps, a.op);
            switch (a.target) {
                case NameExpr n:
                    Load(n.name);
                    Expression(a.value);
                    At(a);
                    Emit(Op.InPlace, op);
                    Store(n.name);
                    break;
                case AttrExpr at:
                    Expression(at.target);
                    Emit(Op.Dup);
                    Emit(Op.LoadAttr, code.Name(at.name));
                    Expression(a.value);
                    At(a);
                    Emit(Op.InPlace, op);
                    Emit(Op.Rot2);
                    Emit(Op.StoreAttr, code.Name(at.name));
                    break;
                case IndexExpr ix:
                    Expression(ix.target);
                    Expression(ix.index);
                    Emit(Op.Dup2);
                    Emit(Op.LoadIndex);
                    Expression(a.value);
                    At(a);
                    Emit(Op.InPlace, op);
                    Emit(Op.Rot3);
                    Emit(Op.StoreIndex);
                    break;
                default:
                    throw Error(a, "can't assign to this");
            }
        }

        private void Import(ImportStmt im) {
            Emit(Op.Import, code.Name(im.module));
            if (im.star) {
                if (scope.kind == ScopeInfo.Kind.Function) throw Error(im, "'from ... import *' is only allowed at the top of the program");
                Emit(Op.ImportStar);
                return;
            }
            if (im.names == null) {
                Store(im.alias ?? im.module.Split('.')[0]);
                return;
            }
            foreach (var (name, alias) in im.names) {
                Emit(Op.ImportFrom, code.Name(name));
                Store(alias ?? name);
            }
            Emit(Op.Pop);
        }

        private void Try(TryStmt t) {
            if (t.final != null) {
                // try/finally round the rest.
                var setup = Emit(Op.SetupExcept);
                blocks.Add(new Block { kind = BlockKind.Finally, final = t.final });
                if (t.handlers.Count > 0) TryExcept(t);
                else Body(t.body);
                blocks.RemoveAt(blocks.Count - 1);
                Emit(Op.PopBlock);
                Body(t.final);
                var jend = Emit(Op.Jump);
                Patch(setup, Here);
                Body(t.final);
                Emit(Op.Reraise);
                Patch(jend, Here);
                return;
            }
            TryExcept(t);
        }

        private void TryExcept(TryStmt t) {
            var setup = Emit(Op.SetupExcept);
            blocks.Add(new Block { kind = BlockKind.Try });
            Body(t.body);
            blocks.RemoveAt(blocks.Count - 1);
            Emit(Op.PopBlock);
            var toElse = Emit(Op.Jump);
            Patch(setup, Here);
            var ends = new List<int>();
            var bare = false;
            foreach (var (type, name, body, hline) in t.handlers) {
                line = hline;
                var next = -1;
                if (type != null) {
                    Emit(Op.Dup);
                    Expression(type);
                    Emit(Op.MatchExcept);
                    next = Emit(Op.JumpIfFalse);
                }
                else bare = true;
                if (name != null) Store(name);
                else Emit(Op.Pop);
                blocks.Add(new Block { kind = BlockKind.Handler });
                Body(body);
                blocks.RemoveAt(blocks.Count - 1);
                Emit(Op.PopExcept);
                ends.Add(Emit(Op.Jump));
                if (next >= 0) Patch(next, Here);
                if (type == null) break;
            }
            if (!bare) Emit(Op.Reraise);
            Patch(toElse, Here);
            if (t.orElse != null) Body(t.orElse);
            foreach (var e in ends) Patch(e, Here);
        }

        // ------------------------------------------------------------------ functions and classes

        private CodeObject NewChild(string name, ScopeInfo.Kind kind, out ScopeInfo info) {
            info = new ScopeInfo { kind = kind, parent = scope };
            return new CodeObject { name = name, isClassBody = kind == ScopeInfo.Kind.Class };
        }

        private T Nested<T>(CodeObject child, ScopeInfo info, Func<T> build) {
            var (savedCode, savedScope, savedLine) = (code, scope, line);
            var savedBlocks = blocks.ToList();
            blocks.Clear();
            code = child;
            scope = info;
            try {
                return build();
            }
            finally {
                code = savedCode;
                scope = savedScope;
                line = savedLine;
                blocks.Clear();
                blocks.AddRange(savedBlocks);
            }
        }

        private void MakeFunction(FuncDef d) {
            foreach (var def in d.defaults) Expression(def);
            var child = NewChild(d.name, ScopeInfo.Kind.Function, out var info);
            foreach (var p in d.parameters) info.slots.Add(p);
            if (d.star != null) info.slots.Add(d.star);
            if (d.starStar != null) info.slots.Add(d.starStar);
            Assigned(d.body, info);
            foreach (var g in info.globals) info.slots.Remove(g);
            foreach (var g in info.nonlocals) info.slots.Remove(g);
            foreach (var p in d.parameters)
                if (info.globals.Contains(p)) throw Error(d, "'" + p + "' is a parameter and global");
            child.parameters = d.parameters.ToArray();
            child.positionalCount = d.parameters.Count;
            child.hasStar = d.star != null;
            child.hasStarStar = d.starStar != null;
            Nested(child, info, () => {
                line = d.line;
                Body(d.body);
                Emit(Op.LoadNone);
                Emit(Op.Return);
                return 0;
            });
            child.slotCount = info.slots.Count;
            child.slotNames = info.slots.ToArray();
            At(d);
            Emit(Op.MakeFunction, code.Const(child) | (d.defaults.Count << 16));
        }

        private void MakeClass(ClassDef c) {
            foreach (var b in c.bases) Expression(b);
            var child = NewChild(c.name, ScopeInfo.Kind.Class, out var info);
            Nested(child, info, () => {
                line = c.line;
                Body(c.body);
                Emit(Op.LoadNone);
                Emit(Op.Return);
                return 0;
            });
            At(c);
            Emit(Op.MakeClass, code.Const(child) | (c.bases.Count << 16));
        }

        // ------------------------------------------------------------------ expressions

        private void Expression(Expr e) {
            At(e);
            switch (e) {
                case ConstExpr c:
                    if (c.value is PyNone) Emit(Op.LoadNone);
                    else if (c.value is true) Emit(Op.LoadTrue);
                    else if (c.value is false) Emit(Op.LoadFalse);
                    else Emit(Op.LoadConst, code.Const(c.value));
                    break;
                case NameExpr n:
                    Load(n.name);
                    break;
                case AttrExpr a:
                    Expression(a.target);
                    At(a);
                    Emit(Op.LoadAttr, code.Name(a.name));
                    break;
                case IndexExpr ix:
                    Expression(ix.target);
                    Expression(ix.index);
                    At(ix);
                    Emit(Op.LoadIndex);
                    break;
                case SliceExpr s:
                    if (s.start != null) Expression(s.start); else Emit(Op.LoadNone);
                    if (s.stop != null) Expression(s.stop); else Emit(Op.LoadNone);
                    if (s.step != null) Expression(s.step); else Emit(Op.LoadNone);
                    Emit(Op.BuildSlice);
                    break;
                case BinExpr b:
                    Expression(b.left);
                    Expression(b.right);
                    At(b);
                    Emit(Op.Binary, Array.IndexOf(BinOps, b.op));
                    break;
                case UnaryExpr u:
                    Expression(u.operand);
                    At(u);
                    Emit(Op.Unary, Array.IndexOf(UnOps, u.op));
                    break;
                case BoolExpr b: {
                    Expression(b.left);
                    var j = Emit(b.isAnd ? Op.JumpIfFalseOrPop : Op.JumpIfTrueOrPop);
                    Expression(b.right);
                    Patch(j, Here);
                    break;
                }
                case CompareExpr c:
                    Compare(c);
                    break;
                case IfExpr i: {
                    Expression(i.test);
                    var jf = Emit(Op.JumpIfFalse);
                    Expression(i.body);
                    var je = Emit(Op.Jump);
                    Patch(jf, Here);
                    Expression(i.orElse);
                    Patch(je, Here);
                    break;
                }
                case ListExpr l:
                    if (l.items.Any(x => x is StarExpr)) {
                        Emit(Op.BuildList, 0);
                        foreach (var item in l.items) {
                            if (item is StarExpr se) {
                                Expression(se.value);
                                Emit(Op.ListExtend);
                            }
                            else {
                                Expression(item);
                                Emit(Op.ListAppend, 1);
                            }
                        }
                        break;
                    }
                    foreach (var item in l.items) Expression(item);
                    Emit(Op.BuildList, l.items.Count);
                    break;
                case TupleExpr t:
                    if (t.items.Any(x => x is StarExpr)) throw Error(t, "'*' in a tuple isn't supported here: use a list");
                    foreach (var item in t.items) Expression(item);
                    Emit(Op.BuildTuple, t.items.Count);
                    break;
                case DictExpr d:
                    for (var i = 0; i < d.keys.Count; i++) {
                        Expression(d.keys[i]);
                        Expression(d.values[i]);
                    }
                    Emit(Op.BuildDict, d.keys.Count);
                    break;
                case StarExpr s:
                    throw Error(s, "'*' can't be used here");
                case FStringExpr f: {
                    var count = 0;
                    foreach (var part in f.parts) {
                        if (part is string str) Emit(Op.LoadConst, code.Const(str));
                        else if (part is FValue fv) {
                            var saved = lineOverride;
                            lineOverride = f.line;
                            Expression(fv.expr);
                            Emit(Op.FormatValue, fv.conv == null && fv.spec == null ? -1 : code.Const((fv.conv ?? "") + "\0" + (fv.spec ?? "")));
                            lineOverride = saved;
                        }
                        count++;
                    }
                    if (count == 0) Emit(Op.LoadConst, code.Const(""));
                    else if (count > 1) Emit(Op.BuildString, count);
                    else if (f.parts[0] is string) { }
                    else Emit(Op.BuildString, 1);
                    break;
                }
                case LambdaExpr l:
                    MakeFunction(l.def);
                    break;
                case CallExpr c:
                    Call(c);
                    break;
                case CompExpr c:
                    Comprehension(c);
                    break;
                default:
                    throw Error(e, "this expression isn't supported");
            }
        }

        private void Compare(CompareExpr c) {
            Expression(c.first);
            if (c.ops.Count == 1) {
                Expression(c.rest[0]);
                At(c);
                Emit(Op.Compare, Array.IndexOf(CmpOps, c.ops[0]));
                return;
            }
            // a < b < c: b is compared twice but worked out once.
            var cleanups = new List<int>();
            for (var i = 0; i < c.ops.Count; i++) {
                Expression(c.rest[i]);
                At(c);
                if (i < c.ops.Count - 1) {
                    Emit(Op.Dup);
                    Emit(Op.Rot3);
                    Emit(Op.Compare, Array.IndexOf(CmpOps, c.ops[i]));
                    cleanups.Add(Emit(Op.JumpIfFalseOrPop));
                }
                else Emit(Op.Compare, Array.IndexOf(CmpOps, c.ops[i]));
            }
            var end = Emit(Op.Jump);
            foreach (var j in cleanups) Patch(j, Here);
            Emit(Op.Rot2);
            Emit(Op.Pop);
            Patch(end, Here);
        }

        private void Call(CallExpr c) {
            Expression(c.func);
            if (c.starArgs != null || c.starKw != null) {
                foreach (var a in c.args) Expression(a);
                Emit(Op.BuildList, c.args.Count);
                if (c.starArgs != null) {
                    Expression(c.starArgs);
                    Emit(Op.ListExtend);
                }
                for (var i = 0; i < c.kwNames.Count; i++) {
                    Emit(Op.LoadConst, code.Const(c.kwNames[i]));
                    Expression(c.kwValues[i]);
                }
                Emit(Op.BuildDict, c.kwNames.Count);
                if (c.starKw != null) {
                    Expression(c.starKw);
                    Emit(Op.ListExtend);   // (a dict: merged)
                }
                At(c);
                Emit(Op.CallEx);
                return;
            }
            foreach (var a in c.args) Expression(a);
            if (c.kwNames.Count == 0) {
                At(c);
                Emit(Op.Call, c.args.Count);
                return;
            }
            foreach (var v in c.kwValues) Expression(v);
            Emit(Op.LoadConst, code.Const(new PyTuple(c.kwNames.Cast<object>().ToArray())));
            At(c);
            Emit(Op.CallKw, c.args.Count + c.kwNames.Count);
        }

        /// <summary>A comprehension is a small function run at once (its loop variables stay inside it).</summary>
        private void Comprehension(CompExpr c) {
            var child = NewChild("<" + (c.kind == "gen" ? "genexpr" : c.kind + "comp") + ">", ScopeInfo.Kind.Function, out var info);
            info.slots.Add(".0");
            foreach (var (target, _, _) in c.loops) AddTarget(target, info);
            child.parameters = new[] { ".0" };
            child.positionalCount = 1;
            Nested(child, info, () => {
                line = c.line;
                Emit(c.kind == "dict" ? Op.BuildDict : Op.BuildList, 0);
                var tops = new List<(int top, int forIter)>();
                for (var i = 0; i < c.loops.Count; i++) {
                    var (target, iter, ifs) = c.loops[i];
                    if (i == 0) Emit(Op.LoadFast, 0);
                    else {
                        Expression(iter);
                        Emit(Op.GetIter);
                    }
                    var top = Here;
                    var fi = Emit(Op.ForIter);
                    StoreTarget(target);
                    foreach (var cond in ifs) {
                        Expression(cond);
                        Emit(Op.JumpIfFalse, top);
                    }
                    tops.Add((top, fi));
                }
                if (c.kind == "dict") {
                    Expression(c.element);
                    Expression(c.value);
                    Emit(Op.DictSet, c.loops.Count + 1);
                }
                else {
                    Expression(c.element);
                    Emit(Op.ListAppend, c.loops.Count + 1);
                }
                for (var i = tops.Count - 1; i >= 0; i--) {
                    Emit(Op.Jump, tops[i].top);
                    Patch(tops[i].forIter, Here);
                }
                if (c.kind == "set") {
                    // set(list)
                    Emit(Op.LoadGlobal, code.Name("set"));
                    Emit(Op.Rot2);
                    Emit(Op.Call, 1);
                }
                Emit(Op.Return);
                return 0;
            });
            child.slotCount = info.slots.Count;
            child.slotNames = info.slots.ToArray();
            At(c);
            Emit(Op.MakeFunction, code.Const(child));
            Expression(c.loops[0].iter);
            Emit(Op.GetIter);
            Emit(Op.Call, 1);
        }
    }
}
