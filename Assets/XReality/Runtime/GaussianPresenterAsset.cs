using System;
using UnityEngine;

namespace XReality
{
    [PreferBinarySerialization]
    public sealed class GaussianPresenterAsset : ScriptableObject
    {
        public const string ResourcePath = "XReality/GaussianPresenterSample";
        public const int FormatVersion = 1;
        public const int MaxSplats = 60000;

        [Serializable]
        public struct Splat
        {
            public int a, b, c;
            public Vector2 barycentric;
            public float sigma, opacity;
            public Color color;
        }

        [Serializable]
        public sealed class Part
        {
            public string rendererName;
            public Mesh mesh;
            public Splat[] splats;
        }

        public int version = FormatVersion;
        public Shader shader;
        public Part[] parts;
    }
}
