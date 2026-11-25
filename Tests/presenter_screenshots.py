"""Short offline screenshot regression on the loose Windows development build; no ZIP or hardware input."""
import json
from pathlib import Path
import sys
import tempfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Tools'))
from project import (BUNDLED_MODEL, MODEL, MODEL_HASH, PLAYER, PORTRAIT, PORTRAIT_HASH,
                     PRESENTER_LICENSE, ROOT, VERSION, check_screenshot, digest, screenshot_player)


def run():
    builds = ROOT / 'Builds'
    dev = builds / PLAYER
    executable = dev / 'XReality.exe'
    if not executable.is_file():
        raise SystemExit('Build first with Tools/project.py build (without --portable).')
    checks = [(MODEL, MODEL_HASH), (PORTRAIT, PORTRAIT_HASH), (dev / BUNDLED_MODEL, MODEL_HASH)]
    for path, expected in checks:
        if not path.is_file() or digest(path) != expected:
            raise SystemExit(f'Missing or invalid local fixture: {path}')
    if not (dev / PRESENTER_LICENSE).is_file():
        raise SystemExit('Presenter CC0 notice missing from the development player.')
    folder = Path(tempfile.mkdtemp(prefix='QA-RealistDev-', dir=builds))
    log = folder / 'player.log'
    samples, result = screenshot_player(executable, folder, log,
        ['--xr-offline-tracking', str(MODEL), str(PORTRAIT), '--xr-retry-screenshots'])
    for marker in ('XR_PLAYER_HOME_ROUTES_OK', 'XR_PLAYER_CONTROLS_OK', 'XR_PLAYER_OFFLINE_TRACKING_OK',
                   'XR_PLAYER_CALIBRATION_OK', 'XR_PLAYER_CALIBRATION_BLINK_OK', 'XR_PLAYER_CAMERA_SELECTION_OK', 'XR_PLAYER_RETRY_SCREENSHOTS_OK',
                   'XR_PLAYER_LIVE_SAFETY_OK', 'XR_PLAYER_MOTION_RESPONSE_OK', 'XR_PLAYER_FACE_DROPOUT_OK', 'XR_PLAYER_EYE_GAZE_OK', 'XR_PLAYER_LIP_ARTICULATION_OK'):
        if marker not in result:
            raise SystemExit(f'Missing {marker}; see {log}')
    names = ('Home', 'PetRoom', 'LiveOutput', 'LiveTracked', 'LiveTrackingLost', 'LiveTrackingRecovered',
             'LiveCalibrationExpression', 'LiveBundledModelReady', 'LiveControlsRestored',
             'LiveCameraStartupTimeout', 'LiveCameraStalled', 'LivePrivacyStopClean', 'LiveQuickBlink', 'LiveBoundedTurn',
             'LiveBeforeFaceDropout', 'LiveHeldFaceDropout', 'LiveExpiredFaceDropout', 'LiveEyeGaze', 'LiveJawOnly', 'LiveLipRound', 'LiveLipPress')
    for name in names:
        if not (folder / f'{name}.png').is_file():
            raise SystemExit(f'Missing required screenshot: {name}')
    images = sorted(folder.glob('*.png'))
    for path in images:
        check_screenshot(path)
    data = dev / 'XReality_Data'
    binaries = [executable, data / 'Managed/Assembly-CSharp.dll', *(data / f'level{i}' for i in range(3)),
                *sorted(data.glob('sharedassets*.assets*'))]
    report = {'scope': 'loose-development-build', 'version': VERSION, 'window_samples': samples,
              'visible_window_detected': False, 'fixture_sha256': {'model': MODEL_HASH, 'portrait': PORTRAIT_HASH},
              'build_sha256': {path.relative_to(dev).as_posix(): digest(path) for path in binaries},
              'artifacts': {path.name: digest(path) for path in [*images, log]}}
    (folder / 'evidence.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(f'{len(images)} screenshots, {samples} samples with no visible window; {folder}')


if __name__ == '__main__':
    run()
