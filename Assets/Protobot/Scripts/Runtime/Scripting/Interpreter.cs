using System;
using System.Collections.Generic;
using System.Linq;

namespace Protobot.Scripting {
    /// <summary>A thread of the program (VEX's Thread): its own call stack; it waits on <see cref="Suspend"/>s.</summary>
    public sealed class Fiber {
        public string name;
        internal readonly List<Frame> frames = new();
        public Suspend waiting;
        public bool Done { get; internal set; }
        public bool stopped;

        /// <summary>Stops this thread (Thread.stop()).</summary>
        public void Stop() {
            Done = true;
            stopped = true;
        }
        internal object result;
        internal bool sync;   // run to the end at once (a key function, a dunder method): no waiting
        public int Line => frames.Count > 0 ? frames[frames.Count - 1].Line : 0;
    }

    internal sealed class Frame {
        public CodeObject code;
        public Scope scope;
        public PyModule module;
        public Dictionary<string, object> classDict;   // a class body's names
        public int pc;
        public object[] stack = new object[16];
        public int sp;
        public readonly List<(int handler, int depth)> blocks = new();
        public readonly List<PyInstance> handling = new();   // exceptions being handled (bare 'raise')
        public PyInstance initSelf;                          // an __init__ call returns the instance
        public (string name, PyClass[] bases)? classBuild;   // a class body returns the class
        public int Line => pc > 0 && pc <= code.code.Count ? code.code[pc - 1].line : code.code.Count > 0 ? code.code[0].line : 0;

        public void Push(object o) {
            if (sp == stack.Length) Array.Resize(ref stack, stack.Length * 2);
            stack[sp++] = o;
        }

        public object Pop() => stack[--sp];
        public object Top => stack[sp - 1];
    }

    /// <summary>
    /// V2 (Phase 9, robot code): runs compiled Python a few thousand instructions at a time. Each thread (<see
    /// cref="Fiber"/>) keeps its own stack of frames, so a built-in can make it wait (wait(20, MSEC), spin_for)
    /// anywhere, in any nested call, and the host (the simulation) steps all of them every physics step.
    /// </summary>
    public sealed class Interpreter {
        private static readonly object Unbound = new();
        public const int MaxDepth = 200;

        public readonly Dictionary<string, object> builtins = new();
        public readonly Dictionary<string, PyModule> modules = new();
        public readonly PyModule main = new("__main__");
        public readonly List<Fiber> fibers = new();

        /// <summary>The time the program sees (seconds; the host sets it each step).</summary>
        public double Time;
        /// <summary>Instructions each thread may run per step (about what a V5 brain manages).</summary>
        public int Budget = 4000;
        public Action<string> Output = _ => { };
        /// <summary>A thread raised an error nothing caught: the program stops.</summary>
        public PyError Failed { get; private set; }
        public long Executed { get; private set; }

        public Interpreter() {
            Builtins.Install(this);
        }

        public bool Running => Failed == null && fibers.Any(f => !f.Done);

        // ------------------------------------------------------------------ starting

        /// <summary>Compiles the program (SyntaxErr on a mistake) and starts its main thread.</summary>
        public Fiber Load(string source, string name = "main") => Load(Compiler.Compile(source), name);

        /// <summary>V2: a program already compiled (C++ translated to the same syntax tree).</summary>
        public Fiber Load(CodeObject code, string name = "main") {
            main.globals["__name__"] = "__main__";
            var fiber = new Fiber { name = name };
            var frame = new Frame { code = code, scope = new Scope(0, null), module = main };
            fiber.frames.Add(frame);
            fibers.Add(fiber);
            return fiber;
        }

        /// <summary>Starts a thread calling a function (Thread(f), an event's callback, a competition period).</summary>
        public Fiber Start(object callable, object[] args, string name) {
            var fiber = new Fiber { name = name };
            fibers.Add(fiber);
            try {
                Invoke(fiber, null, callable, args ?? Array.Empty<object>(), null);
                if (fiber.frames.Count == 0 && fiber.waiting == null) fiber.Done = true;   // a built-in: done at once
            }
            catch (PyError e) {
                Fail(e, fiber);
            }
            return fiber;
        }

        /// <summary>Calls a function now and returns its value (it can't wait): sorted(key=...), __str__.</summary>
        public object CallSync(object callable, params object[] args) => CallSync(callable, args, null);

        public object CallSync(object callable, object[] args, Dictionary<string, object> kw) {
            if (callable is PyBuiltin b) {
                var r = b.fn(new Args(this, b.name, args, kw));
                if (r is Suspend) throw new PyError("RuntimeError", b.name + "() can't wait here");
                return r;
            }
            var fiber = new Fiber { name = "call", sync = true };
            Invoke(fiber, null, callable, args, kw);
            if (fiber.frames.Count == 0) return fiber.result;
            var guard = 0;
            while (!fiber.Done) {
                RunFiber(fiber, 1_000_000);
                if (fiber.waiting != null) throw new PyError("RuntimeError", "this function can't wait (it runs inside another call)");
                if (++guard > 100) throw new PyError("RuntimeError", "this function took too long");
            }
            return fiber.result;
        }

