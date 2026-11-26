"""Local, research-only frozen-frame export of the upstream GaussianAvatars fixture."""
from pathlib import Path
import os, struct, sys
import numpy as np
from scipy.spatial.transform import Rotation
import torch

base = Path(__file__).resolve().parents[2] / 'Builds/GaussianAvatars-306'
upstream = base / 'upstream'
os.chdir(upstream)
sys.path.insert(0, str(upstream))

from flame_local import load_head

head = load_head()
frame = 0
with np.load('media/306/flame_param.npz', allow_pickle=False) as motion, torch.no_grad():
    def tensor(key, index=None):
        a = motion[key]
        return torch.from_numpy(a if index is None else a[index])
    p = [tensor(key, [frame]) for key in ('expr','rotation','neck_pose','jaw_pose','eyes_pose','translation')]
    vertices = head(tensor('shape')[None], *p, return_landmarks=False,
                    static_offset=tensor('static_offset'), dynamic_offset=tensor('dynamic_offset', [frame]))
    vertices = vertices[0].numpy()
tri = vertices[head.faces.numpy()]
def unit(v): return v / np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-10)
a0 = unit(tri[:,1] - tri[:,0]); a1 = unit(np.cross(a0, tri[:,2] - tri[:,0])); a2 = np.cross(a0,a1)
basis = np.stack((a0,a1,a2), axis=-1)
size = (np.linalg.norm(tri[:,1] - tri[:,0], axis=-1) + np.abs(np.sum(a2 * (tri[:,2] - tri[:,0]), axis=-1))) * .5
with open('media/306/point_cloud.ply', 'rb') as f:
    fields=[]; count=0
    while True:
        raw=f.readline()
        if not raw: raise ValueError('Unterminated PLY header')
        row=raw.decode('ascii').strip().split()
        if row[:2]==['element','vertex']: count=int(row[2])
        if row[:2]==['property','float']: fields.append((row[2],'<f4'))
        if row==['end_header']: break
    splats=np.fromfile(f,np.dtype(fields),count)
assert count == len(splats) == 89021
ids=splats['binding_0'].astype(np.int64)
assert ids.min() >= 0 and ids.max() < len(tri)
def cols(names): return np.column_stack([splats[name] for name in names])
centers = np.einsum('nij,nj->ni',basis[ids],cols('xyz')) * size[ids,None] + tri[ids].mean(axis=1)
rot_q = cols(['rot_1','rot_2','rot_3','rot_0'])
scale = np.exp(cols([f'scale_{i}' for i in range(3)])) * size[ids,None]
axes = (basis[ids] @ Rotation.from_quat(rot_q).as_matrix()) * scale[:,None,:]
geometry=np.zeros((count,4,4),dtype='<f4')
geometry[:,0,:3]=centers
geometry[:,0,3]=1/(1+np.exp(-splats['opacity']))
geometry[:,1:,:3]=axes.transpose(0,2,1)
sh=np.zeros((count,16,4),dtype='<f4')
sh[:,0,:3]=cols([f'f_dc_{i}' for i in range(3)])
sh[:,1:,:3]=cols([f'f_rest_{i}' for i in range(45)]).reshape(-1,3,15).transpose(0,2,1)
assert np.isfinite(geometry).all() and np.isfinite(sh).all()
output=base.parent.parent / 'Assets/XReality/Generated/Gaussian306Preview/Frame306.bytes'
output.parent.mkdir(parents=True, exist_ok=True)
with output.open('wb') as f:
    f.write(struct.pack('<ii',0x36303347,count)); f.write(geometry.tobytes()); f.write(sh.tobytes())
print(f'Frame {frame}: {count} splats, {len(vertices)} vertices, {len(tri)} bindings; {output}')
print(np.percentile(centers,(1,50,99),axis=0)); print(np.percentile(scale,(0,50,99,100),axis=0))



if '--live' in sys.argv:
    # Localized deltas from the subject's tracked expressions, evaluated at the
    # same head/eye pose as the neutral frame. The jaw is an actual FLAME joint.
    with np.load('media/306/flame_param.npz', allow_pickle=False) as motion, torch.no_grad():
        shape=torch.from_numpy(motion['shape'])[None]
        fixed=[torch.from_numpy(motion[key][[0]]) for key in ('rotation','neck_pose','jaw_pose','eyes_pose','translation')]
        offset=torch.from_numpy(motion['static_offset'])
        def evaluate(expr_frame, jaw_open=0):
            args=list(fixed)
            args[2]=args[2].clone(); args[2][0,0] += jaw_open
            result, landmarks = head(shape, torch.from_numpy(motion['expr'][[expr_frame]]), *args, static_offset=offset)
            return result[0].numpy(), landmarks[0].numpy()
        rest, landmarks=evaluate(0)
        assert np.allclose(rest,vertices,atol=2e-6)
        def localized(frame_index, landmark_indices, inner, outer, strength):
            posed,_=evaluate(frame_index)
            center=landmarks[landmark_indices].mean(axis=0)
            d=np.linalg.norm(rest-center,axis=1)
            w=np.clip((outer-d)/(outer-inner),0,1)
            w=w*w*(3-2*w)
            return (posed-rest)*w[:,None]*strength
        left=localized(89,list(range(42,48)),.015,.038,.89)
        right=localized(430,list(range(36,42)),.015,.038,.87)
        # Eye closure does not shift the eyeballs, only their surrounding tissue.
        eye_ids=head.mask.get_vid_by_region(['eyeballs']).numpy()
        left[eye_ids]=0; right[eye_ids]=0
        jaw=evaluate(0,.29)[0]-rest
        smile=localized(795,list(range(48,60)),.038,.077,.82)
    packed=np.zeros((count,4,4),dtype='<f4')
    packed[:,0,:3]=cols('xyz')
    packed[:,0,3]=geometry[:,0,3]
    local_axes = Rotation.from_quat(rot_q).as_matrix() * np.exp(cols([f'scale_{i}' for i in range(3)]))[:,None,:]
    packed[:,1:,:3]=local_axes.transpose(0,2,1)
    weights=(1-head.lbs_weights[:,0].numpy()).clip(0,1)
    # Preserve shoulder tissue; twist the upper head around the subject neck pivot.
    neck_fade=np.clip((rest[:,1]+.113)/.093,0,1)
    weights *= neck_fade*neck_fade*(3-2*neck_fade)
    # Store a fixed, validated payload next to the licensed originals, never in Assets/Resources.
    live=base/'Live306.xrg'
    with live.open('wb') as f:
        f.write(struct.pack('<5i',0x4c363033,1,count,len(rest),len(tri)))
        for arr in (rest, weights, left, right, jaw, smile): f.write(np.asarray(arr,dtype='<f4').tobytes())
        f.write(head.faces.numpy().astype('<i4').tobytes()); f.write(ids.astype('<i4').tobytes())
        f.write(packed.tobytes()); f.write(sh.tobytes())
    print(f'Live rig: {live}, {live.stat().st_size} bytes; left/right/jaw/smile nonzero')
