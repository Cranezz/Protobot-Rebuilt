using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Protobot.Devices;
using Protobot.Motion;
using Protobot.Scripting;
using UnityEngine;

namespace Protobot.RobotCode {
    /// <summary>
    /// V2 (Phase 9, robot code): the robot's program running in the simulation. Its threads step every physics step
    /// (before the gear trains turn, Sandbox.FixedUpdate), its time is the simulation's, and the motors it commands
    /// are run by a V5 motor's own controller (velocity, position, hold) instead of the keys.
    /// </summary>
    public sealed class RobotProgram {
        public enum Period { Program, Autonomous, Driver }

        public static RobotProgram Current { get; private set; }
        public static bool Active => Current != null && !Current.ended;

        /// <summary>Text the program printed (print(), brain.screen.print); the code panel shows it.</summary>
        public static event Action<string> Printed;
        /// <summary>Started, stopped, failed or finished.</summary>
        public static event Action StateChanged;

        public readonly Interpreter vm;
        public readonly Period period;
        public readonly double limit;   // seconds the period lasts (0: no limit)
        public double Time { get; private set; }
        public string Status { get; private set; } = "Running";
        public int ErrorLine { get; private set; }
        public bool Failed { get; private set; }
        private bool ended;

        internal readonly VexBrain Brain;
        /// <summary>V2: a C++ program (screen prints take printf formats).</summary>
        internal bool Cpp { get; private set; }
        private VexController primary, partner;
        private object driverFn, autonFn;
        private bool competitionStarted;
        private double periodStart = -1;
        private Fiber mainFiber;
        internal readonly List<Action> pollers = new();        // events: each step, after the threads
        private readonly Dictionary<MotorDrive, MotorControl> motors = new();
        private readonly HashSet<string> warned = new();
        /// <summary>V2: the program as written (a project: all its files) and its files that aren't code (path files).</summary>
        internal string source = "";
        internal Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);

        // V2 (the code window's Monitor): what monitor_variable / monitor_sensor asked for, and the names the
        // interpreter had before the program ran (not the program's own variables).
        internal readonly List<string> monitoredVariables = new();
        internal readonly List<object> monitoredSensors = new();
        internal HashSet<string> systemNames = new();
        /// <summary>V2: the program's threads still running (the main program first).</summary>
        public IEnumerable<Fiber> Threads => vm.fibers.Where(f => !f.Done && !f.Sync);
        public Fiber MainThread => mainFiber;

        private RobotProgram(Period period, double limit) {
            this.period = period;
            this.limit = limit;
            vm = new Interpreter { Output = Print };
            Brain = new VexBrain(this);
            vm.modules["vex"] = VexModule.Build(this);
            // PROS-style and VEXcode-style names some programs use for the same module.
            vm.modules["vexcode"] = vm.modules["vex"];
            vm.modules["motion"] = MotionModule.Build(this);   // V2: odometry and LemLib-style motions
        }

        // ------------------------------------------------------------------ running

        /// <summary>Starts a program on the simulated robot (the simulation must be running). False and the error
        /// when it can't even start (a mistake in how it's written).</summary>
        public static bool Start(string source, Period period, double limit, out string error, out int line) {
            Stop(null);
            error = null;
            line = 0;
            var program = new RobotProgram(period, limit);
            // V2: a project (several files): its code files are the program, the rest are files it reads.
            program.source = source ?? "";
            program.files = CodeProject.Files(source);
            source = CodeProject.CodeOnly(source);
            try {
                // V2: C++ (PROS / LemLib / VEXcode C++) runs on the same interpreter, translated.
                if (Scripting.CppTranslator.LooksLikeCpp(source)) {
                    program.Cpp = true;
                    var body = Scripting.CppTranslator.Translate(source, out var warnings);
                    CppRuntime.Install(program.vm);
                    CppApi.Install(program, program.vm.modules["vex"], program.vm.modules["motion"], source);
                    program.mainFiber = program.vm.Load(Compiler.CompileModule(body));
                    foreach (var w in warnings) program.Warn("cpp:" + w, w);
                }
                else program.mainFiber = program.vm.Load(source ?? "");
            }
            catch (SyntaxErr e) {
                error = "Line " + e.line + program.InFile(e.line) + ": " + e.Message;
                line = e.line;
                return false;
            }
            program.systemNames = new HashSet<string>(program.vm.main.globals.Keys);   // V2 (Monitor)
            Current = program;
            TuningData.Begin();   // V2: a new run, a new graph
            V5Brain.Refresh(true);
            foreach (var d in V5Brain.Devices) d.PowerOn();
            Builds.ActionLog.Write("code", "start " + period + (limit > 0 ? " " + limit + " s" : ""));
            StateChanged?.Invoke();
            return true;
        }

        /// <summary>Stops the program (its motors stop); reason null: quietly.</summary>
        public static void Stop(string reason) {
            var p = Current;
            if (p == null) return;
            p.End(reason ?? "Stopped");
            Current = null;
            StateChanged?.Invoke();
        }

