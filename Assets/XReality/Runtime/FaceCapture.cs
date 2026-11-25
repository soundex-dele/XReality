using System;
using System.IO;
using UnityEngine;

namespace XReality
{
    public struct FacePose
    {
        public Quaternion rotation;
        public float left, right, jaw;
        public float smileLeft, smileRight, browLeft, browRight, lipRound, lipPress;
        // X: local yaw, Y: local pitch; both normalized and signed.
        public Vector2 gazeLeft, gazeRight;
        public static FacePose Neutral => new FacePose
        {
            rotation = Quaternion.identity
        };
    }

    // No renderer, microphone, file writer, or network transport has access to camera frames.
    public sealed class FaceCapture : MonoBehaviour
    {
        WebCamTexture cameraInput;
        Color32[] pixels;
        FaceInference inference;
        readonly NeutralCalibration calibration = new NeutralCalibration();
        readonly CaptureMetrics metrics = new CaptureMetrics();
        internal CaptureMetrics Metrics => metrics;
        internal bool CleanOutput { get; set; }
        bool seen, receivedVideo;
        const double FrameStallSeconds = .35, FrameTimeoutSeconds = 6;
        const double FaceGraceSeconds = .12;
        double lastFrame, lastFace = double.NegativeInfinity;
        long previousStamp;
        FacePose lastPose = FacePose.Neutral;
        internal FacePose PoseAt(double now) => !calibration.Pending && now - lastFace < FaceGraceSeconds
            ? lastPose : FacePose.Neutral;
        public bool Calibrating => calibration.Pending;
        public bool Active => cameraInput != null;
        public bool HasFace => Active && seen && Time.realtimeSinceStartupAsDouble - lastFace < FaceGraceSeconds;
        public string Status { get; private set; } = "摄像头未启用";
        internal enum SetupIssue { None, Model, Dependency, Tracking, Camera }
        internal SetupIssue Issue { get; private set; }
        internal const string DependencyStatus = "无法加载面捕组件，请检查 VC++ x64 运行库和 Windows 媒体功能。";

        public void EnableCamera(string device)
        {
            Release();
            bool openingCamera = false;
            try
            {
                inference = new FaceInference(FaceModel.ReadForCapture(out _));
                openingCamera = true;
                cameraInput = new WebCamTexture(device, 640, 480, 30);
                cameraInput.Play();
                BeginFrameSession(Time.realtimeSinceStartupAsDouble);
            }
            catch (Exception e)
            {
                HandleSetupFailure(e, openingCamera);
            }
        }

        internal void HandleSetupFailure(Exception error, bool openingCamera)
        {
            var issue = openingCamera ? SetupIssue.Camera : SetupIssue.Tracking;
            for (var cause = error; cause != null; cause = cause.InnerException)
            {
                if (cause is DllNotFoundException || cause is BadImageFormatException || cause is EntryPointNotFoundException)
                {
                    issue = SetupIssue.Dependency;
                    break;
                }
                if (!openingCamera && (cause is IOException || cause is InvalidDataException || cause is UnauthorizedAccessException))
                    issue = SetupIssue.Model;
            }
            string message = issue switch
            {
                SetupIssue.Dependency => DependencyStatus,
                SetupIssue.Model => "面捕模型无法读取或校验，请重新安装。",
                SetupIssue.Tracking => "面捕组件初始化失败，请确认模型后重试。",
                _ => "摄像头启动失败，请检查权限及设备占用后重试。"
            };
            // Avoid exporting machine-specific paths or identifiers from exception messages.
            Debug.LogWarning($"面捕启动失败 [{issue}]: {error.GetType().Name}");
            Release("setup error");
            Issue = issue;
            Status = message;
        }

        internal void BeginFrameSession(double now)
        {
            lastFrame = now;
            receivedVideo = false;
            metrics.Begin(now);
            Status = "正在等待摄像头画面…";
        }

        internal void NoteVideoFrame(double now)
        {
            lastFrame = now;
            receivedVideo = true;
        }

