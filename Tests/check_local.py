"""Windows x64 sanity checks without Unity or camera access. Python 3.9+ stdlib."""
import ctypes as c
import os
from pathlib import Path
import subprocess
import sys
import tarfile
import tempfile
import zipfile
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT/'Tools'))
from project import PLUGIN, MODEL, setup, fixtures

def native(temp):
    path = temp/'mediapipe_c.dll'
    with tarfile.open(PLUGIN) as archive:
        path.write_bytes(archive.extractfile('package/Runtime/Plugins/mediapipe_c.dll').read())
    lib = c.CDLL(str(path))
    ptr, size, status = c.c_void_p, c.c_size_t, c.c_int
    def bind(name, result, *args):
        f = getattr(lib,'TfLite'+name); f.restype = result; f.argtypes = args; return f
    create = bind('ModelCreate',ptr,ptr,size); destroy = bind('ModelDelete',None,ptr)
    interpreter = bind('InterpreterCreate',ptr,ptr,ptr); close = bind('InterpreterDelete',None,ptr)
    allocate = bind('InterpreterAllocateTensors',status,ptr); invoke = bind('InterpreterInvoke',status,ptr)
    tensor = bind('InterpreterGetInputTensor',ptr,ptr,status); nbytes = bind('TensorByteSize',size,ptr)
    write = bind('TensorCopyFromBuffer',status,ptr,ptr,size)
    with zipfile.ZipFile(MODEL) as bundle:
        data = bundle.read('face_detector.tflite'); buf = c.create_string_buffer(data)
        model = create(buf,len(data)); assert model
        cpu = interpreter(model,None); assert cpu
        try:
            assert allocate(cpu)==0
            inp = tensor(cpu,0); n = nbytes(inp); assert 0<n<64*1024*1024
            assert write(inp,c.create_string_buffer(n),n)==0 and invoke(cpu)==0
        finally: close(cpu); destroy(model)
    print('PASS: packaged Win64 DLL executed the actual detector submodel with blank CPU input.')

if __name__=='__main__':
    setup()
    fixtures()
    if '--native-child' in sys.argv:
        native(Path(sys.argv[-1]))
    else:
        with tempfile.TemporaryDirectory(prefix='xr-check-') as scratch:
            t=Path(scratch); exe=t/'pet-check.exe'
            compiler=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
            subprocess.run([str(compiler),'/nologo','/out:'+str(exe),str(ROOT/'Assets/XReality/Runtime/PetBrain.cs'),str(ROOT/'Assets/XReality/Runtime/PlayMemory.cs'),str(ROOT/'Tests/BrainChecks.cs')],check=True)
            subprocess.run([str(exe)],check=True)
            # Separate process releases the DLL before the temporary directory is removed.
            subprocess.run([sys.executable,__file__,'--native-child',scratch],check=True)
