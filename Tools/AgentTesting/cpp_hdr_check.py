# Compile-checks the VS Code robot-code headers with libclang (pip install libclang; it brings its own clang).
# Usage: python -I cpp_hdr_check.py <headers folder written by cs/t_hdr2.cs> [programs...]
# With no programs it checks each header alone, cpp/*.cpp, and the C++ programs inside the C++ test scripts
# (cs/t_cpp_pros, t_cpp_vex, t_ez1-3: their first @"..." string). Prints every warning and error; "total 0" = clean.
import sys, os, glob, tempfile
import clang.cindex as ci

here = os.path.dirname(os.path.abspath(__file__))
hdr = sys.argv[1]
files = sys.argv[2:]
tmp = tempfile.mkdtemp()

def verbatim(path):
    t = open(path, encoding='utf-8').read()
    i = t.find('@"')
    if i < 0: return None
    j, s = i + 2, []
    while True:
        if t[j] == '"':
            if j + 1 < len(t) and t[j + 1] == '"': s.append('"'); j += 2; continue
            return ''.join(s)
        s.append(t[j]); j += 1

if not files:
    for h in ['api.h', 'vex.h', 'lemlib/api.hpp', 'EZ-Template/api.hpp', 'main.h']:
        f = os.path.join(tmp, 'header_' + h.replace('/', '_') + '.cpp')
        open(f, 'w').write('#include "%s"\n' % h); files.append(f)
    files += glob.glob(os.path.join(here, 'cpp', '*.cpp'))
    for n in ['t_cpp_pros', 't_cpp_vex', 't_ez1', 't_ez2', 't_ez3']:
        code = verbatim(os.path.join(here, 'cs', n + '.cs'))
        if code:
            f = os.path.join(tmp, n + '.cpp'); open(f, 'w', encoding='utf-8').write(code); files.append(f)

args = ['-xc++', '-std=c++20', '-I' + os.path.join(hdr, 'include'), '-DPROS_USE_SIMPLE_NAMES', '-fsyntax-only']
idx = ci.Index.create()
total = 0
for f in files:
    tu = idx.parse(f, args=args)
    ds = [d for d in tu.diagnostics if d.severity >= ci.Diagnostic.Warning]
    print(f"== {os.path.basename(f)}: {len(ds)}")
    for d in ds[:60]:
        loc = d.location
        print(f"  {os.path.basename(str(loc.file)) if loc.file else '?'}:{loc.line}:{loc.column} {d.spelling}")
    total += len(ds)
print("total", total)
