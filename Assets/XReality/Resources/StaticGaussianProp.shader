Shader "XReality/Static Gaussian Prop"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "DisableBatching"="True" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off ZWrite Off Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            struct Splat { float4 center, ax, ay, az; };
            StructuredBuffer<Splat> _Geometry;
            StructuredBuffer<float3> _Coeffs;
            float4 _Basis0, _Basis1, _Basis2;
            StructuredBuffer<uint> _Order;
            struct Fragment { float4 clip : SV_POSITION; float2 uv : TEXCOORD0; nointerpolation float4 rgba : COLOR; };
            float2 Derivative(float3 axis, float4 center)
            {
                float4 delta = mul(GetWorldToHClipMatrix(), float4(axis, 0));
                return (delta.xy - center.xy / center.w * delta.w) / center.w;
            }
            float3 EvaluateSH(uint i, float3 d)
            {
                float x=d.x, y=d.y, z=d.z, xx=x*x, yy=y*y, zz=z*z, xy=x*y;
                float b[16] = { .282094792, -.488602512*y, .488602512*z, -.488602512*x,
                    1.092548431*xy, -1.092548431*y*z, .315391565*(2*zz-xx-yy), -1.092548431*x*z, .546274215*(xx-yy),
                    -.590043590*y*(3*xx-yy), 2.890611443*xy*z, -.457045799*y*(4*zz-xx-yy),
                    .373176333*z*(2*zz-3*xx-3*yy), -.457045799*x*(4*zz-xx-yy), 1.445305721*z*(xx-yy), -.590043590*x*(xx-3*yy) };
                float3 rgb = 0.5;
                [unroll] for (uint j=0;j<16;j++) rgb += b[j] * _Coeffs[i*16+j].rgb;
                rgb = max(0, rgb);
                #if !defined(UNITY_COLORSPACE_GAMMA)
                rgb = SRGBToLinear(rgb);
                #endif
                return rgb;
            }
            Fragment Vert(uint vertex : SV_VertexID)
            {
                uint i = _Order[vertex/4];
                Splat s = _Geometry[i];
                Fragment o = (Fragment)0;
                float3 local = s.center.xyz;
                float4 p = TransformObjectToHClip(local);
                if (p.w < 0.011 || s.center.w < (1.0/255)) { o.clip=float4(2,2,0,1); return o; }
                float2 a = Derivative(TransformObjectToWorldDir(s.ax.xyz,false), p);
                float2 b = Derivative(TransformObjectToWorldDir(s.ay.xyz,false), p);
                float2 c = Derivative(TransformObjectToWorldDir(s.az.xyz,false), p);
                float2 pixel = 2.0 / GetScaledScreenParams().xy;
                float3 u = float3(a.x,b.x,c.x), v = float3(a.y,b.y,c.y);
                float sx = sqrt(dot(u,u) + .3*pixel.x*pixel.x);
                float shear = dot(u,v)/sx;
                float sy = sqrt(max(dot(v,v) + .3*pixel.y*pixel.y - shear*shear, .3*pixel.y*pixel.y));
                o.uv = (float2(vertex&1, (vertex>>1)&1)*2-1)*3;
                p.xy += float2(sx*o.uv.x, shear*o.uv.x+sy*o.uv.y)*p.w;
                o.clip = p;
                float3 d = normalize(local - TransformWorldToObject(_WorldSpaceCameraPos));
                d = _Basis0.xyz*d.x + _Basis1.xyz*d.y + _Basis2.xyz*d.z;
                o.rgba = float4(EvaluateSH(i, d), s.center.w);
                return o;
            }
            half4 Frag(Fragment i) : SV_Target
            {
                float d=dot(i.uv,i.uv);
                float alpha=min(.99, i.rgba.a*exp(-.5*d));
                clip(min(9-d,alpha-(1.0/255)));
                return half4(i.rgba.rgb*alpha,alpha);
            }
            ENDHLSL
        }
    }
}
