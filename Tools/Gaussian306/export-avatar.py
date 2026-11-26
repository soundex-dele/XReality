"""Export the pinned mesh-bound GaussianAvatars snapshot to XReality XRG v2."""
import argparse
import os
from pathlib import Path
import struct
import sys

import numpy as np
from scipy.special import expit
from scipy.spatial.transform import Rotation
import torch

from flame_local import load_head


def bounded(array, low, high):
    a = np.asarray(array, dtype='<f4')
    if not np.all((a >= low) & (a <= high)):
        raise ValueError('Non-finite or out-of-range avatar data')
    return a


def pack_splats(ply, face_count):
    with ply.open('rb') as stream:
        if stream.readline() != b'ply\n' or stream.readline().strip() != b'format binary_little_endian 1.0':
            raise ValueError('Expected binary little-endian GaussianAvatars PLY')
        fields, count = [], None
        for _ in range(128):
            row = stream.readline(256).decode('ascii').split()
            if row[:2] == ['element', 'vertex'] and count is None:
                count = int(row[2])
            elif row[:2] == ['property', 'float'] and len(row) == 3:
                fields.append((row[2], '<f4'))
            elif row == ['end_header']:
                break
            elif row[:1] != ['comment']:
                raise ValueError('Unsupported PLY field')
        else:
            raise ValueError('Missing PLY header terminator')
        dtype = np.dtype(fields)
        if count is None or not 1 <= count <= 200000 or ply.stat().st_size != stream.tell() + count * dtype.itemsize:
            raise ValueError('Invalid or oversized GaussianAvatars snapshot')
        points = np.fromfile(stream, dtype, count)
    binding = points['binding_0']
    if not np.all((binding >= 0) & (binding < face_count) & (binding == np.floor(binding))):
        raise ValueError('Invalid triangle bindings')

    def cols(names):
        return np.column_stack([points[name] for name in names])

    geo = np.zeros((count, 4, 4), dtype='<f4')
    geo[:, 0, :3] = bounded(cols('xyz'), -1000, 1000)
    if not np.isfinite(points['opacity']).all():
        raise ValueError('Invalid opacity')
    geo[:, 0, 3] = expit(points['opacity'])
    with np.errstate(over='raise', invalid='raise'):
        axes = Rotation.from_quat(cols(['rot_1', 'rot_2', 'rot_3', 'rot_0'])).as_matrix()
        axes *= np.exp(cols([f'scale_{i}' for i in range(3)]))[:, None, :]
    geo[:, 1:, :3] = bounded(axes.transpose(0, 2, 1), -1000, 1000)
    sh = np.zeros((count, 16, 4), dtype='<f4')
    sh[:, 0, :3] = bounded(cols([f'f_dc_{i}' for i in range(3)]), -100, 100)
    sh[:, 1:, :3] = bounded(cols([f'f_rest_{i}' for i in range(45)]).reshape(-1, 3, 15).transpose(0, 2, 1), -100, 100)
    return binding.astype('<i4'), geo, sh


def export(upstream, snapshot, output, frames):
    sys.path.insert(0, str(upstream))
    os.chdir(upstream)
    head = load_head()
    neutral, left, right, smile = frames
    with np.load(snapshot / 'flame_param.npz', allow_pickle=False) as motion, torch.no_grad():
        def tensor(key, i=None):
            value = motion[key]
            return torch.as_tensor(value if i is None else value[[i]], dtype=torch.float32)
        shape = tensor('shape')[None]
        offset = tensor('static_offset') + tensor('dynamic_offset', neutral)
        fixed = [torch.zeros((1, 3)), torch.zeros((1, 3)), tensor('jaw_pose', neutral),
                 tensor('eyes_pose', neutral), torch.zeros((1, 3))]

        def evaluate(frame, jaw_open=0, canonical=False):
            args = list(fixed)
            args[2] = args[2].clone()
            args[2][0, 0] += jaw_open
            return head(shape, tensor('expr', frame), *args, static_offset=offset, return_verts_cano=canonical)

        rest, canonical, landmarks = (a[0].numpy() for a in evaluate(neutral, canonical=True))
        neck = head.J_regressor[1].numpy() @ canonical

        def localized(frame, ids, inner, outer):
            posed, _ = evaluate(frame)
            d = np.linalg.norm(rest - landmarks[ids].mean(axis=0), axis=1)
            w = np.clip((outer - d) / (outer - inner), 0, 1)
            return (posed[0].numpy() - rest) * (w*w*(3-2*w))[:, None]

        l = localized(left, list(range(42, 48)), .015, .038)
        r = localized(right, list(range(36, 42)), .015, .038)
        eyeballs = head.mask.get_vid_by_region(['eyeballs']).numpy()
        l[eyeballs] = r[eyeballs] = 0
        j = evaluate(neutral, .29)[0][0].numpy() - rest
        s = localized(smile, list(range(48, 60)), .038, .077)
        if min(np.max(np.linalg.norm(d, axis=1)) for d in (l, r, j, s)) < .0001:
            raise ValueError('Calibration poses do not contain enough distinct expression movement')
        w = np.clip((rest[:, 1] - neck[1] + .032) / .093, 0, 1)
        weights = np.clip(1 - head.lbs_weights[:, 0].numpy(), 0, 1) * w*w*(3-2*w)
    faces = head.faces.numpy().astype('<i4')
    binding, geom, sh = pack_splats(snapshot / 'point_cloud.ply', len(faces))
    if not 3 <= len(rest) <= 20000 or not 1 <= len(faces) <= 40000:
        raise ValueError('Unsupported avatar mesh size')
    temporary = output.with_suffix('.tmp')
    with temporary.open('wb') as f:
        f.write(struct.pack('<5i', 0x4c363033, 2, len(binding), len(rest), len(faces)))
        for array in (bounded(neck, -1, 1), bounded(rest, -1, 1), bounded(weights, 0, 1),
                      *(bounded(d, -.25, .25) for d in (l, r, j, s)), faces, binding, geom, sh):
            f.write(array.tobytes())
    temporary.replace(output)


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    for arg in ('upstream', 'snapshot', 'output'):
        p.add_argument('--' + arg, required=True, type=lambda value: Path(value).resolve())
    p.add_argument('--frames', nargs=4, type=int, required=True, metavar=('NEUTRAL', 'LEFT', 'RIGHT', 'SMILE'))
    a = p.parse_args()
    export(a.upstream, a.snapshot, a.output, a.frames)
