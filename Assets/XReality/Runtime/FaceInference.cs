using System;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using UnityEngine;

namespace XReality
{
    internal sealed class FaceInference : IDisposable
    {
        readonly FaceLandmarker detector;
        FaceLandmarkerResult result = FaceLandmarkerResult.Alloc(1, true, true);
        Texture2D source;
        Color32[] topFirst;
        internal FaceInference(byte[] verifiedModel)
        {
            detector = FaceLandmarker.CreateFromOptions(new FaceLandmarkerOptions(
                new BaseOptions(BaseOptions.Delegate.CPU, modelAssetBuffer: verifiedModel), RunningMode.VIDEO,
                outputFaceBlendshapes: true, outputFaceTransformationMatrixes: true));
        }

        // WebCamTexture and QA fixture decode both feed this exact CPU-only path.
        internal bool Process(Color32[] pixels, int width, int height, bool needsVerticalFlip, int rotation, long timestamp, out FacePose pose)
        {
            if (source == null || source.width != width || source.height != height)
            {
                if (source) UnityEngine.Object.Destroy(source);
                source = new Texture2D(width, height, TextureFormat.RGBA32, false);
                topFirst = new Color32[width * height];
            }
            if (needsVerticalFlip)
            {
                for (int row = 0; row < height; row++)
                    Array.Copy(pixels, (height - row - 1) * width, topFirst, row * width, width);
            }
            source.SetPixels32(needsVerticalFlip ? topFirst : pixels);
            using var image = new Mediapipe.Image(source);
            pose = FacePose.Neutral;
            if (!detector.TryDetectForVideo(image, timestamp, new ImageProcessingOptions(rotationDegrees: rotation), ref result)
                || (result.facialTransformationMatrixes?.Count ?? 0) == 0
                || (result.faceBlendshapes?.Count ?? 0) == 0)
                return false;
            var candidate = FacePose.Neutral;
            candidate.rotation = result.facialTransformationMatrixes[0].rotation;
            float normSquared = Quaternion.Dot(candidate.rotation, candidate.rotation);
            if (!Finite(normSquared) || normSquared < .01f) return false;
            candidate.rotation = candidate.rotation.normalized;
            foreach (var category in result.faceBlendshapes[0].categories)
            {
                switch (category.categoryName)
                {
                    case "eyeBlinkLeft": candidate.left = category.score; break;
                    case "eyeBlinkRight": candidate.right = category.score; break;
                    case "jawOpen": candidate.jaw = category.score; break;
                }
            }
            // Invalid native output must not feed NaN into the avatar's persistent smoothing state.
            if (!Finite(candidate.left) || !Finite(candidate.right) || !Finite(candidate.jaw)) return false;
            candidate.left = Mathf.Clamp01(candidate.left);
            candidate.right = Mathf.Clamp01(candidate.right);
            candidate.jaw = Mathf.Clamp01(candidate.jaw);
            pose = candidate;
            return true;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public void Dispose()
        {
            if (source) UnityEngine.Object.Destroy(source);
            source = null;
            topFirst = null;
            ((IDisposable)detector).Dispose();
        }
    }
}