        public void StopAll() {
            foreach (var f in fibers) {
                f.Done = true;
                f.stopped = true;
            }
        }

        private void Fail(PyError e, Fiber f) {
            if (f.sync) throw e;
            Failed ??= e;
            StopAll();
        }

        // ------------------------------------------------------------------ stepping

        /// <summary>Runs every thread that isn't waiting for its budget.</summary>
        public void Step() {
            if (Failed != null) return;
            for (var i = 0; i < fibers.Count; i++) {
                var f = fibers[i];
                if (f.Done) continue;
                if (f.waiting != null) {
                    var w = f.waiting;
                    if (w.done != null ? !SafeDone(w, f) : Time + 1e-9 < w.until) continue;
                    f.waiting = null;
                }
                if (Failed != null) return;
                RunFiber(f, Budget);
            }
            fibers.RemoveAll(f => f.Done);
        }

        private bool SafeDone(Suspend w, Fiber f) {
            try {
                return w.done();
            }
            catch (PyError e) {
                Fail(e, f);
                return true;
            }
        }

        /// <summary>The thread running now (built-ins like super() look at its frame).</summary>
        public Fiber Current { get; private set; }

        internal Frame CurrentFrame => Current != null && Current.frames.Count > 0 ? Current.frames[Current.frames.Count - 1] : null;

        private void RunFiber(Fiber f, int budget) {
            var outer = Current;
            Current = f;
            try {
                RunFiberInner(f, budget);
            }
            finally {
                Current = outer;
            }
        }

        private void RunFiberInner(Fiber f, int budget) {
            while (budget > 0 && !f.Done && f.waiting == null) {
                if (f.frames.Count == 0) {
                    f.Done = true;
                    return;
                }
                try {
                    budget = Run(f, budget);
                }
                catch (PyError e) {
                    Raise(f, e);
                }
                catch (OverflowException) {
                    Raise(f, new PyError("OverflowError", "the number got too big"));
                }
                catch (InvalidCastException ex) {
                    Raise(f, new PyError("TypeError", ex.Message));
                }
                catch (IndexOutOfRangeException) {
                    Raise(f, new PyError("RuntimeError", "the interpreter's stack went wrong (please report this program)"));
                }
                catch (Exception ex) {
                    // A fault in the app under a built-in (a device that went away...): the program gets a clear error
                    // on its line instead of a broken stack.
                    UnityEngine.Debug.LogException(ex);
                    Raise(f, new PyError("RuntimeError", "something in the app failed under this line: " + ex.Message));
                }
            }
        }

        /// <summary>An error in a thread: the nearest try block takes it, or the thread (and the program) stops.</summary>
        private void Raise(Fiber f, PyError e) {
            var instance = e.instance ?? Builtins.MakeException(this, e.type, e.Message);
            e.instance = instance;
            var first = true;
            while (f.frames.Count > 0) {
                var frame = f.frames[f.frames.Count - 1];
                if (first && e.line == 0) e.line = frame.Line;
                first = false;
                if (frame.blocks.Count > 0) {
                    var (handler, depth) = frame.blocks[frame.blocks.Count - 1];
                    frame.blocks.RemoveAt(frame.blocks.Count - 1);
                    frame.sp = depth;
                    frame.Push(instance);
                    frame.handling.Add(instance);
                    frame.pc = handler;
                    lastError[instance] = e;
                    return;
                }
                e.trace.Add("line " + frame.Line + (frame.code.isModule ? "" : ", in " + frame.code.name + "()"));
                f.frames.RemoveAt(f.frames.Count - 1);
            }
            f.Done = true;
            Fail(e, f);
        }

        // The C# error behind a caught instance (its line), for re-raising.
        private readonly Dictionary<PyInstance, PyError> lastError = new();

        // ------------------------------------------------------------------ calls

        /// <summary>Calls something: Python functions get a new frame (run by the loop); built-ins run now.</summary>
        private void Invoke(Fiber f, Frame caller, object callee, object[] pos, Dictionary<string, object> kw) {
            switch (callee) {
                case PyFunction fn:
                    PushFrame(f, fn, pos, kw, null);
                    return;
                case PyBoundMethod m: {
                    var all = new object[pos.Length + 1];
                    all[0] = m.self;
                    Array.Copy(pos, 0, all, 1, pos.Length);
                    Invoke(f, caller, m.function, all, kw);
                    return;
                }
                case PyBuiltin b: {
                    var r = b.fn(new Args(this, b.name, pos, kw));
                    if (r is Suspend s) {
                        if (f.sync) throw new PyError("RuntimeError", b.name + "() can't wait here (inside a function called by another)");
                        f.waiting = s;
                        r = s.result ?? PyNone.Value;
                    }
                    Return(f, caller, r ?? PyNone.Value);
                    return;
                }
                case PyClass cls: {
                    var maker = cls.mro.FirstOrDefault(c => c.construct != null);
                    if (maker != null && !UserInitAbove(cls, maker)) {
                        var made = maker.construct(cls, new Args(this, cls.name, pos, kw));
                        Return(f, caller, made);
                        return;
                    }
                    var self = new PyInstance(cls);
                    if (cls.mro.Any(c => c.name == "BaseException")) self.dict["args"] = new PyTuple(pos);
                    if (cls.Lookup("__init__", out var init) && init is PyFunction initFn) {
                        var all = new object[pos.Length + 1];
                        all[0] = self;
                        Array.Copy(pos, 0, all, 1, pos.Length);
                        PushFrame(f, initFn, all, kw, self);
                        return;
                    }
                    if (pos.Length > 0 && !cls.mro.Any(c => c.name == "BaseException")) throw new PyError("TypeError", cls.name + "() takes no arguments (it has no __init__)");
                    Return(f, caller, self);
                    return;
                }
                case PyInstance inst when inst.cls.Lookup("__call__", out var call):
                    Invoke(f, caller, new PyBoundMethod(inst, call), pos, kw);
                    return;
            }
            throw new PyError("TypeError", Ops.Describe(callee) + " can't be called like a function" + (callee is PyNone ? " (a function that returns nothing gave None)" : ""));
        }

