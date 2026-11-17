// 巨兽之手 v3（11-10 用户：手粗糙难看没有力量感、到处缺面、手背结构丑 → 带骨骼的整只手 + 这个着色器）。
// 霓虹动漫 + 美式卡通涂鸦：三阶色调光照、冷色轮廓光、象牙骨甲（上釉高光带）、墨黑爪、霓虹沟槽 / 爪环自发光（按怒气点亮）、粗黑描边。
// 骨甲 / 沟槽的轮廓逐像素算（顶点里存的是连续参数，见 Tools/blender_hand.py），所以边缘利落、和网格疏密无关：
//   UV1.y 区域：0 手掌组（手背甲壳 + 前臂骨甲）  0.1 手指  0.25 硬骨甲（骨刺 / 骨鳍）  0.75 爪  1 霓虹（爪环）
//   手指：UV0 = (节号 + 节内位置, 朝背程度)，UV2 = (沿手指长度 m, 绕手指角度) → 鳞片
//   手掌组：UV0 = (离两片甲壳中线的距离差, 沿掌骨位置)，UV1.x = 前臂：沿前臂距离 / 手背：1 + 沟号，UV2 = 俯视坐标，顶点色 alpha = 朝上程度
Shader "Monster/MonsterHand"
{
    Properties
    {
        _Rage ("怒气 0-1", Range(0, 1)) = 0
        _Hp ("体力 0-1", Range(0, 1)) = 1
        _Hurt ("受伤闪红", Range(0, 1)) = 0
        _Armor ("骨甲", Color) = (0.17, 0.14, 0.25, 1)
        _ArmorD ("骨甲亮边", Color) = (0.62, 0.55, 0.85, 1)
        _Claw ("爪", Color) = (0.03, 0.025, 0.045, 1)
        _Neon ("霓虹青", Color) = (0.35, 0.95, 1, 1)
        _Neon2 ("霓虹粉", Color) = (1, 0.32, 0.72, 1)
        _Rim ("轮廓光", Color) = (0.45, 0.85, 1, 1)
        _Ink ("描边", Color) = (0.06, 0.03, 0.1, 1)
        _OutlineW ("描边粗细（米）", Float) = 0.0011
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half _Rage, _Hp, _Hurt; half4 _Armor, _ArmorD, _Claw, _Neon, _Neon2, _Rim, _Ink; float _OutlineW;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Hand"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 pos : POSITION; float3 n : NORMAL; float4 col : COLOR; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; float2 uv2 : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs : SV_POSITION; float3 n : TEXCOORD0; float3 w : TEXCOORD1; float4 col : TEXCOORD2; float2 uv0 : TEXCOORD3; float2 uv1 : TEXCOORD4; float2 uv2 : TEXCOORD5; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 w = TransformObjectToWorld(v.pos.xyz);
                o.cs = TransformWorldToHClip(w); o.w = w; o.n = TransformObjectToWorldNormal(v.n);
                o.col = v.col; o.uv0 = v.uv0; o.uv1 = v.uv1; o.uv2 = v.uv2;
                return o;
            }
            // 抗锯齿的阶跃：边缘宽度按屏幕导数
            float aa(float edge, float x) { float w = max(fwidth(x), 1e-5) * 1.2; return smoothstep(edge - w, edge + w, x); }
            // 叠瓦鳞片：错行的半圆弧，弧线压暗
            float scales(float2 p)
            {
                float row = floor(p.y); float2 q = float2(p.x + 0.5 * fmod(abs(row), 2), p.y);
                float2 c = frac(q) - float2(0.5, 0.0);
                float d = length(c * float2(1.0, 1.25));
                float w = max(fwidth(d), 1e-4) * 1.2;
                return smoothstep(0.42 - w, 0.42 + w, d) * (1 - smoothstep(0.5 - w, 0.5 + w, d));
            }
            half4 frag(V i) : SV_Target
            {
                float3 N = normalize(i.n), Vd = normalize(GetWorldSpaceViewDir(i.w));
                Light L = GetMainLight();
                float ndl = dot(N, L.direction);
                half band = ndl > 0.35 ? 1.0 : (ndl > -0.12 ? 0.78 : 0.58);   // 三阶色调
                float region = i.uv1.y;
                half3 base = i.col.rgb; half armor = 0, neon = 0, claw = 0, lost = 0; float order = 0; half bevel = 1;
                if (region < 0.05)
                {
                    float up = i.col.a * 2 - 1;
                    if (false)
                    {
                        // 前臂三段骨甲（= 三格体力），段间缝是霓虹
                        float yy = i.uv1.x;
                        float k = floor((yy - 0.035) / 0.056); float loc = yy - (0.035 + k * 0.056);
                        half side = aa(0.2, up);   // 只盖前臂正面（原来包了大半圈，像缠绷带）
                        half valid = step(0, k) * step(k, 2);
                        half inP = aa(0.0, loc) * (1 - aa(0.049, loc)) * side * valid;
                        half inG = aa(0.049, loc) * (1 - aa(0.056, loc)) * side * step(0, k) * step(k, 1);
                        armor = inP; neon = inG; order = 0.05 + k * 0.1;
                        bevel = smoothstep(0.0, 0.006, min(loc, 0.049 - loc));
                        int lostN = 3 - (int)ceil(saturate(_Hp) * 3 - 0.001);
                        lost = inP * step(k, lostN - 1);
                    }
                    else
                    {
                        // 手背甲壳：四条顺着掌骨的长甲片，中间凹沟嵌霓虹
                        float gap = i.uv0.x, t = i.uv0.y, lat = i.uv1.x - 1;
                        half lng = aa(0.2, t) * (1 - aa(0.9, t));
                        half wide = aa(-0.24, lat) * (1 - aa(1.24, lat));
                        half back = aa(0.62, i.col.a);
                        half area = lng * wide * back;
                        half plate = aa(0.0024, gap) * area;
                        half groove = (1 - aa(0.0016, gap)) * area;
                        // 11-10：不要手臂了 → 体力改到手背四条甲片上（掉一格烧黑一条，从小指那边开始）；沟的点亮顺序 小指 → 食指
                        armor = plate; neon = groove; order = 0.05 + saturate(lat) * 0.45;
                        float pk = clamp(floor(lat * 3 + 0.5), 0, 3);
                        int lostN = 4 - (int)ceil(saturate(_Hp) * 4 - 0.001);
                        lost = plate * step(pk, lostN - 1);
                        // 亮边：离沟、离甲壳外缘都近的一圈
                        bevel = smoothstep(0.0024, 0.0045, gap) * smoothstep(0.2, 0.26, t) * (1 - smoothstep(0.84, 0.9, t)) * smoothstep(-0.24, -0.16, lat) * (1 - smoothstep(1.16, 1.24, lat));
                    }
                }
                else if (region < 0.2)
                {
                    // 手指：每节一片甲（节与节之间露皮）
                    float s = i.uv0.x, si = floor(s), t = frac(s);
                    float lo = si < 0.5 ? 0.16 : (si < 1.5 ? 0.18 : 0.10), hi = si < 0.5 ? 0.86 : (si < 1.5 ? 0.84 : 0.62);
                    half m = aa(lo, t) * (1 - aa(hi, t)) * aa(0.45, i.uv0.y);
                    armor = m; bevel = smoothstep(0.0, 0.08, min(t - lo, hi - t)) * smoothstep(0.45, 0.7, i.uv0.y);
                }
                else if (region < 0.5) { claw = -1; }   // 骨刺 / 骨鳍：保留自己的骨白色（深色骨甲上的亮点缀）
                else if (region < 0.9) claw = 1;
                else { neon = 1; order = i.uv1.x; }

                // 皮：手背鳞片（掌心不画）
                half skinBack = saturate((base.b - base.r) * 6);
                float2 sp = region < 0.05 ? i.uv2 * 260 : float2(i.uv2.x * 230, i.uv2.y * 230);
                base *= 1 - 0.32 * scales(sp) * skinBack * (1 - armor);
                half3 c = base * band;
                // 冷色轮廓光（动漫边缘光）
                float fres = 1 - saturate(dot(N, Vd));
                c += _Rim.rgb * smoothstep(0.62, 0.72, fres) * 0.28 * (1 - saturate(claw)) * (1 - neon);
                // 骨甲：象牙上釉，边缘一圈暗边（倒角感），一道硬边高光带
                half3 ar = lerp(_ArmorD.rgb, _Armor.rgb, smoothstep(0.0, 0.35, bevel)) * band;
                float3 H = normalize(L.direction + Vd);
                // 骨甲不加镜面高光（甲面平，高光会铺成一片灰白污斑）；靠薰衣草亮边 + 描边勾形
                // 掉了的体力骨甲：烧黑 + 橙色裂纹
                // 龟裂纹：Voronoi 细胞边（F2 − F1 小 = 在两块碎片之间）
                float2 cp = i.uv2 * 110; float2 ci = floor(cp); float f1 = 9, f2 = 9;
                for (int yy2 = -1; yy2 <= 1; yy2++) for (int xx2 = -1; xx2 <= 1; xx2++)
                {
                    float2 g = ci + float2(xx2, yy2);
                    float2 h2 = frac(sin(float2(dot(g, float2(127.1, 311.7)), dot(g, float2(269.5, 183.3)))) * 43758.5453);
                    float dd = length(g + h2 - cp);
                    if (dd < f1) { f2 = f1; f1 = dd; } else if (dd < f2) f2 = dd;
                }
                float edge = f2 - f1, ew = max(fwidth(edge), 1e-4) * 1.2;
                half3 burnt = half3(0.13, 0.07, 0.09) * band + half3(1.0, 0.42, 0.1) * (1 - smoothstep(0.035 - ew, 0.035 + ew, edge)) * 1.4;
                ar = lerp(ar, burnt, lost);
                c = lerp(c, ar, armor);
                // 爪：墨黑 + 锐利反光
                half3 cl = _Claw.rgb * band + smoothstep(0.95, 0.965, dot(N, H)) * 0.8 + _Rim.rgb * smoothstep(0.6, 0.7, fres) * 0.3;
                c = lerp(c, cl, saturate(claw));
                if (claw < 0) c += half3(1, 0.95, 0.85) * smoothstep(0.96, 0.97, dot(N, H)) * 0.3;
                // 霓虹：按怒气点亮（order ≤ 怒气的亮），怒气满时青粉慢慢交替脉动（不频闪）
                half lit = step(order, _Rage + 0.001);
                half full = step(0.999, _Rage);
                half pulse = 0.85 + 0.15 * sin(_Time.y * 3.0);
                half3 nc = lerp(_Neon.rgb, _Neon2.rgb, full * (0.5 + 0.5 * sin(_Time.y * 1.6 + order * 6)));
                half3 glow = nc * lerp(0.35, 2.4 * pulse, lit);
                c = lerp(c, glow, neon);
                // 受伤闪红
                c = lerp(c, half3(1, 0.15, 0.2), _Hurt * 0.55);
                return half4(c, 1);
            }
            ENDHLSL
        }

        // 粗黑描边（反面外扩）
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv1 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs : SV_POSITION; half neon : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float w = _OutlineW * (v.uv1.y > 0.9 ? 0.6 : (v.uv1.y > 0.6 ? 0.8 : 1.0));   // 爪、霓虹环描边细一点
                float3 p = TransformObjectToWorld(v.pos.xyz) + normalize(TransformObjectToWorldNormal(v.n)) * w;
                o.cs = TransformWorldToHClip(p); o.neon = v.uv1.y > 0.9;
                return o;
            }
            half4 frag(V i) : SV_Target { return half4(_Ink.rgb, 1); }
            ENDHLSL
        }
    }
}
