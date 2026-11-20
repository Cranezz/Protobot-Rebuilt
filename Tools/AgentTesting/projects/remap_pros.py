"""PROS + LemLib projects onto the 6-motor drive: drive motors left 4,5,6 / right 1,2,3 reversed, 3.25" wheels at 360 rpm,
14.125" track, no tracking wheels (odometry from the drive motors), the IMU on port 7, other port-1..7 devices moved.
Writes runs/<n>.cpp as a project (file markers, static/ path files included)."""
import os, re, sys
out = sys.argv[1]
projects = [l.strip() for l in open(sys.argv[2], encoding="utf-8") if l.strip()]
M = "//// file: "
for n, d in enumerate(projects):
    if not os.path.exists(os.path.join(d, "project.pros")):
        continue
    src = open(os.path.join(d, "src", "main.cpp"), encoding="utf-8", errors="replace").read()
    notes = []
    groups = re.findall(r"pros::Motor_?Group\s+(\w+)\s*\(\s*\{([^}]*)\}\s*\)", src)
    sides = {}
    for gname, members in groups:
        names = [x.strip() for x in members.split(",")]
        if "left" in gname.lower(): sides["L"] = names
        elif "right" in gname.lower(): sides["R"] = names
    for side, ports, rev in (("L", [4, 5, 6], "false"), ("R", [1, 2, 3], "true")):
        for name, port in zip(sides.get(side, []), ports):
            src = re.sub(r"pros::Motor\s+%s\s*\([^;]*\);" % name, "pros::Motor %s(%d, pros::E_MOTOR_GEARSET_06, %s);" % (name, port, rev), src)
        notes.append("%s %s -> %s" % (side, sides.get(side), ports))
    drive = set(sides.get("L", []) + sides.get("R", []))
    used = iter(range(14, 22))
    def free(m):
        kind, name, port = m.group(1), m.group(2), int(m.group(3))
        if name in drive or port > 7: return m.group(0)
        q = next(used)
        notes.append("%s %s -> %d" % (kind, name, q))
        return m.group(0).replace("(%d" % port, "(%d" % q, 1)
    src = re.sub(r"pros::(Motor|Rotation|Distance|Optical|Vision)\s+(\w+)\s*\(\s*(\d+)", free, src)
    src = re.sub(r"(pros::Imu\s+\w+\s*\(\s*)\d+", r"\g<1>7", src)
    # the drivetrain struct: track, wheel, rpm (0.4: Drivetrain_t {&l, &r, track, wheel, rpm}; 0.5-rc: Drivetrain(...))
    def dt(m):
        items = [x.strip() for x in re.split(r",(?![^(]*\))", m.group(2))]
        items = [x for x in items if x and not x.startswith("//")]
        body = re.sub(r"//[^\n]*", "", m.group(2))
        items = [x.strip() for x in body.split(",") if x.strip()]
        items[2:5] = ["14.125", "3.25", "360"]
        notes.append("drivetrain " + ", ".join(items))
        return m.group(1) + "\n    " + ",\n    ".join(items) + "\n" + m.group(3)
    src = re.sub(r"(lemlib::Drivetrain(?:_t)?\s+\w+\s*[({])(.*?)([)}]\s*;)", dt, src, count=1, flags=re.S)
    # no tracking wheels: the drive motors track
    def sens(m):
        body = re.sub(r"//[^\n]*", "", m.group(2))
        items = [x.strip() for x in body.split(",") if x.strip()]
        items[:4] = ["nullptr"] * 4
        notes.append("sensors " + ", ".join(items))
        return m.group(1) + ", ".join(items) + m.group(3)
    src = re.sub(r"(lemlib::OdomSensors(?:_t)?\s+\w+\s*[({])(.*?)([)}]\s*;)", sens, src, count=1, flags=re.S)
    files = [("src/main.cpp", src)]
    static = os.path.join(d, "static")
    if os.path.isdir(static):
        for f in sorted(os.listdir(static)):
            files.append(("static/" + f, open(os.path.join(static, f), encoding="utf-8", errors="replace").read()))
    text = "\n".join(M + p + "\n" + t.replace("\r\n", "\n").rstrip("\n") for p, t in files)
    open(os.path.join(out, "%d.cpp" % n), "w", encoding="utf-8").write(text)
    print(n, os.path.basename(d), " | ".join(notes))
