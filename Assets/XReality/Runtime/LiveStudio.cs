using System;
using System.Linq;
using UnityEngine;

namespace XReality
{
    public sealed class LiveStudio : DeskPanel
    {
        // Leave scheduling headroom above the 30 FPS output target. The camera
        // still requests 30 FPS and inference only runs on new camera frames.
        internal const int CleanFrameRateCap = 40;
        public FaceCapture capture;
        public Transform head, torso, eyeLeft, eyeRight;
        public SkinnedMeshRenderer faceSurface, teeth, brows;
        public Camera stageCamera;
        string[] devices = Array.Empty<string>();
        string deviceStatus;
        bool selectedIsUnique;
        int index;
        FaceModel model;
        LiveDeskView view;
        bool clean;
        string reportMessage, propMessage;
        Vector3 bodyBase, camBase;
        float l, r, j, sl, sr, bl, br, round, press;
        internal FacePose? TestPose;
        GaussianPresenter gaussian;
        Gaussian306Live gaussian306;
        StaticGaussianProp staticProp;
        internal const string GaussianFlag = "-avatar-3dgs";
        void Start()
        {
            bodyBase = torso.localPosition;
            var args = Environment.GetCommandLineArgs();
            if (Gaussian306Live.Requested(args)) Start306(args);
            else if (Array.Exists(args, arg => string.Equals(arg, GaussianFlag, StringComparison.OrdinalIgnoreCase)))
                StartGaussianPresenter();
            StartStaticProp(args);
            RefreshInstalledModel();
            RefreshDevices(EnumerateDevices);
            ShowDesk(true);
        }

        internal StaticGaussianProp StartStaticProp(string[] args)
        {
            if (staticProp != null) return staticProp;
            int propArg = Array.FindIndex(args, arg => string.Equals(arg, StaticGaussianProp.Flag, StringComparison.OrdinalIgnoreCase));
            if (propArg >= 0)
            {
                if (StaticGaussianProp.TryLoad(propArg + 1 < args.Length ? args[propArg + 1] : null,
                    stageCamera, torso.gameObject.layer, out staticProp))
                    propMessage = "3DGS 道具：J/L 旋转 · I/K 缩放 · U 复位 · O 显隐";
                else
                {
                    propMessage = "未加载 3DGS 道具，请检查文件和 D3D11。";
                    Debug.LogWarning("XR_XRS_FALLBACK: no valid static prop; keeping the studio and presenter.");
                }
            }
            return staticProp;
        }

        internal bool Start306(string[] args)
        {
            if (gaussian306 != null) return true;
            if (Gaussian306Live.TryStart(args, torso, stageCamera, out gaussian306))
            {
                Debug.Log("XR_GA_306_LIVE_ACTIVE: external research rig");
                return true;
            }
            Debug.LogWarning("XR_GA_306_LIVE_FALLBACK: missing or invalid -ga306-asset, or unsupported graphics; keeping mesh presenter.");
            return false;
        }

        internal bool Refresh306()
        {
            if (gaussian306 == null) return false;
            try
            {
                gaussian306.Refresh(head.localRotation, new Vector4(l, r, j, (sl + sr) * .5f * (1 - .45f * j)));
                return true;
            }
            catch (Exception error)
            {
                gaussian306.Dispose();
                gaussian306 = null;
                Debug.LogWarning("XR_GA_306_LIVE_FALLBACK: pose upload failed; keeping mesh presenter: " + error.GetType().Name);
                return false;
            }
        }

        internal bool StartGaussianPresenter()
        {
            if (gaussian != null) return true;
            var sample = Resources.Load<GaussianPresenterAsset>(GaussianPresenterAsset.ResourcePath);
            if (GaussianPresenter.TryCreate(sample, torso, stageCamera, out gaussian))
            {
                Debug.Log($"XR_3DGS_ACTIVE: {gaussian.Count} splats");
                return true;
            }
            Debug.LogWarning("XR_3DGS_FALLBACK: sample missing, incompatible rig or unsupported D3D11 renderer; keeping mesh presenter.");
            return false;
        }

        static string[] EnumerateDevices() => WebCamTexture.devices.Select(device => device.name).ToArray();
        string SelectedDevice => devices.Length == 0 ? null : devices[index];