        // A user class with its own __init__ that derives from a built-in type (an Exception): plain instance + __init__.
        private static bool UserInitAbove(PyClass cls, PyClass maker) {
            foreach (var c in cls.mro) {
                if (c == maker) return false;
                if (c.dict.ContainsKey("__init__")) return true;
            }
            return false;
        }

        /// <summary>A value back to the caller's frame (or the fiber's result when nothing called it).</summary>
        private static void Return(Fiber f, Frame caller, object value) {
            if (caller != null) caller.Push(value);
            else if (f.frames.Count == 0) {
                f.result = value;
                if (f.waiting == null) f.Done = true;
            }
            else f.frames[f.frames.Count - 1].Push(value);
        }

        private void PushFrame(Fiber f, PyFunction fn, object[] pos, Dictionary<string, object> kw, PyInstance initSelf) {
            if (f.frames.Count >= MaxDepth) throw new PyError("RecursionError", "too many calls inside each other (" + MaxDepth + "): a function that calls itself forever?");
            var code = fn.code;
            var scope = new Scope(code.slotCount, fn.closure);
            var slots = scope.slots;
            for (var i = 0; i < slots.Length; i++) slots[i] = Unbound;
            var n = code.positionalCount;
            PyList extra = null;
            for (var i = 0; i < pos.Length; i++) {
                if (i < n) slots[i] = pos[i];
                else if (code.hasStar) (extra ??= new PyList()).items.Add(pos[i]);
                else throw new PyError("TypeError", fn.Name + "() takes " + Plural(n - (initSelf != null ? 1 : 0), "argument") + " but " + (pos.Length - (initSelf != null ? 1 : 0)) + " were given");
            }
            PyDict extraKw = null;
            if (kw != null)
                foreach (var kv in kw) {
                    var idx = Array.IndexOf(code.parameters, kv.Key);
                    if (idx >= 0) {
                        if (slots[idx] != Unbound) throw new PyError("TypeError", fn.Name + "() got two values for '" + kv.Key + "'");
                        slots[idx] = kv.Value;
                    }
                    else if (code.hasStarStar) (extraKw ??= new PyDict()).Set(kv.Key, kv.Value);
                    else throw new PyError("TypeError", fn.Name + "() has no parameter called '" + kv.Key + "'");
                }
            var firstDefault = n - fn.defaults.Length;
            for (var i = 0; i < n; i++) {
                if (slots[i] != Unbound) continue;
                if (i >= firstDefault) slots[i] = fn.defaults[i - firstDefault];
                else throw new PyError("TypeError", fn.Name + "() is missing its argument '" + code.parameters[i] + "'");
            }
            if (code.hasStar) slots[n] = extra != null ? new PyTuple(extra.items.ToArray()) : PyTuple.Empty;
            if (code.hasStarStar) slots[n + (code.hasStar ? 1 : 0)] = extraKw ?? new PyDict();
            f.frames.Add(new Frame { code = code, scope = scope, module = fn.module, initSelf = initSelf });
        }

        private static string Plural(int n, string word) => n + " " + word + (n == 1 ? "" : "s");

        // ------------------------------------------------------------------ the loop

