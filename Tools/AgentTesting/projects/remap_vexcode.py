"""Combine each VEXcode project's files and point its robot-config at the 6-motor drive (left 4,5,6; right 1,2,3
reversed; 3.25" wheels, 60:36), other motors to free ports, the inertial to port 7. Writes runs/<n>.cpp + runs/index.txt"""
import os, re, sys
root, out = sys.argv[1], sys.argv[2]
projects = [l.strip() for l in open(sys.argv[3], encoding="utf-8") if l.strip()]
index = []
for n, d in enumerate(projects):
    if not os.path.isdir(os.path.join(d, "src")) and not os.path.isdir(os.path.join(d, "src (1)")):
        continue
    parts = []
    for inc in ("include", "include (1)"):
        p = os.path.join(d, inc)
        if os.path.isdir(p):
            for f in sorted(os.listdir(p)):
                if f.lower().startswith("vex") or f in ("main.h", "api.h") or not os.path.isfile(os.path.join(p, f)): continue
                if "(1)" in f and inc == "include": continue
                parts.append(os.path.join(p, f))
    for s in ("src", "src (1)"):
        p = os.path.join(d, s)
        if os.path.isdir(p):
            fs = [f for f in os.listdir(p) if f.endswith((".cpp", ".c")) and ("(1)" not in f or "(1)" in s)]
            fs.sort(key=lambda f: 0 if f.startswith("robot-config") else 1)
            parts += [os.path.join(p, f) for f in fs]
    src = "\n".join(open(f, encoding="utf-8", errors="replace").read() for f in parts)
    notes = []
    m = re.search(r"(drivetrain|smartdrive)\s+(\w+)\s*=\s*(drivetrain|smartdrive)\s*\(\s*(\w+)\s*,\s*(\w+)\s*,(.*?)\);", src)
    if m:
        L, R = m.group(4), m.group(5)
        def members(name):
            g = re.search(r"motor_group\s+%s\s*=\s*motor_group\s*\(([^)]*)\)" % name, src)
            return [x.strip() for x in g.group(1).split(",")] if g else [name]
        drive = {}
        for side, ports, rev in ((L, [4, 5, 6], "false"), (R, [1, 2, 3], "true")):
            mem = members(side)
            if mem == [side]:   # a single motor: make it a group of three
                src = re.sub(r"motor\s+%s\s*=\s*motor\s*\([^)]*\);" % side,
                             "motor_group %s = motor_group(%s);" % (side, ", ".join("motor(PORT%d, ratio6_1, %s)" % (p, rev) for p in ports)), src)
                notes.append("%s -> ports %s" % (side, ports))
            else:
                for name, p in zip(mem, ports):
                    src = re.sub(r"motor\s+%s\s*=\s*motor\s*\([^)]*\);" % name, "motor %s = motor(PORT%d, ratio6_1, %s);" % (name, p, rev), src)
                    drive[name] = p
                notes.append("%s %s -> ports %s" % (side, mem, ports[:len(mem)]))
            for name in mem: drive[name] = 1
        # this drive: 3.25" wheels (10.21" travel), 14.125" track, motor 36T -> wheel 60T
        args = m.group(6)
        nums = re.findall(r"[-\d.]+|\w+", args)
        new = re.sub(r"\(\s*(\w+)\s*,\s*(\w+)\s*,(.*?)\);", lambda mm: "(%s, %s, %s);" % (mm.group(1), mm.group(2),
              ("%s, " % re.match(r"\s*(\w+)\s*,", mm.group(3)).group(1) if m.group(1) == "smartdrive" else "") + "259.3, 358.8, 300, mm, 1.6667"), m.group(0), count=1)
        src = src.replace(m.group(0), new)
        notes.append("drive: " + new)
        used = iter(range(14, 22))
        def free(mm):
            name, port = mm.group(1), int(mm.group(2))
            if name in drive or port > 7: return mm.group(0)
            q = next(used)
            notes.append("%s port %d -> %d" % (name, port, q))
            return mm.group(0).replace("PORT%d" % port, "PORT%d" % q, 1)
        src = re.sub(r"motor\s+(\w+)\s*=\s*motor\s*\(\s*PORT(\d+)[^;]*;", free, src)
        src = re.sub(r"vision\s+(\w+)\s*=\s*vision\s*\(\s*PORT(\d+)", lambda mm: mm.group(0).replace("PORT" + mm.group(2), "PORT20"), src)
        src = re.sub(r"(inertial\s+(\w+)\s*=\s*inertial\s*\(\s*)PORT\d+", r"\1PORT7", src)
    open(os.path.join(out, "%d.cpp" % n), "w", encoding="utf-8").write(src)
    index.append("%d\t%s\t%s" % (n, os.path.basename(d), " | ".join(notes)))
open(os.path.join(out, "index.txt"), "w", encoding="utf-8").write("\n".join(index) + "\n")
print("\n".join(index))
