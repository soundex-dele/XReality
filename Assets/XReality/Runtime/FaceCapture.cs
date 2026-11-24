using System;
using UnityEngine;

namespace XReality
{
    public struct FacePose
    {
        public Quaternion rotation;
        public float left, right, jaw;
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
        bool seen;
        double lastFrame, lastFace;
        long previousStamp;
        public FacePose Pose { get; private set; } = FacePose.Neutral;
        public bool Calibrating => calibration.Pending;
        public bool Active => cameraInput != null;
        public bool HasFace => Active && seen && Time.realtimeSinceStartupAsDouble - lastFace < .35;
        public string Status { get; private set; } = "摄像头未启用";

        public void EnableCamera(string device)
        {
            Release();
            try
            {
                inference = new FaceInference(FaceModel.ReadVerified(FaceModel.CachePath));
                cameraInput = new WebCamTexture(device, 640, 480, 30);
                cameraInput.Play();
                lastFrame = Time.realtimeSinceStartupAsDouble;
                metrics.Begin(lastFrame);
                Status = "正在等待摄像头画面…";
            }
            catch (Exception e)
            {
                Fail("启动失败，请检查相机权限及模型后重试", e);
            }
        }

        public void Calibrate()
        {
            if (!HasFace)
                return;
            calibration.Begin();
            Status = "请正视镜头，放松闭嘴约一秒";
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
                if (now - lastFrame > .35)
                {
                    seen = false;
                    metrics.Track(false);
                    calibration.Interrupt();
                    Pose = FacePose.Neutral;
                }

                if (now - lastFrame > 6)
                {
                    Release("camera timeout");
                    Status = "未收到画面，请释放摄像头或检查 Windows 相机权限后重试";
                }

                return;
            }

            lastFrame = now;
            try
            {
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                int w = cameraInput.width, h = cameraInput.height;
                if (pixels == null || pixels.Length != w*h) pixels = new Color32[w*h];
                pixels = cameraInput.GetPixels32(pixels);
                previousStamp = Math.Max(previousStamp + 1, (long)(now * 1000));
                seen = inference.Process(pixels, w, h, !cameraInput.videoVerticallyMirrored, cameraInput.videoRotationAngle, previousStamp, out var pose);
                metrics.Processed((System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency, seen);
                if (!seen)
                {
                    Pose = FacePose.Neutral;
                    calibration.Interrupt();
                    Status = calibration.Pending ? "请回到画面，校准将重新计时" : "等待面部进入画面";
                    return;
                }

                lastFace = now;
                calibration.Add(pose, now);
                Pose = calibration.Apply(pose);
                Status = calibration.Pending
                    ? $"正视镜头，放松闭嘴 · {Mathf.FloorToInt(calibration.Progress * 100)}%"
                    : calibration.Calibrated ? "已校准 · 正在跟踪" : "已找到面部 · 请校准";
            }
            catch (Exception e)
            {
                Fail("跟踪中断，可重新启用摄像头", e);
            }
        }

        void Fail(string message, Exception e)
        {
            Debug.LogWarning(message + " (" + e.GetType().Name + ": " + e.Message + ")");
            Release("capture error");
            Status = message;
        }

        public void Release(string reason = "stopped")
        {
            metrics.End(Time.realtimeSinceStartupAsDouble, reason);
            if (cameraInput)
            {
                if (cameraInput.isPlaying)
                    cameraInput.Stop();
                Destroy(cameraInput);
                cameraInput = null;
            }

            try { inference?.Dispose(); }
            catch (Exception e) { Debug.LogWarning("面捕关闭: " + e.GetType().Name); }
            finally { inference = null; }
            pixels = null;
            calibration.Reset();
            seen = false;
            previousStamp = 0;
            Pose = FacePose.Neutral;
            Status = "摄像头未启用";
        }

        void OnDisable() => Release();
    }
}