        private int Run(Fiber f, int budget) {
            var frame = f.frames[f.frames.Count - 1];
            var code = frame.code.code;
            while (budget-- > 0) {
                var ins = code[frame.pc++];
                Executed++;
                switch (ins.op) {
                    case Op.Nop: break;
                    case Op.LoadConst: frame.Push(frame.code.consts[ins.arg]); break;
                    case Op.LoadNone: frame.Push(PyNone.Value); break;
                    case Op.LoadTrue: frame.Push(true); break;
                    case Op.LoadFalse: frame.Push(false); break;
                    case Op.LoadFast: {
                        var v = frame.scope.slots[ins.arg];
                        if (v == Unbound) throw new PyError("UnboundLocalError", "'" + frame.code.slotNames[ins.arg] + "' is used before it's given a value in " + frame.code.name + "() (to change a variable from outside the function, say 'global " + frame.code.slotNames[ins.arg] + "' first)");
                        frame.Push(v);
                        break;
                    }
                    case Op.StoreFast: frame.scope.slots[ins.arg] = frame.Pop(); break;
                    case Op.DeleteFast: frame.scope.slots[ins.arg] = Unbound; break;
                    case Op.LoadDeref: {
                        var s = frame.scope;
                        for (var d = ins.arg >> 16; d > 0; d--) s = s.parent;
                        var v = s.slots[ins.arg & 0xFFFF];
                        if (v == Unbound) throw new PyError("NameError", "a variable of the outer function is used before it's given a value");
                        frame.Push(v);
                        break;
                    }
                    case Op.StoreDeref: {
                        var s = frame.scope;
                        for (var d = ins.arg >> 16; d > 0; d--) s = s.parent;
                        s.slots[ins.arg & 0xFFFF] = frame.Pop();
                        break;
                    }
                    case Op.LoadGlobal: {
                        var name = frame.code.names[ins.arg];
                        if (frame.module.globals.TryGetValue(name, out var v) || builtins.TryGetValue(name, out v)) frame.Push(v);
                        else throw NameError(name);
                        break;
                    }
                    case Op.StoreGlobal: frame.module.globals[frame.code.names[ins.arg]] = frame.Pop(); break;
                    case Op.DeleteGlobal: {
                        var name = frame.code.names[ins.arg];
                        if (frame.classDict != null) frame.classDict.Remove(name);
                        else if (!frame.module.globals.Remove(name)) throw NameError(name);
                        break;
                    }
                    case Op.LoadName: {
                        var name = frame.code.names[ins.arg];
                        if (frame.classDict != null && frame.classDict.TryGetValue(name, out var v) || frame.module.globals.TryGetValue(name, out v) || builtins.TryGetValue(name, out v)) frame.Push(v);
                        else throw NameError(name);
                        break;
                    }
                    case Op.StoreName: frame.classDict[frame.code.names[ins.arg]] = frame.Pop(); break;
                    case Op.LoadAttr: frame.Push(GetAttr(frame.Pop(), frame.code.names[ins.arg])); break;
                    case Op.StoreAttr: {
                        var obj = frame.Pop();
                        SetAttr(obj, frame.code.names[ins.arg], frame.Pop());
                        break;
                    }
                    case Op.LoadIndex: {
                        var index = frame.Pop();
                        var obj = frame.Pop();
                        if (obj is PyInstance pi && pi.cls.Lookup("__getitem__", out var gi)) frame.Push(CallSync(new PyBoundMethod(pi, gi), index));
                        else frame.Push(Ops.GetItem(obj, index));
                        break;
                    }
                    case Op.StoreIndex: {
                        var index = frame.Pop();
                        var obj = frame.Pop();
                        var value = frame.Pop();
                        if (obj is PyInstance pi && pi.cls.Lookup("__setitem__", out var si)) CallSync(new PyBoundMethod(pi, si), index, value);
                        else Ops.SetItem(obj, index, value);
                        break;
                    }
                    case Op.DeleteIndex: {
                        var index = frame.Pop();
                        Ops.DelItem(frame.Pop(), index);
                        break;
                    }
                    case Op.BuildSlice: {
                        var step = frame.Pop();
                        var stop = frame.Pop();
                        frame.Push(new PySlice(frame.Pop(), stop, step));
                        break;
                    }
                    case Op.Binary:
                    case Op.InPlace: {
                        var b = frame.Pop();
                        var a = frame.Pop();
                        if (ins.op == Op.InPlace && a is PyList la && ins.arg == 0) {   // list += iterable changes the list
                            la.items.AddRange(Ops.Iterate(b));
                            frame.Push(la);
                        }
                        else frame.Push(BinaryOp(Compiler.BinOps[ins.arg], a, b));
                        break;
                    }
                    case Op.Unary: {
                        var a = frame.Pop();
                        if (a is PyInstance pi && ins.arg == 0 && pi.cls.Lookup("__neg__", out var neg)) frame.Push(CallSync(new PyBoundMethod(pi, neg)));
                        else frame.Push(ins.arg == 3 ? !Truthy(a) : Ops.Unary(Compiler.UnOps[ins.arg], a));
                        break;
                    }
                    case Op.Compare: {
                        var b = frame.Pop();
                        var a = frame.Pop();
                        frame.Push(CompareOp(Compiler.CmpOps[ins.arg], a, b));
                        break;
                    }
                    case Op.Jump: frame.pc = ins.arg; break;
                    case Op.JumpIfFalse:
                        if (!Truthy(frame.Pop())) frame.pc = ins.arg;
                        break;
                    case Op.JumpIfTrue:
                        if (Truthy(frame.Pop())) frame.pc = ins.arg;
                        break;
                    case Op.JumpIfFalseOrPop:
                        if (!Truthy(frame.Top)) frame.pc = ins.arg;
                        else frame.sp--;
                        break;
                    case Op.JumpIfTrueOrPop:
                        if (Truthy(frame.Top)) frame.pc = ins.arg;
                        else frame.sp--;
                        break;
                    case Op.GetIter: frame.Push(Ops.GetIter(frame.Pop())); break;
                    case Op.ForIter: {
                        var it = (PyIterator)frame.Top;
                        if (it.e.MoveNext()) frame.Push(it.e.Current);
                        else {
                            frame.sp--;
                            frame.pc = ins.arg;
                        }
                        break;
                    }
                    case Op.Call:
                    case Op.CallKw:
                    case Op.CallEx: {
                        object callee;
                        object[] pos;
                        Dictionary<string, object> kw = null;
                        if (ins.op == Op.Call) {
                            pos = new object[ins.arg];
                            for (var i = ins.arg - 1; i >= 0; i--) pos[i] = frame.Pop();
                        }
                        else if (ins.op == Op.CallKw) {
                            var names = ((PyTuple)frame.Pop()).items;
                            kw = new Dictionary<string, object>();
                            for (var i = names.Length - 1; i >= 0; i--) kw[(string)names[i]] = frame.Pop();
                            pos = new object[ins.arg - names.Length];
                            for (var i = pos.Length - 1; i >= 0; i--) pos[i] = frame.Pop();
                        }
                        else {
                            var kwd = (PyDict)frame.Pop();
                            pos = ((PyList)frame.Pop()).items.ToArray();
                            if (kwd.items.Count > 0) {
                                kw = new Dictionary<string, object>();
                                foreach (var k in kwd.Keys) kw[k as string ?? throw new PyError("TypeError", "keyword names must be strings")] = kwd.items[k];
                            }
                        }
                        callee = frame.Pop();
                        var depth = f.frames.Count;
                        Invoke(f, frame, callee, pos, kw);
                        if (f.frames.Count != depth || f.waiting != null) return budget;   // a new frame, or the thread waits
                        break;
                    }
                    case Op.Return: {
                        var value = frame.Pop();
                        f.frames.RemoveAt(f.frames.Count - 1);
                        if (frame.initSelf != null) {
                            if (!(value is PyNone)) throw new PyError("TypeError", "__init__() should return nothing");
                            value = frame.initSelf;
                        }
                        else if (frame.classBuild.HasValue) value = BuildClass(frame.classBuild.Value.name, frame.classBuild.Value.bases, frame.classDict);
                        if (f.frames.Count == 0) {
                            f.result = value;
                            f.Done = true;
                            return budget;
                        }
                        f.frames[f.frames.Count - 1].Push(value);
                        return budget;
                    }
                    case Op.Pop: frame.sp--; break;
                    case Op.Dup: frame.Push(frame.Top); break;
                    case Op.Dup2: {
                        var b = frame.stack[frame.sp - 1];
                        var a = frame.stack[frame.sp - 2];
                        frame.Push(a);
                        frame.Push(b);
                        break;
                    }
                    case Op.Rot2: {
                        var s = frame.stack;
                        (s[frame.sp - 1], s[frame.sp - 2]) = (s[frame.sp - 2], s[frame.sp - 1]);
                        break;
                    }
                    case Op.Rot3: {   // [a b c] -> [c a b]
                        var s = frame.stack;
                        var c = s[frame.sp - 1];
                        s[frame.sp - 1] = s[frame.sp - 2];
                        s[frame.sp - 2] = s[frame.sp - 3];
                        s[frame.sp - 3] = c;
                        break;
                    }
                    case Op.BuildList: {
                        var l = new PyList();
                        var start = frame.sp - ins.arg;
                        for (var i = start; i < frame.sp; i++) l.items.Add(frame.stack[i]);
                        frame.sp = start;
                        frame.Push(l);
                        break;
                    }
                    case Op.BuildTuple: {
                        var items = new object[ins.arg];
                        var start = frame.sp - ins.arg;
                        Array.Copy(frame.stack, start, items, 0, ins.arg);
                        frame.sp = start;
                        frame.Push(ins.arg == 0 ? PyTuple.Empty : new PyTuple(items));
                        break;
                    }
                    case Op.BuildDict: {
                        var d = new PyDict();
                        var start = frame.sp - ins.arg * 2;
                        for (var i = start; i < frame.sp; i += 2) d.Set(frame.stack[i], frame.stack[i + 1]);
                        frame.sp = start;
                        frame.Push(d);
                        break;
                    }
                    case Op.BuildString: {
                        var start = frame.sp - ins.arg;
                        var sb = new System.Text.StringBuilder();
                        for (var i = start; i < frame.sp; i++) sb.Append((string)frame.stack[i]);
                        frame.sp = start;
                        frame.Push(sb.ToString());
                        break;
                    }
                    case Op.ListAppend: {
                        var v = frame.Pop();
                        ((PyList)frame.stack[frame.sp - ins.arg]).items.Add(v);
                        break;
                    }
                    case Op.DictSet: {
                        var v = frame.Pop();
                        var k = frame.Pop();
                        ((PyDict)frame.stack[frame.sp - ins.arg]).Set(k, v);
                        break;
                    }
                    case Op.ListExtend: {
                        var v = frame.Pop();
                        if (frame.Top is PyDict target) {
                            if (!(v is PyDict src)) throw new PyError("TypeError", "** needs a dict, not " + Ops.Describe(v));
                            foreach (var k in src.Keys) target.Set(k, src.items[k]);
                        }
                        else ((PyList)frame.Top).items.AddRange(Ops.Iterate(v));
                        break;
                    }
                    case Op.Unpack: {
                        var count = ins.arg & 0xFFFF;
                        var star = (ins.arg >> 16) - 1;
                        var items = Ops.Iterate(frame.Pop()).ToList();
                        if (star < 0 && items.Count != count)
                            throw new PyError("ValueError", items.Count < count ? "not enough values to unpack (expected " + count + ", got " + items.Count + ")" : "too many values to unpack (expected " + count + ")");
                        if (star >= 0) {
                            if (items.Count < count - 1) throw new PyError("ValueError", "not enough values to unpack (expected at least " + (count - 1) + ", got " + items.Count + ")");
                            var after = count - 1 - star;
                            var middle = new PyList(items.Skip(star).Take(items.Count - star - after));
                            items = items.Take(star).Concat(new object[] { middle }).Concat(items.Skip(items.Count - after)).ToList();
                        }
                        for (var i = items.Count - 1; i >= 0; i--) frame.Push(items[i]);
                        break;
                    }
                    case Op.FormatValue: {
                        var v = frame.Pop();
                        if (ins.arg < 0) frame.Push(StrOf(v));
                        else {
                            var parts = ((string)frame.code.consts[ins.arg]).Split('\0');
                            if (parts[0] == "r") v = ReprOf(v);
                            else if (parts[0] == "s") v = StrOf(v);
                            frame.Push(Ops.Format(v, parts[1]));
                        }
                        break;
                    }
                    case Op.MakeFunction: {
                        var child = (CodeObject)frame.code.consts[ins.arg & 0xFFFF];
                        var count = ins.arg >> 16;
                        var defaults = new object[count];
                        for (var i = count - 1; i >= 0; i--) defaults[i] = frame.Pop();
                        frame.Push(new PyFunction(child, defaults, frame.scope, frame.module));
                        break;
                    }
                    case Op.MakeClass: {
                        var body = (CodeObject)frame.code.consts[ins.arg & 0xFFFF];
                        var count = ins.arg >> 16;
                        var bases = new PyClass[count];
                        for (var i = count - 1; i >= 0; i--) {
                            var b = frame.Pop();
                            bases[i] = b as PyClass ?? throw new PyError("TypeError", "a class can only build on another class, not " + Ops.Describe(b));
                        }
                        if (f.frames.Count >= MaxDepth) throw new PyError("RecursionError", "too deep");
                        f.frames.Add(new Frame {
                            code = body, scope = new Scope(0, frame.scope), module = frame.module,
                            classDict = new Dictionary<string, object> { ["__module__"] = frame.module.name },
                            classBuild = (body.name, bases),
                        });
                        return budget;
                    }
                    case Op.Import: {
                        var name = frame.code.names[ins.arg];
                        if (!modules.TryGetValue(name, out var module))
                            throw new PyError("ImportError", "there's no module called '" + name + "' in robot code (available: " + string.Join(", ", modules.Keys.OrderBy(k => k)) + ")");
                        frame.Push(module);
                        break;
                    }
                    case Op.ImportFrom: {
                        var module = (PyModule)frame.Top;
                        var name = frame.code.names[ins.arg];
                        if (!module.globals.TryGetValue(name, out var v)) throw new PyError("ImportError", "the module '" + module.name + "' has nothing called '" + name + "'");
                        frame.Push(v);
                        break;
                    }
                    case Op.ImportStar: {
                        var module = (PyModule)frame.Pop();
                        foreach (var kv in module.globals)
                            if (!kv.Key.StartsWith("_")) {
                                if (frame.classDict != null) frame.classDict[kv.Key] = kv.Value;
                                else frame.module.globals[kv.Key] = kv.Value;
                            }
                        break;
                    }
                    case Op.SetupExcept: frame.blocks.Add((ins.arg, frame.sp)); break;
                    case Op.PopBlock: frame.blocks.RemoveAt(frame.blocks.Count - 1); break;
                    case Op.Raise: {
                        if (ins.arg == 0) {
                            if (frame.handling.Count == 0) throw new PyError("RuntimeError", "'raise' on its own only works inside an 'except' block");
                            var again = frame.handling[frame.handling.Count - 1];
                            throw lastError.TryGetValue(again, out var old) ? Rethrown(old) : new PyError(again);
                        }
                        var exc = frame.Pop();
                        if (exc is PyClass ec) exc = CallSync(ec);
                        if (!(exc is PyInstance pi) || !pi.cls.mro.Any(c => c.name == "BaseException"))
                            throw new PyError("TypeError", "only exceptions can be raised (like ValueError(\"...\")), not " + Ops.Describe(exc));
                        throw new PyError(pi);
                    }
                    case Op.Reraise: {
                        var exc = (PyInstance)frame.Pop();
                        if (frame.handling.Count > 0) frame.handling.RemoveAt(frame.handling.Count - 1);
                        throw lastError.TryGetValue(exc, out var old) ? Rethrown(old) : new PyError(exc);
                    }
                    case Op.MatchExcept: {
                        var type = frame.Pop();
                        var exc = (PyInstance)frame.Pop();
                        frame.Push(ExceptionMatches(exc, type));
                        break;
                    }
                    case Op.PopExcept:
                        if (frame.handling.Count > 0) {
                            lastError.Remove(frame.handling[frame.handling.Count - 1]);
                            frame.handling.RemoveAt(frame.handling.Count - 1);
                        }
                        break;
                    case Op.Assert: {
                        var message = frame.Pop();
                        throw new PyError("AssertionError", message is PyNone ? "" : StrOf(message));
                    }
                    default:
                        throw new PyError("RuntimeError", "unknown instruction " + ins.op);
                }
            }
            return 0;
        }

