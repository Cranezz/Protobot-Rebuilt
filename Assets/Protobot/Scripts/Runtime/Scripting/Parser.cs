using System.Collections.Generic;

namespace Protobot.Scripting {
    /// <summary>V2 (robot code): Python tokens -> statements (a recursive-descent parser).</summary>
    public sealed class Parser {
        private readonly List<Token> t;
        private int p;

        private static readonly HashSet<string> Keywords = new() {
            "False", "None", "True", "and", "as", "assert", "break", "class", "continue", "def", "del", "elif", "else",
            "except", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or",
            "pass", "raise", "return", "try", "while", "with", "yield", "async", "await",
        };

        private static readonly HashSet<string> AugOps = new() { "+=", "-=", "*=", "/=", "//=", "%=", "**=", "&=", "|=", "^=", ">>=", "<<=" };

        public Parser(List<Token> tokens) => t = tokens;

        public static List<Stmt> Parse(string source) => new Parser(Lexer.Tokenize(source)).Module();

        // ------------------------------------------------------------------ helpers

        private Token Peek => t[p];
        private Token PeekAt(int k) => t[System.Math.Min(p + k, t.Count - 1)];
        private Token Next() => t[p++];

        private bool IsOp(string op) => Peek.kind == Tok.Op && Peek.text == op;
        private bool IsKw(string kw) => Peek.kind == Tok.Name && Peek.text == kw;

        private bool AcceptOp(string op) {
            if (!IsOp(op)) return false;
            p++;
            return true;
        }

        private bool AcceptKw(string kw) {
            if (!IsKw(kw)) return false;
            p++;
            return true;
        }

        private SyntaxErr Error(string message, Token? at = null) {
            var tok = at ?? Peek;
            return new SyntaxErr(message, tok.line, tok.column);
        }

        private static string Describe(Token tok) => tok.kind switch {
            Tok.Newline => "the end of the line",
            Tok.End => "the end of the program",
            Tok.Indent => "an indented line",
            Tok.Dedent => "the end of the block",
            Tok.Str or Tok.FStr => "a string",
            Tok.Int or Tok.Float => "the number " + tok.text,
            _ => "'" + tok.text + "'",
        };

        private void ExpectOp(string op, string why = null) {
            if (AcceptOp(op)) return;
            throw Error((why ?? "expected '" + op + "'") + " but found " + Describe(Peek));
        }

        private void ExpectKw(string kw) {
            if (AcceptKw(kw)) return;
            throw Error("expected '" + kw + "' but found " + Describe(Peek));
        }

        private string ExpectName(string what = "a name") {
            if (Peek.kind == Tok.Name && !Keywords.Contains(Peek.text)) return Next().text;
            if (Peek.kind == Tok.Name) throw Error("'" + Peek.text + "' is a Python keyword, so it can't be used as " + what);
            throw Error("expected " + what + " but found " + Describe(Peek));
        }

        private T At<T>(T node, Token tok) where T : Node {
            node.line = tok.line;
            node.column = tok.column;
            return node;
        }

        // ------------------------------------------------------------------ statements

        public List<Stmt> Module() {
            var body = new List<Stmt>();
            while (Peek.kind != Tok.End) {
                if (Peek.kind == Tok.Newline) {
                    p++;
                    continue;
                }
                if (Peek.kind == Tok.Indent) throw Error("this line is indented, but nothing above it starts a block (like 'if ...:' or 'def ...:')");
                Statement(body);
            }
            return body;
        }

        private List<Stmt> Block() {
            var colon = Peek;
            ExpectOp(":", "expected ':' at the end of this line");
            var body = new List<Stmt>();
            if (Peek.kind != Tok.Newline) {   // if x: y = 1
                SimpleStatements(body);
                return body;
            }
            p++;
            if (Peek.kind != Tok.Indent) throw Error("expected an indented block after line " + colon.line);
            p++;
            while (Peek.kind != Tok.Dedent && Peek.kind != Tok.End) {
                if (Peek.kind == Tok.Newline) {
                    p++;
                    continue;
                }
                if (Peek.kind == Tok.Indent) throw Error("unexpected indentation");
                Statement(body);
            }
            if (Peek.kind == Tok.Dedent) p++;
            return body;
        }