        private void End(string status) {
            if (ended) return;
            ended = true;
            Status = status;
            vm.StopAll();
            foreach (var c in motors.Values) c.Release();
            motors.Clear();
        }

        /// <summary>One physics step (Sandbox.FixedUpdate, before the gear trains turn).</summary>
        public static void Step(float dt) {
            var p = Current;
            if (p == null || p.ended) return;
            p.DoStep(dt);
        }

        private void DoStep(float dt) {
            Time += dt;
            vm.Time = Time;
            V5Brain.Want(0.5f);
            StartCompetitionPeriod();
            foreach (var poll in pollers.ToList()) poll();
            vm.Step();
            if (vm.Failed != null) {
                Fail(vm.Failed);
                return;
            }
            foreach (var c in motors.Values) c.Control(dt);
            TuningData.Sample((float)Time, motors.Values);   // V2: the tuning graph (50 samples a second)
            if (limit > 0 && periodStart >= 0 && Time - periodStart >= limit) {
                Print("\n[" + PeriodName + " ended after " + limit.ToString("0") + " s]\n");
                End(PeriodName + " over");
                StateChanged?.Invoke();
                return;
            }
            if (!vm.Running && pollers.Count == 0 && (driverFn == null || competitionStarted) && Status == "Running") {
                Status = "Finished";
                StateChanged?.Invoke();
            }
        }

        private string PeriodName => period switch {
            Period.Autonomous => "Autonomous",
            Period.Driver => "Driver control",
            _ => "Program",
        };

        private void Fail(PyError e) {
            Failed = true;
            ErrorLine = e.line;
            var sb = new StringBuilder();
            sb.Append("\nError on line ").Append(e.line).Append(": ").Append(e.Describe).Append(InFile(e.line)).Append('\n');
            foreach (var t in e.trace.Skip(1).Reverse()) sb.Append("  called from ").Append(t).Append('\n');
            Print(sb.ToString());
            Builds.ActionLog.Write("code", "error line " + e.line + " " + e.Describe);
            End("Error on line " + e.line);
            StateChanged?.Invoke();
        }

        internal void Print(string text) {
            // V2: each listener on its own: one that fails (a closed window's) must not break the program (an
            // exception thrown up through print() left the interpreter's stack wrong).
            if (Printed != null)
                foreach (Action<string> listener in Printed.GetInvocationList())
                    try { listener(text); }
                    catch (Exception e) { Debug.LogWarning("V2: a print listener failed: " + e.Message); }
            TuningData.Printed((float)Time, text);   // V2: "name: number" values go on the tuning graph
        }

        /// <summary>Once per thing a program does wrong without stopping (a motor on an empty port).</summary>
        internal void Warn(string key, string text) {
            if (warned.Add(key)) Print("[warning, line " + vm.Current?.Line + InFile(vm.Current?.Line ?? 0) + "] " + text + "\n");
        }

        /// <summary>V2: " (src/main.cpp line 42)" for a line of a project, "" for a single program.</summary>
        internal string InFile(int line) {
            var where = CodeProject.Where(source, line);
            return where == "" ? "" : " (" + where + ")";
        }

        internal int Line => vm.Current?.Line ?? 0;

        // ------------------------------------------------------------------ competition

        internal object SetCompetition(object driver, object auton) {
            driverFn = driver;
            autonFn = auton;
            return new VexCompetition(this);
        }

        internal bool IsAutonomous => period == Period.Autonomous;
        internal bool IsDriver => period != Period.Autonomous;
        internal bool Enabled => !ended;

        // The period's callback starts once the main program has set things up (it ended, or it's still waiting
        // after 3 s: a field waits that long before a match at least).
        private void StartCompetitionPeriod() {
            if (competitionStarted) return;
            var callback = period == Period.Autonomous ? autonFn : driverFn;
            if (driverFn == null && autonFn == null) {
                if (periodStart < 0) periodStart = 0;   // no Competition: the whole program is the period
                return;
            }
            if (mainFiber != null && !mainFiber.Done && Time < 3) return;
            competitionStarted = true;
            periodStart = Time;
            if (callback == null || callback is PyNone) return;
            Print("[" + PeriodName + " starts]\n");
            vm.Start(callback, null, PeriodName);
        }

        // ------------------------------------------------------------------ devices

        internal VexController Controller(bool isPartner) =>
            isPartner ? partner ??= new VexController(this, true) : primary ??= new VexController(this, false);

        internal MotorControl Motor(MotorDrive drive) {
            if (drive == null) return null;
            if (!motors.TryGetValue(drive, out var c)) motors[drive] = c = new MotorControl(drive);
            return c;
        }

        /// <summary>A motor the program drives (the keys and the Devices panel leave it alone).</summary>
        public static bool Owns(MotorDrive motor) => Current != null && !Current.ended && Current.motors.TryGetValue(motor, out var c) && c.commanded;

