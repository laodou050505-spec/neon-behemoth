// 霓虹 / 窗灯 / 能量线：不受光照的自发光，_Fade 用于渐隐（郊野过渡带也用它的透明版本）
Shader "Monster/CityGlow"
{
    Properties
    {
        [HDR] _Color ("颜色", Color) = (1,0.3,0.7,1)
        _BaseMap ("遮罩（a 通道）", 2D) = "white" {}
        _Alpha ("透明度", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial) half4 _Color; float4 _BaseMap_ST; half _Alpha; CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half fog : TEXCOORD1; float3 posWS : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            float4 _Blackout;   // 结局“熄灯”：全局量，xyz = 圆心（世界坐标），w = 半径；离圆心比半径近的灯灭掉（w = 0 时全亮）
            half BlackoutK(float3 p) { return smoothstep(_Blackout.w - 0.06, _Blackout.w, distance(p, _Blackout.xyz)); }
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.posWS = TransformObjectToWorld(v.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.posWS); o.uv = v.uv; o.fog = ComputeFogFactor(o.positionCS.z); return o; }
            half4 frag(V i) : SV_Target { return half4(MixFog(_Color.rgb * lerp(0.07, 1, BlackoutK(i.posWS)), i.fog), 1); }   // 熄灯后剩一点点底色（灯管本身的颜色）
            ENDHLSL
        }
    }
}