        private void Statement(List<Stmt> into) {
            var tok = Peek;
            if (tok.kind == Tok.Op && tok.text == "@") {
                var decorators = new List<Expr>();
                while (AcceptOp("@")) {
                    decorators.Add(Test());
                    if (Peek.kind != Tok.Newline) throw Error("expected the end of the line after a decorator");
                    p++;
                }
                if (!IsKw("def") && !IsKw("class")) throw Error("a decorator goes just above a 'def' or 'class'");
                var start = into.Count;
                Statement(into);
                if (into[start] is FuncDef fd) fd.decorators = decorators;
                return;
            }
            if (tok.kind == Tok.Name)
                switch (tok.text) {
                    case "if": into.Add(If()); return;
                    case "while": {
                        p++;
                        var s = At(new WhileStmt { test = NamedTest() }, tok);
                        s.body = Block();
                        if (AcceptKw("else")) s.orElse = Block();
                        into.Add(s);
                        return;
                    }
                    case "for": {
                        p++;
                        var s = At(new ForStmt(), tok);
                        s.target = TargetList();
                        ExpectKw("in");
                        s.iter = TestList();
                        s.body = Block();
                        if (AcceptKw("else")) s.orElse = Block();
                        into.Add(s);
                        return;
                    }
                    case "def": into.Add(Def()); return;
                    case "class": into.Add(Class()); return;
                    case "try": into.Add(Try()); return;
                    case "with": {
                        p++;
                        var s = At(new WithStmt { context = Test() }, tok);
                        if (AcceptKw("as")) s.target = Target();
                        s.body = Block();
                        into.Add(s);
                        return;
                    }
                    case "elif":
                    case "else":
                    case "except":
                    case "finally":
                        throw Error("'" + tok.text + "' without the statement it belongs to (check the indentation above)");
                }
            SimpleStatements(into);
        }

        private void SimpleStatements(List<Stmt> into) {
            while (true) {
                into.Add(Simple());
                if (AcceptOp(";")) {
                    if (Peek.kind == Tok.Newline) break;
                    continue;
                }
                break;
            }
            if (Peek.kind == Tok.End) return;
            if (Peek.kind != Tok.Newline) throw Error("expected the end of the line but found " + Describe(Peek) + (Peek.kind == Tok.Name || Peek.kind == Tok.Op ? " (a missing operator or comma?)" : ""));
            p++;
        }

        private Stmt If() {
            var tok = Next();   // if / elif
            var s = At(new IfStmt { test = NamedTest() }, tok);
            s.body = Block();
            if (IsKw("elif")) s.orElse = new List<Stmt> { If() };
            else if (AcceptKw("else")) s.orElse = Block();
            return s;
        }

        private Stmt Def() {
            var tok = Next();
            var d = At(new FuncDef { name = ExpectName("a function name") }, tok);
            ExpectOp("(", "expected '(' after the function's name");
            Parameters(d, ")");
            ExpectOp(")");
            if (AcceptOp("->")) Test();   // return annotation: ignored
            d.body = Block();
            return d;
        }

        private void Parameters(FuncDef d, string end) {
            var seenDefault = false;
            while (!IsOp(end)) {
                if (AcceptOp("**")) d.starStar = ExpectName("a parameter name");
                else if (AcceptOp("*")) {
                    if (IsOp(",")) { }   // keyword-only marker: treat what follows as ordinary
                    else d.star = ExpectName("a parameter name");
                }
                else {
                    var nameTok = Peek;
                    var name = ExpectName("a parameter name");
                    if (d.parameters.Contains(name)) throw Error("the parameter '" + name + "' is listed twice", nameTok);
                    if (!d.isLambda && AcceptOp(":")) Test();   // annotation: ignored
                    d.parameters.Add(name);
                    if (AcceptOp("=")) {
                        d.defaults.Add(Test());
                        seenDefault = true;
                    }
                    else if (seenDefault && d.star == null) throw Error("a parameter without a default can't come after one with a default", nameTok);
                }
                if (!AcceptOp(",")) break;
            }
        }

