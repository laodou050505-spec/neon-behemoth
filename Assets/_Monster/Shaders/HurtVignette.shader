// 受伤 / 低血量 / 威胁提示 / 闭眼 / 核爆白光：一张全屏片。
// 11-13 用户真机：①左右眼看到的提示弧位置不一样，中间多出一份，很乱；②血量红框在头显里只露出一点点。
// 原因：上一版按“每只眼渲染画面里的位置”画。左右眼的画面中心并不重合（镜片中心偏向鼻侧），同一个画面位置在两只眼里是不同的方向。
// 这一版按“头的朝向角度”画：每个像素先用这只眼自己的逆投影算出世界方向，再换到头（两眼正中）的坐标系里，
// p = 方向的 (右, 上) 分量 ÷ 前分量 ÷ tan(32°)，r = |p| = 1 对应离视线正前方 32°。两只眼算出来的是同一个角度，效果像贴在很远的地方，不重影。
// 11-13 用户真机：位置提醒的图标是糊的。两个原因，这一版都改了：
//   ① 图形本身的边是用很宽的渐变画的（边缘糊约 1°）→ 改成按“到图形边缘的距离”画，边缘只留 1 个像素的抗锯齿，外面再包一圈深色描边；
//   ② 上一版等于把图标画在无穷远处，而玩家的眼睛对焦在半米外的城市上，远处的东西在两眼里错开约 7°，看着是重影 →
//      现在画在一个以头为球心、半径 _Depth（= 头到城市的距离）的球面上：每只眼的视线先和这个球求交，再算交点相对头的方向，两只眼就会在城市那个距离上重合。
// 各效果的范围（都能在头显可见范围内看到）：红框从 32° 往外，血越少越往里压（最深到约 26°）；威胁弧在 16° 一圈；眼皮上下合。
Shader "Monster/HurtVignette"
{
    Properties
    {
        _Frame ("Frame", 2D) = "black" {}
        _Dark ("Dark", Float) = 0 _Flat ("Flat", Float) = 0 _FrameAmt ("FrameAmt", Float) = 0 _Hit ("Hit", Float) = 0 _Angle ("Angle", Float) = 0
        _Lid ("Lid", Float) = 0 _White ("White", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+50" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_Frame); SAMPLER(sampler_Frame);
            CBUFFER_START(UnityPerMaterial) float4 _Frame_ST; float _Dark, _Flat, _FrameAmt, _Hit, _Angle, _Lid, _White; CBUFFER_END
            float4 _Threat[6];   // x = 方位角，y = 强度，z = 0 黄 / 1 红
            float4 _HeadR, _HeadU, _HeadF, _HeadP;   // 头（两眼正中）的右 / 上 / 前（世界方向）、位置
            float _Depth;                             // 效果所在球面的半径（米）
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; float3 eye : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = float4(v.positionOS.xy, 0.5, 1);
                float4 wp = mul(UNITY_MATRIX_I_VP, o.positionCS);
                o.eye = GetCameraPositionWS();
                o.dir = wp.xyz / wp.w - o.eye;
                return o;
            }
            void Over(inout half3 col, inout float a, half3 c, float sa)
            {
                float oa = sa + a * (1 - sa);
                col = (c * sa + col * a * (1 - sa)) / max(oa, 1e-4); a = oa;
            }
            float AngDiff(float x, float y) { return abs(atan2(sin(x - y), cos(x - y))); }
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                // 这只眼的视线和“头周围半径 _Depth 的球面”的交点，相对头的方向（单眼 / 编辑器里眼睛就在头的位置，d 不变）
                float3 eo = i.eye - _HeadP.xyz; float eb = dot(eo, d);
                float R = max(_Depth, 0.2);
                d = normalize(eo + d * (-eb + sqrt(max(eb * eb - dot(eo, eo) + R * R, 0))));
                float fz = max(dot(d, _HeadF.xyz), 0.05);
                float2 p = float2(dot(d, _HeadR.xyz), dot(d, _HeadU.xyz)) / fz / 0.6249;   // ÷ tan(32°)
                float r = length(p); float ang = atan2(p.y, p.x);
                half3 col = 0; float a = 0;
                // 压暗：血越少，暗角越往里（最深从约 22° 开始，往外渐暗；不是全黑）
                a = saturate(smoothstep(lerp(1.1, 0.62, saturate(_Dark)), 1.5, r) * saturate(_Dark) * 0.85 + _Flat);
                // 红色分三层，从里到外越来越实：
                //   ① 半透明红晕：从 20°～30° 往外慢慢变红（最多三成半不透明）—— 不挡东西，但整个视野外圈都带着红；
                //   ② 泼漆 / 碎玻璃贴图：从 31°～38° 往外露出来（贴图铺在 ±60° 上，头显里看到的是它往里伸的尖和滴痕）；
                //   ③ 最外圈（43° 以外）实心红，托住贴图。
                // 被打的一侧 amt 额外 +0.45：那一侧更红、更往里压
                float side = smoothstep(1.3, 0.2, AngDiff(ang, _Angle)) * saturate(_Hit);
                float amt = saturate(_FrameAmt + side * 0.45);
                half3 red = half3(0.9, 0.05, 0.14);
                Over(col, a, red, smoothstep(lerp(0.95, 0.58, amt), 1.55, r) * saturate(amt * 1.8) * 0.36);
                half3 ft = SAMPLE_TEXTURE2D(_Frame, sampler_Frame, saturate(p / 2.75 * 0.5 + 0.5)).rgb;
                float fa = saturate(max(ft.r, max(ft.g, ft.b)) * 1.8);
                float reveal = smoothstep(lerp(1.28, 0.96, amt), lerp(1.6, 1.28, amt), r);
                Over(col, a, ft * 1.15, fa * reveal * saturate(amt * 3.2));
                Over(col, a, red, smoothstep(1.5, 1.95, r) * saturate(amt * 2.6) * 0.9);
                // 威胁指示（离视线正前方约 15° 的一圈上）：一段实心弧 + 弧外一个指向威胁的实心箭头；边缘锐利（1 像素抗锯齿）、外包深色描边
                float px = max(fwidth(p.x), fwidth(p.y)) * 0.75;
                for (int k = 0; k < 6; k++)
                {
                    float s = _Threat[k].y; if (s <= 0.001) continue;
                    float2 td = float2(cos(_Threat[k].x), sin(_Threat[k].x));
                    float ty = dot(p, td), tx = dot(p, float2(-td.y, td.x));      // 以威胁方向为“上”的坐标：ty 沿方向往外，tx 横向
                    float da = abs(atan2(tx, ty));
                    float w = 0.24 + 0.1 * s;
                    float sdArc = max(abs(r - 0.46) - 0.028, (da - w) * r);
                    float sdTip = max(0.512 - ty, (ty + abs(tx) * 1.214 - 0.6) / 1.573);
                    float sd = min(sdArc, sdTip);
                    float fill = smoothstep(px, -px, sd), rim = smoothstep(px, -px, sd - 0.011);
                    float al = saturate(s * 1.6);
                    half3 tc = _Threat[k].z > 0.5 ? half3(1.0, 0.16, 0.24) : half3(1.0, 0.82, 0.16);
                    Over(col, a, half3(0.07, 0.02, 0.06), rim * al * 0.9);
                    Over(col, a, tc, fill * al);
                }
                // 眼皮（上下两片，中间略鼓）
                float lidEdge = (1 - saturate(_Lid)) * (1.6 - 0.22 * p.x * p.x);
                Over(col, a, half3(0, 0, 0), smoothstep(lidEdge, lidEdge + 0.12, abs(p.y)) * step(0.001, _Lid));
                // 白光
                Over(col, a, half3(1, 1, 1), saturate(_White));
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
}