        internal T Device<T>(int port, bool threeWire, string what) where T : V5Device {
            var d = V5Brain.Get<T>(port, threeWire);
            if (d == null) {
                var other = V5Brain.Devices.FirstOrDefault(x => x.threeWire == threeWire && V5Brain.On(x, port));
                var name = threeWire ? (port > 100 ? "expander " + port / 100 + " " : "") + "3-wire port " + (char)('A' + port % 100 - 1) : "port " + port;
                Warn(what + port + threeWire, other != null
                    ? what + " on " + name + ": that port has a " + V5Device.KindName(other.kind) + ", not a " + what + " (Edit > Devices… shows the ports)"
                    : what + " on " + name + ": nothing is plugged in there (Edit > Devices… shows the ports)");
            }
            return d;
        }
    }

    /// <summary>A V5 motor's own controller: what the program asked for (a voltage, a velocity, a position) turned
    /// into the motor's command every step, as the motor's firmware does.</summary>
    internal sealed class MotorControl {
        public enum Mode { None, Voltage, Velocity, Position }

        public readonly MotorDrive motor;
        public Mode mode;
        public bool commanded;
        public float voltage;       // -1..1 of 12 V
        public float target;        // velocity: share of free speed (raw direction)
        public double targetDeg;    // position mode (raw degrees)
        public float maxSpeed = 1f; // position mode's speed (share of free speed)
        public string stopping = "BRAKE";
        public float maxTorque = 1f;
        public double degrees;      // raw position since power on
        public bool done = true;
        private float integral, settle;

        public MotorControl(MotorDrive motor) => this.motor = motor;

        public float FreeRpm => motor != null ? motor.Rpm : 200f;
        public float Level => motor != null ? motor.Level : 0f;

        public void Spin(float velocityShare) {
            commanded = true;
            if (mode != Mode.Velocity) integral = 0f;
            mode = Mode.Velocity;
            target = Mathf.Clamp(velocityShare, -1f, 1f);
            done = true;
        }

        public void Volts(float share) {
            commanded = true;
            mode = Mode.Voltage;
            voltage = Mathf.Clamp(share, -1f, 1f);
            done = true;
        }

        public void MoveTo(double deg, float speed) {
            commanded = true;
            if (mode != Mode.Position) integral = 0f;
            mode = Mode.Position;
            targetDeg = deg;
            maxSpeed = Mathf.Clamp01(Mathf.Abs(speed));
            done = false;
        }

        public void StopMotor(string how) {
            commanded = true;
            if (how != null) stopping = how;
            done = true;
            if (stopping == "HOLD") {
                mode = Mode.Position;
                targetDeg = degrees;
                maxSpeed = 1f;
                integral = 0f;
            }
            else mode = Mode.None;
        }

        public void Release() {
            if (motor == null) return;
            motor.Command = 0f;
            motor.Coasting = false;
        }

        public void Control(float dt) {
            if (motor == null) return;
            var level = motor.Level;
            degrees += level * FreeRpm * 6.0 * dt;
            if (!commanded) return;
            float cmd;
            switch (mode) {
                case Mode.Voltage:
                    cmd = voltage;
                    break;
                case Mode.Velocity:
                    cmd = VelocityLoop(target, level, dt);
                    break;
                case Mode.Position: {
                    var dps = FreeRpm * 6f;
                    var err = (float)(targetDeg - degrees);
                    // Slow down in time to stop on the spot (a motion profile), then hold. V2: from full speed in about a
                    // third of a second (0.12 s braked a drivetrain harder than its wheels grip: it slid on after the
                    // wheels had stopped, and a coasting robot turned as it slid).
                    var decel = dps / 0.35f;
                    var want = Mathf.Sign(err) * Mathf.Min(maxSpeed * dps, Mathf.Sqrt(2f * decel * Mathf.Abs(err)));
                    if (Mathf.Abs(err) < 3f) want = err * 12f;
                    cmd = VelocityLoop(want / dps, level, dt);
                    // V2: done once it has stayed on target for a moment (a slide had ended moves early); the speed only
                    // has to be low, not still (ground contact makes a held wheel jitter a little).
                    settle = Mathf.Abs(err) < 2f && Mathf.Abs(level) < 0.15f ? settle + dt : 0f;
                    if (!done && settle >= 0.08f) {
                        done = true;
                        if (stopping != "HOLD") {
                            mode = Mode.None;
                            cmd = 0f;
                        }
                    }
                    break;
                }
                default:
                    cmd = 0f;
                    break;
            }
            // set_max_torque: the push it may give (its stall torque share).
            if (maxTorque < 1f) cmd = level + Mathf.Clamp(cmd - level, -maxTorque, maxTorque);
            motor.Command = Mathf.Clamp(cmd, -1f, 1f);
            motor.Coasting = mode == Mode.None && stopping == "COAST";
        }

        // The motor's velocity loop: feed forward plus proportional and integral on the speed error. V2: the
        // proportional gain stays under 1: a lightly loaded motor reaches what it's given within a physics step, so a
        // gain of 2.5 overshot every step (the speed flipped sign each step near a target and moves never settled).
        private float VelocityLoop(float want, float level, float dt) {
            var err = want - level;
            integral = Mathf.Clamp(integral + err * 4f * dt, -0.5f, 0.5f);
            return want + 0.5f * err + integral;
        }
    }
}