        private Stmt Class() {
            var tok = Next();
            var c = At(new ClassDef { name = ExpectName("a class name") }, tok);
            if (AcceptOp("(")) {
                while (!IsOp(")")) {
                    c.bases.Add(Test());
                    if (!AcceptOp(",")) break;
                }
                ExpectOp(")");
            }
            c.body = Block();
            return c;
        }

        private Stmt Try() {
            var tok = Next();
            var s = At(new TryStmt { body = Block() }, tok);
            while (IsKw("except")) {
                var et = Next();
                Expr type = null;
                string name = null;
                if (!IsOp(":")) {
                    type = Test();
                    if (AcceptKw("as")) name = ExpectName();
                    else if (AcceptOp(",")) name = ExpectName();
                }
                s.handlers.Add((type, name, Block(), et.line));
            }
            if (AcceptKw("else")) s.orElse = Block();
            if (AcceptKw("finally")) s.final = Block();
            if (s.handlers.Count == 0 && s.final == null) throw Error("'try' needs an 'except' or 'finally' block");
            return s;
        }

        private Stmt Simple() {
            var tok = Peek;
            if (tok.kind == Tok.Name)
                switch (tok.text) {
                    case "pass": p++; return At(new PassStmt(), tok);
                    case "break": p++; return At(new BreakStmt(), tok);
                    case "continue": p++; return At(new ContinueStmt(), tok);
                    case "return": {
                        p++;
                        var r = At(new ReturnStmt(), tok);
                        if (Peek.kind != Tok.Newline && !IsOp(";") && Peek.kind != Tok.End) r.value = TestList();
                        return r;
                    }
                    case "global":
                    case "nonlocal": {
                        p++;
                        var g = At(new GlobalStmt { nonlocal = tok.text == "nonlocal" }, tok);
                        do g.names.Add(ExpectName()); while (AcceptOp(","));
                        return g;
                    }
                    case "del": {
                        p++;
                        var d = At(new DelStmt(), tok);
                        do d.targets.Add(Target()); while (AcceptOp(","));
                        return d;
                    }
                    case "assert": {
                        p++;
                        var a = At(new AssertStmt { test = Test() }, tok);
                        if (AcceptOp(",")) a.message = Test();
                        return a;
                    }
                    case "raise": {
                        p++;
                        var r = At(new RaiseStmt(), tok);
                        if (Peek.kind != Tok.Newline && Peek.kind != Tok.End) r.exc = Test();
                        if (AcceptKw("from")) Test();
                        return r;
                    }
                    case "import": {
                        p++;
                        var name = DottedName();
                        var s = At(new ImportStmt { module = name }, tok);
                        if (AcceptKw("as")) s.alias = ExpectName();
                        if (IsOp(",")) throw Error("import one module per line here");
                        return s;
                    }
                    case "from": {
                        p++;
                        var name = DottedName();
                        ExpectKw("import");
                        var s = At(new ImportStmt { module = name, names = new List<(string, string)>() }, tok);
                        if (AcceptOp("*")) s.star = true;
                        else {
                            var paren = AcceptOp("(");
                            do {
                                if (paren && IsOp(")")) break;
                                var n = ExpectName();
                                string alias = null;
                                if (AcceptKw("as")) alias = ExpectName();
                                s.names.Add((n, alias));
                            } while (AcceptOp(","));
                            if (paren) ExpectOp(")");
                        }
                        return s;
                    }
                    case "yield":
                    case "async":
                    case "await":
                        throw Error("'" + tok.text + "' isn't supported in robot code (use Thread(...) for things that run at the same time)");
                }
            // An expression, an assignment or an augmented assignment.
            var first = TestListStar();
            if (Peek.kind == Tok.Op && AugOps.Contains(Peek.text)) {
                var op = Next().text;
                CheckTarget(first, true);
                return At(new AugAssignStmt { target = first, op = op.Substring(0, op.Length - 1), value = TestList() }, tok);
            }
            if (IsOp(":") && first is NameExpr or AttrExpr or IndexExpr) {   // x: int = 5 (annotation)
                p++;
                Test();
                if (!AcceptOp("=")) return At(new PassStmt(), tok);
                CheckTarget(first, false);
                return At(new AssignStmt { targets = { first }, value = TestList() }, tok);
            }
            if (IsOp("=")) {
                var a = At(new AssignStmt(), tok);
                var value = first;
                while (AcceptOp("=")) {
                    CheckTarget(value, false);
                    a.targets.Add(value);
                    value = TestListStar();
                }
                a.value = value;
                return a;
            }
            return At(new ExprStmt { expr = first }, tok);
        }

