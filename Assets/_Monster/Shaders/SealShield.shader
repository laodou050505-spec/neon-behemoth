// 封印塔护盾：三渲二风格的能量罩（半透明）
//  - 六边形蜂窝网格（按物体空间的柱面坐标铺，不会在侧面拉伸），格线比格子亮；少数格子随时间闪亮
//  - 边缘光（菲涅尔）分两阶：边缘一圈实色亮带，往里快速变淡
//  - 两道竖向往上扫的光带 + 整体呼吸脉动
//  - 地面以下（_GroundY 世界高度）直接裁掉：MR 里虚拟物体会盖在真实桌面上，桌面以下的部分不能露出来；贴地 1cm 渐亮成一圈接地光环
//  - 先画背面（淡）再画正面，看进去能看到罩子内侧的网格
Shader "Monster/SealShield"
{
    Properties
    {
        [HDR] _Color ("颜色", Color) = (0.8, 0.3, 1, 1)
        _GroundY ("地面高度（世界）", Float) = 0
        _HexScale ("蜂窝密度", Float) = 9
        _Alpha ("整体透明度", Range(0, 1)) = 1
        _Hit ("受击闪光", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Color; float _GroundY, _HexScale, _Alpha, _Hit;
        CBUFFER_END
        struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct V { float4 positionCS : SV_POSITION; float3 posOS : TEXCOORD0; float3 posWS : TEXCOORD1; float3 nWS : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
        V vert(A v)
        {
            V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.posOS = v.positionOS.xyz; o.posWS = TransformObjectToWorld(v.positionOS.xyz);
            o.nWS = TransformObjectToWorldNormal(v.normalOS);
            o.positionCS = TransformWorldToHClip(o.posWS);
            return o;
        }
        // 六边形格：返回 (到格边的距离 0~0.5, 格子编号哈希)
        float2 hex(float2 p)
        {
            const float2 s = float2(1, 1.7320508);
            float2 a = fmod(abs(p), s) - s * 0.5;
            float2 b = fmod(abs(p - s * 0.5), s) - s * 0.5;
            float2 g = dot(a, a) < dot(b, b) ? a : b;
            float2 id = p - g;
            float2 q = abs(g);
            float edge = 0.5 - max(dot(q, s * 0.5), q.x);
            return float2(edge, frac(sin(dot(floor(id * 3.1), float2(12.9898, 78.233))) * 43758.5453));
        }
        half4 shade(V i, float back)
        {
            clip(i.posWS.y - _GroundY);
            float t = _Time.y;
            float3 n = normalize(i.nWS); float3 vd = normalize(GetWorldSpaceViewDir(i.posWS));
            float fres = 1 - saturate(abs(dot(n, vd)));
            // 柱面坐标：绕 y 的角度 × 半径感 + 高度（物体空间，球半径 0.5）
            float ang = atan2(i.posOS.z, i.posOS.x) / 6.2831853;
            float2 hp = float2(ang * _HexScale * 2.2, i.posOS.y * _HexScale * 1.2);
            float2 h = hex(hp);
            float line_ = 1 - smoothstep(0.02, 0.06, h.x);                    // 格线
            float blink = step(0.965, frac(h.y + floor(t * 0.7 + h.y * 5) * 0.37)); // 偶尔亮一格
            float scan = 0;
            float y01 = i.posOS.y + 0.5;
            for (int k = 0; k < 2; k++) { float c = frac(t * 0.22 + k * 0.5); scan += smoothstep(0.06, 0, abs(y01 - c)); }
            float rim = step(0.72, fres) * 0.9 + smoothstep(0.35, 0.72, fres) * 0.35;  // 两阶边缘
            float pulse = 0.85 + 0.15 * sin(t * 2.1);
            float ground = smoothstep(0.012, 0, i.posWS.y - _GroundY);         // 贴地光环
            float a = (0.05 + line_ * 0.32 + blink * 0.25 + scan * 0.22 * (0.4 + line_) + rim + ground * 0.8) * pulse;
            a = lerp(a, 1, _Hit * 0.6);
            a *= back > 0.5 ? 0.45 : 1;
            half3 c = _Color.rgb * (0.8 + line_ * 0.8 + rim * 0.9 + scan * 0.6 + ground) + _Hit;
            return half4(c, saturate(a * _Alpha));
        }
        ENDHLSL
        Pass
        {
            Name "Back"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragB
            #pragma multi_compile_instancing
            half4 fragB(V i) : SV_Target { return shade(i, 1); }
            ENDHLSL
        }
        Pass
        {
            Name "Front"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragF
            #pragma multi_compile_instancing
            half4 fragF(V i) : SV_Target { return shade(i, 0); }
            ENDHLSL
        }
    }
}
