# GaussianAvatars 306 local evaluation

The Windows frozen-frame preview is intentionally separate from the main XReality build. Keep upstream source, FLAME model files, derived geometry and builds out of Git. Use for local, noncommercial evaluation and comply with each upstream license.

1. Clone official GaussianAvatars commit `29b0021308d45c6dce0b522044a4893e189c7a23` into `Builds/GaussianAvatars-306/upstream`. Obtain the official FLAME 2023 with jaw rotation and FLAME Vertex Masks separately; follow upstream `doc/download.md` for placement.
2. With CPU PyTorch, NumPy, SciPy and iopath available, run `python Tools/Gaussian306/bake-frame306.py`. It generates `Assets/XReality/Generated/Gaussian306Preview/Frame306.bytes`.
3. Copy `Frozen306.cs` and `Frozen306.shader` into that generated directory and `Frozen306Editor.cs` into its `Editor` subdirectory. Launch Unity 6000.3.10f1 for this project with `-batchmode -quit -force-d3d11 -executeMethod XReality.LocalPreview.Frozen306Editor.Run`; add `-ga306-build` for a separate Windows build under `Builds/GaussianAvatars-306/Unity-Windows/`.
4. Run `Gaussian306.exe -force-d3d11`. Drag to orbit, scroll to zoom, use G to toggle green background, R to reset, Esc to quit. `-batchmode -ga306-smoke` runs a no-camera player snapshot check. Frame 0 only; no runtime FLAME or live tracking in this preview.