        private string DottedName() {
            var name = ExpectName("a module name");
            while (AcceptOp(".")) name += "." + ExpectName("a module name");
            return name;
        }

        private void CheckTarget(Expr e, bool single) {
            switch (e) {
                case NameExpr:
                case AttrExpr:
                case IndexExpr:
                    return;
                case TupleExpr te when !single:
                    foreach (var i in te.items) CheckTarget(i, false);
                    return;
                case ListExpr le when !single:
                    foreach (var i in le.items) CheckTarget(i, false);
                    return;
                case StarExpr se when !single:
                    CheckTarget(se.value, true);
                    return;
                case CallExpr:
                    throw new SyntaxErr("can't assign to a function call (did you mean '==' to compare?)", e.line, e.column);
                case ConstExpr:
                    throw new SyntaxErr("can't assign to a value (did you mean '==' to compare?)", e.line, e.column);
                default:
                    throw new SyntaxErr("can't assign to this (did you mean '==' to compare?)", e.line, e.column);
            }
        }

        /// <summary>A for loop's target: names, attributes or items, with commas (a, b).</summary>
        private Expr TargetList() {
            var tok = Peek;
            var first = Target();
            if (!IsOp(",")) return first;
            var tuple = At(new TupleExpr { items = { first } }, tok);
            while (AcceptOp(",")) {
                if (IsKw("in")) break;
                tuple.items.Add(Target());
            }
            return tuple;
        }

        private Expr Target() {
            var e = BitOr();
            CheckTarget(e, false);
            return e;
        }

        // ------------------------------------------------------------------ expressions

        private Expr TestList() {
            var tok = Peek;
            var first = Test();
            if (!IsOp(",")) return first;
            var tuple = At(new TupleExpr { items = { first } }, tok);
            while (AcceptOp(",")) {
                if (EndsList()) break;
                tuple.items.Add(Test());
            }
            return tuple;
        }

        private Expr TestListStar() {
            var tok = Peek;
            var first = TestOrStar();
            if (!IsOp(",")) return first;
            var tuple = At(new TupleExpr { items = { first } }, tok);
            while (AcceptOp(",")) {
                if (EndsList() || IsOp("=")) break;
                tuple.items.Add(TestOrStar());
            }
            return tuple;
        }

        private bool EndsList() => Peek.kind == Tok.Newline || Peek.kind == Tok.End || IsOp(")") || IsOp("]") || IsOp("}") || IsOp(";") || IsOp(":");

        private Expr TestOrStar() {
            var tok = Peek;
            if (AcceptOp("*")) return At(new StarExpr { value = BitOr() }, tok);
            return Test();
        }

        /// <summary>A condition that may be a walrus (x := ...): parsed as an ordinary test here.</summary>
        private Expr NamedTest() {
            var e = Test();
            if (IsOp(":=")) throw Error("':=' isn't supported in robot code: assign on the line before");
            return e;
        }

        public Expr Test() {
            var tok = Peek;
            if (IsKw("lambda")) return Lambda();
            var e = OrTest();
            if (IsKw("if") ) {
                p++;
                var test = OrTest();
                if (!AcceptKw("else")) throw Error("expected 'else' in 'x if condition else y'");
                return At(new IfExpr { body = e, test = test, orElse = Test() }, tok);
            }
            return e;
        }

