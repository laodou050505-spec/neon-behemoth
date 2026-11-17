// 渐变天空：顶 → 地平线 → 地面三段颜色 + 太阳方向一团暖光。
// 只用来给玻璃 / 金属提供反射和环境光（头显 MR 里看不到天空本身，相机背景是透视画面）。
Shader "Monster/GradientSky"
{
    Properties
    {
        _Top ("天顶", Color) = (0.32, 0.22, 0.55, 1)
        _Horizon ("地平线", Color) = (1.0, 0.62, 0.62, 1)
        _Ground ("地面", Color) = (0.25, 0.18, 0.22, 1)
        _SunColor ("太阳光晕", Color) = (1.0, 0.75, 0.5, 1)
        _SunDir ("太阳方向", Vector) = (-0.5, 0.25, 0.8, 0)
        _SunSize ("光晕大小", Range(1, 64)) = 10
        _Exposure ("亮度", Range(0, 4)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            half4 _Top, _Horizon, _Ground, _SunColor; float4 _SunDir; float _SunSize, _Exposure;
            struct V { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            V vert(float4 v : POSITION) { V o; o.pos = UnityObjectToClipPos(v); o.dir = v.xyz; return o; }
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float y = d.y;
                half3 c = y > 0 ? lerp(_Horizon.rgb, _Top.rgb, pow(saturate(y), 0.55)) : lerp(_Horizon.rgb, _Ground.rgb, saturate(-y * 4));
                float s = saturate(dot(d, normalize(_SunDir.xyz)));
                c += _SunColor.rgb * pow(s, _SunSize) * 0.9;
                return half4(c * _Exposure, 1);
            }
            ENDHLSL
        }
    }
}
