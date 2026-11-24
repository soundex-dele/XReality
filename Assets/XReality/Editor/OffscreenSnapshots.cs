using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace XReality.Editor
{
    public static class OffscreenSnapshots
    {
        const int Width = OffscreenFrame.Width;
        static readonly string Folder = Path.GetFullPath("Builds/QA-Offscreen");

        // Editor-only: opening saved scenes in edit mode does not start pet profiles,
        // webcam enumeration, inference, or the application's IMGUI controls.
        public static void Run()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Use Unity -batchmode with graphics enabled.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Offscreen snapshots require a graphics device; omit -nographics.");

            Debug.Log($"XR_OFFSCREEN_START: {SystemInfo.graphicsDeviceType}; {SystemInfo.graphicsDeviceName}");
            Directory.CreateDirectory(Folder);
            if (!File.Exists(StudioProject.Asset("LiveStudio.unity")))
                StudioProject.Prepare();
            foreach (string name in new[] { "Home", "PetRoom", "LiveStudio" })
            {
                EditorSceneManager.OpenScene(StudioProject.Asset(name + ".unity"));
                Camera camera = Camera.main;
                if (!camera)
                    throw new InvalidOperationException("Scene is missing its main camera: " + name);
                Save(camera, name);
                if (name == "PetRoom")
                    PetPoses(camera, UnityEngine.Object.FindFirstObjectByType<PetRoom>());
                if (name != "LiveStudio") continue;

                // A static fixture, not a test of the runtime output toggle or face tracking.
                var stage = UnityEngine.Object.FindFirstObjectByType<LiveStudio>();
                Vector3 position = camera.transform.position;
                camera.transform.position = new Vector3(stage.torso.position.x, position.y, position.z);
                camera.backgroundColor = Color.green;
                var neutral = Expression(camera, stage, "LiveNeutral", 0, 0, 0);
                CheckBlink(neutral, Expression(camera, stage, "LiveBlinkHalf", 55, 0, 0), 685, 595, "left halfway");
                CheckBlink(neutral, Expression(camera, stage, "LiveBlinkLeft", 100, 0, 0), 685, 595, "left");
                CheckBlink(neutral, Expression(camera, stage, "LiveBlinkRight", 0, 100, 0), 595, 685, "right");
                int mouthDiff = Changes(neutral, Expression(camera, stage, "LiveJawOpen", 0, 0, 100),
                    new RectInt(600, 309, 80, 30));
                if (mouthDiff < 350)
                    throw new InvalidOperationException("Jaw fixture did not visibly change the mouth region.");
                Debug.Log($"XR_OFFSCREEN_JAW: {mouthDiff} changed mouth pixels");
                stage.head.localRotation = Quaternion.Euler(3, 16, -4);
                Expression(camera, stage, "LiveOutput", 100, 0, 98);
            }
            Debug.Log("XR_OFFSCREEN_OK");
        }

        static void PetPoses(Camera camera, PetRoom room)
        {
            var clips = room.pose.runtimeAnimatorController.animationClips;
            Color32[] Pose(Mood mood)
            {
                var clip = clips.Single(c => c.name == mood.ToString());
                AnimationMode.StartAnimationMode();
                try
                {
                    AnimationMode.BeginSampling();
                    // Notice only lasts 0.55 seconds in PetBrain: show a pose the live state actually reaches.
                    float time = mood == Mood.Notice ? .42f : clip.length * .5f;
                    try { AnimationMode.SampleAnimationClip(room.cat.gameObject, clip, time); }
                    finally { AnimationMode.EndSampling(); }
                    float headHeight = room.head.position.y - room.cat.position.y;
                    if (headHeight < .55f || headHeight > 1.3f)
                        throw new InvalidOperationException($"{mood}: head pivot is outside the expected range ({headHeight:F2}).");
                    return Save(camera, "Pet" + mood);
                }
                finally { AnimationMode.StopAnimationMode(); }
            }

            var watch = Pose(Mood.Watch);
            var sleep = Pose(Mood.Sleep);
            var ear = Pose(Mood.EarFlick);
            var nuzzle = Pose(Mood.Nuzzle);
            var notice = Pose(Mood.Notice);
            var chase = Pose(Mood.Chase);
            var cat = new RectInt(555, 130, 380, 375);
            int rest = Changes(watch, sleep, cat);
            int earOnly = Changes(sleep, ear, cat);
            int affection = Changes(watch, nuzzle, cat);
            int ready = Changes(watch, notice, cat);
            int jump = Changes(notice, chase, cat);
            if (rest < 15000 || earOnly < 300 || earOnly * 4 > rest || affection < 5000 || ready < 9000 || jump < 9000)
                throw new InvalidOperationException("Pet poses are too similar, or the sleeping ear flick changed too much of the cat.");
            Debug.Log($"XR_OFFSCREEN_PET: watch/sleep {rest}; sleep/ear {earOnly}; watch/nuzzle {affection}; " +
                $"watch/notice {ready}; notice/chase {jump}");
        }

        // Batch mode does not tick play-mode skinning. Bake the authored renderer at the
        // requested weights and render that exact geometry with the same material/transform.
        static Color32[] Expression(Camera camera, LiveStudio stage, string name, float left, float right, float jaw)
        {
            var sources = new[] { stage.leftLid, stage.rightLid, stage.lip, stage.faceSurface };
            var meshes = new Mesh[sources.Length];
            var copies = new GameObject[sources.Length];
            try
            {
                stage.ApplyExpressions(left / 100, right / 100, jaw / 100);
                for (int i = 0; i < sources.Length; i++)
                {
                    var source = sources[i];
                    meshes[i] = new Mesh();
                    source.BakeMesh(meshes[i]);
                    var copy = copies[i] = new GameObject(source.name + " capture fixture");
                    copy.transform.SetParent(source.transform, false);
                    copy.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                    copy.AddComponent<MeshRenderer>().sharedMaterials = source.sharedMaterials;
                    source.forceRenderingOff = true;
                }
                return Save(camera, name, true);
            }
            finally
            {
                for (int i = 0; i < sources.Length; i++)
                {
                    if (copies[i]) UnityEngine.Object.DestroyImmediate(copies[i]);
                    if (meshes[i]) UnityEngine.Object.DestroyImmediate(meshes[i]);
                    sources[i].forceRenderingOff = false;
                }
                stage.ApplyExpressions(0, 0, 0);
            }
        }

        static Color32[] Save(Camera camera, string name, bool green = false) =>
            OffscreenFrame.Save(camera, Folder, name, green);

        // These regions are tied to the 720p green-screen framing. On a framing change,
        // re-evaluate them against the neutral capture rather than weakening the thresholds.
        static void CheckBlink(Color32[] neutral, Color32[] expression, int targetX, int otherX, string side)
        {
            int target = Changes(neutral, expression, new RectInt(targetX - 24, 385, 48, 40));
            int other = Changes(neutral, expression, new RectInt(otherX - 24, 385, 48, 40));
            if (target < 300 || other > 25)
                throw new InvalidOperationException($"Unexpected {side} blink pixels: target {target}, other {other}.");
            Debug.Log($"XR_OFFSCREEN_BLINK: {side}; target {target}, other {other}");
        }

        static int Changes(Color32[] baseline, Color32[] pose, RectInt region)
        {
            int changed = 0;
            for (int y = region.yMin; y < region.yMax; y++)
                for (int x = region.xMin; x < region.xMax; x++)
                {
                    int i = y * Width + x;
                    var a = baseline[i];
                    var b = pose[i];
                    if (Mathf.Abs(a.r - b.r) >= 8 || Mathf.Abs(a.g - b.g) >= 8 || Mathf.Abs(a.b - b.b) >= 8)
                        changed++;
                }
            return changed;
        }

    }
}


