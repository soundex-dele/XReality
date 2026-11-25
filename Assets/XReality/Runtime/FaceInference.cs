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
            float innerBrow = 0, funnel = 0, leftPress = 0, rightPress = 0;
            foreach (var category in result.faceBlendshapes[0].categories)
            {
                switch (category.categoryName)
                {
                    case "eyeBlinkLeft": candidate.left = category.score; break;
                    case "eyeBlinkRight": candidate.right = category.score; break;
                    case "eyeLookInLeft": candidate.gazeLeft.x += Score(category.score); break;
                    case "eyeLookOutLeft": candidate.gazeLeft.x -= Score(category.score); break;
                    case "eyeLookOutRight": candidate.gazeRight.x += Score(category.score); break;
                    case "eyeLookInRight": candidate.gazeRight.x -= Score(category.score); break;
                    case "eyeLookUpLeft": candidate.gazeLeft.y += Score(category.score); break;
                    case "eyeLookDownLeft": candidate.gazeLeft.y -= Score(category.score); break;
                    case "eyeLookUpRight": candidate.gazeRight.y += Score(category.score); break;
                    case "eyeLookDownRight": candidate.gazeRight.y -= Score(category.score); break;
                    case "jawOpen": candidate.jaw = category.score; break;
                    case "mouthFunnel": funnel = Score(category.score); break;
                    case "mouthPucker": candidate.lipRound = Score(category.score); break;
                    case "mouthPressLeft": leftPress = Score(category.score); break;
                    case "mouthPressRight": rightPress = Score(category.score); break;
                    case "mouthSmileLeft": candidate.smileLeft = category.score; break;
                    case "mouthSmileRight": candidate.smileRight = category.score; break;
                    case "browInnerUp": innerBrow = Score(category.score); break;
                    case "browOuterUpLeft": candidate.browLeft = category.score; break;
                    case "browOuterUpRight": candidate.browRight = category.score; break;
                }
            }
            // Invalid native output must not feed NaN into the avatar's persistent smoothing state.
            if (!Finite(candidate.left) || !Finite(candidate.right) || !Finite(candidate.jaw)) return false;
            candidate.left = Mathf.Clamp01(candidate.left);
            candidate.right = Mathf.Clamp01(candidate.right);
            candidate.jaw = Mathf.Clamp01(candidate.jaw);
            candidate.lipRound = Mathf.Max(candidate.lipRound, funnel * .9f);
            candidate.lipPress = (leftPress + rightPress) * .5f;
            candidate.smileLeft = Score(candidate.smileLeft);
            candidate.smileRight = Score(candidate.smileRight);
            candidate.browLeft = Mathf.Max(innerBrow * .72f, Score(candidate.browLeft));
            candidate.browRight = Mathf.Max(innerBrow * .72f, Score(candidate.browRight));
            pose = candidate;
            return true;
        }

        static float Score(float value) => Finite(value) ? Mathf.Clamp01(value) : 0;
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
