using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace XReality
{
    // Shared by editor and standalone QA. Does not take a desktop screenshot.
    public static class OffscreenFrame
    {
        public const int Width = 1280, Height = 720;

        public static Color32[] Save(Camera camera, string folder, string name, bool green = false)
        {
            var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                Render(camera, rt);
                return SaveRendered(rt, folder, name, green);
            }
            finally { RenderTexture.ReleaseTemporary(rt); }
        }

        internal static void Render(Camera camera, RenderTexture rt)
        {
            var previous = RenderTexture.active;
            var previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = rt;
                Canvas.ForceUpdateCanvases();
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                if (!RenderPipeline.SupportsRenderRequest(camera, request))
                    throw new InvalidOperationException("URP rejected the offscreen render request.");
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
            }
        }

        internal static Color32[] SaveRendered(RenderTexture rt, string folder, string name, bool green)
        {
            var previous = RenderTexture.active;
            // Read back display-encoded bytes as-is for PNG.
            var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                var pixels = image.GetPixels32();
                var colors = new System.Collections.Generic.HashSet<Color32>();
                int foreground = 0;
                foreach (var pixel in pixels)
                {
                    if (colors.Count < 64) colors.Add(pixel);
                    if (green && !IsGreen(pixel)) foreground++;
                }
                if (colors.Count < 64)
                    throw new InvalidOperationException("Image is blank or has too few distinct colors: " + name);
                if (green && (!IsGreen(pixels[0]) || !IsGreen(pixels[Width - 1]) ||
                              !IsGreen(pixels[pixels.Length - Width]) || !IsGreen(pixels[pixels.Length - 1]) ||
                              foreground < pixels.Length / 20 || foreground > pixels.Length * 4 / 5))
                    throw new InvalidOperationException("Expected green corners and a visible character.");

                string path = Path.Combine(folder, name + ".png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                Debug.Log($"XR_OFFSCREEN_SNAPSHOT: {name} {Width}x{Height}" + (green ? $"; foreground {foreground}" : ""));
                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        static bool IsGreen(Color32 color) => color.r <= 2 && color.g >= 253 && color.b <= 2;
    }
}
