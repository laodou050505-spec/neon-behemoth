// 港口河面（三渲二）：uv.x 沿河（米），uv.y 0 = 码头岸边 → 1 = 对岸（靠玩家）
//  - 颜色三阶：岸边浅青 → 中间蓝 → 深蓝，色带边缘干净
//  - 顺流漂动的小浪纹（短白线），两层不同速度
//  - 岸边一条起伏的白色浪花带
//  - 随机闪光点
//  - 夜里：水变暗，水面上有霓虹 / 窗灯的竖向倒影条（暖黄、粉、青）随波抖动
//  - 两端和对岸 6~10cm 内渐隐到透明（真实地板上不要硬边）；接影子
Shader "Monster/CityWater"
{
    Properties
    {
        _Shallow ("浅水", Color) = (0.35, 0.82, 0.86, 1)
        _Mid ("中水", Color) = (0.18, 0.6, 0.78, 1)
        _Deep ("深水", Color) = (0.1, 0.36, 0.6, 1)
        _Foam ("浪花", Color) = (0.93, 0.98, 1, 1)
        _Night ("夜晚", Range(0, 1)) = 0
        _Len ("河长（米）", Float) = 2
        _Wid ("河宽（米）", Float) = 0.4
        _Flood ("整片水（结局涨潮：四边都渐隐、不分深浅带、没有岸边浪花）", Range(0, 1)) = 0
        _Opacity ("整体不透明度", Range(0, 1)) = 1
        _Stripes ("夜里的霓虹倒影条", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-15" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Shallow, _Mid, _Deep, _Foam; float _Night, _Len, _Wid, _Flood, _Opacity, _Stripes;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 posWS : TEXCOORD1; half fog : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz); o.positionCS = p.positionCS; o.posWS = p.positionWS; o.uv = v.uv; o.fog = ComputeFogFactor(p.positionCS.z); return o; }
            float h1(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float vnoise(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f); return lerp(lerp(h1(i), h1(i + float2(1, 0)), f.x), lerp(h1(i + float2(0, 1)), h1(i + float2(1, 1)), f.x), f.y); }
            // 短浪纹：在一个拉长的格子里画一条短横线，格子随机错开
            float wavelets(float2 p, float t, float speed)
            {
                float2 q = p + float2(t * speed, 0);
                float2 cell = floor(q * float2(1, 3)); float2 f = frac(q * float2(1, 3));
                float r = h1(cell);
                float x0 = r * 0.6, len = 0.15 + h1(cell + 7) * 0.25;
                float on = step(0.8, h1(cell + 3));
                float line_ = step(x0, f.x) * step(f.x, x0 + len) * smoothstep(0.2, 0.05, abs(f.y - 0.5 - 0.15 * sin((f.x + t) * 6)));
                return line_ * on;
            }
            float4 _Blackout;   // 结局“熄灯”：全局量，xyz = 圆心（世界坐标），w = 半径；离圆心比半径近的灯灭掉（w = 0 时全亮）
            half BlackoutK(float3 p) { return smoothstep(_Blackout.w - 0.06, _Blackout.w, distance(p, _Blackout.xyz)); }
            half4 frag(V i) : SV_Target
            {
                float t = _Time.y;
                float x = i.uv.x * _Len, y = i.uv.y;                    // x 米，y 0~1
                float d = y + (vnoise(float2(x * 8, t * 0.2)) - 0.5) * 0.12;
                d = lerp(d, 0.44 + (vnoise(float2(x * 5 + t * 0.07, y * _Wid * 5 - t * 0.05)) - 0.5) * 0.5, _Flood);   // 整片水：大部分是中间色，深浅两种只零星出现
                half3 c = d < 0.18 ? _Shallow.rgb : (d < 0.55 ? _Mid.rgb : _Deep.rgb);
                // 浪纹（城市比例：一格 6cm）
                float w = wavelets(float2(x * 16, y * _Wid * 16 * 3), t, 0.35) + wavelets(float2(x * 23 + 3.7, y * _Wid * 23 * 3), t, -0.2) * 0.7;
                c = lerp(c, _Foam.rgb * (1 - _Night * 0.7), saturate(w) * 0.55);
                // 岸边浪花带
                float edge = 0.05 + 0.025 * sin(x * 70 + t * 2.2) + 0.015 * sin(x * 31 - t * 1.3);
                float foam = smoothstep(edge, edge - 0.012, y);
                c = lerp(c, _Foam.rgb * (1 - _Night * 0.6), foam * (1 - _Flood));
                // 闪光点
                float2 g = floor(float2(x * 160, y * _Wid * 160)); float tw = h1(g + floor(t * 2.5));
                c += step(0.997, tw) * 0.5 * (1 - _Night);
                // 影子
                Light m = GetMainLight(TransformWorldToShadowCoord(i.posWS));
                c *= lerp(0.6, 1, m.shadowAttenuation);
                // 夜晚：压暗 + 竖向倒影条
                {
                    // 夜晚的样子按 _Night 平滑混进去（结局天亮时从 1 慢慢降到 0，不跳变）
                    half3 nt = c * 0.22 + half3(0.0, 0.01, 0.05);   // 夜里水面压暗偏蓝
                    float col = floor(x * 22);
                    float r = h1(float2(col, 1));
                    float wob = (vnoise(float2(x * 40, y * 30 - t * 2)) - 0.5) * 0.4;
                    float stripe = step(0.78, r) * smoothstep(0.22, 0.0, abs(frac(x * 22 + wob) - 0.5)) * smoothstep(0.75, 0.05, y);
                    half3 nc = r < 0.7 ? half3(1, 0.75, 0.4) : (r < 0.85 ? half3(1, 0.35, 0.75) : half3(0.3, 0.95, 1));
                    nt += nc * stripe * 0.45 * (0.6 + 0.4 * step(0.5, frac(y * _Wid * 90 + wob * 3))) * _Stripes * BlackoutK(i.posWS);
                    c = lerp(c, nt, saturate(_Night));
                }
                // 渐隐：两端 10cm、对岸 8cm
                float a = smoothstep(0, 0.1, x) * smoothstep(0, 0.1, _Len - x) * smoothstep(0, 0.08 / _Wid, 1 - y);
                a *= lerp(1, smoothstep(0, 0.08 / _Wid, y), _Flood) * _Opacity;
                return half4(MixFog(c, i.fog), a);
            }
            ENDHLSL
        }
    }
}
