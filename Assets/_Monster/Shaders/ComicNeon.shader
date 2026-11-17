// 漫画格画面（11-11 用户：不改画面内容，效果更像霓虹美式动漫涂鸦）：
//  · 饱和度、对比度略提（霓虹感），亮部往品红 / 青偏一点点（像霓虹灯照着）；
//  · 暗部叠网点（美漫印刷的半调网点，按格子 UV 排列，网点大小随暗度变化）；
//  · 印刷错版：红通道往右上偏一点点、蓝通道往左下（只在边缘明显，画面内容不变）；
//  · 不受光、可整体淡入淡出（_Color.a）。
Shader "Monster/ComicNeon"
{
    Properties
    {
        _MainTex ("贴图", 2D) = "white" {}
        _Color ("颜色", Color) = (1,1,1,1)
        _Dots ("网点密度", Float) = 140
        _Shift ("错版距离（UV）", Float) = 0.0022
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial) float4 _MainTex_ST; half4 _Color; float _Dots, _Shift; CBUFFER_END
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
                float2 uv = i.uv;
                half3 c;
                c.r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(_Shift, _Shift * 0.6)).r;
                c.g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).g;
                c.b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(_Shift * 0.8, _Shift * 0.5)).b;
                // 饱和度 + 对比度
                half l = dot(c, half3(0.299, 0.587, 0.114));
                c = lerp(l.xxx, c, 1.28);
                c = saturate((c - 0.5) * 1.12 + 0.5);
                // 亮部偏霓虹（品红 / 青随画面位置渐变）
                half3 neon = lerp(half3(1.0, 0.45, 0.85), half3(0.4, 0.95, 1.0), saturate(uv.x * 0.8 + uv.y * 0.4));
                c = lerp(c, c * neon * 1.15, smoothstep(0.55, 0.95, l) * 0.35);
                // 暗部网点：旋转 45° 的网格，点半径随暗度变大
                float2 g = mul(float2x2(0.7071, -0.7071, 0.7071, 0.7071), uv * _Dots);
                float2 f = frac(g) - 0.5;
                float d = length(f);
                half dark = saturate((0.42 - l) / 0.42);
                float rad = 0.5 * sqrt(dark);
                float w = max(fwidth(d), 1e-4);
                half dot_ = 1 - smoothstep(rad - w, rad + w, d);
                c = lerp(c, c * half3(0.22, 0.12, 0.32), dot_ * 0.55 * step(0.02, dark));
                return half4(c, 1) * _Color;
            }
            ENDHLSL
        }
    }
}
