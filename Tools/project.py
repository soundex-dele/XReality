"""Resolve build dependencies and create a tested Windows package."""
import argparse
from contextlib import contextmanager, nullcontext
import ctypes
from datetime import datetime, timezone
from ctypes import wintypes
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import ntpath
import os
import re
from pathlib import Path, PureWindowsPath
import shutil
import stat
import struct
import ssl
import subprocess
import tempfile
from threading import Thread
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
VERSION = '0.3.35'
BUNDLED_MODEL = 'XReality_Data/StreamingAssets/Models/face_landmarker.task'
MODEL_LICENSE = 'XReality_Data/StreamingAssets/Notices/FaceLandmarker-Apache-2.0.txt'
PRESENTER_LICENSE = 'XReality_Data/StreamingAssets/Notices/MakeHuman-CC0-1.0.txt'
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
    print('Verified pinned model and private QA portrait. Only the model ships.')


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


def check_screenshot(path):
    with path.open('rb') as image:
        header = image.read(24)
    if (header[:16] != b'\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR' or len(header) != 24
            or struct.unpack('>II', header[16:]) != (1280, 720)):
        raise SystemExit(f'Expected a 1280x720 PNG: {path}')


def screenshot_player(exe, capture_dir, log, extra_args, soak_seconds=0, graphics="auto", *, version=VERSION):
    user32 = ctypes.WinDLL('user32', use_last_error=True)
    callback = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    user32.EnumWindows.argtypes = [callback, wintypes.LPARAM]
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    samples = 0
    print(f'Packaged screenshot log: {log}', flush=True)
    graphics_args = [] if graphics == 'auto' else [f'-force-{graphics}']
    with subprocess.Popen([str(exe), '-batchmode', *graphics_args, '--xr-offscreen', str(capture_dir),
                           '-logFile', str(log), *extra_args], cwd=exe.parent, startupinfo=hidden_startup()) as player:
        visible = []

        @callback
        def check_window(hwnd, _):
            pid = wintypes.DWORD()
            user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
            if pid.value == player.pid and user32.IsWindowVisible(hwnd):
                visible.append(hwnd)
            return True

        online_timeout = 450 if '--xr-manual-resume-screenshots' in extra_args else (250 if any(
            flag in extra_args for flag in ('--xr-download-screenshots', '--xr-resume-screenshots')) else 120)
        deadline = time.monotonic() + soak_seconds + online_timeout
        while player.poll() is None:
            if not user32.EnumWindows(check_window, 0) or visible or time.monotonic() >= deadline:
                player.kill()
                raise SystemExit(f'Packaged screenshot window check failed or timed out; see {log}')
            samples += 1
            time.sleep(.025)
        if player.returncode != 0 or samples == 0:
            raise SystemExit(f'Packaged screenshot exit {player.returncode}, window samples {samples}; see {log}')
    result = log.read_text(encoding='utf-8', errors='replace')
    if f'XR_PLAYER_OFFSCREEN_OK {version}' not in result.splitlines():
        raise SystemExit(f'No packaged screenshot completion marker for {version}; see {log}')
    return samples, result


