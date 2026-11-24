"""Resolve build dependencies and create a tested Windows package."""
import argparse
import ctypes
from ctypes import wintypes
import hashlib
import ntpath
import os
from pathlib import Path, PureWindowsPath
import shutil
import stat
import struct
import subprocess
import tempfile
import time
import uuid
from zipfile import ZIP_DEFLATED, ZipFile

ROOT = Path(__file__).resolve().parents[1]
LOCAL = ROOT / 'Packages/Local'
PLUGIN = LOCAL / 'mediapipe.tgz'
HASH = 'cc3e77a219e0b99618ae3be64c31a566197deedc69c1e136acf52d65d7cf2e79'
URL = 'https://github.com/homuler/MediaPipeUnityPlugin/releases/download/v0.16.3/com.github.homuler.mediapipe-0.16.3.tgz'
MODEL = LOCAL / 'face_landmarker.task'
MODEL_HASH = '64184e229b263107bc2b804c6625db1341ff2bb731874b0bcc2fe6544e0bc9ff'
MODEL_URL = 'https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task'
PORTRAIT = LOCAL / 'portrait.jpg'
PORTRAIT_HASH = 'a6f11efaa834706db23f275b6115058fa87fc7f14362681e6abe14e82749de3e'
PORTRAIT_URL = 'https://storage.googleapis.com/mediapipe-assets/portrait.jpg'
VERSION = '0.3.20'
PLAYER = 'Windows-' + VERSION


def digest(path):
    hash_state = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024*1024), b''):
            hash_state.update(chunk)
    return hash_state.hexdigest()


def pinned_download(dest, url, expected):
    if dest.is_file() and digest(dest) == expected:
        return
    dest.parent.mkdir(exist_ok=True)
    partial = dest.with_name(dest.name + '.partial')
    try:
        subprocess.run(['curl.exe', '--fail', '--location', '--retry', '2', '--max-time', '900', url, '--output', str(partial)], check=True)
        if digest(partial) != expected:
            raise SystemExit('Checksum mismatch: ' + url)
        partial.replace(dest)
    finally:
        partial.unlink(missing_ok=True)


def setup():
    pinned_download(PLUGIN, URL, HASH)
    print('Verified MediaPipe Unity plugin 0.16.3.')


def fixtures():
    pinned_download(MODEL, MODEL_URL, MODEL_HASH)
    pinned_download(PORTRAIT, PORTRAIT_URL, PORTRAIT_HASH)
    print('Verified private QA fixtures. They are outside Assets and never packaged.')


def smoke_player(exe, log, args, expected):
    log.unlink(missing_ok=True)
    result = subprocess.run([str(exe), '--xr-smoke', '-logFile', str(log), *map(str,args)], cwd=exe.parent, timeout=235 if '--xr-test-download' in args else 180)
    text = log.read_text(encoding='utf-8', errors='replace') if log.exists() else ''
    if result.returncode != 0 or any(marker not in text for marker in ('XR_SMOKE_OK', *expected)):
        raise SystemExit(f'Player verification failed ({result.returncode}): {log}')


def run_qa(exe, out, snapshots, verify_download):
    fixtures()
    with tempfile.TemporaryDirectory(prefix='XReality-QA-') as tmp:
        cache = Path(tmp)/'Models'
        args = ['--xr-cache', cache, '--xr-test-model', MODEL, '--xr-test-portrait', PORTRAIT]
        if snapshots:
            args += ['--xr-snapshots', out/'QA']
        markers = ['XR_PORTRAIT_OK']
        if verify_download:
            args += ['--xr-test-download']
            markers.append('XR_DOWNLOAD_OK')
        smoke_player(exe, out/'player-smoke.log', args, markers)
        profile = 'release-check-' + uuid.uuid4().hex
        for phase in ('write','read'):
            smoke_player(exe, out/f'pet-{phase}.log', ['--xr-pet-profile',profile,'--xr-pet-phase',phase], [f'XR_PET_{phase.upper()}_OK'])
    print('Packaged player passed portrait and two-process pet persistence checks (no camera used).')


def hidden_startup():
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = subprocess.SW_HIDE
    return startup


def screenshots(editor):
    if not editor.is_file():
        raise SystemExit('Unity 6000.3.10f1 not found; pass --unity PATH.')
    out = ROOT / 'Builds'
    out.mkdir(exist_ok=True)
    log = out / 'unity-offscreen-expressions.log'
    subprocess.run([str(editor.resolve()), '-batchmode', '-quit', '-projectPath', str(ROOT),
                    '-executeMethod', 'XReality.Editor.OffscreenSnapshots.Run', '-logFile', str(log)],
                   cwd=ROOT, startupinfo=hidden_startup(), check=True)
    result = log.read_text(encoding='utf-8', errors='replace')
    if 'XR_OFFSCREEN_OK' not in result:
        raise SystemExit(f'Offscreen check did not finish; see {log}')
    print(out / 'QA-Offscreen')


