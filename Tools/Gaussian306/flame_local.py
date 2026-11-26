"""Load only the expected objects from locally supplied official FLAME assets."""
import pickle
from pathlib import Path
import numpy as np
import scipy.sparse

class ChArray:
    """The official 2023 pickle contains a legacy chumpy array; only use its stored x."""
    def __setstate__(self, state): self.x = state['x']
    def __array__(self, dtype=None, copy=None):
        result = np.asarray(self.x, dtype=dtype)
        return result.copy() if copy else result

class ModelReader(pickle.Unpickler):
    allowed = {
        ('numpy.core.multiarray', '_reconstruct'): __import__("numpy.core.multiarray", fromlist=["_reconstruct"])._reconstruct,
        ('numpy', 'ndarray'): np.ndarray,
        ('numpy', 'dtype'): np.dtype,
        ('scipy.sparse.csc', 'csc_matrix'): scipy.sparse.csc_matrix,
        ('chumpy.ch', 'Ch'): ChArray,
        ('__builtin__', 'set'): set,
    }
    def find_class(self, module, name):
        try: return self.allowed[(module, name)]
        except KeyError: raise pickle.UnpicklingError(f'Unexpected FLAME pickle reference: {module}.{name}') from None

def load_head():
    from flame_model.flame import FlameHead
    # FLAME uses pickle.load in its constructor to read the two supplied model files.
    original = pickle.load
    try:
        def read_pkl(file, **kw):
            if isinstance(getattr(file, 'name', None), str) and Path(file.name).name in ('flame2023.pkl', 'FLAME_masks.pkl'):
                return ModelReader(file, **kw).load()
            return original(file, **kw)  # Embedded landmark data from the pinned upstream repository.
        pickle.load = read_pkl
        return FlameHead(shape_params=300, expr_params=100, include_mask=True, add_teeth=True).eval()
    finally:
        pickle.load = original