        internal bool RefreshDevices(Func<string[]> enumerate, bool cycle = false)
        {
            string previous = SelectedDevice;
            try
            {
                var names = enumerate();
                var current = names.Where(name => !string.IsNullOrEmpty(name)).Distinct().ToArray();
                int preserved = previous == null ? -1 : Array.IndexOf(current, previous);
                bool sameList = devices.SequenceEqual(current);
                index = preserved < 0 ? 0 : cycle && sameList ? (index + 1) % current.Length : preserved;
                devices = current;
                selectedIsUnique = SelectedDevice != null && names.Count(name => name == SelectedDevice) == 1;
                deviceStatus = current.Length == 0 ? "未发现摄像头 · 可刷新设备" : !selectedIsUnique
                    ? "设备同名，请切换或断开重复设备" : null;
                return true;
            }
            catch (Exception error)
            {
                // Don't allow the old list to start capture after an enumeration failure.
                devices = Array.Empty<string>();
                index = 0;
                selectedIsUnique = false;
                deviceStatus = "无法读取摄像头列表，请重试";
                Debug.LogWarning("Camera enumeration: " + error.GetType().Name);
                return false;
            }
        }

        internal void ToggleOutput()
        {
            if (!clean) camBase = stageCamera.transform.position;
            clean = !clean;
            capture.CleanOutput = clean;
            Application.targetFrameRate = clean ? CleanFrameRateCap : DefaultFrameRate;
            if (view != null) view.Visible = !clean;
            stageCamera.backgroundColor = clean ? Color.green : new Color(.25f, .34f, .41f);
            stageCamera.transform.position = clean ? new Vector3(torso.position.x, camBase.y, camBase.z) : camBase;
            Cursor.visible = !clean;
        }

        void Update()
        {
            HandleStudioCommands(Input.GetKeyDown(KeyCode.F9), Input.GetKeyDown(KeyCode.F8), Input.GetKeyDown(KeyCode.Escape));
            if (staticProp != null)
            {
                float seconds = Time.unscaledDeltaTime;
                float yaw = (Input.GetKey(KeyCode.L) ? 1 : 0) - (Input.GetKey(KeyCode.J) ? 1 : 0);
                float zoom = (Input.GetKey(KeyCode.I) ? 1 : 0) - (Input.GetKey(KeyCode.K) ? 1 : 0);
                staticProp.Control(80 * seconds * yaw, seconds * zoom, Input.GetKeyDown(KeyCode.U), Input.GetKeyDown(KeyCode.O));
            }
            if (clean) return;
            if (Input.GetKeyDown(KeyCode.Tab))
                view?.Tab(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            RefreshDesk();
        }

        internal void HandleStudioCommands(bool stop, bool toggle, bool escape)
        {
            // Privacy takes precedence if two shortcuts arrive in the same frame.
            if (stop) StopCamera();
            else if (toggle || (clean && escape)) ToggleOutput();
        }

        void StopCamera()
        {
            if (!capture.Active && !capture.Metrics.Running) return;
            capture.Release();
            RefreshDesk();
        }

        void LateUpdate()
        {
            ApplyFacePose(TestPose ?? (capture.Active ? capture.PoseAt(Time.realtimeSinceStartupAsDouble) : FacePose.Neutral), Time.unscaledDeltaTime);
            torso.localPosition = bodyBase + Vector3.up * (.014f * Mathf.Sin(Time.unscaledTime * 1.8f));
            if (gaussian306 != null) Refresh306();
            else RefreshGaussianPresenter();
            if (staticProp != null)
            {
                try { staticProp.Place(); }
                catch (Exception error)
                {
                    staticProp.Dispose();
                    staticProp = null;
                    propMessage = "3DGS 道具渲染失败，主播不受影响。";
                    Debug.LogWarning("XR_XRS_FALLBACK: prop rendering failed: " + error.GetType().Name);
                }
            }
        }

        internal bool RefreshGaussianPresenter()
        {
            if (gaussian == null) return false;
            try { gaussian.Refresh(); return true; }
            catch (Exception error)
            {
                gaussian.Dispose();
                gaussian = null;
                Debug.LogWarning("XR_3DGS_FALLBACK: runtime refresh failed; keeping mesh presenter: " + error.GetType().Name);
                return false;
            }
        }

        internal void ApplyFacePose(FacePose pose, float seconds)
        {
            if (seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            head.localRotation = Quaternion.Slerp(head.localRotation, BoundHead(pose.rotation), Blend(15, seconds));
            l = SmoothChannel(l, pose.left * 1.25f, seconds, 48, 29);
            r = SmoothChannel(r, pose.right * 1.25f, seconds, 48, 29);
            j = SmoothChannel(j, pose.jaw * 1.4f, seconds, 25, 19);
            sl = SmoothChannel(sl, pose.smileLeft, seconds, 14, 12);
            sr = SmoothChannel(sr, pose.smileRight, seconds, 14, 12);
            bl = SmoothChannel(bl, pose.browLeft, seconds, 20, 16);
            br = SmoothChannel(br, pose.browRight, seconds, 20, 16);
            round = SmoothChannel(round, (pose.lipRound - .06f) * 1.3f, seconds, 26, 21);
            press = SmoothChannel(press, (pose.lipPress - .08f) * 1.4f, seconds, 29, 21);
            ApplyExpressions(l, r, j, sl, sr, bl, br, round, press);
            // Eye direction estimates are noisy during eyelid closure. Keep the
            // last direction until the eyelids reopen, unless tracking resets.
            if (pose.left < .55f && pose.right < .55f)
            {
                var left = SignedGaze(pose.gazeLeft);
                var right = SignedGaze(pose.gazeRight);
                // Favor a coordinated gaze while preserving small per-eye differences.
                var shared = (left + right) * .5f;
                DriveEye(eyeLeft, Vector2.Lerp(shared, left, .3f), seconds);
                DriveEye(eyeRight, Vector2.Lerp(shared, right, .3f), seconds);
            }
        }

        static Vector2 SignedGaze(Vector2 value)
        {
            float Axis(float n)
            {
                if (float.IsNaN(n) || float.IsInfinity(n) || Mathf.Abs(n) < .07f) return 0;
                return Mathf.Sign(n) * Mathf.Clamp01((Mathf.Abs(n) - .07f) / .83f);
            }
            return new Vector2(Axis(value.x), Axis(value.y));
        }

        static void DriveEye(Transform eye, Vector2 gaze, float seconds)
        {
            if (!eye) return;
            var bounded = Vector2.ClampMagnitude(gaze, 1);
            eye.localRotation = Quaternion.Slerp(eye.localRotation,
                Quaternion.Euler(bounded.y * 8, bounded.x * 14, 0), Blend(22, seconds));
        }

        static float Blend(float rate, float seconds) => 1 - Mathf.Exp(-rate * seconds);

        static float SmoothChannel(float current, float value, float seconds, float attack, float release)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) value = 0;
            float target = Mathf.Clamp01(value);
            return Mathf.Lerp(current, target, Blend(target > current ? attack : release, seconds));
        }