def screenshot_player(exe, capture_dir, log, extra_args, soak_seconds=0):
    user32 = ctypes.WinDLL('user32', use_last_error=True)
    callback = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    user32.EnumWindows.argtypes = [callback, wintypes.LPARAM]
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    samples = 0
    print(f'Packaged screenshot log: {log}', flush=True)
    with subprocess.Popen([str(exe), '-batchmode', '-force-d3d11', '--xr-offscreen', str(capture_dir),
                           '-logFile', str(log), *extra_args], cwd=exe.parent, startupinfo=hidden_startup()) as player:
        visible = []

        @callback
        def check_window(hwnd, _):
            pid = wintypes.DWORD()
            user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
            if pid.value == player.pid and user32.IsWindowVisible(hwnd):
                visible.append(hwnd)
            return True

        deadline = time.monotonic() + soak_seconds + (250 if any(flag in extra_args for flag in ('--xr-download-screenshots', '--xr-resume-screenshots')) else 120)
        while player.poll() is None:
            if not user32.EnumWindows(check_window, 0) or visible or time.monotonic() >= deadline:
                player.kill()
                raise SystemExit(f'Packaged screenshot window check failed or timed out; see {log}')
            samples += 1
            time.sleep(.025)
        if player.returncode != 0 or samples == 0:
            raise SystemExit(f'Packaged screenshot exit {player.returncode}, window samples {samples}; see {log}')
    result = log.read_text(encoding='utf-8', errors='replace')
    if f'XR_PLAYER_OFFSCREEN_OK {VERSION}' not in result:
        raise SystemExit(f'No packaged screenshot completion marker for {VERSION}; see {log}')
    return samples, result