@contextmanager
def local_range_server():
    if not MODEL.is_file() or digest(MODEL) != MODEL_HASH:
        raise SystemExit('Local model fixture missing or invalid; prepare it separately with fixtures.')
    openssl = shutil.which('openssl.exe')
    if not openssl and (git := shutil.which('git.exe')):
        openssl = next((str(path) for root in Path(git).parents[:3]
                        if (path := root/'usr/bin/openssl.exe').is_file()), None)
    if not openssl:
        raise SystemExit('The isolated HTTPS test needs OpenSSL (the Git for Windows copy works).')
    model = MODEL.read_bytes()
    cut = 512 * 1024
    seen = []
    expected = f'bytes={cut}-'

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass

        def do_GET(self):
            if self.path != '/model':
                self.send_error(404)
                return
            requested = self.headers.get('Range')
            seen.append(requested)
            if requested not in (None, expected):
                self.send_error(416)
                return
            self.send_response(200 if requested is None else 206)
            self.send_header('Content-Type', 'application/octet-stream')
            self.send_header('Connection', 'close')
            self.send_header('Accept-Ranges', 'bytes')
            if requested is None:
                self.send_header('Content-Length', str(len(model)))
                body = model[:cut]  # Deliberately close early without rewriting Content-Length.
            else:
                self.send_header('Content-Length', str(len(model) - cut))
                self.send_header('Content-Range', f'bytes {cut}-{len(model)-1}/{len(model)}')
                body = model[cut:]
            self.end_headers()
            try:
                self.wfile.write(body)
                self.wfile.flush()
            except OSError:
                pass  # The initial cancellation screenshot can close its request immediately.
            self.close_connection = True

    with tempfile.TemporaryDirectory(prefix='XReality loopback TLS ') as temp:
        cert, key = Path(temp)/'cert.pem', Path(temp)/'key.pem'
        subprocess.run([openssl, 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-sha256', '-days', '1',
                        '-subj', '/CN=127.0.0.1', '-addext', 'subjectAltName=IP:127.0.0.1',
                        '-keyout', str(key), '-out', str(cert)], capture_output=True, check=True,
                       timeout=15, startupinfo=hidden_startup())
        fingerprint = hashlib.sha256(ssl.PEM_cert_to_DER_cert(cert.read_text(encoding='ascii'))).hexdigest()
        tls = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        tls.load_cert_chain(cert, key)
        with ThreadingHTTPServer(('127.0.0.1', 0), Handler) as server:
            server.socket = tls.wrap_socket(server.socket, server_side=True)
            thread = Thread(target=server.serve_forever, kwargs={'poll_interval': .1}, daemon=True)
            thread.start()
            try:
                yield server.server_port, fingerprint, seen, expected
            finally:
                server.shutdown()
                thread.join()


