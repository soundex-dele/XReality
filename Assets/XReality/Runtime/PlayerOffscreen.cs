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
        bool downloadScreenshots, retryScreenshots, resumeScreenshots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Launch()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, Flag);
            if (index < 0 || !Application.isBatchMode || Application.isEditor) return;
            int tracking = Array.IndexOf(args, TrackingFixtures);
            int pet = Array.IndexOf(args, PetFixtures);
            int soak = Array.IndexOf(args, SoakFlag), duration = 0;
            bool invalidSoak = soak >= 0 && (tracking < 0 || soak + 1 >= args.Length ||
                !int.TryParse(args[soak + 1], out duration) || duration < 30 || duration > 1800);
            bool invalidPet = pet >= 0 && (pet + 2 >= args.Length ||
                (args[pet + 1] != "play" && args[pet + 1] != "restore") || !Guid.TryParseExact(args[pet + 2], "N", out _));
            if (index + 1 >= args.Length || invalidPet || invalidSoak || (tracking >= 0 && tracking + 2 >= args.Length) ||
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
                int avatarPixels = ShowcasePixels(backdrop, 880, 1090, 385, 610);
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
            var stageOnly = OffscreenFrame.Save(live.stageCamera, folder, "LiveStageOnly");
            live.ShowDesk(false); // Real controls, no input module, device enumeration or model setup.
            yield return null;
            var withControls = OffscreenFrame.Save(live.stageCamera, folder, "LiveStudio");
            if (PanelPixelsChanged(stageOnly, withControls) < 100000)
                throw new InvalidOperationException("The retained control panel was not rendered in the live preview.");
            live.ToggleOutput(); // Use the shipped clean-output path, not copied camera values.
            yield return null;
            if (live.capture.Active || !live.capture.CleanOutput || live.stageCamera.backgroundColor != Color.green)
                throw new InvalidOperationException("Clean output did not enable the green stage without capture.");
            var neutral = OffscreenFrame.Save(live.stageCamera, folder, "LiveOutput", true);
            for (int y = 35; y < 680; y++)
                for (int x = 28; x < 372; x++)
                {
                    var pixel = neutral[y * OffscreenFrame.Width + x];
                    if (pixel.r > 2 || pixel.g < 253 || pixel.b > 2)
                        throw new InvalidOperationException("Clean output leaked pixels into the control panel area.");
                }
            if (modelPath != null)
                foreach (var step in CaptureTracking(live, neutral)) yield return step;
            if (soakSeconds > 0)
                foreach (var step in CaptureSoak(live, neutral)) yield return step;
            live.ToggleOutput();
            yield return null;
            var restored = OffscreenFrame.Save(live.stageCamera, folder, "LiveControlsRestored");
            int restoredDifference = PanelPixelsChanged(withControls, restored);
            if (live.capture.Active || restoredDifference > 500)
                throw new InvalidOperationException($"The control panel did not restore cleanly ({restoredDifference} changed pixels).");
            Debug.Log("XR_PLAYER_CONTROLS_OK: preview visible; clean area pure green; controls restored");
            if (modelPath != null)
                foreach (var step in CaptureModelSetup(live, withControls)) yield return step;
            if (retryScreenshots)
                foreach (var step in CaptureRetry(live, withControls)) yield return step;
            if (downloadScreenshots || resumeScreenshots)
                foreach (var step in CaptureDownload(live, withControls)) yield return step;
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
            pet.Stroke();
            for (int i = 0; i < 4; i++) pet.Stroke();
            if (pet.CurrentMood != Mood.EarFlick || pet.Bond != 1)
                throw new InvalidOperationException("Sleepy strokes did not coalesce into one ear flick.");
            yield return new WaitForSeconds(.28f);
            OffscreenFrame.Save(pet.roomCamera, folder, "PetSleepyStroke");

            foreach (var step in Until(() => pet.CurrentMood == Mood.Watch, 10, "Pet did not wake after resting.")) yield return step;
            pet.Stroke();
            if (pet.CurrentMood != Mood.Nuzzle || pet.Bond != 2)
                throw new InvalidOperationException("Awake stroke did not trigger a nuzzle.");
            yield return new WaitForSeconds(.42f);
            OffscreenFrame.Save(pet.roomCamera, folder, "PetAwakeStroke");

            for (int i = 0; i < 3; i++)
            {
                var origin = pet.cat.position;
                int before = pet.Memory.Window;
                pet.OfferToy(2.45f, i % 2 == 0 ? -.95f : 1.05f);
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
            using var inference = new FaceInference(FaceModel.ReadVerified(modelPath));
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
                void AddFixture(int frames)
                {
                    for (int i = 0; i < frames; i++)
                    {
                        if (!Infer(pixels, out face)) throw new InvalidOperationException("Fixture disappeared during calibration.");
                        calibration.Add(face, timestamp / 1000d);
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

                AddFixture(17);
                if (calibration.Pending || !calibration.Calibrated)
                    throw new InvalidOperationException("Continuous fixture samples did not complete calibration.");
                var corrected = calibration.Apply(face);
                if (Quaternion.Angle(corrected.rotation, Quaternion.identity) > 2f ||
                    corrected.left > .025f || corrected.right > .025f || corrected.jaw > .025f)
                    throw new InvalidOperationException("The steady fixture did not calibrate back to rest.");
                live.ApplyFacePose(corrected, 1);
                yield return null;
                var centered = OffscreenFrame.Save(live.stageCamera, folder, "LiveCalibrationNeutral", true);

                face.rotation *= Quaternion.Euler(0, 21, 0);
                face.left = .98f;
                face.jaw = .75f;
                var expression = calibration.Apply(face);
                if (Quaternion.Angle(expression.rotation, Quaternion.identity) < 16 || expression.left < .8f ||
                    expression.right > .04f || expression.jaw < .7f)
                    throw new InvalidOperationException("Calibration suppressed the relative pose or mixed independent expressions.");
                live.ApplyFacePose(expression, 1);
                yield return null;
                var expressive = OffscreenFrame.Save(live.stageCamera, folder, "LiveCalibrationExpression", true);
                int pendingPixels = ChangedFacePixels(neutral, pending);
                int centeredPixels = ChangedFacePixels(neutral, centered);
                int relativePixels = ChangedFacePixels(centered, expressive);
                if (pendingPixels > 100 || centeredPixels > 4500 || relativePixels < 5000 || live.capture.Active)
                    throw new InvalidOperationException($"Unexpected calibration render pixels: {pendingPixels}/{centeredPixels}/{relativePixels}.");
                live.ApplyFacePose(FacePose.Neutral, 1);
                Debug.Log($"XR_PLAYER_CALIBRATION_OK: reset on loss, steady fixture centered, independent synthetic expression; {pendingPixels}/{centeredPixels}/{relativePixels} pixels");

            }
            finally { Destroy(portrait); }
        }

        System.Collections.Generic.IEnumerable<object> CaptureSoak(LiveStudio live, Color32[] neutral)
        {
            using var inference = new FaceInference(FaceModel.ReadVerified(modelPath));
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
                double[] at = { 1d, interval, 2 * interval + 2, 3 * interval, 4 * interval, 5 * interval, soakSeconds };
                long stamp = 0;
                bool reacquired = false;
                stats.Begin(start);
                Debug.Log($"XR_PLAYER_SOAK_START: {soakSeconds}s; CPU fixture {portrait.width}x{portrait.height}; output 1280x720");
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
                    OffscreenFrame.Render(camera, rt);
                    rendered++;
                    double after = Time.realtimeSinceStartupAsDouble;
                    stats.UpdateFrame(after, clean: true);
                    stats.SampleMemory(after);
                    if (checkpoint < at.Length && age >= at[checkpoint])
                    {
                        int id = ++checkpoint;
                        var frame = OffscreenFrame.SaveRendered(rt, folder, $"LiveSoak{id:00}", true);
                        int changed = ChangedFacePixels(neutral, frame);
                        if ((id == 3 && changed > 100) || (id != 3 && changed < 500))
                            throw new InvalidOperationException($"Unexpected tracked/idle fixture state at soak checkpoint {id}: {changed} pixels.");
                        Debug.Log(FormattableString.Invariant($"XR_PLAYER_SOAK_CHECKPOINT: {id} at {age:F2}s; renders {rendered}, portrait {visible}, blank {lost}"));
                    }
                    if (checkpoint == at.Length) break;
                    if (live.capture.Active) throw new InvalidOperationException("Fixture soak must not activate a camera.");
                    yield return null;
                }
                double end = Time.realtimeSinceStartupAsDouble;
                stats.End(end, "offscreen fixture completed");
                double fps = rendered / (end - start);
                File.WriteAllText(Path.Combine(folder, "live-soak.txt"),
                    "Offline CPU fixture + explicit 720p URP draws; not webcam or desktop presentation data.\n" +
                    stats.Report.Replace("Camera", "Fixture").Replace("camera", "fixture"));
                if (stats.MemorySamples < soakSeconds / 10)
                    throw new InvalidOperationException($"Insufficient application working-set samples: {stats.MemorySamples}.");
                if (visible < rendered * .85 || lost == 0 || !reacquired || live.capture.Active || fps < 25)
                    throw new InvalidOperationException(FormattableString.Invariant($"Soak underperformed: {fps:F2} FPS, {visible}/{rendered} recognized, {lost} blank, reacquired={reacquired}."));
                Debug.Log(FormattableString.Invariant($"XR_PLAYER_SOAK_OK: {end - start:F2}s; {rendered} renders; {fps:F2} FPS; {visible} faces; {lost} blank frames"));
            }
            finally
            {
                camera.enabled = cameraEnabled;
                rt.Release();
                Destroy(rt);
                Destroy(portrait);
            }
        }

        static int ChangedFacePixels(Color32[] baseline, Color32[] pose)
        {
            int changed = 0;
            for (int y = 255; y < 575; y++)
                for (int x = 470; x < 810; x++)
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