        static Quaternion BoundHead(Quaternion raw)
        {
            float norm = Quaternion.Dot(raw, raw);
            if (float.IsNaN(norm) || float.IsInfinity(norm) || norm < .01f) return Quaternion.identity;
            // The authored neck has no clavicle or torso tracking. Keep rotations inside its working range.
            var angles = raw.normalized.eulerAngles;
            var normalized = new Vector3(Mathf.Clamp(Mathf.DeltaAngle(0, angles.x) / 26, -1, 1),
                Mathf.Clamp(Mathf.DeltaAngle(0, angles.y) / 44, -1, 1),
                Mathf.Clamp(Mathf.DeltaAngle(0, angles.z) / 22, -1, 1));
            // Turning and tilting at the same time must share the available neck motion.
            normalized = Vector3.ClampMagnitude(normalized, 1);
            return Quaternion.Euler(normalized.x * 26, normalized.y * 44, normalized.z * 22);
        }

        // Used by the live driver and the editor's deterministic expression fixtures.
        public void ApplyExpressions(float left, float right, float jaw, float smileLeft = 0, float smileRight = 0,
            float browLeft = 0, float browRight = 0, float lipRound = 0, float lipPress = 0)
        {
            left = Mathf.Clamp01(left);
            right = Mathf.Clamp01(right);
            jaw = Mathf.Clamp01(jaw);
            // Keep incompatible lip poses from summing at full weight. An open jaw
            // carries the teeth; compression only wins when the mouth is near closed.
            lipRound = Mathf.Clamp01(lipRound);
            lipPress = Mathf.Clamp01(lipPress) * (1 - lipRound) * (1 - .94f * jaw);
            jaw = Mathf.Max(0, jaw - .26f * lipPress);
            faceSurface.SetBlendShapeWeight(0, left * 100);
            faceSurface.SetBlendShapeWeight(1, right * 100);
            faceSurface.SetBlendShapeWeight(2, jaw * 100);
            teeth.SetBlendShapeWeight(0, jaw * 100);
            faceSurface.SetBlendShapeWeight(7, lipRound * 100);
            faceSurface.SetBlendShapeWeight(8, lipPress * 100);
            float smileLimit = Mathf.Lerp(100, 60, jaw) * (1 - .82f * Mathf.Max(lipRound, lipPress));
            faceSurface.SetBlendShapeWeight(3, Mathf.Clamp01(smileLeft) * smileLimit);
            faceSurface.SetBlendShapeWeight(4, Mathf.Clamp01(smileRight) * smileLimit);
            faceSurface.SetBlendShapeWeight(5, Mathf.Clamp01(browLeft) * 100);
            faceSurface.SetBlendShapeWeight(6, Mathf.Clamp01(browRight) * 100);
            brows.SetBlendShapeWeight(0, Mathf.Clamp01(browLeft) * 100);
            brows.SetBlendShapeWeight(1, Mathf.Clamp01(browRight) * 100);
        }