        private static PyError Rethrown(PyError old) {
            var e = new PyError(old.instance) { line = old.line };
            return e;
        }

        private PyError NameError(string name) {
            var close = main.globals.Keys.Concat(builtins.Keys).Where(k => Close(k, name)).Take(1).ToList();
            return new PyError("NameError", "'" + name + "' isn't defined" + (close.Count > 0 ? " (did you mean '" + close[0] + "'?)" : ""));
        }

        private static bool Close(string a, string b) {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            if (Math.Abs(a.Length - b.Length) > 1 || a.Length < 3) return false;
            // One letter different, missing or extra.
            int i = 0, j = 0, edits = 0;
            while (i < a.Length && j < b.Length) {
                if (a[i] == b[j]) {
                    i++;
                    j++;
                    continue;
                }
                if (++edits > 1) return false;
                if (a.Length > b.Length) i++;
                else if (b.Length > a.Length) j++;
                else {
                    i++;
                    j++;
                }
            }
            return edits + (a.Length - i) + (b.Length - j) <= 1;
        }

        private PyClass BuildClass(string name, PyClass[] bases, Dictionary<string, object> dict) {
            var cls = new PyClass(name, bases);
            foreach (var kv in dict) cls.dict[kv.Key] = kv.Value;
            return cls;
        }