        private Expr Lambda() {
            var tok = Next();
            var d = At(new FuncDef { name = "<lambda>", isLambda = true }, tok);
            Parameters(d, ":");
            ExpectOp(":");
            var body = Test();
            d.body = new List<Stmt> { At(new ReturnStmt { value = body }, tok) };
            return At(new LambdaExpr { def = d }, tok);
        }

        private Expr OrTest() {
            var e = AndTest();
            while (IsKw("or")) {
                var tok = Next();
                e = At(new BoolExpr { isAnd = false, left = e, right = AndTest() }, tok);
            }
            return e;
        }

        private Expr AndTest() {
            var e = NotTest();
            while (IsKw("and")) {
                var tok = Next();
                e = At(new BoolExpr { isAnd = true, left = e, right = NotTest() }, tok);
            }
            return e;
        }

        private Expr NotTest() {
            var tok = Peek;
            if (AcceptKw("not")) return At(new UnaryExpr { op = "not", operand = NotTest() }, tok);
            return Comparison();
        }

        private Expr Comparison() {
            var tok = Peek;
            var first = BitOr();
            CompareExpr c = null;
            while (true) {
                string op = null;
                if (Peek.kind == Tok.Op && (Peek.text is "<" or ">" or "==" or ">=" or "<=" or "!=")) op = Next().text;
                else if (IsKw("in")) {
                    p++;
                    op = "in";
                }
                else if (IsKw("not") && PeekAt(1).kind == Tok.Name && PeekAt(1).text == "in") {
                    p += 2;
                    op = "not in";
                }
                else if (IsKw("is")) {
                    p++;
                    op = AcceptKw("not") ? "is not" : "is";
                }
                if (op == null) break;
                c ??= At(new CompareExpr { first = first }, tok);
                c.ops.Add(op);
                c.rest.Add(BitOr());
            }
            return (Expr)c ?? first;
        }

        private Expr BitOr() {
            var e = BitXor();
            while (IsOp("|")) {
                var tok = Next();
                e = At(new BinExpr { op = "|", left = e, right = BitXor() }, tok);
            }
            return e;
        }

        private Expr BitXor() {
            var e = BitAnd();
            while (IsOp("^")) {
                var tok = Next();
                e = At(new BinExpr { op = "^", left = e, right = BitAnd() }, tok);
            }
            return e;
        }

        private Expr BitAnd() {
            var e = Shift();
            while (IsOp("&")) {
                var tok = Next();
                e = At(new BinExpr { op = "&", left = e, right = Shift() }, tok);
            }
            return e;
        }

        private Expr Shift() {
            var e = Arith();
            while (IsOp("<<") || IsOp(">>")) {
                var tok = Next();
                e = At(new BinExpr { op = tok.text, left = e, right = Arith() }, tok);
            }
            return e;
        }

        private Expr Arith() {
            var e = Term();
            while (IsOp("+") || IsOp("-")) {
                var tok = Next();
                e = At(new BinExpr { op = tok.text, left = e, right = Term() }, tok);
            }
            return e;
        }

        private Expr Term() {
            var e = Factor();
            while (IsOp("*") || IsOp("/") || IsOp("//") || IsOp("%") || IsOp("@")) {
                var tok = Next();
                e = At(new BinExpr { op = tok.text, left = e, right = Factor() }, tok);
            }
            return e;
        }

        private Expr Factor() {
            var tok = Peek;
            if (IsOp("-") || IsOp("+") || IsOp("~")) {
                p++;
                var operand = Factor();
                if (tok.text == "-" && operand is ConstExpr ce && ce.value is long l) return At(new ConstExpr { value = -l }, tok);
                if (tok.text == "-" && operand is ConstExpr cd && cd.value is double d) return At(new ConstExpr { value = -d }, tok);
                return At(new UnaryExpr { op = tok.text, operand = operand }, tok);
            }
            return Power();
        }

        private Expr Power() {
            var e = AtomExpr();
            if (IsOp("**")) {
                var tok = Next();
                return At(new BinExpr { op = "**", left = e, right = Factor() }, tok);
            }
            return e;
        }

