#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace XReality
{
    public static class StaticGaussianPropQA
    {
        public static void Run()
        {
            Require(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11, "D3D11 required");
            string directory = Path.GetFullPath("Builds/QA-XRS");
            Directory.CreateDirectory(directory);
            var valid = Path.Combine(directory, "fixture.xrs");
            var invalid = Path.Combine(directory, "invalid.xrs");
            var truncated = Path.Combine(directory, "truncated.xrs");
            Fixture(valid, false);
            Fixture(invalid, true);
            File.WriteAllBytes(truncated, File.ReadAllBytes(valid).SkipLast(1).ToArray());
            var cameraObject = new GameObject("XRS QA camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 1.4f, -3);
            camera.fieldOfView = 35;
            camera.aspect = 16f / 9;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.green;
            var target = RenderTexture.GetTemporary(320, 180, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var pixels = new Texture2D(320, 180, TextureFormat.RGBA32, false, true);
            try
            {
                Require(!StaticGaussianProp.TryLoad(truncated, camera, 0, out _), "accepted truncated XRS");
                Require(!StaticGaussianProp.TryLoad(invalid, camera, 0, out _), "accepted NaN XRS");
                Require(StaticGaussianProp.TryLoad(valid, camera, 0, out var prop) && prop.Count == 3, "could not load static XRS");
                using (prop)
                {
                    Color32[] Render(string name)
                    {
                        prop.Place();
                        OffscreenFrame.Render(camera, target, false);
                        var previous = RenderTexture.active;
                        try
                        {
                            RenderTexture.active = target;
                            pixels.ReadPixels(new Rect(0, 0, 320, 180), 0, 0);
                            var colors = pixels.GetPixels32();
                            File.WriteAllBytes(Path.Combine(directory, name + ".png"), pixels.EncodeToPNG());
                            return colors;
                        }
                        finally { RenderTexture.active = previous; }
                    }
                    var initial = Render("initial");
                    Require(initial[0].g > 250 && initial.Count(p => p.r > 120) > 15, "expected visible splats on green");
                    prop.Control(110, 0, false, false);
                    Require(!initial.SequenceEqual(Render("rotated")), "rotation did not change output");
                    prop.Control(0, .65f, false, false);
                    var zoomed = Render("zoomed");
                    Require(zoomed.Count(p => p.r > 120) > initial.Count(p => p.r > 120), "zoom did not increase footprint");
                    prop.Control(0, 0, true, false);
                    Require(initial.SequenceEqual(Render("reset")), "reset did not restore output");
                    prop.Control(0, 0, false, true);
                    Require(!Render("hidden").Any(p => p.r > 120), "hide did not suppress output");
                }
                Debug.Log("XR_XRS_QA_OK");
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(pixels);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        static void Fixture(string path, bool corrupt)
        {
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write(0x31535258); writer.Write(1); writer.Write(3); writer.Write(3);
            foreach (float component in new float[] { 1,0,0, 0,-1,0, 0,0,1 }) writer.Write(component);
            for (int i = 0; i < 3; i++)
            {
                foreach (float component in new[] { corrupt ? float.NaN : i * .4f - .4f, i * .31f - .31f, i * -.32f, .92f,
                    .22f, 0, 0, 0, 0, .19f, 0, 0, 0, 0, .14f, 0, 1.6f, -1.4f, -1.4f }) writer.Write(component);
                for (int k = 0; k < 45; k++) writer.Write(0f);
            }
        }

        static void Require(bool condition, string diagnostic)
        {
            if (!condition) throw new InvalidOperationException(diagnostic);
        }
    }
}
#endif
