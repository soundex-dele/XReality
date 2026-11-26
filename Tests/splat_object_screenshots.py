"""Verify a downloaded static XRS in the built Windows player without a visible window."""
import argparse
import json
from pathlib import Path
import re
import sys
import tempfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Tools'))
from project import PLAYER, ROOT, VERSION, check_screenshot, digest, screenshot_player


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('asset', type=Path, help='XRS downloaded from the workbench')
    parser.add_argument('--sha256', help='Model resource hash reported by the workbench')
    args = parser.parse_args()
    asset = args.asset.resolve()
    if not asset.is_file() or asset.suffix.lower() != '.xrs':
        parser.error('expected an existing .xrs file')
    sha256 = digest(asset)
    if args.sha256 is not None and sha256 != args.sha256.lower():
        parser.error('XRS SHA-256 differs from the expected value')

    executable = ROOT / 'Builds' / PLAYER / 'XReality.exe'
    assembly = executable.parent / 'XReality_Data/Managed/Assembly-CSharp.dll'
    if not executable.is_file() or not assembly.is_file():
        parser.error('build the Windows Player with Tools/project.py build first')
    folder = ROOT / 'Builds/QA-XRS'
    folder.mkdir(parents=True, exist_ok=True)
    out = Path(tempfile.mkdtemp(prefix='real-object-player-', dir=folder))
    log = out / 'player.log'
    samples, output = screenshot_player(executable, out, log, ['-splat-object', str(asset)], graphics='d3d11')
    match = re.search(r'^XR_PLAYER_XRS_OK: (\d+) static splats; (.+)$', output, re.MULTILINE)
    if not match or not re.search(r'^\s*Version:\s+Direct3D 11\.', output, re.MULTILINE):
        raise SystemExit(f'Missing XRS or D3D11 verification; see {log}')
    pictures = [out / f'Xrs{name}.png' for name in ('Front', 'Rotated', 'Zoomed', 'Hidden', 'Reset')]
    for picture in pictures:
        check_screenshot(picture)
    report = {'version': VERSION, 'graphics': 'D3D11', 'asset': str(asset), 'asset_sha256': sha256,
              'splats': int(match[1]), 'verification': match[2], 'window_samples': samples,
              'player': str(executable), 'player_sha256': digest(executable),
              'managed_assembly_sha256': digest(assembly),
              'artifacts': {path.name: digest(path) for path in [*pictures, log]}}
    (out / 'evidence.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(f'{match[0]}; {samples} visibility checks passed\nEvidence: {out}')


if __name__ == '__main__':
    main()