        private Expr AtomExpr() {
            var e = Atom();
            while (true) {
                var tok = Peek;
                if (AcceptOp("(")) e = Call(e, tok);
                else if (AcceptOp("[")) {
                    var index = Subscript();
                    ExpectOp("]", "expected ']'");
                    e = At(new IndexExpr { target = e, index = index }, tok);
                }
                else if (AcceptOp(".")) e = At(new AttrExpr { target = e, name = ExpectName("a name after '.'") }, tok);
                else return e;
            }
        }

        private Expr Call(Expr func, Token tok) {
            var call = At(new CallExpr { func = func }, tok);
            while (!IsOp(")")) {
                if (Peek.kind == Tok.End || Peek.kind == Tok.Newline) throw Error("a '(' without its ')'", tok);
                if (AcceptOp("**")) call.starKw = Test();
                else if (AcceptOp("*")) call.starArgs = Test();
                else if (Peek.kind == Tok.Name && PeekAt(1).kind == Tok.Op && PeekAt(1).text == "=") {
                    var name = Next().text;
                    p++;
                    if (call.kwNames.Contains(name)) throw Error("the keyword argument '" + name + "' is given twice");
                    call.kwNames.Add(name);
                    call.kwValues.Add(Test());
                }
                else {
                    if (call.kwNames.Count > 0) throw Error("a plain argument can't come after a keyword argument (name=value)");
                    var arg = Test();
                    if (IsKw("for")) arg = Comprehension("gen", arg, null, Peek);
                    call.args.Add(arg);
                }
                if (!AcceptOp(",")) break;
            }
            if (!IsOp(")") && Peek.line > tok.line) throw Error("the '(' on line " + tok.line + " is never closed (a missing ')')", tok);
            ExpectOp(")", "expected ')' or ','");
            return call;
        }

        private Expr Subscript() {
            var tok = Peek;
            Expr first = null;
            if (!IsOp(":")) {
                first = Test();
                if (!IsOp(":")) {
                    if (!IsOp(",")) return first;
                    var tuple = At(new TupleExpr { items = { first } }, tok);
                    while (AcceptOp(",")) {
                        if (IsOp("]")) break;
                        tuple.items.Add(Test());
                    }
                    return tuple;
                }
            }
            var s = At(new SliceExpr { start = first }, tok);
            ExpectOp(":");
            if (!IsOp(":") && !IsOp("]")) s.stop = Test();
            if (AcceptOp(":") && !IsOp("]")) s.step = Test();
            return s;
        }

