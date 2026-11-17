// 巨兽之手：三渲二两阶明暗 + 墨线外描边。物体本地 +Y = 手背方向，+Z = 骨头方向（MonsterHand 摆每节骨头时保证）。
//   手背（法线朝 +Y）：深蓝青绿的叠瓦鳞片（按骨头本地“米制”坐标铺，手动起来鳞片不游动）；掌心（朝 −Y）：沙色皮肤。
//   潮汐纹：手背正中一条细线，_Lit = 1 时亮青色（怒气条），满怒时脉动。
//   骨甲（_Plate = 1）：骨白、亮高光；_Crack = 1 时出现一道墨色裂纹并压暗（体力少了一格）。
//   _Hurt：受伤时整体泛红闪一下。
Shader "Monster/CreatureSkin"
{
    Properties
    {
        _Back ("手背", Color) = (0.08,0.15,0.25,1)
        _Back2 ("手背亮", Color) = (0.2,0.46,0.5,1)
        _Belly ("掌心", Color) = (0.84,0.72,0.55,1)
        _Bone ("骨甲", Color) = (0.93,0.89,0.8,1)
        _Claw ("爪", Color) = (0.16,0.15,0.18,1)
        [HDR] _Tide ("潮汐纹", Color) = (0.3,2.2,2.4,1)
        _Ink ("墨线", Color) = (0.06,0.05,0.1,1)
        _Outline ("描边宽", Float) = 0.0011
        _Scale ("鳞片大小(米)", Float) = 0.0085
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Back, _Back2, _Belly, _Bone, _Claw, _Tide, _Ink; float _Outline, _Scale;
        CBUFFER_END
        UNITY_INSTANCING_BUFFER_START(P)
            UNITY_DEFINE_INSTANCED_PROP(float, _Lit)
            UNITY_DEFINE_INSTANCED_PROP(float, _Plate)
            UNITY_DEFINE_INSTANCED_PROP(float, _Crack)
            UNITY_DEFINE_INSTANCED_PROP(float, _IsClaw)
            UNITY_DEFINE_INSTANCED_PROP(float, _Hurt)
            UNITY_DEFINE_INSTANCED_PROP(float, _Pulse)
        UNITY_INSTANCING_BUFFER_END(P)
        ENDHLSL
        Pass
        {
            Name "Skin"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 nW : TEXCOORD0; float3 nO : TEXCOORD1; float3 pM : TEXCOORD2; float3 pW : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pW = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.pW);
                o.nW = TransformObjectToWorldNormal(v.normalOS);
                o.nO = v.normalOS;
                // 骨头本地坐标换成米（每个轴乘物体缩放），鳞片按真实尺寸铺
                float3x3 M = (float3x3)UNITY_MATRIX_M;   // 实例化时 unity_ObjectToWorld 不是实例自己的矩阵，要用 UNITY_MATRIX_M
                float3 s = float3(length(mul(M, float3(1, 0, 0))), length(mul(M, float3(0, 1, 0))), length(mul(M, float3(0, 0, 1))));
                o.pM = v.positionOS.xyz * s;
                return o;
            }
            // 叠瓦鳞片：沿骨头（z）一排排，隔排错半格；每片是朝指尖方向的圆弧，离边越近越暗，边上一道墨线
            half3 Scales(float2 p, out half edge)
            {
                float S = _Scale;
                float row = floor(p.y / (S * 0.8));
                float2 q = float2(p.x / S + (fmod(abs(row), 2) * 0.5), p.y / (S * 0.8));
                float2 c = float2(floor(q.x) + 0.5, row + 0.15);
                float2 d = (q - c) * float2(1, 1.15);
                float r = length(d);
                // 抗锯齿：一个鳞片在屏幕上小于约 6 像素时，边线逐渐淡掉（远处变成平滑的色块，不出摩尔纹）
                float px = max(fwidth(q.x), fwidth(q.y));
                float aa = saturate(1 - (px - 0.08) / 0.12);
                edge = smoothstep(0.4 - px, 0.5 + px, r) * aa;
                half k = lerp(0.45, saturate(1 - r * 1.6) * 0.75 + 0.25 * (1 - saturate(q.y - row)), aa);
                return lerp(_Back.rgb, _Back2.rgb, k);
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float lit = UNITY_ACCESS_INSTANCED_PROP(P, _Lit), plate = UNITY_ACCESS_INSTANCED_PROP(P, _Plate), crack = UNITY_ACCESS_INSTANCED_PROP(P, _Crack);
                float isClaw = UNITY_ACCESS_INSTANCED_PROP(P, _IsClaw), hurt = UNITY_ACCESS_INSTANCED_PROP(P, _Hurt), pulse = UNITY_ACCESS_INSTANCED_PROP(P, _Pulse);
                float3 n = normalize(i.nW);
                Light L = GetMainLight(TransformWorldToShadowCoord(i.pW));
                half ndl = dot(n, L.direction) * L.shadowAttenuation;
                half band = ndl > 0.35 ? 1 : ndl > -0.05 ? 0.78 : 0.58;
                float dorsal = smoothstep(-0.15, 0.35, normalize(i.nO).y);
                half edge; half3 back = Scales(i.pM.xz, edge);
                back = lerp(back, _Ink.rgb, edge * 0.8);
                half3 c = lerp(_Belly.rgb, back, dorsal);
                // 潮汐纹：手背正中细线（只在手背、骨头中段）
                half tideLn = (1 - smoothstep(0.0011, 0.0019, abs(i.pM.x))) * step(0.8, dorsal);
                half glow = tideLn * lit * (0.75 + 0.25 * sin(_Time.y * 6) * pulse + pulse * 0.25);
                c = lerp(c, _Ink.rgb, tideLn * (1 - lit) * 0.6);   // 没亮时是一道暗槽
                // 骨甲 / 爪
                c = lerp(c, _Bone.rgb, plate);
                half crackLine = plate * crack * (1 - smoothstep(0.08, 0.16, abs(i.pM.x * 400 - 0.35 * sin(i.pM.z * 900))));
                c = lerp(c, c * 0.62, plate * crack);
                c = lerp(c, _Ink.rgb, crackLine);
                c = lerp(c, _Claw.rgb, isClaw);
                c *= band * lerp(half3(1, 1, 1), L.color, 0.35);
                // 骨甲 / 爪的硬高光
                float3 vdir = GetWorldSpaceNormalizeViewDir(i.pW);
                half spec = step(0.9, dot(n, normalize(L.direction + vdir))) * saturate(plate + isClaw) * 0.55;
                // 骨甲 / 爪边缘一道冷色反光，让它们从鳞片里跳出来
                spec += pow(1 - saturate(dot(n, vdir)), 3) * saturate(plate + isClaw) * 0.25;
                c += spec;
                c += _Tide.rgb * glow;
                c = lerp(c, half3(1, 0.25, 0.2), hurt * 0.55);
                return half4(c, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 w = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = normalize(TransformObjectToWorldNormal(v.normalOS));
                o.positionCS = TransformWorldToHClip(w + n * _Outline);
                return o;
            }
            half4 frag(V i) : SV_Target { return _Ink; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half4 frag(V i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
