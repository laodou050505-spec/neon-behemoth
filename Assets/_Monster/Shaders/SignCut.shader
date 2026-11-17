// 标题招牌的灯箱字 / 字的侧面切片：不受光的贴图 × 颜色，按透明度裁剪（AlphaToMask，MSAA 下边缘平滑），写深度。
// _Reveal：uv.x 小于它的部分通电（亮），右边还没通电的灯管按 _Dark 压暗 —— 入场时从左到右一个字一个字亮起来。
Shader "Monster/SignCut"
{
    Properties
    {
        _MainTex ("贴图", 2D) = "white" {}
        _Color ("颜色", Color) = (1,1,1,1)
        _Cutoff ("裁剪", Range(0,1)) = 0.5
        _Reveal ("通电到（uv.x）", Float) = 2
        _Dark ("没通电的亮度", Range(0,1)) = 0.16
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            AlphaToMask On
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial) float4 _MainTex_ST; half4 _Color; half _Cutoff; float _Reveal; half _Dark; CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color;
                // 裁剪边缘做成可被 MSAA 平滑的 alpha（不直接 clip，交给 AlphaToMask）
                half a = saturate((c.a - _Cutoff) / max(fwidth(c.a), 1e-4) + 0.5);
                half on = step(i.uv.x, _Reveal);
                return half4(c.rgb * lerp(_Dark, 1, on), a);
            }
            ENDHLSL
        }
    }
}
