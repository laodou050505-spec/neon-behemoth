// 《我是怪兽》城市卡通着色器（URP）
//  - 三阶色带：亮部 / 中间 / 暗部，暗部偏紫（日系动画的黄昏感），和立面贴图相乘
//  - 窗灯自发光：_EmissionMap（亮着的窗）× _EmissionColor × _NightGlow（白天 0、夜里 1）
//  - 烧焦：_Burn 0~1，颜色压暗发黑、窗灯熄灭
//  - 外描边：第二个 Pass，沿平滑法线（UV3，导入时烘好；没有就用顶点法线）外扩，按视距补偿，远近粗细一致
Shader "Monster/CityToon"
{
    Properties
    {
        _BaseMap ("贴图", 2D) = "white" {}
        _BaseColor ("颜色", Color) = (1,1,1,1)
        _ShadowTint ("暗部色", Color) = (0.56,0.5,0.74,1)
        _MidTint ("中间调", Color) = (0.82,0.78,0.92,1)
        _Steps ("色带阈值(暗/中)", Vector) = (0.2, 0.55, 0, 0)
        _Soft ("色带过渡", Range(0.001, 0.2)) = 0.03
        _RimColor ("边缘光", Color) = (1,0.85,0.9,1)
        _RimStrength ("边缘光强度", Range(0, 1)) = 0.18
        _EmissionMap ("窗灯遮罩", 2D) = "black" {}
        [HDR] _EmissionColor ("窗灯颜色", Color) = (1.6,1.25,0.7,1)
        _NightGlow ("夜晚亮灯(0~1)", Range(0,1)) = 0
        _RandomLit ("窗灯随机（楼模块用）", Range(0,1)) = 0
        _LitFrac ("亮灯比例", Range(0,1)) = 0.4
        _Glow ("整体自发光（霓虹用）", Range(0, 4)) = 0
        _Burn ("烧焦", Range(0,1)) = 0
        _OutlineColor ("描边颜色", Color) = (0.13,0.08,0.18,1)
        _OutlineWidth ("描边宽度", Range(0, 0.01)) = 0.0038
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _ShadowTint, _MidTint, _RimColor, _EmissionColor, _OutlineColor;
            float4 _Steps;
            float _Soft, _RimStrength, _NightGlow, _Glow, _Burn, _OutlineWidth, _RandomLit, _LitFrac;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 lit : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2; half fog : TEXCOORD3; float2 lit : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO
            };

            V vert(A v)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                // 窗灯随机：每扇窗的随机号（uv2.x，非玻璃面 = 2）+ 这一层物体的世界位置 → 每层、每栋都不同
                float3 op = UNITY_MATRIX_M._m03_m13_m23;
                float seed = v.lit.y > 0 ? v.lit.y : frac(sin(dot(op, float3(127.1, 311.7, 74.7))) * 43758.5453);   // 合批后每层的种子烘在 uv2.y
                o.lit = float2(v.lit.x, seed);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float4 _Blackout;   // 结局“熄灯”：全局量，xyz = 圆心（世界坐标），w = 半径；离圆心比半径近的灯灭掉（w = 0 时全亮）
            half BlackoutK(float3 p) { return smoothstep(_Blackout.w - 0.06, _Blackout.w, distance(p, _Blackout.xyz)); }
            float _Relight;     // 结局“城市自己亮灯”：0 → 1。把空间切成小格（约 3×2×3cm），每格一个随机数：四成半的格子会亮，各自在不同的时刻亮起来
            half RelightK(float3 p)
            {
                float3 c = floor(p * float3(34, 52, 34));
                float h = frac(sin(dot(c, float3(12.9898, 78.233, 37.719))) * 43758.5453), h2 = frac(sin(dot(c, float3(39.346, 11.135, 83.155))) * 24634.6345);
                return step(h2, 0.45) * smoothstep(h * 0.85, h * 0.85 + 0.12, _Relight) * 0.9;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                float3 n = normalize(i.normalWS);
                Light main = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                // 半兰伯特 × 阴影 → 三阶色带
                float ndl = dot(n, main.direction) * 0.5 + 0.5;
                float lit = ndl * lerp(0.35, 1.0, main.shadowAttenuation);
                float mid = smoothstep(_Steps.x - _Soft, _Steps.x + _Soft, lit);
                float hi = smoothstep(_Steps.y - _Soft, _Steps.y + _Soft, lit);
                half3 band = lerp(_ShadowTint.rgb, lerp(_MidTint.rgb, half3(1,1,1), hi), mid);
                half3 light = main.color * band + SampleSH(n) * 0.35;
                half3 col = tex.rgb * light;
                // 边缘光：亮部一侧淡淡的粉色轮廓
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float rim = pow(1 - saturate(dot(n, viewDir)), 4) * hi;
                col += _RimColor.rgb * rim * _RimStrength;
                // 烧焦：压暗偏褐黑
                col = lerp(col, col * half3(0.18, 0.14, 0.13), _Burn * 0.85);
                // 窗灯 / 霓虹
                half3 em = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb * _NightGlow;
                if (_RandomLit > 0.5 && i.lit.x < 1.5)
                {
                    // 两次哈希：亮不亮（约 _LitFrac）、亮度（0.45~1.1，偶尔很暗像只开了台灯）
                    float h1 = frac(sin(i.lit.x * 91.7 + i.lit.y * 47.3) * 43758.5453);
                    float h2 = frac(sin(i.lit.x * 17.3 + i.lit.y * 93.1) * 24634.6345);
                    em *= step(h1, _LitFrac) * lerp(0.45, 1.1, h2 * h2);
                }
                // 结局：熄灯以后（BlackoutK = 0），窗灯按 _Relight 一格一格重新亮起来（只有窗灯：集团的霓虹不再亮）
                half bk = BlackoutK(i.positionWS);
                em *= max(bk, RelightK(i.positionWS));
                em += tex.rgb * _Glow * bk;
                col += em * (1 - _Burn);
                col = MixFog(col, i.fog);
                return half4(col, 1);
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
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float3 smoothN : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; half fog : TEXCOORD0; half soft : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 nOS = dot(v.smoothN, v.smoothN) > 0.01 ? v.smoothN : v.normalOS;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = normalize(TransformObjectToWorldNormal(nOS));
                // 11-11 用户：模型结构小的时候描边把整栋楼都填满了 —— 原来是“屏幕上恒定粗细”（0.0038 × 距离，1m 外往外扩 3.8mm），
                // 比很多零件（栏杆、空调、车）本身还宽。现在：屏幕上细到约 40%，并且真实宽度封顶 1.2mm、至少 0.5mm（按材质的描边宽度比例缩放），
                // 小零件只围一圈细线
                float d = distance(posWS, GetCameraPositionWS());
                float k = _OutlineWidth / 0.0038;
                float w = clamp(0.0015 * d, 0.0005, 0.0012) * k;
                // 11-16 用户：楼的描边有点闪。两个原因：① 1.2mm 的封顶让 1.2 米以外的描边在头显里不到一个像素宽，头稍微一动就时有时无；
                // ② 1.6 米以外换成不带描边的简化模型，换的那一下整片楼的描边一起消失 / 出现。
                // 现在描边从 1.15 米开始越远越细、到 1.5 米收没（抗锯齿下看起来是慢慢变淡），到换模型的距离时已经没有描边，不再有“啪”的一下
                w *= 1 - smoothstep(1.15, 1.5, d);
                // 11-17 用户真机：描边还是在闪。按头显单眼分辨率渲染、相机每帧转四分之一个像素来量（editor_checks/shimmer.cs）：
                // 描边让画面中心“帧间变化大的像素”从 4.6‰ 升到 8.3‰。描边在头显里只有一到两个像素宽，又是近黑色压在亮着的窗和浅色墙上，
                // 头稍微一动，这条细线落在哪几个像素上就变一次。试过加宽（没用：闪的是线的边，不是线的粗细），有效的是三项，合起来降到 4.5‰（和不画描边一样）：
                //   ① 朝下的面不往外扩（阳台、雨棚、挑檐底下那一圈细线最碎）；
                //   ② 正对着视线的面不往外扩（描边只该出现在轮廓上；正对玩家的面外扩出来的边是画在楼身里面的碎线）；
                //   ③ 描边颜色从近黑提到深蓝灰（见片元里的 lerp）：离得很近（30cm 以内，线有好几个像素宽）时还是原来的深色。
                w *= 1 - smoothstep(-0.05, -0.45, nWS.y);
                float3 vdir = normalize(GetCameraPositionWS() - posWS);
                w *= 1 - smoothstep(0.35, 0.8, abs(dot(nWS, vdir)));
                o.soft = smoothstep(0.25, 0.5, d) * 0.5;
                posWS += nWS * w;
                o.positionCS = TransformWorldToHClip(posWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half3 c = lerp(_OutlineColor.rgb, half3(0.05, 0.04, 0.04), _Burn);
                c = lerp(c, half3(0.3, 0.27, 0.36), i.soft);
                return half4(MixFog(c, i.fog), 1);
            }
            ENDHLSL
        }

        // 自己的 ShadowCaster / DepthOnly：和上面两个 Pass 共用同一个 UnityPerMaterial（借 Lit 的 Pass 会让整个着色器用不了 SRP Batcher）
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex sv
            #pragma fragment sf
            #pragma multi_compile_instancing
            float3 _LightDirection;
            struct SA { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 sv(SA v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 p = TransformObjectToWorld(v.positionOS.xyz); float3 n = TransformObjectToWorldNormal(v.normalOS);
                float4 c = TransformWorldToHClip(ApplyShadowBias(p, n, _LightDirection));
                #if UNITY_REVERSED_Z
                c.z = min(c.z, UNITY_NEAR_CLIP_VALUE);
                #else
                c.z = max(c.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return c;
            }
            half4 sf() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex dv
            #pragma fragment df
            #pragma multi_compile_instancing
            struct DA { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct DV { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            DV dv(DA v) { DV o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half df(DV i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
