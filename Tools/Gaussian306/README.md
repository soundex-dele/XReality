# GaussianAvatars 306 local evaluation

The Windows frozen-frame preview is intentionally separate from the main XReality build. Keep upstream source, FLAME model files, derived geometry and builds out of Git. Use for local, noncommercial evaluation and comply with each upstream license.

1. Clone official GaussianAvatars commit `29b0021308d45c6dce0b522044a4893e189c7a23` into `Builds/GaussianAvatars-306/upstream`. Obtain the official FLAME 2023 with jaw rotation and FLAME Vertex Masks separately; follow upstream `doc/download.md` for placement.
2. With CPU PyTorch, NumPy, SciPy and iopath available, run `python Tools/Gaussian306/bake-frame306.py`. It generates `Assets/XReality/Generated/Gaussian306Preview/Frame306.bytes`.
3. Copy `Frozen306.cs` and `Frozen306.shader` into that generated directory and `Frozen306Editor.cs` into its `Editor` subdirectory. Launch Unity 6000.3.10f1 for this project with `-batchmode -quit -force-d3d11 -executeMethod XReality.LocalPreview.Frozen306Editor.Run`; add `-ga306-build` for a separate Windows build under `Builds/GaussianAvatars-306/Unity-Windows/`.
4. Run `Gaussian306.exe -force-d3d11`. Drag to orbit, scroll to zoom, use G to toggle green background, R to reset, Esc to quit. `-batchmode -ga306-smoke` runs a no-camera player snapshot check. Frame 0 only; no runtime FLAME or live tracking in this preview.

## XReality 实时面捕（本机研究预览）

`306` 是可选后端，不替换原主播，也不包含在普通发布包的数据里。`Live306.xrg` 由你本机下载的官方模型生成，只用于非商业验证，不要分发。

### 直接运行本机已构建版本

```powershell
$dir = 'D:\workspace\XReality\Builds\GaussianAvatars-306'
& "$dir\Live-XReality\Windows\XReality.exe" -force-d3d11 -avatar-ga306 -ga306-asset "$dir\Live306.xrg"
```

进入直播间后手动启用摄像头，闭嘴正视校准，再尝试左右分别眨眼、张嘴、微笑和转头。**F8** 为纯绿幕，**Esc** 返回；**F9** 停止摄像头。能看到 `XR_GA_306_LIVE_ACTIVE` 表示已用官方 `306` 渲染；缺文件、不兼容或运行失效会保留或恢复原网格主播，并记录 `XR_GA_306_LIVE_FALLBACK`。不加 `-avatar-ga306` 不会读取 `Live306.xrg`。

### 重建与无摄像头验证

- `python Tools/Gaussian306/bake-frame306.py --live`：从官方角色和 FLAME 文件生成本地 `Builds/GaussianAvatars-306/Live306.xrg`。保留原绑定、三轴几何和三阶球谐颜色；启动时只读取此二进制资产，不运行 Python、CUDA 或 FLAME。
- Unity 6000.3.10f1 使用 `-batchmode -quit -force-d3d11 -executeMethod XReality.Editor.Gaussian306Build.Windows` 构建独立的 XReality 测试版。新构建如果缺面捕模型，在应用内按提示安装。
- Player 加 `-batchmode --xr-offscreen <截图目录> --xr-offline-tracking <已验证模型> <已验证本地图像>`，再传入正常的 `306` 启动参数。检查 `XR_GA_306_LIVE_QA_OK`、`XR_GA_306_INFERENCE_OK` 和 `XR_PLAYER_OFFSCREEN_OK`。测试不打开摄像头，不输出原始测试人像。

该原型把左右眨眼、下颌和合并后的双侧微笑重定向到四个变形通道，并把已平滑、限幅的头姿作用到颈部以上。暂未做独立视线、眉毛或圆唇/抿嘴映射。肩部及衣领下缘的白边仍然存在；实体摄像头及 OBS 的 1080p/30 fps、音画同步和 30 分钟长测尚未验证。

## 从 nndeploy-flow 导入自己训练的 XRG v2

`platform.TrainGaussianAvatar` 的完整 `.xrg` 可使用同一播放器入口：

```powershell
& 'D:\path\to\XReality.exe' -force-d3d11 -avatar-ga306 -ga306-asset 'D:\path\to\avatar.xrg'
```

先用当前源码重建 **XReality.Editor.Gaussian306Build.Windows**（注意：旧构建无法打开 v2）。视频输入、版本、环境配置和训练约束见 nndeploy-flow 的 `docs/gaussian-avatar-training.md`。`export-avatar.py` 接受本地可信的网格绑定训练快照（`point_cloud.ply` 和 `flame_param.npz`），按指定中性、左闭眼、右闭眼、微笑帧生成 v2；不要向它传入网上下载的任意 pickle 或未授权模型。头颈位姿在导出时中性化，Unity 使用新角色的颈部枢轴施加实时头姿。它仍是 D3D11 非商用研究预览，不用于分发真人资产。

### XRG v2 与无素材测试

文件为小端布局：`306L` 魔数、版本 `2`、高斯点/顶点/三角形数量（均为 32 位整数）、三个 32 位浮点颈部枢轴坐标。后续沿用 v1 的布局：中性顶点、头姿权重、左眼/右眼/下颌/微笑顶点差分、三角形索引、逐高斯绑定索引、各向异性几何和 16 组球谐颜色。播放端上限为 200,000 高斯、20,000 顶点、40,000 三角形；旧 v1 仍固定为 306 的网格与点数。

测试不需要下载 FLAME 或人物模型。使用 Unity `-batchmode -quit -force-d3d11 -executeMethod XReality.GaussianAvatarImportQA.Run`，检查 `XR_XRG_V2_QA_OK` 和 `Builds/QA-XRG-Import/` 截图。仅在本机已有 `Live306.xrg` 时额外检查 `XR_XRG_LEGACY_QA_OK`。测试结果仅说明资产解析、渲染、变形、绿幕和回退正常，不代表真人视频已训练成功。
