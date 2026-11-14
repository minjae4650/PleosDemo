"""Compile the Unity C# outside the Editor, using Unity's own compiler arguments.

Catches compile errors without focusing or restarting Unity. Reads the response
files Unity last generated under Library/Bee, so the project must have been
opened once. Writes only to a temporary directory.
"""
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / 'unity/InputProbe'
DATA = Path(os.environ.get('UNITY_EDITOR_DATA', r'C:\Program Files\Unity\Hub\Editor\2022.3.16f1\Editor\Data'))
DOTNET = DATA / 'NetCoreRuntime/dotnet.exe'
CSC = DATA / 'DotNetSdkRoslyn/csc.dll'


def sources_on_disk(editor):
    found = []
    for path in (PROJECT / 'Assets').rglob('*.cs'):
        relative = path.relative_to(PROJECT).as_posix()
        if ('/Editor/' in relative) == editor:
            found.append(relative)
    return found


def compile_assembly(name, editor, out_dir, runtime_ref=None):
    bee = PROJECT / 'Library/Bee/artifacts'
    dag = next(bee.glob('*.dag'), None) if bee.exists() else None
    if dag is None:
        sys.exit(f'Library/Bee/artifacts not found under {PROJECT}: open the project in the Unity editor once '
                 '(this check reuses the compiler arguments Unity writes there), then run it again.')
    lines = (dag / f'{name}.rsp').read_text(encoding='utf-8').splitlines()
    args, listed = [], set()
    out_path = out_dir / f'{name}.dll'
    ref_path = out_dir / f'{name}.ref.dll'
    for line in lines:
        if line.startswith('-out:'):
            args.append(f'-out:"{out_path}"')
        elif line.startswith('-refout:'):
            args.append(f'-refout:"{ref_path}"')
        elif line.startswith(('/additionalfile', '-analyzer')):
            continue
        elif runtime_ref and line.startswith('-r:') and 'Assembly-CSharp.ref.dll' in line:
            args.append(f'-r:"{runtime_ref}"')
        elif re.match(r'^"Assets/.*\.cs"$', line):
            source = line.strip('"')
            if (PROJECT / source).exists():
                listed.add(source)
                args.append(line)
        else:
            args.append(line)
    for source in sources_on_disk(editor):
        # Plugins compile into firstpass assemblies that are already referenced.
        if source not in listed and not source.startswith('Assets/Plugins/'):
            args.append(f'"{source}"')
    rsp = out_dir / f'{name}.rsp'
    rsp.write_text('\n'.join(args), encoding='utf-8')
    result = subprocess.run([str(DOTNET), 'exec', str(CSC), '/noconfig', f'@{rsp}'], cwd=PROJECT,
                            capture_output=True, encoding='utf-8', errors='replace')
    errors = [l for l in result.stdout.splitlines() if ': error ' in l]
    print(f'{name}: {"OK" if result.returncode == 0 else "FAILED"} ({len(errors)} errors)')
    for line in errors[:40]:
        print('  ' + line)
    return result.returncode == 0, ref_path


def main():
    with tempfile.TemporaryDirectory() as tmp:
        out_dir = Path(tmp)
        ok, runtime_ref = compile_assembly('Assembly-CSharp', False, out_dir)
        if not ok:
            sys.exit(1)
        ok, _ = compile_assembly('Assembly-CSharp-Editor', True, out_dir, runtime_ref)
        sys.exit(0 if ok else 1)


if __name__ == '__main__':
    main()
