// 标题招牌的霓虹光晕：贴图颜色 × a × _Color 加法叠上去，不写深度（MR 里叠在真实房间上就是一圈光）。
// _Reveal 同 SignCut：还没通电的部分不发光。
Shader "Monster/SignGlow"
{
    Properties
    {
        _MainTex ("光晕", 2D) = "black" {}
        _Color ("颜色", Color) = (1,1,1,1)
        _Reveal ("通电到（uv.x）", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial) float4 _MainTex_ST; half4 _Color; float _Reveal; CBUFFER_END
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
                half4 g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half on = saturate((_Reveal - i.uv.x) * 12);
                return half4(g.rgb * g.a * _Color.rgb * on, 0);
            }
            ENDHLSL
        }
    }
}