def packaged_screenshots(offline_tracking=False, pet_interaction=False, download_screenshots=False, retry_screenshots=False, resume_screenshots=False, soak_seconds=0, graphics="auto", manual_resume=False, local_manual_resume=False):
    out = ROOT / 'Builds'
    package = out / f'XReality-{VERSION}-Windows-x64-portable-preview.zip'
    if not package.is_file():
        raise SystemExit(f'Missing {package}; run build --portable first.')
    checksum = package_checksum(package)
    extra = []
    names = ['Home', 'HomeBackdrop', 'HomeReturned', 'PetRoom', 'LiveStageOnly', 'LiveStudio', 'LiveOutput', 'LiveControlsRestored', 'LiveBundledModelReady',
             'LiveCameraChanged', 'LiveCameraEnumerationFailed', 'LiveCameraStartupTimeout', 'LiveCameraStalled', 'LivePrivacyStopClean',
             'LiveQuickBlink', 'LiveBoundedTurn', 'LiveBeforeFaceDropout', 'LiveHeldFaceDropout', 'LiveExpiredFaceDropout', 'LiveEyeGaze', 'LiveJawOnly', 'LiveLipRound', 'LiveLipPress']
    if offline_tracking:
        for path, expected in ((MODEL, MODEL_HASH), (PORTRAIT, PORTRAIT_HASH)):
            if not path.is_file() or digest(path) != expected:
                raise SystemExit(f'Missing or invalid local QA fixture: {path}; this command does not download files.')
        extra = ['--xr-offline-tracking', str(MODEL), str(PORTRAIT)]
        names.extend(['LiveTracked', 'LiveTrackingLost', 'LiveTrackingRecovered',
                      'LiveCalibrationPending', 'LiveCalibrationNeutral', 'LiveCalibrationExpression',
                      'LiveModelMissing', 'LiveModelCorrupt', 'LiveModelReadyNoCamera', 'LiveDependencyMissing'])
    if soak_seconds:
        extra += ['--xr-soak-seconds', str(soak_seconds)]
        names.extend(f'LiveSoak{i:02}' for i in range(1, 8))
    if retry_screenshots:
        extra += ['--xr-retry-screenshots']
        names.extend(['LiveModelAutoRetry', 'LiveModelRetryExhausted'])
    manual_resume = manual_resume or local_manual_resume
    if download_screenshots or resume_screenshots or manual_resume:
        extra += ['--xr-manual-resume-screenshots' if manual_resume else
                  '--xr-resume-screenshots' if resume_screenshots else '--xr-download-screenshots']
        names.extend(['LiveModelDownloading', 'LiveModelDownloadCancelled', 'LiveModelDownloadInstalled'])
    if resume_screenshots:
        names.append('LiveModelResuming')
    if manual_resume:
        names.append('LiveModelWaitingToContinue')
    if pet_interaction:
        profile = uuid.uuid4().hex
        extra += ['--xr-pet-screenshots', 'play', profile]
        names.extend(['PetSleeping', 'PetSleepyStroke', 'PetAwakeStroke', 'PetNoticingToy', 'PetChasingToy',
                      'PetRestoredStart', 'PetRestoredWindowVisit'])
    captures = Path(tempfile.mkdtemp(prefix='QA-Packaged-', dir=out))
    log = captures / 'player.log'
    with tempfile.TemporaryDirectory(prefix='XReality offscreen extraction ') as temp:
        with ZipFile(package) as archive:
            verify_archive(archive, require_model=True, require_presenter=True)
            archive.extractall(temp)
        exe = Path(temp) / 'XReality.exe'
        with (local_range_server() if local_manual_resume else nullcontext()) as endpoint:
            if endpoint:
                port, fingerprint, seen, expected = endpoint
                extra += ['--xr-download-loopback', str(port), fingerprint]
            samples, result = screenshot_player(exe, captures, log, extra, soak_seconds, graphics)
            if endpoint and (None not in seen or expected not in seen or any(x not in (None, expected) for x in seen)):
                raise SystemExit(f'Loopback did not observe the expected Range {expected!r}: {seen}')
            if endpoint:
                (captures/'loopback-range.txt').write_text(
                    f'Truncated full response at 524288 bytes. Observed {expected}.\n', encoding='ascii')
        if pet_interaction:
            restore_log = captures / 'player-pet-restore.log'
            more, restored = screenshot_player(exe, captures, restore_log, ['--xr-pet-screenshots', 'restore', profile], graphics=graphics)
            samples += more
            if ('XR_PLAYER_PET_PLAY_OK' not in result or 'XR_PLAYER_PET_POINTER_OK' not in result
                    or 'XR_PLAYER_PET_RESTORE_OK' not in restored):
                raise SystemExit(f'No pet interaction or restoration marker; see logs in {captures}')
    if 'XR_PLAYER_HOME_SHOWCASE_OK' not in result:
        raise SystemExit(f'No homepage character showcase screenshot marker; see {log}')
    if 'XR_PLAYER_HOME_ROUTES_OK' not in result:
        raise SystemExit(f'No homepage route screenshot marker; see {log}')
    if 'XR_PLAYER_CONTROLS_OK' not in result:
        raise SystemExit(f'No live control isolation screenshot marker; see {log}')
    if 'XR_PLAYER_BUNDLED_MODEL_OK' not in result:
        raise SystemExit(f'No offline bundled model screenshot marker; see {log}')
    if 'XR_PLAYER_CAMERA_SELECTION_OK' not in result:
        raise SystemExit(f'No camera selection fixture completion marker; see {log}')
    if 'XR_PLAYER_FACE_DROPOUT_OK' not in result:
        raise SystemExit(f'No face-dropout screenshot completion marker; see {log}')
    if 'XR_PLAYER_LIP_ARTICULATION_OK' not in result:
        raise SystemExit(f'No lip articulation screenshot completion marker; see {log}')
    if 'XR_PLAYER_EYE_GAZE_OK' not in result:
        raise SystemExit(f'No eye gaze screenshot completion marker; see {log}')
    if 'XR_PLAYER_MOTION_RESPONSE_OK' not in result:
        raise SystemExit(f'No motion response screenshot marker; see {log}')
    if 'XR_PLAYER_LIVE_SAFETY_OK' not in result:
        raise SystemExit(f'No live safety screenshot marker; see {log}')
    if offline_tracking and any(marker not in result for marker in ('XR_PLAYER_OFFLINE_TRACKING_OK', 'XR_PLAYER_CALIBRATION_OK', 'XR_PLAYER_CALIBRATION_BLINK_OK')):
        raise SystemExit(f'No offline tracking screenshot completion marker; see {log}')
    if offline_tracking and any(marker not in result for marker in ('XR_PLAYER_MODEL_SETUP_OK', 'XR_PLAYER_MODEL_RANGE_OK', 'XR_PLAYER_SETUP_ERRORS_OK')):
        raise SystemExit(f'No model setup screenshot completion marker; see {log}')
    if (download_screenshots or resume_screenshots or manual_resume) and 'XR_PLAYER_DOWNLOAD_SCREENSHOTS_OK' not in result:
        raise SystemExit(f'No network download screenshot completion marker; see {log}')
    if resume_screenshots and 'XR_PLAYER_RESUME_SCREENSHOTS_OK' not in result:
        raise SystemExit(f'No verified HTTP 206 resume screenshot marker; see {log}')
    if manual_resume and 'XR_PLAYER_MANUAL_RESUME_OK' not in result:
        raise SystemExit(f'No verified manual continuation screenshot marker; see {log}')
    if retry_screenshots and any(marker not in result for marker in ('XR_PLAYER_RETRY_SCREENSHOTS_OK', 'XR_PLAYER_TRANSFER_BUDGET_OK')):
        raise SystemExit(f'No retry screenshot completion marker; see {log}')
    if soak_seconds and ('XR_PLAYER_SOAK_OK' not in result or not (captures/'live-soak.txt').is_file()):
        raise SystemExit(f'No Windows player fixture soak confirmation; see {log}')
    for name in names:
        check_screenshot(captures / f'{name}.png')
    # Bind the media and test settings to the package that actually ran. Do not
    # label this as manual Windows, webcam, desktop rendering, or release evidence.
    artifacts = [f'{name}.png' for name in names] + ['player.log']
    if pet_interaction:
        artifacts.append('player-pet-restore.log')
    if soak_seconds:
        artifacts.append('live-soak.txt')
    if local_manual_resume:
        artifacts.append('loopback-range.txt')
    detected = re.search(r'^\s+Version:\s+(Direct3D[^\r\n]*)', result, flags=re.MULTILINE)
    if graphics != 'auto' and (not detected or graphics[-2:] != detected[1].split()[1].split('.')[0]):
        raise SystemExit(f'Requested graphics backend {graphics} not reported; see {log}')
    evidence = {
        'graphics': {'requested': graphics, 'reported': detected[1] if detected else None},
        'schema': 1, 'scope': 'offscreen-fixture', 'version': VERSION,
        'completed_at_utc': datetime.now(timezone.utc).isoformat(timespec='seconds'),
        'package': {'name': package.name, 'sha256': checksum},
        'window_check': {'samples': samples, 'visible_detected': False},
        'settings': {
            'offline_tracking': offline_tracking, 'pet_interaction': pet_interaction,
            'download_screenshots': download_screenshots, 'retry_screenshots': retry_screenshots,
            'resume_screenshots': resume_screenshots, 'manual_resume': manual_resume, 'local_manual_resume': local_manual_resume, 'soak_seconds': soak_seconds,
        },
        'fixture_sha256': {'model': MODEL_HASH, 'portrait': PORTRAIT_HASH} if offline_tracking else ({'model': MODEL_HASH} if local_manual_resume else {}),
        'artifacts': [{'name': name, 'sha256': digest(captures/name)} for name in artifacts],
    }
    draft = captures/'evidence.json.partial'
    draft.write_text(json.dumps(evidence, indent=2, sort_keys=True) + '\n', encoding='utf-8')
    draft.replace(captures/'evidence.json')
    print(f'{len(names)} packaged screenshots; {samples} window checks, none visible; {captures}')