def packaged_screenshots(offline_tracking=False, pet_interaction=False, download_screenshots=False, retry_screenshots=False, resume_screenshots=False, soak_seconds=0):
    out = ROOT / 'Builds'
    package = out / f'XReality-{VERSION}-Windows-x64-portable-preview.zip'
    if not package.is_file():
        raise SystemExit(f'Missing {package}; run build --portable first.')
    checksum, name = package.with_suffix('.sha256').read_text(encoding='ascii').split(maxsplit=1)
    if name.strip() != package.name or digest(package) != checksum:
        raise SystemExit('Portable package checksum mismatch')
    extra = []
    names = ['Home', 'HomeBackdrop', 'HomeReturned', 'PetRoom', 'LiveStageOnly', 'LiveStudio', 'LiveOutput', 'LiveControlsRestored']
    if offline_tracking:
        for path, expected in ((MODEL, MODEL_HASH), (PORTRAIT, PORTRAIT_HASH)):
            if not path.is_file() or digest(path) != expected:
                raise SystemExit(f'Missing or invalid local QA fixture: {path}; this command does not download files.')
        extra = ['--xr-offline-tracking', str(MODEL), str(PORTRAIT)]
        names.extend(['LiveTracked', 'LiveTrackingLost', 'LiveTrackingRecovered',
                      'LiveCalibrationPending', 'LiveCalibrationNeutral', 'LiveCalibrationExpression',
                      'LiveModelMissing', 'LiveModelCorrupt', 'LiveModelReadyNoCamera'])
    if soak_seconds:
        extra += ['--xr-soak-seconds', str(soak_seconds)]
        names.extend(f'LiveSoak{i:02}' for i in range(1, 8))
    if retry_screenshots:
        extra += ['--xr-retry-screenshots']
        names.extend(['LiveModelAutoRetry', 'LiveModelRetryExhausted'])
    if download_screenshots or resume_screenshots:
        extra += ['--xr-resume-screenshots' if resume_screenshots else '--xr-download-screenshots']
        names.extend(['LiveModelDownloading', 'LiveModelDownloadCancelled', 'LiveModelDownloadInstalled'])
    if resume_screenshots:
        names.append('LiveModelResuming')
    if pet_interaction:
        profile = uuid.uuid4().hex
        extra += ['--xr-pet-screenshots', 'play', profile]
        names.extend(['PetSleeping', 'PetSleepyStroke', 'PetAwakeStroke', 'PetNoticingToy', 'PetChasingToy',
                      'PetRestoredStart', 'PetRestoredWindowVisit'])
    captures = Path(tempfile.mkdtemp(prefix='QA-Packaged-', dir=out))
    log = captures / 'player.log'
    with tempfile.TemporaryDirectory(prefix='XReality offscreen extraction ') as temp:
        with ZipFile(package) as archive:
            verify_archive(archive)
            archive.extractall(temp)
        exe = Path(temp) / 'XReality.exe'
        samples, result = screenshot_player(exe, captures, log, extra, soak_seconds)
        if pet_interaction:
            restore_log = captures / 'player-pet-restore.log'
            more, restored = screenshot_player(exe, captures, restore_log, ['--xr-pet-screenshots', 'restore', profile])
            samples += more
            if 'XR_PLAYER_PET_PLAY_OK' not in result or 'XR_PLAYER_PET_RESTORE_OK' not in restored:
                raise SystemExit(f'No pet interaction or restoration marker; see logs in {captures}')
    if 'XR_PLAYER_HOME_SHOWCASE_OK' not in result:
        raise SystemExit(f'No homepage character showcase screenshot marker; see {log}')
    if 'XR_PLAYER_HOME_ROUTES_OK' not in result:
        raise SystemExit(f'No homepage route screenshot marker; see {log}')
    if 'XR_PLAYER_CONTROLS_OK' not in result:
        raise SystemExit(f'No live control isolation screenshot marker; see {log}')
    if offline_tracking and any(marker not in result for marker in ('XR_PLAYER_OFFLINE_TRACKING_OK', 'XR_PLAYER_CALIBRATION_OK')):
        raise SystemExit(f'No offline tracking screenshot completion marker; see {log}')
    if offline_tracking and any(marker not in result for marker in ('XR_PLAYER_MODEL_SETUP_OK', 'XR_PLAYER_MODEL_RANGE_OK')):
        raise SystemExit(f'No model setup screenshot completion marker; see {log}')
    if (download_screenshots or resume_screenshots) and 'XR_PLAYER_DOWNLOAD_SCREENSHOTS_OK' not in result:
        raise SystemExit(f'No network download screenshot completion marker; see {log}')
    if resume_screenshots and 'XR_PLAYER_RESUME_SCREENSHOTS_OK' not in result:
        raise SystemExit(f'No verified HTTP 206 resume screenshot marker; see {log}')
    if retry_screenshots and any(marker not in result for marker in ('XR_PLAYER_RETRY_SCREENSHOTS_OK', 'XR_PLAYER_TRANSFER_BUDGET_OK')):
        raise SystemExit(f'No retry screenshot completion marker; see {log}')
    if soak_seconds and ('XR_PLAYER_SOAK_OK' not in result or not (captures/'live-soak.txt').is_file()):
        raise SystemExit(f'No Windows player fixture soak confirmation; see {log}')
    for name in names:
        with (captures / f'{name}.png').open('rb') as image:
            head = image.read(24)
        if head[:8] != b'\x89PNG\r\n\x1a\n' or head[12:16] != b'IHDR' or (
            int.from_bytes(head[16:20], 'big'), int.from_bytes(head[20:24], 'big')) != (1280, 720):
            raise SystemExit(f'{name} is not a 1280x720 PNG')
    print(f'{len(names)} packaged screenshots; {samples} window checks, none visible; {captures}')


def build(editor, smoke, snapshots=False, candidate=False, verify_download=False, portable=False):
    if not editor.is_file():
        raise SystemExit('Unity 6000.3.10f1 not found. Install/activate the editor or pass --unity PATH.')
    out = ROOT / 'Builds'; out.mkdir(exist_ok=True)
    subprocess.run([str(editor), '-batchmode', '-quit', '-buildTarget', 'Win64', '-projectPath', str(ROOT), '-executeMethod', 'XReality.Editor.StudioProject.BuildWindows', '-logFile', str(out/'unity-build.log')], startupinfo=hidden_startup(), check=True)
    exe = out/PLAYER/'XReality.exe'
    if not exe.is_file():
        raise SystemExit('Player not produced; inspect unity-build.log')
    print(exe)
    if candidate or portable:
        package_candidate(out, snapshots, verify_download, run_player=candidate)
    elif smoke or snapshots or verify_download:
        run_qa(exe,out,snapshots,verify_download)