        private static bool ExceptionMatches(PyInstance exc, object type) {
            if (type is PyTuple t) return t.items.Any(x => ExceptionMatches(exc, x));
            if (type is PyClass c) return exc.cls.IsSubclassOf(c);
            throw new PyError("TypeError", "'except' needs an exception class (like ValueError), not " + Ops.Describe(type));
        }

        // ------------------------------------------------------------------ values with user classes

        public bool Truthy(object o) {
            if (o is PyInstance pi) {
                if (pi.cls.Lookup("__bool__", out var b)) return Ops.Truthy(CallSync(new PyBoundMethod(pi, b)));
                if (pi.cls.Lookup("__len__", out var l)) return Ops.Truthy(CallSync(new PyBoundMethod(pi, l)));
            }
            return Ops.Truthy(o);
        }

        public string StrOf(object o) {
            if (o is PyInstance pi && (pi.cls.Lookup("__str__", out var s) || pi.cls.Lookup("__repr__", out s)))
                return CallSync(new PyBoundMethod(pi, s)) as string ?? throw new PyError("TypeError", "__str__ should return a string");
            if (o is PyInstance ex && ex.cls.mro.Any(c => c.name == "BaseException")) return Ops.ExceptionMessage(ex);
            if (o is PyList or PyTuple or PyDict) return ReprOf(o);
            // V2: a built-in object can give its own text (a Pose prints its x, y and heading).
            if (o is IPyObject po && (po.TryGetAttr("__str__", out var ps) || po.TryGetAttr("__repr__", out ps)) && CallSync(ps) is string text) return text;
            return Ops.Str(o);
        }

