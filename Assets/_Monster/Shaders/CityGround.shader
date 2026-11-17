// 城区地面（MR 用）：只画马路 / 楼底人行道 / 港口水面，其余全透明露出真实桌面；
// 同时接住楼的影子（shadow catcher）——透明处只叠一层半透明的影子颜色，楼就像真的立在桌上。
//  - _BaseMap：rgb 路面颜色，a = 有没有路（边缘渐隐已经烘进 a）
//  - 有路的地方：路面 × 卡通光照（暗部偏紫）；没路的地方：只在影子里输出 _ShadowColor × _ShadowAlpha
Shader "Monster/CityGround"
{
    Properties
    {
        _BaseMap ("路网（a=有路）", 2D) = "black" {}
        _ShadowTint ("路面暗部色", Color) = (0.42,0.36,0.62,1)
        _ShadowColor ("投影颜色", Color) = (0.1,0.06,0.16,1)
        _ShadowAlpha ("投影浓度", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            // 11-13 用户：城市地面不要闪。地面是一张贴在草地 / 桌垫上方 2～3mm 的半透明片，头显深度精度有限时会和下面那层抢着显示：
            // 给它一个最小的深度偏移（往眼睛方向挪一个深度单位），和下面那层打平时永远是它赢；偏移量小于它和上面物件的间距，不会盖住路面上的东西
            Offset -1, -1
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial) float4 _BaseMap_ST; half4 _ShadowTint, _ShadowColor; half _ShadowAlpha; CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; half fog : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz); o.positionCS = p.positionCS; o.positionWS = p.positionWS; o.uv = TRANSFORM_TEX(v.uv, _BaseMap); o.fog = ComputeFogFactor(p.positionCS.z); return o; }
            half4 frag(V i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                Light m = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half lit01 = m.shadowAttenuation;
                half3 road = t.rgb * lerp(_ShadowTint.rgb, m.color, lerp(0.45, 1, lit01));
                half sa = (1 - lit01) * _ShadowAlpha;
                half a = t.a + (1 - t.a) * sa;
                half3 c = (road * t.a + _ShadowColor.rgb * sa * (1 - t.a)) / max(a, 1e-3);
                return half4(MixFog(c, i.fog), a);
            }
            ENDHLSL
        }
    }
}