def build(editor, smoke, snapshots=False, candidate=False, verify_download=False, portable=False):
    if not editor.is_file():
        raise SystemExit('Unity 6000.3.10f1 not found. Install/activate the editor or pass --unity PATH.')
    out = ROOT / 'Builds'; out.mkdir(exist_ok=True)
    subprocess.run([str(editor), '-batchmode', '-quit', '-buildTarget', 'Win64', '-projectPath', str(ROOT), '-executeMethod', 'XReality.Editor.StudioProject.BuildWindows', '-logFile', str(out/'unity-build.log')], startupinfo=hidden_startup(), check=True)
    exe = out/PLAYER/'XReality.exe'
    if not exe.is_file():
        raise SystemExit('Player not produced; inspect unity-build.log')
    bundled = exe.parent / BUNDLED_MODEL
    bundled.parent.mkdir(parents=True, exist_ok=True)
    pinned_download(MODEL, MODEL_URL, MODEL_HASH)
    shutil.copyfile(MODEL, bundled)
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


def verify_archive(archive, *, require_model=False, require_presenter=False):
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
                or Path(name).suffix.lower() == '.pdb'
                or (Path(name).suffix.lower() == '.task' and name != BUNDLED_MODEL)
                or parts[-1].casefold() == PORTRAIT.name.casefold()):
            raise SystemExit(f'Debug or QA data in package: {name}')
        names.add(name)
        folded.add(name.casefold())
    required = REQUIRED_PACKAGE_FILES | ({BUNDLED_MODEL, MODEL_LICENSE} if require_model or BUNDLED_MODEL in names else set())
    if require_presenter:
        required = required | {PRESENTER_LICENSE, "AVATAR-ASSETS.md"}
    missing = required - names
    if missing:
        raise SystemExit(f'Package missing required files: {sorted(missing)}')
    if BUNDLED_MODEL in names:
        if archive.getinfo(BUNDLED_MODEL).file_size != 3758596 or hashlib.sha256(archive.read(BUNDLED_MODEL)).hexdigest() != MODEL_HASH:
            raise SystemExit('Bundled Face Landmarker model does not match its pinned size and SHA-256.')
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


