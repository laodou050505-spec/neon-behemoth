// 城区边缘的郊野过渡带：贴图 × 简单卡通光照，a 通道渐隐到透明，露出真实桌面 / 地板
Shader "Monster/CityFade"
{
    Properties
    {
        _BaseMap ("贴图（a=渐隐）", 2D) = "white" {}
        _BaseColor ("颜色", Color) = (1,1,1,1)
        _ShadowTint ("暗部色", Color) = (0.42,0.36,0.62,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial) float4 _BaseMap_ST; half4 _BaseColor, _ShadowTint; CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; half fog : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz); o.positionCS = p.positionCS; o.positionWS = p.positionWS; o.uv = TRANSFORM_TEX(v.uv, _BaseMap); o.fog = ComputeFogFactor(p.positionCS.z); return o; }
            half4 frag(V i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                Light m = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 c = t.rgb * lerp(_ShadowTint.rgb, m.color, lerp(0.4, 1, m.shadowAttenuation));
                return half4(MixFog(c, i.fog), t.a);
            }
            ENDHLSL
        }
    }
}