        public string ReprOf(object o) {
            if (o is PyInstance pi && pi.cls.Lookup("__repr__", out var r))
                return CallSync(new PyBoundMethod(pi, r)) as string ?? throw new PyError("TypeError", "__repr__ should return a string");
            if (o is IPyObject po && po.TryGetAttr("__repr__", out var pr) && CallSync(pr) is string text) return text;   // V2
            switch (o) {
                case PyList l: return "[" + string.Join(", ", l.items.Select(ReprOf)) + "]";
                case PyTuple t: return t.items.Length == 1 ? "(" + ReprOf(t.items[0]) + ",)" : "(" + string.Join(", ", t.items.Select(ReprOf)) + ")";
                case PyDict d: return "{" + string.Join(", ", d.Keys.Select(k => ReprOf(k) + ": " + ReprOf(d.items[k]))) + "}";
            }
            return Ops.Repr(o);
        }

        private static readonly Dictionary<string, string> Dunders = new() {
            ["+"] = "__add__", ["-"] = "__sub__", ["*"] = "__mul__", ["/"] = "__truediv__", ["//"] = "__floordiv__",
            ["%"] = "__mod__", ["**"] = "__pow__", ["@"] = "__matmul__", ["&"] = "__and__", ["|"] = "__or__", ["^"] = "__xor__",
            ["<<"] = "__lshift__", [">>"] = "__rshift__",
        };

        public object BinaryOp(string op, object a, object b) {
            if (a is PyInstance pa && Dunders.TryGetValue(op, out var name) && pa.cls.Lookup(name, out var m)) return CallSync(new PyBoundMethod(pa, m), b);
            if (b is PyInstance pb && Dunders.TryGetValue(op, out var rname) && pb.cls.Lookup("__r" + rname.Substring(2), out var rm)) return CallSync(new PyBoundMethod(pb, rm), a);
            return Ops.Binary(op, a, b);
        }

