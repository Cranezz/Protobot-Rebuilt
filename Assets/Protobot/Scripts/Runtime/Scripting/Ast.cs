using System.Collections.Generic;

namespace Protobot.Scripting {
    // V2 (robot code): the parsed program. Python's grammar, the parts robot code uses.

    public abstract class Node {
        public int line, column;
    }

    // ------------------------------------------------------------------ expressions

    public abstract class Expr : Node { }

    public sealed class ConstExpr : Expr { public object value; }
    public sealed class NameExpr : Expr { public string name; }
    public sealed class AttrExpr : Expr { public Expr target; public string name; }
    public sealed class IndexExpr : Expr { public Expr target, index; }
    public sealed class SliceExpr : Expr { public Expr start, stop, step; }
    public sealed class BinExpr : Expr { public string op; public Expr left, right; }
    public sealed class UnaryExpr : Expr { public string op; public Expr operand; }
    public sealed class BoolExpr : Expr { public bool isAnd; public Expr left, right; }
    public sealed class CompareExpr : Expr { public Expr first; public List<string> ops = new(); public List<Expr> rest = new(); }
    public sealed class IfExpr : Expr { public Expr test, body, orElse; }
    public sealed class ListExpr : Expr { public List<Expr> items = new(); }
    public sealed class TupleExpr : Expr { public List<Expr> items = new(); }
    public sealed class DictExpr : Expr { public List<Expr> keys = new(), values = new(); }
    public sealed class StarExpr : Expr { public Expr value; }
    public sealed class FStringExpr : Expr { public List<object> parts = new(); }   // string, or (Expr expr, string conv, string spec)
    public sealed class FValue { public Expr expr; public string conv, spec; }
    public sealed class LambdaExpr : Expr { public FuncDef def; }

    public sealed class CallExpr : Expr {
        public Expr func;
        public List<Expr> args = new();
        public List<string> kwNames = new();
        public List<Expr> kwValues = new();
        public Expr starArgs, starKw;
    }

    /// <summary>[elt for x in it if cond ...], {k: v for ...}.</summary>
    public sealed class CompExpr : Expr {
        public string kind;   // "list", "dict", "set", "gen"
        public Expr element, value;
        public List<(Expr target, Expr iter, List<Expr> ifs)> loops = new();
    }

    // ------------------------------------------------------------------ statements

    public abstract class Stmt : Node { }

    public sealed class ExprStmt : Stmt { public Expr expr; }
    public sealed class AssignStmt : Stmt { public List<Expr> targets = new(); public Expr value; }
    public sealed class AugAssignStmt : Stmt { public Expr target; public string op; public Expr value; }
    public sealed class IfStmt : Stmt { public Expr test; public List<Stmt> body, orElse; }
    public sealed class WhileStmt : Stmt { public Expr test; public List<Stmt> body, orElse; }
    public sealed class ForStmt : Stmt { public Expr target, iter; public List<Stmt> body, orElse; }
    public sealed class BreakStmt : Stmt { }
    public sealed class ContinueStmt : Stmt { }
    public sealed class PassStmt : Stmt { }
    public sealed class ReturnStmt : Stmt { public Expr value; }
    public sealed class GlobalStmt : Stmt { public List<string> names = new(); public bool nonlocal; }
    public sealed class DelStmt : Stmt { public List<Expr> targets = new(); }
    public sealed class AssertStmt : Stmt { public Expr test, message; }
    public sealed class RaiseStmt : Stmt { public Expr exc; }
    public sealed class ImportStmt : Stmt { public string module; public string alias; public List<(string name, string alias)> names; public bool star; }

    public sealed class TryStmt : Stmt {
        public List<Stmt> body;
        public List<(Expr type, string name, List<Stmt> body, int line)> handlers = new();
        public List<Stmt> orElse, final;
    }

    public sealed class FuncDef : Stmt {
        public string name;
        public List<string> parameters = new();
        public List<Expr> defaults = new();   // for the last parameters
        public string star, starStar;
        public List<Stmt> body;
        public List<Expr> decorators = new();
        public bool isLambda;
    }

    public sealed class ClassDef : Stmt {
        public string name;
        public List<Expr> bases = new();
        public List<Stmt> body;
    }

    public sealed class WithStmt : Stmt { public Expr context; public Expr target; public List<Stmt> body; }
}
