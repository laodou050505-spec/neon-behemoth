// 电站的护盾（11-12 用户：电站的护甲要看得见 —— 一个罩子，被打会裂、打破会碎；要比封印塔的蜂窝护盾简单）。
// 低面数的半球罩：每个三角面淡淡地填色 + 亮边（顶点里存了重心坐标，离边近就画线）+ 轮廓更亮（菲涅尔）。
// _Crack（0 完好 → 1 快碎）：越大，越多的三角面上多出一道裂纹、少数几块直接缺掉；_Hit：刚挨打整罩闪白。
Shader "Monster/StationShield"
{
    Properties { _Color ("Color", Color) = (1,0.85,0.3,1) _Crack ("Crack", Float) = 0 _Hit ("Hit", Float) = 0 _Fade ("Fade", Float) = 1 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+5" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) half4 _Color; float _Crack, _Hit, _Fade; CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 bary : TEXCOORD0; float3 n : TEXCOORD1; float3 vdir : TEXCOORD2; float id : TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 w = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(w);
                o.bary = float3(v.uv.x, v.uv.y, 1 - v.uv.x - v.uv.y);
                o.n = TransformObjectToWorldNormal(v.normalOS); o.vdir = GetWorldSpaceNormalizeViewDir(w); o.id = v.uv2.x;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float e = min(i.bary.x, min(i.bary.y, i.bary.z));
                float gone = step(i.id, _Crack * 0.3);                       // 缺掉的面：只剩暗边
                float cracked = step(i.id, _Crack * 0.85) * (1 - gone);      // 裂了的面：多一道折线裂纹
                float edge = 1 - smoothstep(0.02, 0.05, e);
                float crack = cracked * (1 - smoothstep(0.012, 0.03, abs(i.bary.x - i.bary.y * 0.6 - 0.12 * sin(i.bary.z * 9.0))));
                float fres = pow(1 - saturate(dot(normalize(i.n), i.vdir)), 2.0);
                float a = (0.1 + 0.3 * fres) * (1 - gone) + edge * lerp(0.75, 0.25, gone) + crack * 0.9;
                half3 c = lerp(_Color.rgb, half3(1, 1, 1), saturate(edge * 0.45 + crack * 0.8 + _Hit));
                return half4(c, saturate(a + _Hit * 0.5) * _Fade);
            }
            ENDHLSL
        }
    }
}