        internal void ShowDesk(bool interactive)
        {
            if (view != null) return;
            view = new LiveDeskView(stageCamera, transform, interactive,
                BeginModelDownload, () => Application.OpenURL(FaceModel.DownloadUrl),
                OpenModelFolder, RefreshInstalledModel, CancelModelDownload, CycleDevice,
                EnableSelectedCamera, capture.Calibrate, StopCamera, ExportReport,
                ToggleOutput, () => { capture.Release(); Home(); });
            view.Visible = !clean;
            RefreshDesk();
        }

        internal void RefreshInstalledModel()
        {
            if (!model) model = gameObject.AddComponent<FaceModel>();
            model.Refresh();
            RefreshDesk();
        }

        void BeginModelDownload()
        {
            if (!model) return;
            model.BeginDownload();
            RefreshDesk();
        }

        void CancelModelDownload()
        {
            if (!model) return;
            model.Cancel();
            RefreshDesk();
        }

        internal LiveDeskView Desk => view;
        internal void SetDownloadQaFaults(int count) => model.SetQaTransientFailures(count);
        internal int DownloadQaAttempts => model.QaAttempts;
        internal void ArmDownloadQaResume(int attempt = 1) => model.ArmQaResume(attempt);
        internal void ConfigureDownloadQaLoopback(int port, string fingerprint) => model.ConfigureQaLoopback(port, fingerprint);
        internal bool DownloadQaResumed => model.QaResumeUsed;
        internal bool ModelDownloading => model && model.Busy;
        internal string ModelStatus => model ? model.Status : FaceModel.FirstUse;

        internal bool InstalledModelReady => model && model.Ready;
        internal bool DeskOffersCamera => view != null && view.OffersCamera;
        internal bool DeskCanStartCamera => view != null && view.CanStartCamera;

        internal void RefreshDesk()
        {
            bool busy = model && model.Busy;
            bool ready = model && model.Ready && !busy;
            string status = ready ? deviceStatus ?? capture.Status : ModelStatus;
            bool canResume = model && model.CanResume;
            if (busy || canResume) status += $" · {Mathf.FloorToInt(model.Progress * 100)}%";
            view?.Update(ready, busy, canResume, status, SelectedDevice ?? "未发现摄像头",
                capture.Active, !capture.Active && selectedIsUnique, capture.HasFace && !capture.Calibrating,
                !capture.Active && capture.Metrics.Report != null, reportMessage ?? propMessage);
        }

        void CycleDevice()
        {
            if (capture.Active) return;
            RefreshDevices(EnumerateDevices, cycle: true);
            RefreshDesk();
        }

        void EnableSelectedCamera() => TryEnableSelectedCamera(EnumerateDevices, capture.EnableCamera);

        internal void TryEnableSelectedCamera(Func<string[]> enumerate, Action<string> open)
        {
            if (capture.Active || !InstalledModelReady || !selectedIsUnique) return;
            string requested = SelectedDevice;
            // The name shown when the user clicked is the only device they authorized.
            // A revised list may be shown, but should never start an alternate device.
            if (RefreshDevices(enumerate) && selectedIsUnique)
            {
                if (SelectedDevice == requested)
                {
                    reportMessage = null;
                    open(requested);
                    if (capture.Issue == FaceCapture.SetupIssue.Model) RefreshInstalledModel();
                }
                else deviceStatus = "设备已变化，请确认后重新启用";
            }
            RefreshDesk();
        }

        void ExportReport()
        {
            try { OpenFolder(capture.Metrics.Export()); reportMessage = "报告已保存，不含画面或面部数据。"; }
            catch (Exception e) { Debug.LogWarning("Diagnostic export: " + e.GetType().Name); reportMessage="无法打开报告目录。"; }
        }

        void OpenModelFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(FaceModel.CacheFolder);
                OpenFolder(FaceModel.CacheFolder);
            }
            catch (Exception e) { Debug.LogWarning("Could not open model folder: " + e.GetType().Name); }
        }

        static void OpenFolder(string folder) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });

        protected override void OnDestroy()
        {
            staticProp?.Dispose();
            gaussian306?.Dispose();
            gaussian?.Dispose();
            view?.Dispose();
            Cursor.visible = true;
            base.OnDestroy();
        }
    }
}
