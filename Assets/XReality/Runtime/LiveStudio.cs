using System;
using UnityEngine;

namespace XReality
{
    public sealed class LiveStudio : DeskPanel
    {
        public FaceCapture capture;
        public Transform head, torso, leftEye, rightEye;
        public SkinnedMeshRenderer leftLid, rightLid, lip, faceSurface;
        public Camera stageCamera;
        WebCamDevice[] devices = Array.Empty<WebCamDevice>();
        int index;
        FaceModel model;
        LiveDeskView view;
        bool clean;
        string reportMessage;
        Vector3 bodyBase, camBase;
        float l, r, j;
        internal FacePose? TestPose;
        void Start()
        {
            bodyBase = torso.localPosition;
            RefreshInstalledModel();
            Refresh();
            ShowDesk(true);
        }

        void Refresh()
        {
            devices = WebCamTexture.devices;
            index = Mathf.Clamp(index, 0, Mathf.Max(0, devices.Length - 1));
        }

        internal void ToggleOutput()
        {
            if (!clean) camBase = stageCamera.transform.position;
            clean = !clean;
            capture.CleanOutput = clean;
            if (view != null) view.Visible = !clean;
            stageCamera.backgroundColor = clean ? Color.green : new Color(.25f, .34f, .41f);
            stageCamera.transform.position = clean ? new Vector3(torso.position.x, camBase.y, camBase.z) : camBase;
            Cursor.visible = !clean;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8) || (clean && Input.GetKeyDown(KeyCode.Escape)))
                ToggleOutput();
            if (clean) return;
            if (Input.GetKeyDown(KeyCode.Tab))
                view?.Tab(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            RefreshDesk();
        }

        void LateUpdate()
        {
            ApplyFacePose(TestPose ?? (capture.HasFace ? capture.Pose : FacePose.Neutral), Time.unscaledDeltaTime);
            torso.localPosition = bodyBase + Vector3.up * (.014f * Mathf.Sin(Time.unscaledTime * 1.8f));
        }

        internal void ApplyFacePose(FacePose pose, float seconds)
        {
            float blend = 1 - Mathf.Exp(-14 * seconds);
            head.localRotation = Quaternion.Slerp(head.localRotation, pose.rotation, blend);
            l = Mathf.Lerp(l, Mathf.Clamp01(pose.left * 1.25f), blend);
            r = Mathf.Lerp(r, Mathf.Clamp01(pose.right * 1.25f), blend);
            j = Mathf.Lerp(j, Mathf.Clamp01(pose.jaw * 1.4f), blend);
            ApplyExpressions(l, r, j);
        }

        // Used by the live driver and the editor's deterministic expression fixtures.
        public void ApplyExpressions(float left, float right, float jaw)
        {
            left = Mathf.Clamp01(left);
            right = Mathf.Clamp01(right);
            jaw = Mathf.Clamp01(jaw);
            BlinkEye(leftEye, left);
            BlinkEye(rightEye, right);
            leftLid.SetBlendShapeWeight(0, left * 100);
            rightLid.SetBlendShapeWeight(0, right * 100);
            lip.SetBlendShapeWeight(0, jaw * 100);
            faceSurface.SetBlendShapeWeight(0, jaw * 100);
        }

        static void BlinkEye(Transform eye, float blink)
        {
            if (!eye) return; // Earlier generated scenes can still be opened for reference.
            eye.localScale = new Vector3(1, Mathf.Lerp(1, .015f, blink), 1);
            eye.gameObject.SetActive(blink < .98f);
        }

        internal void ShowDesk(bool interactive)
        {
            if (view != null) return;
            view = new LiveDeskView(stageCamera, transform, interactive,
                BeginModelDownload, () => Application.OpenURL(FaceModel.DownloadUrl),
                OpenModelFolder, RefreshInstalledModel, CancelModelDownload, CycleDevice,
                EnableSelectedCamera, capture.Calibrate, () => capture.Release(), ExportReport,
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
        internal void ArmDownloadQaResume() => model.ArmQaResume();
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
            string status = ready && devices.Length > 0 ? capture.Status : ModelStatus;
            if (busy) status += $" · {Mathf.FloorToInt(model.Progress * 100)}%";
            view?.Update(ready, busy, status, devices.Length == 0 ? "未发现摄像头" : devices[index].name,
                capture.Active, !capture.Active && devices.Length > 0, capture.HasFace && !capture.Calibrating,
                !capture.Active && capture.Metrics.Report != null, reportMessage);
        }

        void CycleDevice()
        {
            Refresh();
            if (devices.Length > 0) index = (index + 1) % devices.Length;
        }

        void EnableSelectedCamera()
        {
            if (capture.Active || devices.Length == 0) return;
            reportMessage = null;
            capture.EnableCamera(devices[index].name);
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
            view?.Dispose();
            Cursor.visible = true;
            base.OnDestroy();
        }
    }
}
