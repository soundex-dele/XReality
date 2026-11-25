Shader "XReality/Gaussian Presenter"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "DisableBatching"="True" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite Off
            Cull Off
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            struct Splat { float4 centerOpacity, tangent, bitangent, color; };
            StructuredBuffer<Splat> _AvatarSplats;
            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 gaussian : TEXCOORD0;
                nointerpolation float4 color : COLOR;
            };

            float2 ProjectAxis(float3 axis, float4 center)
            {
                float4 delta = mul(GetWorldToHClipMatrix(), float4(TransformObjectToWorldDir(axis, false), 0));
                // Perspective-divide Jacobian; also works for the orthographic live camera.
                return (delta.xy - (center.xy / center.w) * delta.w) / center.w;
            }

            Varyings Vert(uint vertex : SV_VertexID)
            {
                Splat s = _AvatarSplats[vertex / 4];
                float2 corner = float2(vertex & 1, (vertex >> 1) & 1) * 2 - 1;
                float4 center = TransformObjectToHClip(s.centerOpacity.xyz);
                Varyings o = (Varyings)0;
                if (center.w <= 0.0001)
                {
                    o.position = float4(0, 0, -2, 1);
                    return o;
                }
                float2 a = ProjectAxis(s.tangent.xyz, center);
                float2 b = ProjectAxis(s.bitangent.xyz, center);
                float3 n = cross(s.tangent.xyz, s.bitangent.xyz);
                float2 c = ProjectAxis(n * rsqrt(max(dot(n, n), 1e-20)) * length(s.tangent.xyz) * 0.14, center);
                float2 pixel = 2.0 / GetScaledScreenParams().xy;
                // Project the 3D covariance and factor the 2D covariance via Cholesky.
                float xx = dot(float3(a.x, b.x, c.x), float3(a.x, b.x, c.x)) + 0.09 * pixel.x * pixel.x;
                float yy = dot(float3(a.y, b.y, c.y), float3(a.y, b.y, c.y)) + 0.09 * pixel.y * pixel.y;
                float xy = dot(float3(a.x, b.x, c.x), float3(a.y, b.y, c.y));
                float sx = sqrt(xx), shear = xy / sx;
                float sy = sqrt(max(yy - shear * shear, 0.09 * pixel.y * pixel.y));
                o.gaussian = corner * 3;
                o.position = center;
                o.position.xy += float2(sx * o.gaussian.x, shear * o.gaussian.x + sy * o.gaussian.y) * center.w;
                o.color = float4(s.color.rgb, s.centerOpacity.w);
                #if defined(UNITY_COLORSPACE_GAMMA)
                o.color.rgb = LinearToSRGB(o.color.rgb);
                #endif
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float radius = dot(i.gaussian, i.gaussian);
                clip(9 - radius);
                float opacity = i.color.a * exp(-0.5 * radius);
                return half4(i.color.rgb * opacity, opacity);
            }
            ENDHLSL
        }
    }
}
