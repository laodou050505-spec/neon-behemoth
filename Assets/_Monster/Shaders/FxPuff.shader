// 特效块（烟、尘、火舌、爆炸火球、碎块）：三渲二两阶明暗 + 墨色边缘，用 Graphics.RenderMeshInstanced 一次画一批。
// 每个实例：_Tint（rgb 颜色，a 透明度）、_Glow（0 = 受光的烟尘，1 = 自发光的火）。
Shader "Monster/FxPuff"
{
    Properties
    {
        _Shade ("暗部倍数", Float) = 0.62 _Ink ("边缘墨色", Color) = (0.13,0.08,0.18,1)
        // 11-09：瓦砾要不透明、写深度（不然一堆块互相乱叠、发虚）——同一个着色器，混合方式由材质属性定，不增加变体
        [HideInInspector] _SrcBlend ("", Float) = 5 [HideInInspector] _DstBlend ("", Float) = 10 [HideInInspector] _ZWrite ("", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial) half _Shade; half4 _Ink; CBUFFER_END
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
                UNITY_DEFINE_INSTANCED_PROP(float, _Glow)
            UNITY_INSTANCING_BUFFER_END(Props)
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 n : TEXCOORD0; float3 vdir : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 w = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(w);
                o.n = TransformObjectToWorldNormal(v.normalOS);
                o.vdir = GetWorldSpaceNormalizeViewDir(w);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Tint);
                float glow = UNITY_ACCESS_INSTANCED_PROP(Props, _Glow);
                float3 n = normalize(i.n);
                Light L = GetMainLight();
                half lit = step(0.15, dot(n, L.direction));
                half3 c = tint.rgb * lerp(_Shade, 1, lit) * lerp(L.color, 1, 0.35);
                // 火：中心亮、边缘偏红（按视线夹角）
                half facing = saturate(dot(n, i.vdir));
                half3 fire = lerp(tint.rgb * 0.75, tint.rgb * 1.6 + 0.15, step(0.55, facing));
                c = lerp(c, fire, glow);
                // 墨色轮廓：边缘一圈压暗（火不压）
                half rim = step(facing, 0.22) * (1 - glow);
                c = lerp(c, _Ink.rgb, rim * 0.85);
                return half4(c, tint.a);
            }
            ENDHLSL
        }
    }
}