def package_checksum(path):
    if not path.is_file() or not path.with_suffix('.sha256').is_file():
        raise SystemExit(f'Package and its .sha256 sidecar are required: {path}')
    fields = path.with_suffix('.sha256').read_text(encoding='ascii').split(maxsplit=1)
    if len(fields) != 2 or fields[1].strip() != path.name or digest(path) != fields[0]:
        raise SystemExit('Portable package checksum mismatch')
    return fields[0]


def verify_package(path):
    package_checksum(path)
    with ZipFile(path) as archive:
        match = re.match(r'XReality-(\d+)\.(\d+)\.(\d+)-Windows-x64-', path.name)
        version = tuple(map(int, match.groups())) if match else (0, 0, 0)
        entries = verify_archive(archive, require_model=version >= (0, 3, 25), require_presenter=version >= (0, 3, 29))
    print(f'Archive verified: SHA-256, CRC, {entries} files, {len(NATIVE_WINDOWS_X64)} native AMD64 images; {path}')


def portable_version(path):
    match = re.fullmatch(r'XReality-(\d+)\.(\d+)\.(\d+)-Windows-x64-portable-preview\.zip', path.name)
    if not match:
        raise SystemExit(f'Expected a versioned Windows portable preview ZIP: {path}')
    return tuple(map(int, match.groups()))


def upgrade_screenshots(source, target):
    old, new = portable_version(source), portable_version(target)
    if not (old >= (0, 3, 24) and old < new):
        raise SystemExit('Upgrade screenshots need an older compatible preview (0.3.24 or later) and a newer target.')
    old_version, new_version = ('.'.join(map(str, version)) for version in (old, new))
    source_record = {'name': source.name, 'sha256': package_checksum(source)}
    target_record = {'name': target.name, 'sha256': package_checksum(target)}
    out = ROOT / 'Builds'
    out.mkdir(exist_ok=True)
    captures = Path(tempfile.mkdtemp(prefix='QA-Upgrade-', dir=out))
    profile = uuid.uuid4().hex  # Only writes to the isolated offscreen QA profile.
    samples = {}
    for package, version, phase, marker, log_name in (
        (source, old_version, 'play', 'XR_PLAYER_PET_PLAY_OK', 'player.log'),
        (target, new_version, 'restore', 'XR_PLAYER_PET_RESTORE_OK', 'player-upgrade-restore.log'),
    ):
        with tempfile.TemporaryDirectory(prefix=f'XReality {phase} {version} ') as temp, ZipFile(package) as archive:
            verify_archive(archive, require_model=portable_version(package) >= (0, 3, 25),
                           require_presenter=portable_version(package) >= (0, 3, 29))
            archive.extractall(temp)
            log = captures / log_name
            samples[phase], result = screenshot_player(Path(temp) / 'XReality.exe', captures, log,
                ['--xr-pet-screenshots', phase, profile], version=version)
            if marker not in result.splitlines():
                raise SystemExit(f'Missing cross-version pet marker: {log}')
    images = sorted(captures.glob('*.png'))
    if not {'PetSleeping.png', 'PetAwakeStroke.png', 'PetChasingToy.png', 'PetRestoredStart.png',
            'PetRestoredWindowVisit.png'} <= {file.name for file in images}:
        raise SystemExit(f'Incomplete upgrade screenshots: {captures}')
    for file in images:
        check_screenshot(file)
    artifacts = [*images, captures / 'player.log', captures / 'player-upgrade-restore.log']
    evidence = {
        'schema': 1, 'scope': 'offscreen-upgrade', 'version': new_version,
        'completed_at_utc': datetime.now(timezone.utc).isoformat(timespec='seconds'),
        'package': target_record, 'upgrade_source': source_record,
        'window_check': {'samples': sum(samples.values()), 'phases': samples, 'visible_detected': False},
        'artifacts': [{'name': path.name, 'sha256': digest(path)} for path in artifacts],
    }
    report = captures / 'evidence.json'
    report.write_text(json.dumps(evidence, ensure_ascii=False, indent=2, sort_keys=True) + '\n', encoding='utf-8')
    verify_evidence(report, target, source)
    print(f'{old_version} -> {new_version}: {len(images)} upgrade screenshots, {sum(samples.values())} window checks, none visible; {captures}')


