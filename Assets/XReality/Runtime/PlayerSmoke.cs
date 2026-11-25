using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace XReality
{
    // Opt-in player check. The blank test texture never touches a capture device.
    public sealed class PlayerSmoke : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--xr-smoke") < 0 || Array.IndexOf(args, PlayerOffscreen.Flag) >= 0)
                return;
            FaceModel.QaCacheFolder = Option("--xr-cache");
            PetRoom.QaProfile = Option("--xr-pet-profile");
            var go = new GameObject("Player smoke");
            DontDestroyOnLoad(go);
            go.AddComponent<PlayerSmoke>();
        }

        static void Require(bool value, string error)
        {
            if (!value)
                throw new InvalidOperationException(error);
        }

        IEnumerator Start()
        {
            using var steps = Execute().GetEnumerator();
            while (true)
            {
                object wait;
                try
                {
                    if (!steps.MoveNext())
                        break;
                    wait = steps.Current;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    Application.Quit(1);
                    yield break;
                }

                yield return wait;
            }

            Debug.Log("XR_SMOKE_OK");
            Application.Quit(0);
        }

        static string Option(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var flag = Array.IndexOf(args, name);
            return flag >= 0 && flag + 1 < args.Length ? args[flag + 1] : null;
        }

        static void Snapshot(string folder,string scene)
        {
            Directory.CreateDirectory(folder);
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(Path.Combine(folder,scene+".png"),texture.EncodeToPNG()); }
            finally { Destroy(texture); }
        }

        static void CalibrationChecks()
        {
            var calibration=new NeutralCalibration();
            var idle=new FacePose {rotation=Quaternion.Euler(5,11,-3),left=.095f,right=.11f,jaw=.105f};
            calibration.Begin();
            for(int i=0;i<8;i++) calibration.Add(idle,i*.04);
            calibration.Interrupt();
            for(int i=8;i<22;i++) calibration.Add(idle,i*.04);
            Require(calibration.Pending && !calibration.Calibrated,"Face loss restarts calibration clock");
            for(int i=22;i<34;i++) calibration.Add(idle,i*.04);
            Require(calibration.Calibrated && !calibration.Pending,"Stable neutral finishes calibration");
            var neutral=calibration.Apply(idle);
            Require(Quaternion.Angle(neutral.rotation,Quaternion.identity)<.2f && neutral.left==0 && neutral.right==0 && neutral.jaw==0,"Neutral output has no drift");
            idle.left=.8f;idle.jaw=.62f;
            var expression=calibration.Apply(idle);
            Require(expression.left>.6f && expression.right==0 && expression.jaw>.6f,"Independent face signals survive neutral correction");
        }

        System.Collections.Generic.IEnumerable<object> Checks()
        {
            CalibrationChecks();
            var stats = new CaptureMetrics();
            stats.Begin(0);
            for (int i=1;i<=30;i++) { stats.UpdateFrame(i/30.0); stats.Processed(12,i<15 || i>=20); }
            stats.End(1,"QA");
            Require(stats.Report.Contains("Mean player FPS: 30.00") && stats.Report.Contains("Face reacquisitions: 1") && stats.Report.Contains("inference (ms): 12"),"Performance report aggregates");
            // A resize or return to controls must break continuity. Focus transitions
            // count only intervals entirely spent in the background.
            stats.Begin(0);
            stats.UpdateFrame(1, true);
            stats.UpdateFrame(2, true, true);
            stats.UpdateFrame(3, true, true);
            stats.UpdateFrame(4, false);
            stats.UpdateFrame(5, true);
            stats.UpdateFrame(6, true);
            stats.End(6, "QA");
            Require(stats.Report.Contains("720p clean output seconds: 3.00") &&
                    stats.Report.Contains("longest continuous seconds: 2.00") &&
                    stats.Report.Contains("unfocused seconds: 1.00") &&
                    stats.Report.Contains("unfocused mean FPS: 1.00"), "Clean-output diagnostics must isolate qualifying intervals");
            Require(SceneManager.GetActiveScene().name == "Home", "Unexpected entry scene");
            var folder = Option("--xr-snapshots");
            var captured = new System.Collections.Generic.HashSet<string>();
            if(folder != null) { yield return new WaitForEndOfFrame(); Snapshot(folder,"Home"); captured.Add("Home"); }
            foreach (string scene in new[]
            {
                "PetRoom",
                "LiveStudio",
                "Home",
                "LiveStudio",
                "PetRoom",
                "Home"
            }

            )
            {
                yield return SceneManager.LoadSceneAsync(scene);
                yield return null;
                var pet = FindFirstObjectByType<PetRoom>();
                var live = FindFirstObjectByType<LiveStudio>();
                Require((pet != null) == (scene == "PetRoom") && (live != null) == (scene == "LiveStudio"), "Mode isolation");
                if (pet)
                {
                    Require(pet.cat && pet.toy && pet.pose && pet.pose.runtimeAnimatorController, "Missing cat binding");
                    foreach (Mood m in Enum.GetValues(typeof(Mood)))
                        Require(pet.pose.HasState(0, Animator.StringToHash("Base Layer." + m)), "Missing pose: " + m);
                }

                if (live)
                    Require(!live.capture.Active && live.faceSurface && live.teeth && live.brows && live.head, "Avatar bindings or camera privacy");
                if (live)
                {
                    live.ApplyExpressions(1,0,.8f);
                    Require(live.faceSurface.GetBlendShapeWeight(0)==100 && live.faceSurface.GetBlendShapeWeight(1)==0 &&
                        live.faceSurface.GetBlendShapeWeight(2)>75 && live.teeth.GetBlendShapeWeight(0)>75,"Independent expressions");
                    live.ApplyExpressions(0,0,0);
                }
                if(pet)
                {
                    var face = pet.head.Find("Sculpted cheeks, muzzle and chin").GetComponent<MeshFilter>().sharedMesh;
                    Require(face.vertexCount > 1000 && face.colors.Length == face.vertexCount, "Sculpted face was not packaged");
                }
                if(folder != null && captured.Add(scene))
                {
                    yield return new WaitForEndOfFrame(); Snapshot(folder,scene);
                    if(live)
                    {
                        live.TestPose=new FacePose {rotation=Quaternion.Euler(3,16,-4),left=1,jaw=.70f};
                        live.ToggleOutput();
                        yield return new WaitForSecondsRealtime(.48f);
                        yield return new WaitForEndOfFrame(); Snapshot(folder,"LiveOutput");
                        Require(live.stageCamera.backgroundColor == Color.green && !live.capture.Active,"Clean mode must not enable camera");
                        live.TestPose=null;
                        live.ToggleOutput();
                    }
                }
            }

        }

        System.Collections.Generic.IEnumerable<object> Execute()
        {
            var phase=Option("--xr-pet-phase");
            if(phase != null)
            {
                Require(!string.IsNullOrEmpty(PetRoom.QaProfile),"Pet smoke requires an isolated profile");
                foreach(var step in PetChecks(phase)) yield return step;
                yield break;
            }
            foreach(var step in Checks()) yield return step;
            var modelPath=Option("--xr-test-model");
            var portraitPath=Option("--xr-test-portrait");
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--xr-test-download")>=0)
            {
                Require(FaceModel.QaCacheFolder != null && !File.Exists(FaceModel.CachePath),"Download test requires an empty isolated cache");
                var model=gameObject.AddComponent<FaceModel>();
                model.Refresh();Require(!model.Ready,"Model must not be bundled");
                model.BeginDownload();Require(model.Busy,"Download did not start");
                model.Cancel();Require(!model.Busy && !File.Exists(FaceModel.CachePath) && Directory.GetFiles(FaceModel.CacheFolder,"*.partial").Length==0,"Cancellation left partial data");
                model.BeginDownload();
                double deadline=Time.realtimeSinceStartupAsDouble+FaceModel.QaDownloadDeadlineSeconds;
                while(model.Busy && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
                Require(model.Ready && !model.Busy, "Model download failed: " + model.Status);
                modelPath=FaceModel.CachePath;
                Debug.Log("XR_DOWNLOAD_OK");
            }
            Require(modelPath!=null && portraitPath!=null,"Supply the QA model and portrait outside the release package");
            var modelBytes=FaceModel.ReadVerified(modelPath);
            using var inference=new FaceInference(modelBytes);
            var blank=new Color32[128*128];
            Require(!inference.Process(blank,128,128,true,0,1,out _),"Unexpected face on blank image");
            var tex=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try
            {
                Require(tex.LoadImage(File.ReadAllBytes(portraitPath)),"Test portrait did not decode");
                var pixels=tex.GetPixels32();
                Require(inference.Process(pixels,tex.width,tex.height,true,0,33,out var face),"Official portrait not detected");
                Require(!float.IsNaN(face.rotation.w) && Mathf.Abs(1-Quaternion.Dot(face.rotation,face.rotation))<.02f,"Invalid head rotation");
                Require(face.left>=0 && face.left<=1 && face.right>=0 && face.right<=1 && face.jaw>=0 && face.jaw<=1,"Invalid expression weights");
                Require(inference.Process(pixels,tex.width,tex.height,true,0,66,out _),"Fixture tracking did not continue");
                Require(!inference.Process(blank,128,128,true,0,100,out _),"Detector did not report absence");
                Require(inference.Process(pixels,tex.width,tex.height,true,0,133,out _),"Detector did not reacquire known face");
                Debug.Log("XR_PORTRAIT_OK: blank -> face -> tracking -> blank -> reacquire");
            }
            finally { Destroy(tex); }
        }

        static System.Collections.Generic.IEnumerable<object> PetChecks(string phase)
        {
            if(phase=="write") PetRoom.DeleteQaProfile();
            yield return SceneManager.LoadSceneAsync("PetRoom");
            yield return null;
            var pet=FindFirstObjectByType<PetRoom>();
            if(phase=="write")
            {
                pet.Rename("窗边栗子");
                pet.Stroke();
                Require(pet.Bond==1,"Pet stroke must advance bond once");
                for(int i=0;i<3;i++)
                {
                    int previous=pet.Memory.Window;
                    pet.OfferToy(2.45f, i%2==0?-.95f:1.05f);
                    Require(pet.Memory.Window==previous,"Offering a toy must not record unfinished play");
                    var deadline=Time.realtimeSinceStartupAsDouble+5;
                    while(pet.Memory.Window==previous && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
                    Require(pet.Memory.Window==previous+1,"Completed chase must record exactly once");
                }
                Require(pet.Memory.Favorite==PlayMemory.Zone.Window && pet.Bond==4,"Expected learned window preference");
            }
            else
            {
                Require(phase=="read" && pet.CatName=="窗边栗子" && pet.Bond==4 && pet.Memory.Favorite==PlayMemory.Zone.Window,"Profile did not survive player restart");
                float x=pet.cat.position.x;
                double deadline=Time.realtimeSinceStartupAsDouble+6;
                while(pet.cat.position.x<x+.8f && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
                Require(pet.cat.position.x>=x+.8f,"Learned preference did not cause a window visit after restart");
                PetRoom.DeleteQaProfile();
            }
            Debug.Log("XR_PET_"+phase.ToUpperInvariant()+"_OK");
        }
    }
}
