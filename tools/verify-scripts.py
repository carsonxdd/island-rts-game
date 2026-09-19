"""Roslyn compile-verify of foundingtide scripts in four configs, no Unity launch.

    py toolserify-scripts.py        errors per config (exit 1 on any)
    py toolserify-scripts.py -w     also print warnings

Configs: editor (UNITY_EDITOR), dev player (DEVELOPMENT_BUILD), release player,
and the Assets/Editor assembly against the editor build. References the editor
install's UnityEngine modules + Unity.Scripting (PreserveAttribute) and the
package DLLs Unity already compiled into Library/ScriptAssemblies, so the
project must have been opened once in the editor. Never references
UnityEditor.dll beside the UnityEditor.*Module.dll files (spurious CS0433).
Response files and logs land in tools/verify-out/ (gitignored).
"""
import glob, os, subprocess, sys, re
ROOT = r"V:\islandrtsgame\foundingtide"
E = r"D:\Programs\unity editor\6000.5.9f1\Editor\Data"
CSC = os.path.join(E, r"DotNetSdk\sdk\8.0.318\Roslyn\bincore\csc.dll")
DOTNET = os.path.join(E, r"DotNetSdk\dotnet.exe")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "verify-out"); os.makedirs(OUT, exist_ok=True)

def refs():
    r = [os.path.join(E, r"NetStandard\ref\2.1.0\netstandard.dll")]
    r += glob.glob(os.path.join(E, r"Managed\UnityEngine\UnityEngine*.dll"))
    r += glob.glob(os.path.join(E, r"Managed\UnityEngine\Unity.Scripting*.dll"))
    r += glob.glob(os.path.join(E, r"Managed\UnityEngine\UnityEditor.*Module.dll"))
    r += glob.glob(os.path.join(E, r"Managed\UnityEngine\UnityEditor.Graphs.dll"))
    r += [f for f in glob.glob(os.path.join(ROOT, r"Library\ScriptAssemblies\*.dll")) if not os.path.basename(f).startswith("Assembly-CSharp")]
    return ['-r:"%s"' % p for p in r]

def build(name, defines, src, extra=()):
    rsp = os.path.join(OUT, name + ".rsp")
    lines = ["-nologo", "-target:library", "-nowarn:CS1701,CS1702", "-langversion:9.0", '-out:"%s"' % os.path.join(OUT, name + ".dll")]
    if defines: lines.append("-define:" + defines)
    lines += refs(); lines += list(extra)
    lines += ['"%s"' % p for p in glob.glob(os.path.join(src, "**", "*.cs"), recursive=True)]
    open(rsp, "w").write("\n".join(lines))
    p = subprocess.run([DOTNET, CSC, "@" + rsp], capture_output=True, text=True)
    log = p.stdout + p.stderr; open(os.path.join(OUT, name + ".log"), "w").write(log)
    errs = [l for l in log.splitlines() if "error CS" in l]; warns = [l for l in log.splitlines() if "warning CS" in l]
    print("%s: errors=%d warnings=%d" % (name, len(errs), len(warns)))
    for l in errs[:40]: print("  " + l)
    if "-w" in sys.argv:
        for l in warns[:40]: print("  " + l)
    return len(errs)

common = "UNITY_5_3_OR_NEWER;UNITY_6000_0_OR_NEWER"
n = 0
n += build("editor", "UNITY_EDITOR;" + common, os.path.join(ROOT, r"Assets\Scripts"))
n += build("devplayer", "DEVELOPMENT_BUILD;UNITY_STANDALONE;" + common, os.path.join(ROOT, r"Assets\Scripts"))
n += build("release", "UNITY_STANDALONE;" + common, os.path.join(ROOT, r"Assets\Scripts"))
n += build("editorasm", "UNITY_EDITOR;" + common, os.path.join(ROOT, r"Assets\Editor"), ['-r:"%s"' % os.path.join(OUT, "editor.dll")])
sys.exit(1 if n else 0)
