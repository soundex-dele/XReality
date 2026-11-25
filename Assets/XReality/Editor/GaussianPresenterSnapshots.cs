using System;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace XReality.Editor
{
    public static class GaussianPresenterSnapshots
    {
        // Runs entirely in edit mode; the webcam/controller never starts.
        public static void Run()
        {
            if (!Application.isBatchMode || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
                throw new InvalidOperationException("Run 3DGS QA in -batchmode -force-d3d11 without -nographics.");
            var path = StudioProject.Asset("LiveStudio.unity");
            if (!File.Exists(path)) StudioProject.Prepare();
            var live = GaussianPresenterBake.Studio(EditorSceneManager.OpenScene(path));
            var asset = GaussianPresenterBake.Bake(live);
            var persisted = Resources.Load<GaussianPresenterAsset>(GaussianPresenterAsset.ResourcePath);
            Require(persisted == asset, "baked asset is not discoverable at the player resource path");
            var cam = live.stageCamera;
            var position = cam.transform.position;
            cam.transform.position = new Vector3(live.torso.position.x, position.y, position.z);
            cam.backgroundColor = Color.green;
            string folder = Path.GetFullPath("Builds/QA-3DGS");
            Directory.CreateDirectory(folder);
            var sources = live.torso.GetComponentsInChildren<Renderer>();
            var prior = sources.Select(r => r.forceRenderingOff).ToArray();
            Require(!GaussianPresenter.TryCreate(null, live.torso, cam, out _) &&
                    sources.Select(r => r.forceRenderingOff).SequenceEqual(prior), "missing sample changed the mesh fallback");
            Require(GaussianPresenter.TryCreate(persisted, live.torso, cam, out var presenter), "sample did not initialize");
            using (presenter)
            {
                Require(sources.All(r => r.forceRenderingOff), "source mesh shares the splat output");
                live.ApplyExpressions(0, 0, 0);
                presenter.Refresh();
                var neutral = OffscreenFrame.Save(cam, folder, "01-neutral", true);
                live.ApplyExpressions(1, 0, 0);
                presenter.Refresh();
                var blink = OffscreenFrame.Save(cam, folder, "02-left-blink", true);
                live.ApplyExpressions(0, 0, .95f);
                presenter.Refresh();
                var jaw = OffscreenFrame.Save(cam, folder, "03-jaw", true);
                live.ApplyExpressions(0, 0, 0, .90f, .90f);
                presenter.Refresh();
                var smile = OffscreenFrame.Save(cam, folder, "04-smile", true);
                live.head.localRotation = Quaternion.Euler(3, 22, -4);
                presenter.Refresh();
                var turn = OffscreenFrame.Save(cam, folder, "05-turn", true);
                int eyeChange = Changes(neutral, blink, new RectInt(548, 370, 185, 75));
                var mouth = new RectInt(550, 272, 180, 105);
                int jawChange = Changes(neutral, jaw, mouth), smileChange = Changes(neutral, smile, mouth);
                int headChange = Changes(neutral, turn, new RectInt(475, 255, 330, 320));
                Debug.Log($"XR_3DGS_POSES: {presenter.Count} splats; blink {eyeChange}; jaw {jawChange}; smile {smileChange}; turn {headChange}");
                Require(eyeChange > 100 && jawChange > 300 && smileChange > 150 && headChange > 3000,
                    "pose changes missing from Gaussian output");
            }
            Require(sources.Select(r => r.forceRenderingOff).SequenceEqual(prior), "cleanup did not restore source renderers");
            Debug.Log("XR_3DGS_QA_OK");
        }

        static int Changes(Color32[] a, Color32[] b, RectInt region)
        {
            int count = 0;
            for (int y = region.yMin; y < region.yMax; y++)
                for (int x = region.xMin; x < region.xMax; x++)
                {
                    int i = y * OffscreenFrame.Width + x;
                    if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) >= 24)
                        count++;
                }
            return count;
        }

        static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException("3DGS QA: " + message);
        }
    }
}