        // No new frame is not the same as a new frame without a face.
        internal void OnMissingVideoFrame(double now)
        {
            if (!metrics.Running) return;
            double gap = now - lastFrame;
            if (gap >= FrameTimeoutSeconds)
            {
                bool disconnected = receivedVideo;
                Release(disconnected ? "camera stalled" : "camera startup timeout", now);
                Issue = SetupIssue.Camera;
                Status = disconnected ? "摄像头画面已中断，检查设备后重试" : "未收到画面，请检查相机权限或设备占用后重试";
            }
            else if (receivedVideo && gap >= FrameStallSeconds)
            {
                metrics.Track(false);
                calibration.Interrupt();
                ClearTrackedFace();
                Status = "摄像头画面暂停，正在等待恢复…";
            }
        }

        public void Calibrate()
        {
            if (!HasFace)
                return;
            calibration.Begin();
            Status = "请正视镜头，放松闭嘴 · 可自然眨眼";
        }

        void Update()
        {
            if (!Active)
                return;
            double now = Time.realtimeSinceStartupAsDouble;
            metrics.UpdateFrame(now, CleanOutput && Screen.width == 1280 && Screen.height == 720, !Application.isFocused);
            metrics.SampleMemory(now);
            if (!cameraInput.didUpdateThisFrame || cameraInput.width <= 16 || cameraInput.height <= 16)
            {
                OnMissingVideoFrame(now);
                return;
            }

            NoteVideoFrame(now);
            try
            {
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                int w = cameraInput.width, h = cameraInput.height;
                if (pixels == null || pixels.Length != w*h) pixels = new Color32[w*h];
                pixels = cameraInput.GetPixels32(pixels);
                previousStamp = Math.Max(previousStamp + 1, (long)(now * 1000));
                bool detected = inference.Process(pixels, w, h, !cameraInput.videoVerticallyMirrored, cameraInput.videoRotationAngle, previousStamp, out var pose);
                metrics.Processed((System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency, detected);
                AcceptFaceSample(detected, pose, now);
            }
            catch (Exception e)
            {
                Fail("跟踪中断，可重新启用摄像头", e);
            }
        }

        // Hold the rendered expression through isolated detector misses, but never treat a held
        // pose as a fresh detection for calibration or session metrics.
        internal void AcceptFaceSample(bool detected, FacePose pose, double now)
        {
            seen = detected;
            if (!detected)
            {
                calibration.Interrupt();
                if (calibration.Pending) Status = "请回到画面，校准将重新计时";
                else if (now - lastFace >= FaceGraceSeconds) Status = "等待面部进入画面";
                return;
            }

            lastFace = now;
            calibration.Add(pose, now);
            lastPose = calibration.Apply(pose);
            Status = calibration.Pending
                ? $"正视镜头，放松闭嘴 · {Mathf.FloorToInt(calibration.Progress * 100)}%"
                : calibration.Calibrated ? "已校准 · 正在跟踪" : "已找到面部 · 请校准";
        }

        void ClearTrackedFace()
        {
            seen = false;
            lastPose = FacePose.Neutral;
            lastFace = double.NegativeInfinity;
        }

        void Fail(string message, Exception e)
        {
            Debug.LogWarning(message + " (" + e.GetType().Name + ")");
            Release("capture error");
            Status = message;
        }

        public void Release(string reason = "stopped") => Release(reason, Time.realtimeSinceStartupAsDouble);

        void Release(string reason, double now)
        {
            metrics.End(now, reason);
            var input = cameraInput;
            cameraInput = null;
            if (input)
            {
                try { if (input.isPlaying) input.Stop(); }
                catch (Exception e) { Debug.LogWarning("Camera shutdown: " + e.GetType().Name); }
                finally { Destroy(input); }
            }

            try { inference?.Dispose(); }
            catch (Exception e) { Debug.LogWarning("面捕关闭: " + e.GetType().Name); }
            finally { inference = null; }
            pixels = null;
            calibration.Reset();
            receivedVideo = false;
            previousStamp = 0;
            ClearTrackedFace();
            Issue = SetupIssue.None;
            Status = "摄像头未启用";
        }

        void OnDisable() => Release();
    }
}
