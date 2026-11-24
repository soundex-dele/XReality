Shader "XReality/Storybook Clay"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; half3 color : COLOR; float3 positionWS : TEXCOORD1; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color.rgb;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                half3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half key = saturate(dot(n, normalize(half3(-.64,.74,-.4))));
                half sky = saturate(n.y*.5+.5);
                half rim = pow(1-saturate(dot(n,view)),3)*saturate(n.x*.65+.34);
                return half4(i.color*(half3(.53,.55,.59) + sky*.16 + key*half3(.37,.33,.27)) + rim*half3(.075,.061,.044),1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