def verify_evidence(path, package, upgrade_source=None):
    try:
        summary = json.loads(path.read_text(encoding='utf-8'))
    except (OSError, ValueError) as error:
        raise SystemExit(f'Cannot read evidence: {error}') from None
    if not isinstance(summary, dict) or summary.get('schema') != 1 or summary.get('scope') not in ('offscreen-fixture', 'offscreen-upgrade'):
        raise SystemExit('Expected schema 1 offscreen evidence, not a manual test report.')
    is_upgrade = summary['scope'] == 'offscreen-upgrade'
    if is_upgrade != (upgrade_source is not None):
        raise SystemExit('Pass --upgrade-from SOURCE.zip for cross-version evidence only.')
    if is_upgrade:
        if (portable_version(upgrade_source) >= portable_version(package) or
                summary.get('upgrade_source') != {'name': upgrade_source.name, 'sha256': package_checksum(upgrade_source)}):
            raise SystemExit('Evidence does not belong to this upgrade source package.')
    checksum = package_checksum(package)
    if summary.get('package') != {'name': package.name, 'sha256': checksum}:
        raise SystemExit('Evidence does not belong to this package.')
    window = summary.get('window_check', {})
    if (not isinstance(window, dict) or window.get('visible_detected') is not False
            or type(window.get('samples')) is not int or window['samples'] <= 0):
        raise SystemExit('Evidence missing successful window samples.')
    if is_upgrade:
        phases = window.get('phases')
        if (not isinstance(phases, dict) or set(phases) != {'play', 'restore'}
                or any(type(value) is not int or value <= 0 for value in phases.values())
                or sum(phases.values()) != window['samples']):
            raise SystemExit('Missing successful window checks for both upgrade phases.')
    names = set()
    artifacts = summary.get('artifacts')
    if not isinstance(artifacts, list):
        raise SystemExit('Evidence missing the file inventory.')
    for artifact in artifacts:
        if not isinstance(artifact, dict):
            raise SystemExit('Malformed evidence artifact.')
        name = artifact.get('name', '')
        if (not isinstance(name, str) or not name or any(sep in name for sep in ('/', '\\', ':'))
                or name.endswith((' ', '.')) or reserved_windows_name(name) or name.casefold() in names):
            raise SystemExit(f'Invalid evidence artifact name: {name!r}')
        file = path.parent/name
        if file.is_symlink() or not file.is_file() or digest(file) != artifact.get('sha256'):
            raise SystemExit(f'Missing or changed evidence artifact: {name}')
        names.add(name.casefold())
    if not {'home.png', 'petroom.png', 'liveoutput.png', 'player.log'} <= names:
        raise SystemExit('Evidence does not include expected scenes and log.')
    if is_upgrade:
        if not {'petrestoredstart.png', 'petrestoredwindowvisit.png', 'player-upgrade-restore.log'} <= names:
            raise SystemExit('Evidence does not include target upgrade screenshots and log.')
        for filename, phase_package, marker in (
            ('player.log', upgrade_source, 'XR_PLAYER_PET_PLAY_OK'),
            ('player-upgrade-restore.log', package, 'XR_PLAYER_PET_RESTORE_OK'),
        ):
            expected = '.'.join(map(str, portable_version(phase_package)))
            events = (path.parent / filename).read_text(encoding='utf-8', errors='replace').splitlines()
            if marker not in events or f'XR_PLAYER_OFFSCREEN_OK {expected}' not in events:
                raise SystemExit(f'Upgrade evidence missing the pet result or version in {filename}.')
    print(f'Offscreen evidence integrity verified: {len(names)} files; {package.name}; {path}')


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
                if (path.suffix.lower() == '.task' and rel.as_posix() != BUNDLED_MODEL) or path.name.casefold() == PORTRAIT.name.casefold():
                    raise SystemExit(f'Unexpected model or QA portrait in a distribution: {rel}')
                archive.write(path, rel.as_posix())
            archive.write(ROOT/'RELEASE.md', 'README-Preview.md')
            archive.write(ROOT/'QA-WINDOWS.md', 'QA-WINDOWS.md')
            archive.write(ROOT/'AVATAR-ASSETS.md', 'AVATAR-ASSETS.md')
            archive.write(ROOT/'WINDOWS-PREREQUISITES.md', 'WINDOWS-PREREQUISITES.md')
        with ZipFile(draft) as archive:
            verify_archive(archive, require_model=True, require_presenter=True)
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
    parser.add_argument('action',choices=['setup','fixtures','build','screenshots','packaged-screenshots','upgrade-screenshots','verify-package','verify-evidence'])
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
    parser.add_argument('--package', type=Path, help='ZIP to validate (defaults to current portable preview)')
    parser.add_argument('--upgrade-from', type=Path, help='Older portable ZIP for upgrade-screenshots or to verify cross-version evidence')
    parser.add_argument('--evidence', type=Path, help='Path to generated evidence.json for verify-evidence')
    parser.add_argument('--graphics', choices=['auto', 'd3d11', 'd3d12'], help='Offscreen player graphics backend (default: no API override)')
    parser.add_argument('--manual-resume-screenshots', action='store_true', help='Verify continuing a model download after its last automatic attempt')
    parser.add_argument('--local-manual-resume-screenshots', action='store_true', help='Verify HTTPS continuation against an isolated fixture server; no internet')
    args = parser.parse_args()
    if args.local_manual_resume_screenshots and (args.action != 'packaged-screenshots' or args.manual_resume_screenshots or args.resume_screenshots or args.download_screenshots):
        parser.error('--local-manual-resume-screenshots requires packaged-screenshots and cannot combine with other download flows')
    if args.manual_resume_screenshots and (args.action != 'packaged-screenshots' or args.resume_screenshots or args.download_screenshots):
        parser.error('--manual-resume-screenshots requires packaged-screenshots and cannot combine with other online flows')
    if args.graphics is not None and args.action != 'packaged-screenshots':
        parser.error('--graphics requires packaged-screenshots')
    if args.package is not None and args.action not in ('verify-package', 'verify-evidence', 'upgrade-screenshots'):
        parser.error('--package requires verify-package, verify-evidence or upgrade-screenshots')
    if args.action == 'upgrade-screenshots' and args.upgrade_from is None:
        parser.error('upgrade-screenshots requires --upgrade-from SOURCE.zip')
    if args.upgrade_from is not None and args.action not in ('upgrade-screenshots', 'verify-evidence'):
        parser.error('--upgrade-from requires upgrade-screenshots or verify-evidence')
    if (args.evidence is not None) != (args.action == 'verify-evidence'):
        parser.error('verify-evidence requires --evidence PATH')
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
            packaged_screenshots(args.offline_tracking, args.pet_interaction, args.download_screenshots, args.retry_screenshots, args.resume_screenshots, args.soak_seconds or 0, args.graphics or 'auto', args.manual_resume_screenshots, args.local_manual_resume_screenshots)
        raise SystemExit(0)
    if args.action in ('verify-package', 'verify-evidence', 'upgrade-screenshots'):
        if args.smoke or args.snapshots or args.candidate or args.verify_download:
            parser.error('Archive and evidence validation never run or rebuild the application')
        package = args.package or ROOT/'Builds'/f'XReality-{VERSION}-Windows-x64-portable-preview.zip'
        if args.action == 'verify-package':
            verify_package(package)
        elif args.action == 'upgrade-screenshots':
            upgrade_screenshots(args.upgrade_from, package)
        else:
            verify_evidence(args.evidence, package, args.upgrade_from)
        raise SystemExit(0)
    setup()
    if args.action == 'fixtures': fixtures()
    elif args.action == 'build': build(args.unity,args.smoke,args.snapshots,args.candidate,args.verify_download,args.portable)
