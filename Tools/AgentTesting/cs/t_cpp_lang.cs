// C++ language test: translate, run without the robot, return the output.
var src = @"
#include <cmath>
#include <vector>
#define KP 0.5
#define LIMIT 10
using namespace std;

namespace util {
    int clampi(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
}

enum class Side { LEFT, RIGHT = 5, BOTH };
enum Mode { IDLE, RUN };

int counter = 0;
double total = 0;

struct Gains { double kP; double kI = 0; double kD; };

class Pid {
  public:
    Pid(double p, double d) : kP(p), kD(d) {}
    double update(double error) {
        double derivative = error - last;
        last = error;
        count++;
        return kP * error + kD * derivative;
    }
    int count = 0;
  private:
    double kP, kD;
    double last = 0;
};

class Base {
  public:
    Base(int x) : value(x) {}
    int value;
    virtual int get() { return value; }
};

class Derived : public Base {
  public:
    Derived(int x) : Base(x * 2) {}
    int get() override { return value + 1; }
};

void bump() { counter++; total += 1.5; }

int main() {
    printf(""int div %d %d mod %d %d\n"", 7 / 2, -7 / 2, 7 % 3, -7 % 3);
    double d = 7 / 2.0;
    printf(""double %.2f %5.1f|%-4d|%04d|%x|%s|%c\n"", d, 3.14159, 42, 7, 255, ""hi"", 'A');
    int n = 3.9;
    printf(""int from double %d\n"", n);
    int sum = 0;
    for (int i = 0; i < 10; i++) {
        if (i % 2 == 0) continue;
        sum += i;
    }
    printf(""odd sum %d\n"", sum);
    int k = 0;
    do { k += 3; } while (k < 10);
    printf(""do-while %d\n"", k);
    for (int i = 0; i < 4; i++) {
        switch (i) {
            case 0: printf(""zero ""); break;
            case 1:
            case 2: printf(""one-or-two ""); break;
            default: printf(""other""); break;
        }
    }
    printf(""\n"");
    Gains g{1.0, 0.0, 2.0};
    Gains g2 = {.kP = 3, .kD = 4};
    printf(""gains %.1f %.1f %.1f %.1f\n"", g.kP, g.kD, g2.kP, g2.kD);
    Pid pid(KP, 0.1);
    double out = pid.update(10);
    out = pid.update(8);
    printf(""pid %.2f count %d\n"", out, pid.count);
    Derived dv(5);
    printf(""derived %d %d\n"", dv.value, dv.get());
    std::vector<double> v = {1.5, 2.5};
    v.push_back(3.0);
    double vs = 0;
    for (auto x : v) vs += x;
    printf(""vector size %d sum %.1f back %.1f empty %d\n"", (int)v.size(), vs, v.back(), v.empty());
    auto lambdaCount = 0;
    auto add = [&](int a) { lambdaCount += a; };
    add(2); add(5);
    printf(""lambda %d\n"", lambdaCount);
    bump(); bump();
    printf(""globals %d %.1f\n"", counter, total);
    printf(""enum %d %d %d\n"", (int)Side::RIGHT, (int)Side::BOTH, RUN);
    printf(""ns %d %d\n"", util::clampi(15, 0, LIMIT), util::clampi(-2, 0, LIMIT));
    cout << ""cout "" << 1.0 / 3 << "" "" << 42 << "" "" << true << endl;
    std::string s = ""val: "" + std::to_string(5);
    printf(""%s len %d\n"", s.c_str(), (int)s.length());
    double h = sqrt(16) + fabs(-2.5) + std::abs(-3);
    printf(""math %.1f %.4f\n"", h, M_PI);
    int x = 5;
    x /= 2;
    x *= 3;
    printf(""compound %d ternary %s\n"", x, x > 5 ? ""big"" : ""small"");
    return 0;
}
";
var output = new System.Text.StringBuilder();
try {
    System.Collections.Generic.List<string> warnings;
    var body = Protobot.Scripting.CppTranslator.Translate(src, out warnings);
    var code = Protobot.Scripting.Compiler.CompileModule(body);
    var vm = new Protobot.Scripting.Interpreter();
    vm.Output = s => output.Append(s);
    Protobot.Scripting.CppRuntime.Install(vm);
    vm.Load(code);
    for (var i = 0; i < 2000 && vm.Running; i++) { vm.Time += 0.01; vm.Step(); }
    if (vm.Failed != null) output.Append("\nFAILED line " + vm.Failed.line + ": " + vm.Failed.Message);
    foreach (var w in warnings) output.Append("\nwarning: " + w);
}
catch (Protobot.Scripting.SyntaxErr e) { output.Append("SYNTAX line " + e.line + ":" + e.column + ": " + e.Message); }
catch (System.Exception e) { output.Append("EXCEPTION " + e); }
return output.ToString();
