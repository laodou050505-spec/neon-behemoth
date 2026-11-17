// 叠加发光（路灯在地面的光晕等）：_Color × 贴图 a，加法混合，不写深度
Shader "Monster/CityGlowAdd"
{
    Properties
    {
        _Color ("颜色", Color) = (1,0.7,0.4,1)
        _BaseMap ("形状（a）", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-5" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial) half4 _Color; float4 _BaseMap_ST; CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 posWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            float4 _Blackout;   // 结局“熄灯”：全局量，xyz = 圆心（世界坐标），w = 半径；离圆心比半径近的灯灭掉（w = 0 时全亮）
            half BlackoutK(float3 p) { return smoothstep(_Blackout.w - 0.06, _Blackout.w, distance(p, _Blackout.xyz)); }
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.posWS = TransformObjectToWorld(v.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.posWS); o.uv = v.uv; return o; }
            half4 frag(V i) : SV_Target { half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a; return half4(_Color.rgb * a * BlackoutK(i.posWS), 0); }
            ENDHLSL
        }
    }
}
