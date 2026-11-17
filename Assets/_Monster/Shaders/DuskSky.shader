// 黄昏天幕（11-12 用户：现在看不出黄昏；把环境压暗，同时还要看得见真实空间，把黄昏的场景表现好）。
// MR 里没有天空盒：真实房间是透视画面。这里在所有虚拟物体之前先画一层全屏的半透明色：
//   透视画面 × (1 − a) + 颜色 × a —— 真实房间被压暗、染色，但轮廓都还看得见（a 最大约 0.55，地面方向更浅，保证看得清脚下）。
// 颜色按“视线的世界方向”算（不是屏幕位置），所以转头时它像真的天空一样留在原地：
//   头顶 = 深靛紫 → 地平线 = 橙粉色的晚霞带 → 脚下 = 暗蓝紫；城市远侧的地平线上一轮落日的光晕。
Shader "Monster/DuskSky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.13,0.09,0.30,0.52) _Horizon ("Horizon", Color) = (1,0.5,0.25,0.38) _Ground ("Ground", Color) = (0.10,0.07,0.20,0.40)
        _Sun ("Sun", Color) = (1,0.62,0.3,1) _SunDir ("SunDir", Vector) = (0,0.12,1,0) _Fade ("Fade", Float) = 1 _Stars ("Stars", Float) = 0
        _Cloud ("Cloud noise", 2D) = "gray" {} _CloudLit ("Cloud lit", Color) = (1,0.5,0.6,0.6) _CloudDark ("Cloud dark", Color) = (0.16,0.13,0.32,0.6)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Background+10" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_Cloud); SAMPLER(sampler_Cloud);
            CBUFFER_START(UnityPerMaterial) half4 _Zenith, _Horizon, _Ground, _Sun, _CloudLit, _CloudDark; float4 _SunDir, _Cloud_ST; float _Fade, _Stars; CBUFFER_END
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = float4(v.positionOS.xy, 0.5, 1);
                float4 wp = mul(UNITY_MATRIX_I_VP, o.positionCS);
                o.dir = wp.xyz / wp.w - GetCameraPositionWS();
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir); float h = d.y;
                // 暖色只留在地平线上下窄窄一段（往上 15° 就过渡到天顶色，往下 10° 就到地面色）：蓝调时刻，不是满天红
                half4 c = h > 0 ? lerp(_Horizon, _Zenith, smoothstep(0.0, 0.13, h)) : lerp(_Horizon, _Ground, smoothstep(0.0, -0.1, h));
                // 晚霞带：地平线上下窄窄一条更亮更暖
                float band = exp(-h * h * 22.0);
                float sd = saturate(dot(d, normalize(_SunDir.xyz)));
                // 太阳落到地平线下（_SunDir.y < 0）时没有圆盘，只剩地平线上的一团余晖
                float glow = (pow(sd, 14.0) * 0.16 + pow(sd, 90.0) * 0.5) * smoothstep(-0.2, 0.03, h) * smoothstep(0.45, 0.05, h) + smoothstep(0.9988, 0.9994, sd) * 0.9 * step(0.0, _SunDir.y);
                float a = saturate(c.a + band * 0.06);
                half3 rgb = c.rgb;
                // 云（11-12 用户：纯渐变色“有点假”，要有质感）：一张可平铺的噪声取两次（一大一小、慢慢飘），卡通两阶 ——
                // 云底被晚霞照亮（靠近落日的更亮），云身是暗紫；只在地平线以上一段出现，越高越稀
                float az = atan2(d.x, d.z) * 0.15915494;
                float2 cuv = float2(az * 4.0 + _Time.y * 0.004, h * 2.6);
                float n = SAMPLE_TEXTURE2D(_Cloud, sampler_Cloud, cuv).r * 0.65 + SAMPLE_TEXTURE2D(_Cloud, sampler_Cloud, cuv * 2.3 + float2(0.37, _Time.y * 0.006)).r * 0.35;
                float cover = smoothstep(0.03, 0.14, h) * smoothstep(0.75, 0.3, h);
                float cl = smoothstep(0.5, 0.56, n) * cover;
                float under = smoothstep(0.56, 0.5, n + h * 0.25) ;                       // 云的下缘
                half3 ccol = lerp(_CloudDark.rgb, _CloudLit.rgb, saturate(under * 0.9 + pow(sd, 3.0) * 0.6) * step(0.5, n));
                rgb = lerp(rgb, ccol, cl * 0.85); a = lerp(a, max(a, _CloudDark.a), cl);
                a *= _Fade;
                // 星星（11-16：天压黑以后要让人看得出“这是夜空”，不是一片黑）：把方向切成小格，每格按随机数决定有没有一颗、多大多亮；不闪烁；
                // 只在地平线以上、没有云的地方；越靠近地平线越淡（被城市的光晕盖住）
                float3 sq = d * 70.0; float3 sc = floor(sq); float3 sf = frac(sq) - 0.5;
                float sh = frac(sin(dot(sc, float3(12.9898, 78.233, 37.719))) * 43758.5453), sh2 = frac(sh * 91.7);
                float star = step(0.985, sh) * smoothstep(0.2 + 0.14 * sh2, 0.04, length(sf)) * smoothstep(0.06, 0.3, h) * (1 - cl) * (0.45 + 0.55 * sh2);
                half3 starCol = lerp(half3(0.75, 0.85, 1.0), half3(1.0, 0.93, 0.8), sh2);
                rgb = rgb * a + _Sun.rgb * (glow * _Sun.a + band * 0.05) * _Fade + starCol * star * _Stars * _Fade;
                return half4(rgb, saturate(a + glow * 0.25 * _Sun.a * _Fade));
            }
            ENDHLSL
        }
    }
}