# Managed assemblies can be PE32/AnyCPU. Only the native executables and
# libraries which Windows loads in this build must be AMD64 PE32+.
NATIVE_WINDOWS_X64 = {
    'XReality.exe', 'UnityCrashHandler64.exe', 'UnityPlayer.dll', 'D3D12/D3D12Core.dll',
    'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll',
    'MonoBleedingEdge/EmbedRuntime/MonoPosixHelper.dll',
    'XReality_Data/Plugins/x86_64/mediapipe_c.dll',
    'XReality_Data/Plugins/x86_64/lib_burst_generated.dll',
}
REQUIRED_PACKAGE_FILES = NATIVE_WINDOWS_X64 | {
    'XReality_Data/app.info', 'XReality_Data/boot.config', 'XReality_Data/globalgamemanagers',
    'XReality_Data/level0', 'XReality_Data/level1', 'XReality_Data/level2',
    'XReality_Data/Managed/Assembly-CSharp.dll', 'README-Preview.md', 'QA-WINDOWS.md',
    *(f'XReality_Data/StreamingAssets/Notices/{name}' for name in (
        'MediaPipeUnity-MIT.txt', 'MediaPipeUnity-ThirdParty.txt', 'Provenance.txt')),
}


def reserved_windows_name(name):
    if hasattr(ntpath, 'isreserved'):
        return ntpath.isreserved(name)
    return PureWindowsPath(name).is_reserved()


def verify_archive(archive):
    names = set()
    folded = set()
    for entry in archive.infolist():
        name = entry.filename
        parts = name.split('/')
        if (entry.is_dir() or entry.orig_filename != name or '\\' in name
                or any(not part or part.endswith((' ', '.')) or any(ord(c) < 32 or c in '<>:"|?*' for c in part)
                       or reserved_windows_name(part) for part in parts)
                or stat.S_ISLNK(entry.external_attr >> 16) or name.casefold() in folded):
            raise SystemExit(f'Unsafe or duplicate package member: {name}')
        if (any('donotship' in part.casefold() for part in parts)
                or Path(name).suffix.lower() in ('.task', '.pdb') or parts[-1].casefold() == PORTRAIT.name.casefold()):
            raise SystemExit(f'Debug or QA data in package: {name}')
        names.add(name)
        folded.add(name.casefold())
    missing = REQUIRED_PACKAGE_FILES - names
    if missing:
        raise SystemExit(f'Package missing required files: {sorted(missing)}')
    if archive.read('XReality_Data/app.info').splitlines() != [b'XReality', b'XReality']:
        raise SystemExit('Unity product identity changed; existing local profiles may not migrate.')
    for name in sorted(NATIVE_WINDOWS_X64):
        with archive.open(name) as file:
            dos = file.read(64)
            offset = struct.unpack_from('<I', dos, 60)[0] if len(dos) == 64 else 0
            if dos[:2] != b'MZ' or not 64 <= offset <= min(2**20, archive.getinfo(name).file_size - 26):
                raise SystemExit(f'Invalid native PE header: {name}')
            file.seek(offset)
            header = file.read(26)
            if len(header) != 26 or header[:6] != b'PE\x00\x00\x64\x86' or header[24:26] != b'\x0b\x02':
                raise SystemExit(f'Expected an AMD64 PE32+ image: {name}')
    damaged = archive.testzip()
    if damaged:
        raise SystemExit(f'Package CRC check failed: {damaged}')
    return len(names)


def verify_package(path):
    if not path.is_file() or not path.with_suffix('.sha256').is_file():
        raise SystemExit(f'Package and its .sha256 sidecar are required: {path}')
    fields = path.with_suffix('.sha256').read_text(encoding='ascii').split(maxsplit=1)
    if len(fields) != 2 or fields[1].strip() != path.name or digest(path) != fields[0]:
        raise SystemExit('Portable package checksum mismatch')
    with ZipFile(path) as archive:
        entries = verify_archive(archive)
    print(f'Archive verified: SHA-256, CRC, {entries} files, {len(NATIVE_WINDOWS_X64)} native AMD64 images; {path}')


