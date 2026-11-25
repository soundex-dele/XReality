"""Local, research-only frozen-frame export of the upstream GaussianAvatars fixture."""
from pathlib import Path
import os, pickle, struct, sys
import numpy as np
import scipy.sparse
from scipy.spatial.transform import Rotation
import torch

base = Path(__file__).resolve().parents[2] / 'Builds/GaussianAvatars-306'
upstream = base / 'upstream'
os.chdir(upstream)
sys.path.insert(0, str(upstream))
from flame_model.flame import FlameHead

class ChArray:
    """The official 2023 pickle contains a legacy chumpy array; only use its stored x."""
    def __setstate__(self, state): self.x = state['x']
    def __array__(self, dtype=None, copy=None):
        result = np.asarray(self.x, dtype=dtype)
        return result.copy() if copy else result

class ModelReader(pickle.Unpickler):
    allowed = {
        ('numpy.core.multiarray', '_reconstruct'): np._core.multiarray._reconstruct,
        ('numpy', 'ndarray'): np.ndarray,
        ('numpy', 'dtype'): np.dtype,
        ('scipy.sparse.csc', 'csc_matrix'): scipy.sparse.csc_matrix,
        ('chumpy.ch', 'Ch'): ChArray,
        ('__builtin__', 'set'): set,
    }
    def find_class(self, module, name):
        try: return self.allowed[(module, name)]
        except KeyError: raise pickle.UnpicklingError(f'Unexpected FLAME pickle reference: {module}.{name}') from None

# FLAME uses pickle.load in its constructor to read the two supplied model files.
original = pickle.load
try:
    def read_pkl(file, **kw):
        if isinstance(getattr(file, 'name', None), str) and Path(file.name).name in ('flame2023.pkl', 'FLAME_masks.pkl'):
            return ModelReader(file, **kw).load()
        return original(file, **kw)  # Embedded landmark data from the pinned upstream repository.
    pickle.load = read_pkl
    head = FlameHead(shape_params=300, expr_params=100, include_mask=True, add_teeth=True).eval()
finally:
    pickle.load = original
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


