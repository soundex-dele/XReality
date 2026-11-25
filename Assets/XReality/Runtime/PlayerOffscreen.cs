using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace XReality
{
    // Only a standalone batch player with the explicit screenshot switch can enter QA.
    // It uses normal built scenes, but disables the interactive controllers before Start.
    internal sealed class PlayerOffscreen : MonoBehaviour
    {
        internal const string Flag = "--xr-offscreen";
        const string TrackingFixtures = "--xr-offline-tracking";
        const string SoakFlag = "--xr-soak-seconds";
        int soakSeconds;
        const string PetFixtures = "--xr-pet-screenshots";
        string folder, modelPath, portraitPath, petPhase;
        bool downloadScreenshots, retryScreenshots, resumeScreenshots, manualResumeScreenshots;
        int loopbackPort;
        string loopbackFingerprint;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Launch()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, Flag);
            if (index < 0 || !Application.isBatchMode || Application.isEditor) return;
            int tracking = Array.IndexOf(args, TrackingFixtures);
            int loopback = Array.IndexOf(args, "--xr-download-loopback"), port = 0;
            bool invalidLoopback = loopback >= 0 && (loopback + 2 >= args.Length ||
                !int.TryParse(args[loopback + 1], out port) || port < 1024 || port > 65535 ||
                Array.IndexOf(args, "--xr-manual-resume-screenshots") < 0);
            int pet = Array.IndexOf(args, PetFixtures);
            int soak = Array.IndexOf(args, SoakFlag), duration = 0;
            bool invalidSoak = soak >= 0 && (tracking < 0 || soak + 1 >= args.Length ||
                !int.TryParse(args[soak + 1], out duration) || duration < 30 || duration > 1800);
            bool invalidPet = pet >= 0 && (pet + 2 >= args.Length ||
                (args[pet + 1] != "play" && args[pet + 1] != "restore") || !Guid.TryParseExact(args[pet + 2], "N", out _));
            if (index + 1 >= args.Length || invalidPet || invalidSoak || invalidLoopback || (tracking >= 0 && tracking + 2 >= args.Length) ||
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                Array.IndexOf(args, "--xr-smoke") >= 0)
            {
                Debug.LogError("XR_PLAYER_OFFSCREEN: expected a destination and graphics; do not combine with smoke checks.");
                Application.Quit(2);
                return;
            }
            var host = new GameObject("Batch screenshots");
            DontDestroyOnLoad(host);
            var run = host.AddComponent<PlayerOffscreen>();
            run.folder = args[index + 1];
            run.soakSeconds = duration;
            run.retryScreenshots = Array.IndexOf(args, "--xr-retry-screenshots") >= 0;
            run.downloadScreenshots = Array.IndexOf(args, "--xr-download-screenshots") >= 0;
            run.resumeScreenshots = Array.IndexOf(args, "--xr-resume-screenshots") >= 0;
            run.manualResumeScreenshots = Array.IndexOf(args, "--xr-manual-resume-screenshots") >= 0;
            run.loopbackPort = port;
            if (loopback >= 0) run.loopbackFingerprint = args[loopback + 2];
            if (tracking >= 0)
            {
                run.modelPath = args[tracking + 1];
                run.portraitPath = args[tracking + 2];
            }
            if (pet >= 0)
            {
                run.petPhase = args[pet + 1];
                PetRoom.QaProfile = "offscreen-" + args[pet + 2];
                if (run.petPhase == "play") PetRoom.DeleteQaProfile();
            }
            SceneManager.sceneLoaded += run.StopInteractiveScripts;
        }

        void StopInteractiveScripts(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var panel in root.GetComponentsInChildren<DeskPanel>(true))
                {
                    if (petPhase != null && panel is PetRoom pet)
                    {
                        pet.SuppressInput = true;
                        if (pet.chirp) pet.chirp.mute = true;
                    }
                    else panel.enabled = false;
                }
        }

        IEnumerator Start()
        {
            using var sequence = Capture().GetEnumerator();
            while (true)
            {
                object step;
                try
                {
                    if (!sequence.MoveNext()) break;
                    step = sequence.Current;
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                    if (petPhase != null) PetRoom.DeleteQaProfile();
                    Application.Quit(1);
                    yield break;
                }
                yield return step;
            }
            Debug.Log("XR_PLAYER_OFFSCREEN_OK " + Application.version);
            Application.Quit(0);
        }

        System.Collections.Generic.IEnumerable<object> Capture()
        {
            Directory.CreateDirectory(folder);
            var home = FindFirstObjectByType<HomePage>();
            if (!home || home.enabled || SceneManager.GetActiveScene().name != "Home")
                throw new InvalidOperationException("Batch path failed to isolate the Home controls.");
            yield return null;
            if (petPhase != "restore")
            {
                var showcase = GameObject.Find("Home character showcase");
                if (!showcase || !showcase.transform.Find("Cat preview/Showcase cat/Body/Head") ||
                    !showcase.transform.Find("Avatar preview/Showcase avatar/Torso/Neck pivot") ||
                    showcase.GetComponentsInChildren<Collider>().Length != 0 ||
                    FindFirstObjectByType<PetRoom>() || FindFirstObjectByType<LiveStudio>() || FindFirstObjectByType<FaceCapture>())
                    throw new InvalidOperationException("Home previews must be visual only, without mode controllers or colliders.");
                var backdrop = OffscreenFrame.Save(Camera.main, folder, "HomeBackdrop");
                int catPixels = ShowcasePixels(backdrop, 525, 735, 225, 435);
                // Exclude the pedestal; measure the head in its new, smaller showcase framing.
                int avatarPixels = ShowcasePixels(backdrop, 910, 1050, 312, 447);
                if (catPixels < 9000 || avatarPixels < 9000)
                    throw new InvalidOperationException($"Missing Home character render: {catPixels}/{avatarPixels} pixels.");
                Debug.Log($"XR_PLAYER_HOME_SHOWCASE_OK: cat/character {catPixels}/{avatarPixels} foreground pixels");
                home.ShowDesk(false);
                var menu = OffscreenFrame.Save(Camera.main, folder, "Home");
                if (PanelPixelsChanged(backdrop, menu) < 100000 ||
                    menu[410 * OffscreenFrame.Width + 435].g > 100 ||
                    menu[284 * OffscreenFrame.Width + 435].g > 100)
                    throw new InvalidOperationException("Home selectors did not render with the expected dark card treatment.");
                home.SelectPet();
                if (home.Opening == null) throw new InvalidOperationException("The pet entry has no scene action.");
                yield return home.Opening;
            }
            else yield return SceneManager.LoadSceneAsync("PetRoom");
            yield return null;
            var pet = FindFirstObjectByType<PetRoom>();
            if (!pet || pet.enabled != (petPhase != null) || !pet.cat || !pet.pose || !pet.toy || FindFirstObjectByType<FaceCapture>())
                throw new InvalidOperationException("Packaged pet bindings or scene isolation are invalid.");
            if (petPhase == "restore")
            {
                foreach (var step in CapturePetRestore(pet)) yield return step;
                yield break;
            }
            OffscreenFrame.Save(pet.roomCamera, folder, "PetRoom");
            if (petPhase == "play")
                foreach (var step in CapturePetPlay(pet)) yield return step;

            yield return SceneManager.LoadSceneAsync("Home");
            yield return null;
            var returned = FindFirstObjectByType<HomePage>();
            if (!returned || returned.enabled || FindFirstObjectByType<PetRoom>() || FindFirstObjectByType<FaceCapture>())
                throw new InvalidOperationException("Returning to Home did not unload the pet room.");
            returned.ShowDesk(false);
            OffscreenFrame.Save(Camera.main, folder, "HomeReturned");
            returned.SelectLive();
            if (returned.Opening == null) throw new InvalidOperationException("The live entry has no scene action.");
            yield return returned.Opening;
            yield return null;
            var live = FindFirstObjectByType<LiveStudio>();
            if (!live || live.enabled || !live.capture || live.capture.Active || FindFirstObjectByType<PetRoom>())
                throw new InvalidOperationException("Packaged live scene isolation or capture state is invalid.");
            Debug.Log("XR_PLAYER_HOME_ROUTES_OK: rendered menu; pet entry; home return; live entry, camera inactive");
            if (Gaussian306Live.Requested(Environment.GetCommandLineArgs()))
            {
                Capture306(live);
                yield break;
            }
            if (Array.Exists(Environment.GetCommandLineArgs(), arg => string.Equals(arg, LiveStudio.GaussianFlag, StringComparison.OrdinalIgnoreCase)))
            {
                if (modelPath != null || retryScreenshots || downloadScreenshots || petPhase != null)
                    throw new InvalidOperationException("Run the 3DGS pose smoke test separately from other optional QA fixtures.");
                CaptureGaussian(live);
                yield break;
            }
            var stageOnly = OffscreenFrame.Save(live.stageCamera, folder, "LiveStageOnly");
            live.ShowDesk(false); // Real controls, no input module, device enumeration or model setup.
            yield return null;
            var withControls = OffscreenFrame.Save(live.stageCamera, folder, "LiveStudio");
            if (PanelPixelsChanged(stageOnly, withControls) < 100000)
                throw new InvalidOperationException("The retained control panel was not rendered in the live preview.");
            live.ToggleOutput(); // Use the shipped clean-output path, not copied camera values.
            yield return null;
            if (live.capture.Active || live.Desk.Visible || !live.capture.CleanOutput || live.stageCamera.backgroundColor != Color.green ||
                Application.targetFrameRate != LiveStudio.CleanFrameRateCap)
                throw new InvalidOperationException("Clean output did not enable the green stage without capture.");
            var neutral = OffscreenFrame.Save(live.stageCamera, folder, "LiveOutput", true);
            // The new bust extends into the lower left of the framing. The upper
            // panel region must still be pixel-perfect green; canvas visibility is checked above.
            for (int y = 360; y < 680; y++)
                for (int x = 28; x < 372; x++)
                {
                    var pixel = neutral[y * OffscreenFrame.Width + x];
                    if (pixel.r > 2 || pixel.g < 253 || pixel.b > 2)
                        throw new InvalidOperationException("Clean output leaked pixels into the control panel area.");
                }
            if (modelPath != null)
                foreach (var step in CaptureTracking(live, neutral)) yield return step;
            foreach (var step in CaptureMotionResponse(live, neutral)) yield return step;
            foreach (var step in CaptureFaceDropout(live, neutral)) yield return step;
            foreach (var step in CaptureEyeGaze(live, neutral)) yield return step;
            foreach (var step in CaptureLipArticulation(live, neutral)) yield return step;
            if (soakSeconds > 0)
                foreach (var step in CaptureSoak(live, neutral)) yield return step;
            live.ToggleOutput();
            yield return null;
            var restored = OffscreenFrame.Save(live.stageCamera, folder, "LiveControlsRestored");
            int restoredDifference = PanelPixelsChanged(withControls, restored);
            if (live.capture.Active || !live.Desk.Visible || restoredDifference > 500 || Application.targetFrameRate != DeskPanel.DefaultFrameRate)
                throw new InvalidOperationException($"The control panel did not restore cleanly ({restoredDifference} changed pixels).");
            Debug.Log("XR_PLAYER_CONTROLS_OK: preview visible; clean area pure green; controls restored");
            if (modelPath != null)
                foreach (var step in CaptureModelSetup(live, withControls)) yield return step;
            if (retryScreenshots)
                foreach (var step in CaptureRetry(live, withControls)) yield return step;
            if (downloadScreenshots || resumeScreenshots || manualResumeScreenshots)
                foreach (var step in CaptureDownload(live, withControls)) yield return step;
            live.RefreshInstalledModel();
            FaceModel.ReadForCapture(out bool bundled);
            if (!bundled || !live.InstalledModelReady || !live.DeskOffersCamera || live.DeskCanStartCamera || live.capture.Active)
                throw new InvalidOperationException("Bundled model did not complete camera-free first-use setup.");
            yield return null;
            var bundledSetup = OffscreenFrame.Save(live.stageCamera, folder, "LiveBundledModelReady");
            if (PanelPixelsChanged(withControls, bundledSetup) < 1000)
                throw new InvalidOperationException("Bundled setup did not update the preview controls.");
            Debug.Log("XR_PLAYER_BUNDLED_MODEL_OK: verified packaged model selected, offline; camera inactive");
            foreach (var step in CaptureDeviceSelection(live, bundledSetup)) yield return step;
            foreach (var step in CaptureLiveSafety(live)) yield return step;
        }

        void Capture306(LiveStudio live)
        {
            var source = live.torso.GetComponentsInChildren<Renderer>();
            if (live.Start306(new[] { Gaussian306Live.Flag }) || Array.Exists(source, r => r.forceRenderingOff))
                throw new InvalidOperationException("Missing 306 input did not preserve the default avatar.");
            if (!live.Start306(Environment.GetCommandLineArgs())) throw new InvalidOperationException("Local 306 rig did not start.");
            foreach (var renderer in source)
                if (!renderer.forceRenderingOff) throw new InvalidOperationException("Mesh presenter was not hidden.");
            live.ShowDesk(false);
            live.ToggleOutput();
            Color32[] Pose(string name, FacePose pose)
            {
                live.ApplyFacePose(pose, 1);
                if (!live.Refresh306()) throw new InvalidOperationException("306 pose refresh failed.");
                return OffscreenFrame.Save(live.stageCamera, folder, name, true);
            }
            var neutral = Pose("306-Live-Neutral", FacePose.Neutral);
            var left = Pose("306-Live-LeftBlink", new FacePose { rotation = Quaternion.identity, left = 1 });
            var right = Pose("306-Live-RightBlink", new FacePose { rotation = Quaternion.identity, right = 1 });
            var jaw = Pose("306-Live-Jaw", new FacePose { rotation = Quaternion.identity, jaw = .8f });
            var smile = Pose("306-Live-Smile", new FacePose { rotation = Quaternion.identity, smileLeft = 1, smileRight = 1 });
            var turned = Pose("306-Live-Turn", new FacePose { rotation = Quaternion.Euler(4, 24, -3) });
            var idle = Pose("306-Live-Reset", FacePose.Neutral);
            int LeftEye(Color32[] image) => ChangedFacePixels(neutral, image, 640, 749, 399, 466);
            int RightEye(Color32[] image) => ChangedFacePixels(neutral, image, 528, 639, 399, 466);
            int Mouth(Color32[] image) => ChangedFacePixels(neutral, image, 553, 727, 272, 390);
            int l = LeftEye(left), r = RightEye(right), j = Mouth(jaw), s = Mouth(smile), t = ChangedFacePixels(neutral, turned);
            int lCross = RightEye(left), rCross = LeftEye(right), reset = ChangedFacePixels(neutral, idle);
            Debug.Log($"XR_GA_306_POSES: left/right {l}/{r} (cross {lCross}/{rCross}); jaw/smile {j}/{s}; turn {t}; reset {reset}");
            if (l < 100 || r < 100 || lCross > l * .5f || rCross > r * .5f || j < 300 || s < 250 || t < 3000 || reset > 100)
                throw new InvalidOperationException("306 synthetic pose assertions failed.");
            if (modelPath != null)
            {
                using var inference = new FaceInference(FaceModel.ReadVerified(modelPath));
                var portrait = new Texture2D(2,2,TextureFormat.RGBA32,false);
                try
                {
                    if (!portrait.LoadImage(File.ReadAllBytes(portraitPath))) throw new InvalidDataException("Invalid QA portrait.");
                    var pixels = portrait.GetPixels32(); var blank = new Color32[pixels.Length];
                    if (!inference.Process(pixels, portrait.width, portrait.height, true, 0, 33, out var face))
                        throw new InvalidOperationException("306 test fixture was not detected.");
                    live.capture.AcceptFaceSample(true, face, 1);
                    var tracked = Pose("306-Live-OfflineFace", live.capture.PoseAt(1));
                    bool present = inference.Process(blank, portrait.width, portrait.height, true, 0, 66, out var lost);
                    live.capture.AcceptFaceSample(present, lost, 1.2);
                    var dropped = Pose("306-Live-OfflineLost", live.capture.PoseAt(1.2));
                    int detected = ChangedFacePixels(neutral, tracked), released = ChangedFacePixels(neutral, dropped);
                    if (present || detected < 500 || released > 100) throw new InvalidOperationException($"306 fixture tracking {detected}/{released}.");
                    Debug.Log($"XR_GA_306_INFERENCE_OK: actual MediaPipe portrait/loss {detected}/{released} changed pixels");
                }
                finally { Destroy(portrait); }
            }
            const int workload = 90;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < workload; i++)
            {
                float phase = i * .14f;
                live.ApplyFacePose(new FacePose { rotation = Quaternion.Euler(5 * Mathf.Cos(phase), 23 * Mathf.Sin(phase), 0),
                    left = Mathf.Max(0, Mathf.Sin(phase * 3)), jaw = .3f * (1 + Mathf.Sin(phase * 2)) }, 1f / 30);
                if (!live.Refresh306()) throw new InvalidOperationException("306 repeated pose update failed.");
            }
            Debug.Log($"XR_GA_306_CPU: {watch.Elapsed.TotalMilliseconds / workload:F2} ms average pose update and upload (no capture or render)");
            live.ToggleOutput();
            if (live.capture.Active || !live.Desk.Visible || live.capture.CleanOutput)
                throw new InvalidOperationException("306 exercised camera or failed to restore the desk.");
            Debug.Log("XR_GA_306_LIVE_QA_OK: synthetic tracking and green output; no webcam");
        }

        void CaptureGaussian(LiveStudio live)
        {
            if (!live.StartGaussianPresenter()) throw new InvalidOperationException("Packaged 3DGS sample did not start.");
            live.ShowDesk(false);
            live.ToggleOutput();
            if (live.capture.Active || live.Desk.Visible || !live.capture.CleanOutput)
                throw new InvalidOperationException("3DGS clean output did not hide the desk or unexpectedly started capture.");
            Color32[] Pose(string name, FacePose pose)
            {
                live.ApplyFacePose(pose, 1);
                if (!live.RefreshGaussianPresenter()) throw new InvalidOperationException("3DGS pose refresh failed.");
                return OffscreenFrame.Save(live.stageCamera, folder, name, true);
            }
            var neutral = Pose("3DGS-Neutral", FacePose.Neutral);
            var blink = Pose("3DGS-Blink", new FacePose { rotation = Quaternion.identity, left = 1 });
            var jaw = Pose("3DGS-Jaw", new FacePose { rotation = Quaternion.identity, jaw = .72f });
            var turn = Pose("3DGS-Turn", new FacePose { rotation = Quaternion.Euler(3, 22, -4) });
            var recovered = Pose("3DGS-Recovered", FacePose.Neutral);
            int eyes = ChangedFacePixels(neutral, blink, 548, 733, 370, 455);
            int mouth = ChangedFacePixels(neutral, jaw, 550, 730, 272, 377);
            int head = ChangedFacePixels(neutral, turn);
            int reset = ChangedFacePixels(neutral, recovered);
            if (eyes < 100 || mouth < 300 || head < 3000 || reset > 100)
                throw new InvalidOperationException($"Packaged 3DGS pose differences: {eyes}/{mouth}/{head}; reset {reset}.");
            live.ToggleOutput();
            if (!live.Desk.Visible || live.capture.Active || live.capture.CleanOutput)
                throw new InvalidOperationException("3DGS preview controls did not recover.");
            Debug.Log($"XR_PLAYER_3DGS_OK: blink/jaw/turn {eyes}/{mouth}/{head}; neutral restore {reset}; camera inactive");
        }

        System.Collections.Generic.IEnumerable<object> CaptureLipArticulation(LiveStudio live, Color32[] neutral)
        {
            var rig = live.faceSurface;
            int round = rig.sharedMesh.GetBlendShapeIndex("LipRound");
            int press = rig.sharedMesh.GetBlendShapeIndex("LipPress");
            if (round < 0 || press < 0) throw new InvalidOperationException("The live presenter is missing its lip shapes.");
            var vowel = FacePose.Neutral;
            vowel.jaw = .40f;
            live.ApplyFacePose(vowel, 1);
            yield return null;
            var plain = OffscreenFrame.Save(live.stageCamera, folder, "LiveJawOnly", true);
            vowel.lipRound = .93f;
            live.ApplyFacePose(vowel, 1);
            if (rig.GetBlendShapeWeight(round) < 90 || Mathf.Abs(rig.GetBlendShapeWeight(2) - 56) > .1f)
                throw new InvalidOperationException("The round vowel replaced jaw opening instead of layering onto it.");
            yield return null;
            var rounded = OffscreenFrame.Save(live.stageCamera, folder, "LiveLipRound", true);
            int roundPixels = ChangedFacePixels(plain, rounded, 565, 715, 280, 365);
            if (roundPixels < 240 || ChangedFacePixels(plain, rounded, 490, 790, 425, 490) > 40)
                throw new InvalidOperationException($"The rounded vowel did not visibly change only the mouth area: {roundPixels} lip pixels.");

            var stop = FacePose.Neutral;
            stop.jaw = .15f;
            stop.lipPress = .98f;
            live.ApplyFacePose(stop, 1);
            if (rig.GetBlendShapeWeight(press) < 75 || rig.GetBlendShapeWeight(2) > 2 ||
                Mathf.Abs(rig.GetBlendShapeWeight(2) - live.teeth.GetBlendShapeWeight(0)) > .1f || rig.GetBlendShapeWeight(round) > .1f)
                throw new InvalidOperationException("Lip compression did not close the jaw, or oral meshes lost synchronization.");
            yield return null;
            var compressed = OffscreenFrame.Save(live.stageCamera, folder, "LiveLipPress", true);
            int pressPixels = ChangedFacePixels(neutral, compressed, 565, 715, 280, 365);
            if (pressPixels < 180) throw new InvalidOperationException($"The pressed consonant was not visible: {pressPixels} lip pixels.");

            vowel.lipPress = vowel.smileLeft = vowel.smileRight = 1;
            live.ApplyFacePose(vowel, 1);
            if (rig.GetBlendShapeWeight(press) > 1 || rig.GetBlendShapeWeight(3) > 16 || rig.GetBlendShapeWeight(4) > 16)
                throw new InvalidOperationException("Incompatible shapes stacked at full weight with round lips.");
            live.ApplyFacePose(FacePose.Neutral, 1);
            if (rig.GetBlendShapeWeight(round) > .1f || rig.GetBlendShapeWeight(press) > .1f)
                throw new InvalidOperationException("Lip motion did not release in idle.");
            yield return null;
            Debug.Log($"XR_PLAYER_LIP_ARTICULATION_OK: round/press {roundPixels}/{pressPixels} lip pixels; jaw sync, incompatible shapes constrained, neutral release");
        }

        System.Collections.Generic.IEnumerable<object> CaptureEyeGaze(LiveStudio live, Color32[] neutral)
        {
            if (!live.eyeLeft || !live.eyeRight || live.eyeLeft == live.eyeRight)
                throw new InvalidOperationException("The live presenter has no independent eye pivots.");
            var gaze = FacePose.Neutral;
            gaze.gazeLeft = new Vector2(1, .35f);
            gaze.gazeRight = new Vector2(.9f, .35f);
            live.ApplyFacePose(gaze, 1);
            var left = live.eyeLeft.localRotation;
            var right = live.eyeRight.localRotation;
            float leftYaw = Mathf.DeltaAngle(0, left.eulerAngles.y);
            float rightYaw = Mathf.DeltaAngle(0, right.eulerAngles.y);
            if (leftYaw < 10 || leftYaw > 14.01f || rightYaw < 10 || rightYaw > 14.01f ||
                Mathf.DeltaAngle(0, left.eulerAngles.x) <= 1 || Quaternion.Angle(live.head.localRotation, Quaternion.identity) > .1f)
                throw new InvalidOperationException("Gaze did not reach the eyes independently of the head or exceeded the authored limit.");
            yield return null;
            var side = OffscreenFrame.Save(live.stageCamera, folder, "LiveEyeGaze", true);
            int leftPixels = ChangedFacePixels(neutral, side, 646, 728, 420, 467);
            int rightPixels = ChangedFacePixels(neutral, side, 546, 623, 420, 467);
            if (leftPixels < 35 || rightPixels < 35 || ChangedFacePixels(neutral, side, 470, 810, 255, 418) > 30)
                throw new InvalidOperationException($"Eye motion was not localized to both eyes: {leftPixels}/{rightPixels} pixels.");
            gaze.left = 1;
            gaze.gazeLeft *= -1;
            gaze.gazeRight *= -1;
            live.ApplyFacePose(gaze, 1);
            if (Quaternion.Angle(left, live.eyeLeft.localRotation) > .1f || Quaternion.Angle(right, live.eyeRight.localRotation) > .1f)
                throw new InvalidOperationException("A blink changed the previous eye direction.");
            gaze.left = 0;
            live.ApplyFacePose(gaze, 1);
            if (Mathf.DeltaAngle(0, live.eyeLeft.localEulerAngles.y) > -10 || Mathf.DeltaAngle(0, live.eyeRight.localEulerAngles.y) > -10)
                throw new InvalidOperationException("The gaze could not move to the opposite side after a blink.");
            live.ApplyFacePose(FacePose.Neutral, 1);
            if (Quaternion.Angle(Quaternion.identity, live.eyeLeft.localRotation) > .1f || Quaternion.Angle(Quaternion.identity, live.eyeRight.localRotation) > .1f)
                throw new InvalidOperationException("Idle did not restore centered eyes.");
            yield return null;
            Debug.Log($"XR_PLAYER_EYE_GAZE_OK: coordinated eye motion, {leftPixels}/{rightPixels} eye pixels, bounded, stable on blink, recentered on idle");
        }

        System.Collections.Generic.IEnumerable<object> CaptureFaceDropout(LiveStudio live, Color32[] neutral)
        {
            var capture = live.capture;
            const double t = 10, frame = 1.0 / 30;
            var pose = FacePose.Neutral;
            pose.rotation = Quaternion.Euler(4, 23, 3);
            pose.smileRight = .65f;
            capture.AcceptFaceSample(false, FacePose.Neutral, t - frame);
            if (!capture.Status.Contains("等待面部") || capture.PoseAt(t - frame).rotation != Quaternion.identity)
                throw new InvalidOperationException("The detector must start with no held face.");
            capture.AcceptFaceSample(true, pose, t);
            live.ApplyFacePose(capture.PoseAt(t), 1);
            yield return null; // Let the skinned meshes update before rendering the pose.
            var tracked = OffscreenFrame.Save(live.stageCamera, folder, "LiveBeforeFaceDropout", true);
            for (int i = 1; i <= 3; i++)
            {
                double now = t + i * frame;
                capture.AcceptFaceSample(false, FacePose.Neutral, now);
                live.ApplyFacePose(capture.PoseAt(now), (float)frame);
            }
            yield return null;
            var held = OffscreenFrame.Save(live.stageCamera, folder, "LiveHeldFaceDropout", true);
            if (ChangedFacePixels(tracked, held) > 100 || ChangedFacePixels(neutral, held) < 4000 || capture.Status.Contains("等待面部"))
                throw new InvalidOperationException("An isolated detector miss changed the avatar or the tracking label.");
            if (capture.PoseAt(t + 4 * frame).rotation != Quaternion.identity)
                throw new InvalidOperationException("Without a fresh frame the hold did not expire on the render clock.");
            capture.AcceptFaceSample(false, FacePose.Neutral, t + 4 * frame);
            live.ApplyFacePose(capture.PoseAt(t + 4 * frame), (float)frame);
            float returning = Quaternion.Angle(live.head.localRotation, Quaternion.identity);
            if (!capture.Status.Contains("等待面部") || returning < 1 || returning >= 22)
                throw new InvalidOperationException("A sustained miss did not smoothly start returning to idle.");
            live.ApplyFacePose(capture.PoseAt(t + .5), 1);
            yield return null;
            var lost = OffscreenFrame.Save(live.stageCamera, folder, "LiveExpiredFaceDropout", true);
            if (ChangedFacePixels(neutral, lost) > 100)
                throw new InvalidOperationException("The expired face did not return to the neutral render.");
            pose.rotation = Quaternion.Euler(-4, -12, -3);
            capture.AcceptFaceSample(true, pose, t + .6);
            if (Quaternion.Angle(capture.PoseAt(t + .6).rotation, pose.rotation) > .1f || capture.Status.Contains("等待面部"))
                throw new InvalidOperationException("Reacquisition did not immediately feed the new pose to smoothing.");
            capture.Release();
            if (capture.PoseAt(t + .61).rotation != Quaternion.identity || !capture.Status.Contains("未启用"))
                throw new InvalidOperationException("Stopping did not clear held tracking data.");
            Debug.Log("XR_PLAYER_FACE_DROPOUT_OK: isolated misses held; render-clock expiry; smooth idle; fresh reacquisition; clear on stop");
        }

        System.Collections.Generic.IEnumerable<object> CaptureMotionResponse(LiveStudio live, Color32[] neutral)
        {
            var skin = live.faceSurface;
            var blink = FacePose.Neutral;
            blink.left = 1;
            live.ApplyFacePose(FacePose.Neutral, 1);
            for (int i = 0; i < 2; i++) live.ApplyFacePose(blink, 1f / 30);
            float atThirty = skin.GetBlendShapeWeight(0);
            if (atThirty < 90 || skin.GetBlendShapeWeight(1) > .1f)
                throw new InvalidOperationException("Two 30 FPS blink samples did not close the intended eyelid.");
            yield return null;
            var closed = OffscreenFrame.Save(live.stageCamera, folder, "LiveQuickBlink", true);
            if (ChangedFacePixels(neutral, closed) < 450)
                throw new InvalidOperationException("The quick blink did not visibly reach the rig.");
            for (int i = 0; i < 3; i++) live.ApplyFacePose(FacePose.Neutral, 1f / 30);
            if (skin.GetBlendShapeWeight(0) > 8)
                throw new InvalidOperationException("The eyelid stayed shut too long after a quick blink.");
            live.ApplyFacePose(FacePose.Neutral, 1);
            for (int i = 0; i < 4; i++) live.ApplyFacePose(blink, 1f / 60);
            float atSixty = skin.GetBlendShapeWeight(0);
            if (Mathf.Abs(atThirty - atSixty) > 1)
                throw new InvalidOperationException("Blink response changed with the render frame rate over the same elapsed time.");

            var turn = FacePose.Neutral;
            turn.rotation = Quaternion.Euler(-35, 72, 32);
            live.ApplyFacePose(turn, 1);
            var combinedLimit = Quaternion.Euler(new Vector3(-26, 44, 22) / Mathf.Sqrt(3));
            if (Quaternion.Angle(live.head.localRotation, combinedLimit) > .5f)
                throw new InvalidOperationException("Wide head motion exceeded the authored neck range.");
            yield return null;
            var bounded = OffscreenFrame.Save(live.stageCamera, folder, "LiveBoundedTurn", true);
            if (ChangedFacePixels(neutral, bounded) < 4000)
                throw new InvalidOperationException("The bounded turn suppressed meaningful visible motion.");
            live.ApplyFacePose(FacePose.Neutral, 1);
            if (Quaternion.Angle(live.head.localRotation, Quaternion.identity) > .5f)
                throw new InvalidOperationException("The head did not return to neutral after a large turn.");
            Debug.Log($"XR_PLAYER_MOTION_RESPONSE_OK: blink 30/60 FPS {atThirty:F1}/{atSixty:F1}; bounded turn; neutral recovery");
        }

        System.Collections.Generic.IEnumerable<object> CaptureLiveSafety(LiveStudio live)
        {
            var capture = live.capture;
            // Drive the actual session clock with synthetic timestamps, never a capture device.
            double start = Time.realtimeSinceStartupAsDouble - 8;
            capture.BeginFrameSession(start);
            capture.OnMissingVideoFrame(start + 1);
            if (!capture.Status.Contains("等待摄像头") || !capture.Metrics.Running)
                throw new InvalidOperationException("A camera still warming up was reported as stalled.");
            capture.OnMissingVideoFrame(start + 6);
            if (capture.Metrics.Running || capture.Issue != FaceCapture.SetupIssue.Camera ||
                !capture.Metrics.Report.Contains("End reason: camera startup timeout"))
                throw new InvalidOperationException("Startup without a frame did not produce timeout guidance.");
            live.RefreshDesk();
            yield return null;
            var timeout = OffscreenFrame.Save(live.stageCamera, folder, "LiveCameraStartupTimeout");

            capture.BeginFrameSession(start);
            capture.NoteVideoFrame(start + 1);
            capture.OnMissingVideoFrame(start + 1.2);
            if (capture.Status.Contains("暂停")) throw new InvalidOperationException("A short gap produced a false stall.");
            capture.OnMissingVideoFrame(start + 1.5);
            live.RefreshDesk();
            if (!live.Desk.StatusLabel.Contains("画面暂停") || !capture.Metrics.Running || capture.HasFace)
                throw new InvalidOperationException("Camera stall did not enter waiting-for-video state.");
            yield return null;
            var stalled = OffscreenFrame.Save(live.stageCamera, folder, "LiveCameraStalled");
            if (PanelPixelsChanged(timeout, stalled) < 300)
                throw new InvalidOperationException("The stalled view did not distinguish itself from startup timeout.");
            capture.NoteVideoFrame(start + 4.5);
            capture.OnMissingVideoFrame(start + 7);
            if (!capture.Metrics.Running) throw new InvalidOperationException("A fresh video frame did not extend the deadline.");
            capture.OnMissingVideoFrame(start + 10.5);
            if (capture.Metrics.Running || !capture.Status.Contains("已中断") ||
                !capture.Metrics.Report.Contains("End reason: camera stalled"))
                throw new InvalidOperationException("Persistent interruption did not stop the session with a distinct reason.");

            capture.BeginFrameSession(Time.realtimeSinceStartupAsDouble);
            live.HandleStudioCommands(stop: false, toggle: true, escape: false);
            live.HandleStudioCommands(stop: true, toggle: false, escape: true);
            string report = capture.Metrics.Report;
            if (capture.Active || capture.Metrics.Running || !capture.CleanOutput || live.Desk.Visible ||
                live.stageCamera.backgroundColor != Color.green || report == null || !report.Contains("End reason: stopped"))
                throw new InvalidOperationException("Stop command did not close the session in clean output.");
            live.HandleStudioCommands(stop: true, toggle: false, escape: false);
            if (capture.Metrics.Report != report) throw new InvalidOperationException("Repeated stop changed the previous report.");
            yield return null;
            OffscreenFrame.Save(live.stageCamera, folder, "LivePrivacyStopClean", true);
            live.HandleStudioCommands(stop: false, toggle: false, escape: true);
            if (!live.Desk.Visible || capture.CleanOutput || !live.Desk.StatusLabel.Contains("未启用"))
                throw new InvalidOperationException("Preview did not show the camera as off after closing clean output.");
            Debug.Log("XR_PLAYER_LIVE_SAFETY_OK: short gaps, startup timeout, stalled video, privacy stop retains clean output; no hardware");
        }

        System.Collections.Generic.IEnumerable<object> CaptureDeviceSelection(LiveStudio live, Color32[] noDevice)
        {
            const string a = "QA Camera A", b = "QA Camera B";
            live.RefreshDevices(() => new[] { a, b }, cycle: true);
            live.RefreshDesk();
            if (!live.DeskCanStartCamera || live.Desk.DeviceLabel != a)
                throw new InvalidOperationException("First refresh skipped the first camera.");
            live.RefreshDevices(() => new[] { a, b }, cycle: true);
            live.RefreshDesk();
            if (live.Desk.DeviceLabel != b)
                throw new InvalidOperationException("Camera cycling did not select the next distinct device name.");
            live.RefreshDevices(() => new[] { b, a }, cycle: true);
            live.RefreshDesk();
            if (live.Desk.DeviceLabel != b)
                throw new InvalidOperationException("A reordered list did not preserve the selected camera.");

            string opened = null;
            void Remember(string name) => opened = name; // No hardware enumeration or camera open in QA.
            live.TryEnableSelectedCamera(() => new[] { a }, Remember);
            if (opened != null || !live.DeskCanStartCamera || live.Desk.DeviceLabel != a ||
                !live.Desk.StatusLabel.Contains("重新启用"))
                throw new InvalidOperationException("Removed camera was auto-replaced or the desk did not explain how to retry.");
            yield return null;
            var changed = OffscreenFrame.Save(live.stageCamera, folder, "LiveCameraChanged");
            if (PanelPixelsChanged(noDevice, changed) < 500)
                throw new InvalidOperationException("Camera change did not visibly update the controls.");
            live.TryEnableSelectedCamera(() => new[] { a, a }, Remember);
            if (opened != null || live.DeskCanStartCamera || !live.Desk.StatusLabel.Contains("同名"))
                throw new InvalidOperationException("Ambiguous camera names were not rejected.");
            live.RefreshDevices(() => new[] { a });
            live.RefreshDesk();
            live.TryEnableSelectedCamera(() => throw new InvalidOperationException("QA enumeration failure"), Remember);
            if (opened != null || live.DeskCanStartCamera || !live.Desk.StatusLabel.Contains("重试"))
                throw new InvalidOperationException("Enumeration failure leaked a stale selection.");
            yield return null;
            var failed = OffscreenFrame.Save(live.stageCamera, folder, "LiveCameraEnumerationFailed");
            if (PanelPixelsChanged(changed, failed) < 500)
                throw new InvalidOperationException("Camera enumeration error did not visibly update the controls.");
            live.RefreshDevices(() => new[] { a });
            live.RefreshDesk();
            live.TryEnableSelectedCamera(() => new[] { b, a }, Remember);
            if (opened != a || live.capture.Active)
                throw new InvalidOperationException("Retry failed to route to the confirmed device name without camera access.");
            Debug.Log("XR_PLAYER_CAMERA_SELECTION_OK: reorder, removal, ambiguity, enumeration failure, explicit retry; camera inactive");
        }

        System.Collections.Generic.IEnumerable<object> CaptureModelSetup(LiveStudio live, Color32[] initial)
        {
            string sandbox = Path.Combine(Path.GetTempPath(), "XReality model screenshot " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);
            string previous = FaceModel.QaCacheFolder;
            FaceModel.QaCacheFolder = sandbox;
            string testModel = FaceModel.CachePath;
            try
            {
                live.RefreshInstalledModel();
                if (live.InstalledModelReady || live.DeskOffersCamera)
                    throw new InvalidOperationException("Camera controls appeared with no model installed.");
                yield return null;
                var missing = OffscreenFrame.Save(live.stageCamera, folder, "LiveModelMissing");
                if (PanelPixelsChanged(initial, missing) > 500)
                    throw new InvalidOperationException("The missing model screen did not match first use.");

                // Right size, wrong hash: the checksum (not just file length) must reject it.
                var bytes = FaceModel.ReadVerified(modelPath);
                bytes[0] ^= 1;
                File.WriteAllBytes(testModel, bytes);
                live.RefreshInstalledModel();
                if (live.InstalledModelReady || live.DeskOffersCamera)
                    throw new InvalidOperationException("A same-size corrupted model enabled camera controls.");
                yield return null;
                var corrupt = OffscreenFrame.Save(live.stageCamera, folder, "LiveModelCorrupt");
                if (PanelPixelsChanged(missing, corrupt) < 100)
                    throw new InvalidOperationException("No visible invalid-model feedback.");

                // Split a verified fixture at a non-round byte offset. Exercise the same
                // strict range and segment checks used by the downloader, without a network.
                const int offset = 2_000_017;
                string prefix = Path.Combine(sandbox, "prefix.partial");
                string suffix = Path.Combine(sandbox, "suffix.partial");
                bytes[0] ^= 1;
                using (var part = File.Create(prefix)) part.Write(bytes, 0, offset);
                using (var part = File.Create(suffix)) part.Write(bytes, offset, bytes.Length - offset - 1);
                string header = FormattableString.Invariant($"bytes {offset}-{FaceModel.Bytes - 1}/{FaceModel.Bytes}");
                if (!FaceModel.IsExactRange(header, offset) || FaceModel.IsExactRange(header, offset + 1) ||
                    FaceModel.IsExactRange(header.Replace("/", "/*"), offset) || FaceModel.IsExactRange(null, offset))
                    throw new InvalidOperationException("Model Content-Range validation differs from the request.");
                bool refused = false;
                try { FaceModel.JoinSuffix(prefix, suffix, offset); }
                catch (InvalidDataException) { refused = true; }
                if (!refused || new FileInfo(prefix).Length != offset)
                    throw new InvalidOperationException("A truncated suffix must not mutate the model prefix.");
                using (var part = File.Open(suffix, FileMode.Append)) part.WriteByte(bytes[bytes.Length - 1]);
                FaceModel.JoinSuffix(prefix, suffix, offset);
                FaceModel.ReadVerified(prefix);
                File.Delete(suffix);
                File.Delete(testModel);
                File.Move(prefix, testModel);
                Debug.Log("XR_PLAYER_MODEL_RANGE_OK: exact range, no mutation on short suffix, reassembled SHA-256 verified");
                live.RefreshInstalledModel();
                if (!live.InstalledModelReady || !live.DeskOffersCamera || live.DeskCanStartCamera || live.capture.Active)
                    throw new InvalidOperationException("Expected a ready model with camera controls disabled for zero devices.");
                yield return null;
                var installed = OffscreenFrame.Save(live.stageCamera, folder, "LiveModelReadyNoCamera");
                if (PanelPixelsChanged(corrupt, installed) < 1000)
                    throw new InvalidOperationException("The installed-model controls did not update visibly.");
                Debug.Log("XR_PLAYER_MODEL_SETUP_OK: missing -> checksum rejected -> ready, no camera");

                // Simulate a loader error using the shipped failure path. Neither
                // invalid binaries nor a physical camera are needed for this screenshot.
                live.capture.HandleSetupFailure(new TypeInitializationException("Face native library",
                    new DllNotFoundException()), openingCamera: false);
                live.RefreshDesk();
                yield return null;
                var dependency = OffscreenFrame.Save(live.stageCamera, folder, "LiveDependencyMissing");
                if (!live.InstalledModelReady || live.capture.Active || live.capture.Status != FaceCapture.DependencyStatus ||
                    live.capture.Issue != FaceCapture.SetupIssue.Dependency || PanelPixelsChanged(installed, dependency) < 100)
                    throw new InvalidOperationException("Native load failure must keep the valid model and show its own status.");
                live.capture.HandleSetupFailure(new IOException(), openingCamera: true);
                if (live.capture.Issue != FaceCapture.SetupIssue.Camera)
                    throw new InvalidOperationException("Camera I/O errors must not be mistaken for invalid models.");
                live.capture.HandleSetupFailure(new InvalidDataException(), openingCamera: false);
                if (live.capture.Issue != FaceCapture.SetupIssue.Model)
                    throw new InvalidOperationException("Invalid model contents must not be mistaken for a camera error.");
                live.capture.Release();
                live.RefreshDesk();
                Debug.Log("XR_PLAYER_SETUP_ERRORS_OK: dependency, camera, and model errors differentiated without camera use");
            }
            finally
            {
                FaceModel.QaCacheFolder = previous;
                File.Delete(testModel);
                foreach (var partial in Directory.GetFiles(sandbox, "*.partial")) File.Delete(partial);
                Directory.Delete(sandbox);
            }
        }

        System.Collections.Generic.IEnumerable<object> CaptureDownload(LiveStudio live, Color32[] initial)
        {
            // The executable was extracted to a disposable directory by the screenshot runner.
            string previous = FaceModel.QaCacheFolder;
            string sandbox = Path.Combine(Path.GetTempPath(), "XReality download screenshot " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);
            FaceModel.QaCacheFolder = sandbox;
            try
            {
                live.RefreshInstalledModel();
                if (loopbackPort != 0) live.ConfigureDownloadQaLoopback(loopbackPort, loopbackFingerprint);
                live.Desk.ClickDownload();
                if (!live.ModelDownloading || !live.Desk.OffersCancel || live.Desk.OffersDownload)
                    throw new InvalidOperationException("The download control did not open the progress view.");
                yield return null;
                var downloading = OffscreenFrame.Save(live.stageCamera, folder, "LiveModelDownloading");
                live.Desk.ClickCancel();
                if (live.ModelDownloading || !live.Desk.OffersDownload || live.Desk.OffersCancel || Directory.GetFiles(sandbox).Length != 0)
                    throw new InvalidOperationException("Cancellation did not restore retry or remove partial files.");
                var cancelled = OffscreenFrame.Save(live.stageCamera, folder, "LiveModelDownloadCancelled");
                if (PanelPixelsChanged(initial, downloading) < 1000 || PanelPixelsChanged(initial, cancelled) < 100)
                    throw new InvalidOperationException("Download and cancel feedback must be visible.");

                if (resumeScreenshots) live.ArmDownloadQaResume();
                if (manualResumeScreenshots)
                {
                    live.SetDownloadQaFaults(1); // No data in first request.
                    if (loopbackPort == 0) live.ArmDownloadQaResume(2); // The local server truncates the response itself.
                    live.Desk.ClickDownload();
                    foreach (var step in Until(() => !live.ModelDownloading, FaceModel.QaDownloadDeadlineSeconds,
                        "The last attempt did not finish before the manual-continue check."))
                    {
                        live.RefreshDesk();
                        yield return step;
                    }
                    live.RefreshDesk();
                    var waiting = OffscreenFrame.Save(live.stageCamera, folder, "LiveModelWaitingToContinue");
                    var parts = Directory.GetFiles(sandbox, "*.partial");
                    if (live.ModelStatus != FaceModel.WaitingToResume || !live.Desk.OffersDownload ||
                        live.Desk.DownloadTitle != "继续下载模型" || parts.Length != 1 ||
                        new FileInfo(parts[0]).Length <= 0 || PanelPixelsChanged(cancelled, waiting) < 100)
                        throw new InvalidOperationException("The last automatic attempt did not offer a usable manual resume.");
                    Debug.Log($"XR_PLAYER_MANUAL_RESUME_PENDING: {new FileInfo(parts[0]).Length} bytes retained after automatic attempts");
                }
                live.Desk.ClickDownload();
                if (!live.ModelDownloading) throw new InvalidOperationException("Retry did not start.");
                bool sawResumeView = false;
                foreach (var step in Until(() => !live.ModelDownloading, FaceModel.QaDownloadDeadlineSeconds, "Download retry timed out."))
                {
                    live.RefreshDesk();
                    if (resumeScreenshots && !sawResumeView && live.ModelStatus == FaceModel.Resuming)
                    {
                        var resuming = OffscreenFrame.Save(live.stageCamera, folder, "LiveModelResuming");
                        if (PanelPixelsChanged(downloading, resuming) < 100 || !live.Desk.OffersCancel)
                            throw new InvalidOperationException("Resuming must be visible and cancellable.");
                        sawResumeView = true;
                    }
                    yield return step;
                }
                live.RefreshDesk();
                if (!live.InstalledModelReady || !live.DeskOffersCamera || live.DeskCanStartCamera || live.capture.Active ||
                    Directory.GetFiles(sandbox, "*.partial").Length != 0)
                {
                    OffscreenFrame.Save(live.stageCamera, folder, "LiveModelDownloadFailed");
                    throw new InvalidOperationException("Download did not complete safely: " + live.ModelStatus);
                }
                FaceModel.ReadVerified(FaceModel.CachePath);
                OffscreenFrame.Save(live.stageCamera, folder, "LiveModelDownloadInstalled");
                Debug.Log("XR_PLAYER_DOWNLOAD_SCREENSHOTS_OK: progress -> cancel and cleanup -> retry and checksum, no camera");
                if (resumeScreenshots)
                {
                    if (!sawResumeView || !live.DownloadQaResumed)
                        throw new InvalidOperationException("The download completed, but did not verify a real HTTP 206 resume.");
                    Debug.Log("XR_PLAYER_RESUME_SCREENSHOTS_OK: interrupted -> retained -> HTTP 206 -> verified and cleaned");
                }
                if (manualResumeScreenshots)
                {
                    if (!live.DownloadQaResumed)
                        throw new InvalidOperationException("Manual continuation did not produce a verified HTTP 206 resume.");
                    Debug.Log("XR_PLAYER_MANUAL_RESUME_OK: automatic attempts ended, user continued, SHA-256 verified and temporary data cleared");
                }
            }
            finally
            {
                live.Desk.ClickCancel();
                File.Delete(FaceModel.CachePath);
                foreach (var partial in Directory.GetFiles(sandbox, "*.partial")) File.Delete(partial);
                FaceModel.QaCacheFolder = previous;
                Directory.Delete(sandbox);
            }
        }

        static void CheckTransferBudget()
        {
            var slow = new ModelTransferBudget(0);
            if (slow.Expired(12, 1000) || slow.Expired(23, 2000) || slow.Expired(38, 3000) ||
                slow.Expired(52, 4000) || slow.Expired(60, 5000) || slow.Expired(76, 6000) ||
                slow.Expired(89, 7000) || !slow.Expired(90, 8000))
                throw new InvalidOperationException("Slow, progressing transfers need an independent total cap.");
            var stalled = new ModelTransferBudget(0);
            if (stalled.Expired(8, 1000) || stalled.Expired(31, 1000) || !stalled.Expired(32, 1000))
                throw new InvalidOperationException("An unchanged byte count must expire after the idle budget.");
            var empty = new ModelTransferBudget(0);
            if (empty.Expired(23, 0) || !empty.Expired(24, 0))
                throw new InvalidOperationException("A request with no data must not wait for the entire attempt cap.");
            Debug.Log("XR_PLAYER_TRANSFER_BUDGET_OK: slow progress, stall, no data, hard cap");
        }

        System.Collections.Generic.IEnumerable<object> CaptureRetry(LiveStudio live, Color32[] initial)
        {
            CheckTransferBudget();
            string previous = FaceModel.QaCacheFolder;
            string sandbox = Path.Combine(Path.GetTempPath(), "XReality retry screenshot " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);
            FaceModel.QaCacheFolder = sandbox;
            try
            {
                live.RefreshInstalledModel();
                live.SetDownloadQaFaults(2);
                live.Desk.ClickDownload();
                foreach (var step in Until(() => live.ModelStatus == FaceModel.Retrying, 4, "No retry backoff after the first temporary fault.")) yield return step;
                live.RefreshDesk();
                if (!live.Desk.OffersCancel || live.DownloadQaAttempts != 1) throw new InvalidOperationException("Backoff must be cancellable.");
                var backingOff = OffscreenFrame.Save(live.stageCamera, folder, "LiveModelAutoRetry");
                live.Desk.ClickCancel();
                yield return new WaitForSecondsRealtime(1.4f);
                if (live.ModelDownloading || !live.Desk.OffersDownload || live.DownloadQaAttempts != 1)
                    throw new InvalidOperationException("A cancelled retry performed a later attempt.");

                live.SetDownloadQaFaults(2);
                live.Desk.ClickDownload();
                foreach (var step in Until(() => !live.ModelDownloading, 6, "Auto-retry did not finish within its bound.")) yield return step;
                live.RefreshDesk();
                if (live.ModelStatus != FaceModel.RetryExhausted || live.DownloadQaAttempts != 2 || live.InstalledModelReady ||
                    live.capture.Active || !live.Desk.OffersDownload || Directory.GetFiles(sandbox).Length != 0)
                    throw new InvalidOperationException("Exhausted retry did not return to a safe, recoverable state.");
                var exhausted = OffscreenFrame.Save(live.stageCamera, folder, "LiveModelRetryExhausted");
                if (PanelPixelsChanged(initial, backingOff) < 1000 || PanelPixelsChanged(initial, exhausted) < 100)
                    throw new InvalidOperationException("Auto-retry and fallback must be visible in the panel.");
                Debug.Log("XR_PLAYER_RETRY_SCREENSHOTS_OK: temporary failure -> cancellable backoff -> two attempts, fallback");
            }
            finally
            {
                live.RefreshDesk();
                live.Desk.ClickCancel();
                live.SetDownloadQaFaults(0);
                FaceModel.QaCacheFolder = previous;
                Directory.Delete(sandbox);
            }
        }

        static int ShowcasePixels(Color32[] image, int left, int right, int bottom, int top)
        {
            Color32 background = image[0];
            int changed = 0;
            for (int y = bottom; y < top; y++)
            for (int x = left; x < right; x++)
            {
                Color32 pixel = image[y * OffscreenFrame.Width + x];
                if (Mathf.Abs(pixel.r - background.r) + Mathf.Abs(pixel.g - background.g) +
                    Mathf.Abs(pixel.b - background.b) > 55) changed++;
            }
            return changed;
        }

        static int PanelPixelsChanged(Color32[] baseline, Color32[] image)
        {
            int changed = 0;
            for (int y = 35; y < 680; y++)
                for (int x = 28; x < 372; x++)
                {
                    int i = y * OffscreenFrame.Width + x;
                    var a = baseline[i];
                    var b = image[i];
                    if (Mathf.Abs(a.r - b.r) >= 8 || Mathf.Abs(a.g - b.g) >= 8 || Mathf.Abs(a.b - b.b) >= 8)
                        changed++;
                }
            return changed;
        }

        static System.Collections.Generic.IEnumerable<object> Until(Func<bool> ready, double timeout, string error)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + timeout;
            while (!ready())
            {
                if (Time.realtimeSinceStartupAsDouble >= deadline) throw new InvalidOperationException(error);
                yield return null;
            }
        }

        System.Collections.Generic.IEnumerable<object> CapturePetPlay(PetRoom pet)
        {
            const string name = "窗边栗子";
            pet.Rename(name);
            foreach (var step in Until(() => pet.CurrentMood == Mood.Sleep, 14, "Pet did not settle to sleep.")) yield return step;
            yield return new WaitForSeconds(.35f);
            OffscreenFrame.Save(pet.roomCamera, folder, "PetSleeping");
            Ray At(Vector3 world) => pet.roomCamera.ViewportPointToRay(pet.roomCamera.WorldToViewportPoint(world));
            void ClickCat(bool blocked)
            {
                Physics.SyncTransforms();
                Ray ray = At(pet.head.position);
                pet.HandlePointer(ray, true, true, false, blocked);
                pet.HandlePointer(ray, false, false, true, blocked);
            }
            ClickCat(blocked: true);
            if (pet.Bond != 0 || pet.CurrentMood != Mood.Sleep)
                throw new InvalidOperationException("An overlay press reached the pet.");
            for (int i = 0; i < 5; i++) ClickCat(blocked: false);
            if (pet.CurrentMood != Mood.EarFlick || pet.Bond != 1)
                throw new InvalidOperationException("Sleepy strokes did not coalesce into one ear flick.");
            yield return new WaitForSeconds(.28f);
            OffscreenFrame.Save(pet.roomCamera, folder, "PetSleepyStroke");

            foreach (var step in Until(() => pet.CurrentMood == Mood.Watch, 10, "Pet did not wake after resting.")) yield return step;
            ClickCat(blocked: false);
            if (pet.CurrentMood != Mood.Nuzzle || pet.Bond != 2)
                throw new InvalidOperationException("Awake stroke did not trigger a nuzzle.");
            yield return new WaitForSeconds(.42f);
            OffscreenFrame.Save(pet.roomCamera, folder, "PetAwakeStroke");

            for (int i = 0; i < 3; i++)
            {
                var origin = pet.cat.position;
                int before = pet.Memory.Window;
                if (i == 0)
                {
                    Physics.SyncTransforms();
                    var initial = pet.toy.position;
                    var gripOffset = new Vector3(.10f, 0, 0);
                    Ray first = At(initial + gripOffset);
                    Ray final = At(new Vector3(2.45f, .23f, -.95f) + gripOffset);
                    pet.HandlePointer(first, true, true, false, false);
                    pet.HandlePointer(first, false, true, false, false);
                    if (Vector3.Distance(pet.toy.position, initial) > .002f || pet.CurrentMood != Mood.Nuzzle)
                        throw new InvalidOperationException("Grabbing a toy moved it or started chase before dragging.");
                    pet.HandlePointer(final, false, true, false, true);
                    if (Vector3.Distance(pet.toy.position, initial) > .002f)
                        throw new InvalidOperationException("Dragging over an overlay moved the toy.");
                    pet.HandlePointer(final, false, true, false, false);
                    if (Vector3.Distance(pet.toy.position, new Vector3(2.45f, .23f, -.95f)) > .01f)
                        throw new InvalidOperationException("World-space drag ignored the grab offset or lost capture.");
                    pet.HandlePointer(first, false, false, true, true);
                    var released = pet.toy.position;
                    pet.HandlePointer(first, false, true, false, false);
                    if (Vector3.Distance(pet.toy.position, released) > .002f)
                        throw new InvalidOperationException("Release over overlay left a stale toy grab.");
                    Debug.Log("XR_PLAYER_PET_POINTER_OK: world hit, overlay isolation, grab offset and release");
                }
                else pet.OfferToy(2.45f, i % 2 == 0 ? -.95f : 1.05f);
                if (pet.CurrentMood != Mood.Notice || pet.Memory.Window != before)
                    throw new InvalidOperationException("Offering a toy did not enter Notice without changing the ledger.");
                if (i == 0)
                {
                    yield return new WaitForSeconds(.30f);
                    if (pet.CurrentMood != Mood.Notice || Vector3.Distance(pet.cat.position, origin) > .01f)
                        throw new InvalidOperationException("Pet moved before the notice phase finished.");
                    OffscreenFrame.Save(pet.roomCamera, folder, "PetNoticingToy");
                    foreach (var step in Until(() => pet.CurrentMood == Mood.Chase &&
                        Vector3.Distance(pet.cat.position, origin) > .30f, 3, "Pet did not chase the toy.")) yield return step;
                    OffscreenFrame.Save(pet.roomCamera, folder, "PetChasingToy");
                }
                foreach (var step in Until(() => pet.Memory.Window > before, 5, "Pet did not complete chase.")) yield return step;
                if (pet.Memory.Window != before + 1)
                    throw new InvalidOperationException("A completed chase must record only once.");
            }
            if (pet.Bond != 5 || pet.Memory.Favorite != PlayMemory.Zone.Window || pet.CatName != name)
                throw new InvalidOperationException("Unexpected pet profile after play.");
            Debug.Log("XR_PLAYER_PET_PLAY_OK");
        }

        System.Collections.Generic.IEnumerable<object> CapturePetRestore(PetRoom pet)
        {
            try
            {
                if (pet.CatName != "窗边栗子" || pet.Bond != 5 || pet.Memory.Favorite != PlayMemory.Zone.Window)
                    throw new InvalidOperationException("The isolated profile did not survive player restart.");
                var start = pet.cat.position;
                OffscreenFrame.Save(pet.roomCamera, folder, "PetRestoredStart");
                foreach (var step in Until(() => pet.cat.position.x >= start.x + .8f, 6,
                    "The restored pet did not visit the window without an input.")) yield return step;
                OffscreenFrame.Save(pet.roomCamera, folder, "PetRestoredWindowVisit");
                Debug.Log("XR_PLAYER_PET_RESTORE_OK");
            }
            finally { PetRoom.DeleteQaProfile(); }
        }

        // No WebcamTexture or image material. The fixture goes only through local CPU inference.
        System.Collections.Generic.IEnumerable<object> CaptureTracking(LiveStudio live, Color32[] neutral)
        {
            var modelBytes = FaceModel.ReadForCapture(out bool bundled);
            if (!bundled) throw new InvalidOperationException("Inference QA must use the packaged model.");
            using var inference = new FaceInference(modelBytes);
            var portrait = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!portrait.LoadImage(File.ReadAllBytes(portraitPath)))
                    throw new InvalidOperationException("The tracking fixture is not a readable portrait.");
                int w = portrait.width, h = portrait.height;
                var pixels = portrait.GetPixels32();
                var blank = new Color32[w * h];
                uint timestamp = 0;
                bool Infer(Color32[] input, out FacePose pose) =>
                    inference.Process(input, w, h, true, 0, timestamp += 33, out pose);
                if (Infer(blank, out _) || !Infer(pixels, out var face) || !Infer(pixels, out face))
                    throw new InvalidOperationException("The offline model did not acquire the known portrait after blank input.");
                live.ApplyFacePose(face, 1);
                yield return null; // Allow skinning to consume the new weights.
                var tracked = OffscreenFrame.Save(live.stageCamera, folder, "LiveTracked", true);
                if (Infer(blank, out var lost))
                    throw new InvalidOperationException("The offline model reported a face on blank input.");
                live.ApplyFacePose(lost, 1);
                yield return null;
                var idle = OffscreenFrame.Save(live.stageCamera, folder, "LiveTrackingLost", true);
                if (!Infer(pixels, out face))
                    throw new InvalidOperationException("The offline model did not reacquire the portrait.");
                live.ApplyFacePose(face, 1);
                yield return null;
                var recovered = OffscreenFrame.Save(live.stageCamera, folder, "LiveTrackingRecovered", true);
                int trackingPixels = ChangedFacePixels(neutral, tracked);
                int idlePixels = ChangedFacePixels(neutral, idle);
                int recoveredPixels = ChangedFacePixels(neutral, recovered);
                if (trackingPixels < 500 || idlePixels > 100 || recoveredPixels < 500 || live.capture.Active)
                    throw new InvalidOperationException($"Unexpected tracking render changes: {trackingPixels}/{idlePixels}/{recoveredPixels}.");
                Debug.Log($"XR_PLAYER_OFFLINE_TRACKING_OK: tracked/lost/recovered changed pixels {trackingPixels}/{idlePixels}/{recoveredPixels}");

                // The live camera uses the same calibration class. Start with frames
                // derived from the real CPU fixture, then apply a known relative pose.
                var calibration = new NeutralCalibration();
                calibration.Begin();
                void AddFixture(int frames, bool blink = false)
                {
                    for (int i = 0; i < frames; i++)
                    {
                        if (!Infer(pixels, out face)) throw new InvalidOperationException("Fixture disappeared during calibration.");
                        // The still portrait can already be smiling. Use deterministic
                        // nonzero resting scores to assert the new relative channels independently.
                        face.smileLeft = .16f;
                        face.smileRight = .22f;
                        face.browLeft = .12f;
                        face.browRight = .19f;
                        var sample = face;
                        if (blink) sample.left = sample.right = .98f;
                        calibration.Add(sample, timestamp / 1000d);
                    }
                }
                AddFixture(16);
                if (!calibration.Pending || calibration.Progress < .40f)
                    throw new InvalidOperationException("Stable fixture samples did not start the calibration timer.");
                if (Infer(blank, out _)) throw new InvalidOperationException("Calibration blank frame was not rejected.");
                calibration.Interrupt();
                AddFixture(14);
                if (!calibration.Pending || calibration.Calibrated || calibration.Progress >= .65f)
                    throw new InvalidOperationException("Loss of tracking did not clear accumulated calibration time.");
                live.ApplyFacePose(calibration.Apply(face), 1);
                yield return null;
                var pending = OffscreenFrame.Save(live.stageCamera, folder, "LiveCalibrationPending", true);

                float beforeBlink = calibration.Progress;
                AddFixture(4, blink: true);
                if (calibration.Progress != beforeBlink || !calibration.Pending)
                    throw new InvalidOperationException("A short blink reset or advanced neutral calibration.");
                AddFixture(1);
                if (calibration.Progress != beforeBlink)
                    throw new InvalidOperationException("Time in a blink leaked into neutral calibration.");
                AddFixture(17);
                if (calibration.Pending || !calibration.Calibrated)
                    throw new InvalidOperationException("Continuous fixture samples did not complete calibration.");
                var corrected = calibration.Apply(face);
                if (Quaternion.Angle(corrected.rotation, Quaternion.identity) > 2f ||
                    corrected.left > .025f || corrected.right > .025f || corrected.jaw > .025f ||
                    corrected.smileLeft > .025f || corrected.smileRight > .025f ||
                    corrected.browLeft > .025f || corrected.browRight > .025f ||
                    corrected.gazeLeft.magnitude > .06f || corrected.gazeRight.magnitude > .06f ||
                    corrected.lipRound > .04f || corrected.lipPress > .04f)
                    throw new InvalidOperationException("The steady fixture did not calibrate back to rest.");
                live.ApplyFacePose(corrected, 1);
                yield return null;
                var centered = OffscreenFrame.Save(live.stageCamera, folder, "LiveCalibrationNeutral", true);

                face.rotation *= Quaternion.Euler(0, 21, 0);
                face.left = .98f;
                face.jaw = .75f;
                face.smileLeft = .9f;
                face.browRight = .88f;
                var expression = calibration.Apply(face);
                if (Quaternion.Angle(expression.rotation, Quaternion.identity) < 16 || expression.left < .8f ||
                    expression.right > .04f || expression.jaw < .7f || expression.smileLeft < .65f ||
                    expression.browRight < .65f || expression.smileRight > .04f || expression.browLeft > .04f)
                    throw new InvalidOperationException($"Calibration relative expressions: " +
                        $"eye {expression.left:F3}/{expression.right:F3}, jaw {expression.jaw:F3}, " +
                        $"smile {expression.smileLeft:F3}/{expression.smileRight:F3}, brow {expression.browLeft:F3}/{expression.browRight:F3}");
                live.ApplyFacePose(expression, 1);
                yield return null;
                var expressive = OffscreenFrame.Save(live.stageCamera, folder, "LiveCalibrationExpression", true);
                if (live.faceSurface.GetBlendShapeWeight(3) < 35 || live.faceSurface.GetBlendShapeWeight(4) > 4 ||
                    live.brows.GetBlendShapeWeight(1) < 65 || live.brows.GetBlendShapeWeight(0) > 4)
                    throw new InvalidOperationException("Calibrated smile or brow did not reach the intended side of the live rig.");
                int pendingPixels = ChangedFacePixels(neutral, pending);
                int centeredPixels = ChangedFacePixels(neutral, centered);
                int relativePixels = ChangedFacePixels(centered, expressive);
                if (pendingPixels > 100 || centeredPixels > 4500 || relativePixels < 5000 || live.capture.Active)
                    throw new InvalidOperationException($"Unexpected calibration render pixels: {pendingPixels}/{centeredPixels}/{relativePixels}.");
                AssertCalibrationRestarts();
                live.ApplyFacePose(FacePose.Neutral, 1);
                Debug.Log($"XR_PLAYER_CALIBRATION_OK: reset on loss, steady fixture centered, independent synthetic expression; {pendingPixels}/{centeredPixels}/{relativePixels} pixels");

            }
            finally { Destroy(portrait); }
        }

        static void AssertCalibrationRestarts()
        {
            var calibration = new NeutralCalibration();
            var rest = FacePose.Neutral;
            void StartProgress()
            {
                calibration.Begin();
                for (int i = 0; i < 15; i++) calibration.Add(rest, i * .033);
                if (calibration.Progress < .5f) throw new InvalidOperationException("Calibration setup did not progress.");
            }
            void AssertZero(string cause)
            {
                if (!calibration.Pending || calibration.Progress != 0)
                    throw new InvalidOperationException(cause + " did not reset calibration.");
            }
            var blink = rest;
            blink.right = .98f;
            StartProgress();
            for (int i = 15; i <= 23; i++) calibration.Add(blink, i * .033);
            AssertZero("Sustained closure");
            StartProgress();
            var talking = rest;
            talking.jaw = .9f;
            calibration.Add(talking, .495);
            AssertZero("Open mouth");
            StartProgress();
            calibration.Add(rest, .8);
            AssertZero("Stale samples");
            StartProgress();
            for (int i = 1; i <= 9; i++)
            {
                var drift = rest;
                drift.rotation = Quaternion.Euler(0, i, 0);
                calibration.Add(drift, (14 + i) * .033);
            }
            AssertZero("Slow head turn");
            Debug.Log("XR_PLAYER_CALIBRATION_BLINK_OK: brief blink skipped; prolonged closure, mouth motion, stale samples, and head drift reset");
        }

        System.Collections.Generic.IEnumerable<object> CaptureSoak(LiveStudio live, Color32[] neutral)
        {
            var modelBytes = FaceModel.ReadForCapture(out bool bundled);
            if (!bundled) throw new InvalidOperationException("Inference QA must use the packaged model.");
            using var inference = new FaceInference(modelBytes);
            var portrait = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var camera = live.stageCamera;
            bool cameraEnabled = camera.enabled;
            // Fully offscreen: one explicit URP render per tick, reused 720p texture,
            // still PNGs only at checkpoints. No camera input or recording.
            var rt = new RenderTexture(OffscreenFrame.Width, OffscreenFrame.Height, 24,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                if (!portrait.LoadImage(File.ReadAllBytes(portraitPath)) || !rt.Create())
                    throw new InvalidOperationException("Could not initialize the fixture or 720p render target.");
                var input = portrait.GetPixels32();
                var blank = new Color32[input.Length];
                camera.enabled = false;
                var stats = new CaptureMetrics();
                double start = Time.realtimeSinceStartupAsDouble;
                double interval = soakSeconds / 6d;
                int rendered = 0, visible = 0, lost = 0, checkpoint = 0;
                double[] at = { 1d, interval, 2 * interval + 2, 3 * interval, 4 * interval, 5 * interval };
                const int lastCheckpoint = 7;
                long stamp = 0;
                bool reacquired = false;
                stats.Begin(start);
                Debug.Log($"XR_PLAYER_SOAK_START: {soakSeconds}s; CPU fixture {portrait.width}x{portrait.height}; output 1280x720; cap {Application.targetFrameRate} FPS");
                for (;;)
                {
                    double now = Time.realtimeSinceStartupAsDouble;
                    double age = now - start;
                    // Blank input for a three-second interval to exercise smooth fall-back.
                    bool blankFrame = age >= 2 * interval && age < 2 * interval + 3;
                    long before = System.Diagnostics.Stopwatch.GetTimestamp();
                    stamp = Math.Max(stamp + 1, (long)(age * 1000));
                    bool seen = inference.Process(blankFrame ? blank : input, portrait.width, portrait.height,
                        true, 0, stamp, out var pose);
                    stats.Processed((System.Diagnostics.Stopwatch.GetTimestamp() - before) * 1000d /
                        System.Diagnostics.Stopwatch.Frequency, seen);
                    if (seen) { visible++; reacquired |= lost > 0; }
                    else if (blankFrame) lost++;
                    live.ApplyFacePose(pose, Time.unscaledDeltaTime);
                    OffscreenFrame.Render(camera, rt, rebuildCanvas: false);
                    rendered++;
                    double after = Time.realtimeSinceStartupAsDouble;
                    stats.UpdateFrame(after, clean: true);
                    stats.SampleMemory(after);
                    if ((checkpoint < at.Length && age >= at[checkpoint]) ||
                        (checkpoint == at.Length && stats.LongestCleanSeconds >= soakSeconds))
                    {
                        int id = ++checkpoint;
                        var frame = OffscreenFrame.SaveRendered(rt, folder, $"LiveSoak{id:00}", true);
                        int changed = ChangedFacePixels(neutral, frame);
                        if ((id == 3 && changed > 100) || (id != 3 && changed < 500))
                            throw new InvalidOperationException($"Unexpected tracked/idle fixture state at soak checkpoint {id}: {changed} pixels.");
                        Debug.Log(FormattableString.Invariant($"XR_PLAYER_SOAK_CHECKPOINT: {id} at {age:F2}s; renders {rendered}, portrait {visible}, blank {lost}"));
                    }
                    if (checkpoint == lastCheckpoint) break;
                    if (live.capture.Active) throw new InvalidOperationException("Fixture soak must not activate a camera.");
                    yield return null;
                }
                double end = Time.realtimeSinceStartupAsDouble;
                stats.End(end, "offscreen fixture completed");
                double fps = stats.CleanMeanFps;
                File.WriteAllText(Path.Combine(folder, "live-soak.txt"),
                    "Offline CPU fixture + explicit 720p URP draws; not webcam or desktop presentation data.\n" +
                    stats.Report.Replace("Camera", "Fixture").Replace("camera", "fixture"));
                if (stats.MemorySamples < soakSeconds / 10)
                    throw new InvalidOperationException($"Insufficient application working-set samples: {stats.MemorySamples}.");
                if (visible < rendered * .85 || lost == 0 || !reacquired || live.capture.Active || fps < 30 ||
                    stats.LongestCleanSeconds < soakSeconds)
                    throw new InvalidOperationException(FormattableString.Invariant($"Soak underperformed: {fps:F2} FPS, {visible}/{rendered} recognized, {lost} blank, reacquired={reacquired}."));
                Debug.Log(FormattableString.Invariant($"XR_PLAYER_SOAK_OK: {stats.LongestCleanSeconds:F2}s continuous clean; {rendered} renders; {fps:F2} FPS; {visible} faces; {lost} blank frames"));
            }
            finally
            {
                camera.enabled = cameraEnabled;
                rt.Release();
                Destroy(rt);
                Destroy(portrait);
            }
        }

        static int ChangedFacePixels(Color32[] baseline, Color32[] pose, int minX = 470, int maxX = 810, int minY = 255, int maxY = 575)
        {
            int changed = 0;
            for (int y = minY; y < maxY; y++)
                for (int x = minX; x < maxX; x++)
                {
                    int i = y * OffscreenFrame.Width + x;
                    var a = baseline[i];
                    var b = pose[i];
                    if (Mathf.Abs(a.r - b.r) >= 8 || Mathf.Abs(a.g - b.g) >= 8 || Mathf.Abs(a.b - b.b) >= 8)
                        changed++;
                }
            return changed;
        }
    }
}