        private Expr Atom() {
            var tok = Peek;
            switch (tok.kind) {
                case Tok.Int:
                case Tok.Float:
                    p++;
                    return At(new ConstExpr { value = tok.value }, tok);
                case Tok.Str: {
                    p++;
                    return At(new ConstExpr { value = tok.text }, tok);
                }
                case Tok.FStr: {
                    p++;
                    var f = At(new FStringExpr(), tok);
                    foreach (var part in (List<object>)tok.value) {
                        if (part is string s) f.parts.Add(s);
                        else if (part is FPart fp) f.parts.Add(new FValue { expr = SubExpression(fp.expr, tok), conv = fp.conv, spec = fp.spec });
                    }
                    // "a" f"b" side by side
                    while (Peek.kind == Tok.Str || Peek.kind == Tok.FStr) {
                        var more = Next();
                        if (more.kind == Tok.Str) f.parts.Add(more.text);
                        else
                            foreach (var part in (List<object>)more.value)
                                f.parts.Add(part is FPart fp ? new FValue { expr = SubExpression(fp.expr, more), conv = fp.conv, spec = fp.spec } : part);
                    }
                    return f;
                }
                case Tok.Name:
                    switch (tok.text) {
                        case "None": p++; return At(new ConstExpr { value = PyNone.Value }, tok);
                        case "True": p++; return At(new ConstExpr { value = true }, tok);
                        case "False": p++; return At(new ConstExpr { value = false }, tok);
                    }
                    if (Keywords.Contains(tok.text)) throw Error("'" + tok.text + "' can't be used here");
                    p++;
                    return At(new NameExpr { name = tok.text }, tok);
                case Tok.Op:
                    switch (tok.text) {
                        case "(": {
                            p++;
                            if (AcceptOp(")")) return At(new TupleExpr(), tok);
                            var first = TestOrStar();
                            if (IsKw("for")) {
                                var g = Comprehension("gen", first, null, tok);
                                ExpectOp(")", "expected ')'");
                                return g;
                            }
                            if (AcceptOp(")")) return first;
                            var tuple = At(new TupleExpr { items = { first } }, tok);
                            while (AcceptOp(",")) {
                                if (IsOp(")")) break;
                                tuple.items.Add(TestOrStar());
                            }
                            ExpectOp(")", "expected ')' or ','");
                            return tuple;
                        }
                        case "[": {
                            p++;
                            var list = At(new ListExpr(), tok);
                            if (AcceptOp("]")) return list;
                            var first = TestOrStar();
                            if (IsKw("for")) {
                                var c = Comprehension("list", first, null, tok);
                                ExpectOp("]", "expected ']'");
                                return c;
                            }
                            list.items.Add(first);
                            while (AcceptOp(",")) {
                                if (IsOp("]")) break;
                                list.items.Add(TestOrStar());
                            }
                            ExpectOp("]", "expected ']' or ','");
                            return list;
                        }
                        case "{": {
                            p++;
                            var dict = At(new DictExpr(), tok);
                            if (AcceptOp("}")) return dict;
                            var key = Test();
                            if (!AcceptOp(":")) {   // a set: kept as a list of unique items
                                if (IsKw("for")) {
                                    var c = Comprehension("set", key, null, tok);
                                    ExpectOp("}");
                                    return c;
                                }
                                var set = new CallExpr { func = At(new NameExpr { name = "set" }, tok), line = tok.line, column = tok.column };
                                var items = At(new ListExpr { items = { key } }, tok);
                                while (AcceptOp(",")) {
                                    if (IsOp("}")) break;
                                    items.items.Add(Test());
                                }
                                ExpectOp("}");
                                set.args.Add(items);
                                return set;
                            }
                            var value = Test();
                            if (IsKw("for")) {
                                var c = Comprehension("dict", key, value, tok);
                                ExpectOp("}", "expected '}'");
                                return c;
                            }
                            dict.keys.Add(key);
                            dict.values.Add(value);
                            while (AcceptOp(",")) {
                                if (IsOp("}")) break;
                                dict.keys.Add(Test());
                                ExpectOp(":", "expected ':' between a key and its value");
                                dict.values.Add(Test());
                            }
                            ExpectOp("}", "expected '}' or ','");
                            return dict;
                        }
                        case "...":
                            p++;
                            return At(new ConstExpr { value = PyNone.Value }, tok);
                    }
                    break;
            }
            if (tok.kind == Tok.Newline || tok.kind == Tok.End) throw Error("this line ends before it's finished");
            if (tok.kind == Tok.Indent) throw Error("unexpected indentation");
            throw Error("didn't expect " + Describe(tok) + " here");
        }

        private Expr Comprehension(string kind, Expr element, Expr value, Token tok) {
            var c = At(new CompExpr { kind = kind, element = element, value = value }, tok);
            while (AcceptKw("for")) {
                var target = TargetList();
                ExpectKw("in");
                var iter = OrTest();
                var ifs = new List<Expr>();
                while (IsKw("if")) {
                    p++;
                    ifs.Add(OrTestNoCond());
                }
                c.loops.Add((target, iter, ifs));
            }
            return c;
        }

        private Expr OrTestNoCond() => OrTest();

        /// <summary>An f-string's {expression}: parsed on its own, placed at the string.</summary>
        private static Expr SubExpression(string text, Token at) {
            try {
                var tokens = Lexer.Tokenize("(" + text + ")");
                var parser = new Parser(tokens);
                var e = parser.Test();
                return e;
            }
            catch (SyntaxErr e) {
                throw new SyntaxErr("in an f-string's {" + text + "}: " + e.Message, at.line, at.column);
            }
        }
    }
}