        private static readonly Dictionary<string, string> CmpDunders = new() {
            ["<"] = "__lt__", ["<="] = "__le__", [">"] = "__gt__", [">="] = "__ge__", ["=="] = "__eq__", ["!="] = "__ne__",
        };

        public object CompareOp(string op, object a, object b) {
            switch (op) {
                case "is": return Same(a, b);
                case "is not": return !Same(a, b);
                case "in": return ContainsOp(b, a);
                case "not in": return !ContainsOp(b, a);
            }
            if (a is PyInstance pa && CmpDunders.TryGetValue(op, out var name) && pa.cls.Lookup(name, out var m)) return CallSync(new PyBoundMethod(pa, m), b);
            switch (op) {
                case "==": return Ops.Eq(a, b);
                case "!=": return !Ops.Eq(a, b);
            }
            var c = Ops.CompareValues(a, b, op);
            return op switch {
                "<" => c < 0,
                "<=" => c <= 0,
                ">" => c > 0,
                _ => c >= 0,
            };
        }

        private bool ContainsOp(object container, object item) {
            if (container is PyInstance pi && pi.cls.Lookup("__contains__", out var m)) return Ops.Truthy(CallSync(new PyBoundMethod(pi, m), item));
            return Ops.Contains(container, item);
        }

        private static bool Same(object a, object b) {
            if (ReferenceEquals(a, b)) return true;
            if (a is bool ba && b is bool bb) return ba == bb;
            if (a is long la && b is long lb) return la == lb && la >= -5 && la <= 256;   // small ints are shared in Python
            if (a is string sa && b is string sb) return sa == sb;
            if (a is PyEnum ea && b is PyEnum eb) return Ops.Eq(ea, eb);
            return false;
        }

        // ------------------------------------------------------------------ attributes

        public object GetAttr(object o, string name) {
            switch (o) {
                case PyInstance pi:
                    if (pi.dict.TryGetValue(name, out var v)) return v;
                    if (pi.cls.Lookup(name, out v)) return v is PyFunction or PyBuiltin ? new PyBoundMethod(pi, v) : v;
                    if (name == "__class__") return pi.cls;
                    if (name == "__dict__") {
                        var d = new PyDict();
                        foreach (var kv in pi.dict) d.Set(kv.Key, kv.Value);
                        return d;
                    }
                    break;
                case PyClass c:
                    if (c.Lookup(name, out var cv)) return cv;
                    if (name == "__name__") return c.name;
                    break;
                case PySuper sup: {
                    var mro = sup.self.cls.mro;
                    for (var i = mro.IndexOf(sup.after) + 1; i < mro.Count; i++)
                        if (mro[i].dict.TryGetValue(name, out var sv))
                            return sv is PyFunction or PyBuiltin ? new PyBoundMethod(sup.self, sv) : sv;
                    if (name == "__init__") return new PyBuiltin("__init__", a => {
                        if (sup.self.cls.mro.Any(k => k.name == "BaseException")) sup.self.dict["args"] = new PyTuple(a.pos);
                        return PyNone.Value;
                    });
                    break;
                }
                case PyModule m:
                    if (m.globals.TryGetValue(name, out var mv)) return mv;
                    break;
                case IPyObject p:
                    if (p.TryGetAttr(name, out var pv)) return pv;
                    break;
                case PyEnum e:
                    if (name == "name") return e.name;
                    if (name == "value") return (long)e.value;
                    break;
                case PyFunction fn:
                    if (name == "__name__") return fn.Name;
                    break;
                case PyBuiltin bf:
                    if (name == "__name__") return bf.name;
                    break;
            }
            var method = Builtins.Method(this, o, name);
            if (method != null) return method;
            if (o is PyInstance pe && pe.cls.mro.Any(c => c.name == "BaseException") && name == "args") return new PyTuple(Ops.ExceptionArgs(pe));
            throw new PyError("AttributeError", Ops.Describe(o) + " has no attribute '" + name + "'" + AttrHint(o, name));
        }

        private string AttrHint(object o, string name) {
            IEnumerable<string> known = o switch {
                PyInstance pi => pi.dict.Keys.Concat(pi.cls.mro.SelectMany(c => c.dict.Keys)),
                PyModule m => m.globals.Keys,
                IPyObject p => (p as INamedAttrs)?.AttrNames ?? Enumerable.Empty<string>(),
                _ => Builtins.MethodNames(o),
            };
            var close = known.FirstOrDefault(k => Close(k, name));
            return close != null ? " (did you mean '" + close + "'?)" : "";
        }

        public void SetAttr(object o, string name, object value) {
            switch (o) {
                case PyInstance pi:
                    pi.dict[name] = value;
                    return;
                case PyClass c:
                    c.dict[name] = value;
                    return;
                case PyModule m:
                    m.globals[name] = value;
                    return;
                case IPyObject p when p.TrySetAttr(name, value):
                    return;
            }
            throw new PyError("AttributeError", "can't set '" + name + "' on " + Ops.Describe(o));
        }
    }

    /// <summary>A built-in object that can list its attribute names (for "did you mean" hints).</summary>
    public interface INamedAttrs {
        IEnumerable<string> AttrNames { get; }
    }
}