def package_candidate(out, snapshots, verify_download, *, run_player):
    root = out / PLAYER
    label = 'preview' if run_player else 'portable-preview'
    dst = out / f'XReality-{VERSION}-Windows-x64-{label}.zip'
    draft = dst.with_suffix('.partial')
    try:
        with ZipFile(draft, 'w', ZIP_DEFLATED, compresslevel=5) as archive:
            for path in sorted(root.rglob('*')):
                rel = path.relative_to(root)
                if not path.is_file() or any('DoNotShip' in part for part in rel.parts) or path.suffix == '.pdb':
                    continue
                if path.suffix.lower() == '.task' or path.name == PORTRAIT.name:
                    raise SystemExit(f'Forbid model or QA portrait in a distribution: {rel}')
                archive.write(path, rel.as_posix())
            archive.write(ROOT/'RELEASE.md', 'README-Preview.md')
            archive.write(ROOT/'QA-WINDOWS.md', 'QA-WINDOWS.md')
        with ZipFile(draft) as archive:
            verify_archive(archive)
            if run_player:
                with tempfile.TemporaryDirectory(prefix='XReality fresh extraction ') as temp:
                    archive.extractall(temp)
                    run_qa(Path(temp)/'XReality.exe',out,snapshots,verify_download)
        draft.replace(dst)
        dst.with_suffix('.sha256').write_text(f'{digest(dst)}  {dst.name}\n',encoding='ascii')
        print(f'Preview: {dst} ({dst.stat().st_size / 2**20:.1f} MiB)')
    finally:
        draft.unlink(missing_ok=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action',choices=['setup','fixtures','build','screenshots','packaged-screenshots','verify-package'])
    parser.add_argument('--unity',type=Path,default=Path(os.environ.get('ProgramFiles','C:/Program Files'))/'Unity/Hub/Editor/6000.3.10f1/Editor/Unity.exe')
    parser.add_argument('--smoke',action='store_true')
    parser.add_argument('--snapshots',action='store_true')
    parser.add_argument('--candidate',action='store_true',help='Build, re-extract, smoke-test, then publish a local preview ZIP')
    parser.add_argument('--verify-download',action='store_true',help='Verify cancel/retry and byte-exact download from Google in the packaged player')
    parser.add_argument('--portable', action='store_true', help='Build and package a portable ZIP without starting the player')
    parser.add_argument('--offline-tracking', action='store_true', help='Include avatar screenshots driven by pre-existing local QA fixtures')
    parser.add_argument('--pet-interaction', action='store_true', help='Include live pet action and restart screenshots using an isolated QA profile')
    parser.add_argument('--download-screenshots', action='store_true', help='Capture online model download, cancellation, and retry using an isolated cache')
    parser.add_argument('--retry-screenshots', action='store_true', help='Capture deterministic network retry and fallback without accessing the network')
    parser.add_argument('--resume-screenshots', action='store_true', help='Interrupt a live model transfer and require a verified HTTP 206 resume')
    parser.add_argument('--soak-seconds', type=int, metavar='30-1800', help='Continuous hidden 720p rendering and CPU fixture inference, with seven PNG checkpoints')
    parser.add_argument('--package', type=Path, help='ZIP to validate for verify-package (defaults to current portable preview)')
    args = parser.parse_args()
    if args.package is not None and args.action != 'verify-package':
        parser.error('--package requires verify-package')
    if args.soak_seconds is not None and (args.action != 'packaged-screenshots' or not args.offline_tracking or not 30 <= args.soak_seconds <= 1800):
        parser.error('--soak-seconds requires packaged-screenshots --offline-tracking and a duration in 30..1800 seconds')
    if args.resume_screenshots and (args.action != 'packaged-screenshots' or args.download_screenshots):
        parser.error('--resume-screenshots requires packaged-screenshots and replaces --download-screenshots')
    if args.retry_screenshots and args.action != 'packaged-screenshots':
        parser.error('--retry-screenshots requires packaged-screenshots')
    if args.download_screenshots and args.action != 'packaged-screenshots':
        parser.error('--download-screenshots requires packaged-screenshots')
    if args.pet_interaction and args.action != 'packaged-screenshots':
        parser.error('--pet-interaction requires packaged-screenshots')
    if args.offline_tracking and args.action != 'packaged-screenshots':
        parser.error('--offline-tracking requires packaged-screenshots')
    if args.portable and (args.action != 'build' or args.smoke or args.snapshots or args.candidate or args.verify_download):
        parser.error('--portable requires build and cannot be combined with player test options')
    if args.action in ('screenshots', 'packaged-screenshots'):
        if args.smoke or args.snapshots or args.candidate or args.verify_download:
            parser.error('screenshots cannot be combined with build or smoke options')
        if args.action == 'screenshots':
            screenshots(args.unity)
        else:
            packaged_screenshots(args.offline_tracking, args.pet_interaction, args.download_screenshots, args.retry_screenshots, args.resume_screenshots, args.soak_seconds or 0)
        raise SystemExit(0)
    if args.action == 'verify-package':
        if args.smoke or args.snapshots or args.candidate or args.verify_download:
            parser.error('verify-package never runs or rebuilds the application')
        verify_package(args.package or ROOT/'Builds'/f'XReality-{VERSION}-Windows-x64-portable-preview.zip')
        raise SystemExit(0)
    setup()
    if args.action == 'fixtures': fixtures()
    elif args.action == 'build': build(args.unity,args.smoke,args.snapshots,args.candidate,args.verify_download,args.portable)
