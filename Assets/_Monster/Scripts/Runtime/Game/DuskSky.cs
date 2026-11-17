// 黄昏天幕（见 Shaders/DuskSky）：城市出场时 2 秒淡入；按关卡的时段换一套颜色（黄昏 / 入夜 / 暴雨）。
// 11-14 起压得很暗（不透明度 0.74～0.88，见 Start）：房间只剩轮廓，城市的灯火是最亮的东西。
using UnityEngine;

namespace Monster.Game
{
    public class DuskSky : MonoBehaviour
    {
        public Camera cam; public string time = "dusk"; public Vector3 sunDir = new(0, 0.12f, 1);
        Material mat; Transform quad; float fade, stars0;
        /// <summary>结局：天亮（0 → 1）。天从这一关的夜色慢慢换成清晨（淡蓝的天、地平线一抹金色、太阳升出地平线），整体更透 —— 真实的房间跟着亮回来</summary>
        public float dawn;
        readonly Color[] c0 = new Color[6]; Vector3 sun0; bool got;
        static readonly string[] Names = { "_Zenith", "_Horizon", "_Ground", "_Sun", "_CloudLit", "_CloudDark" };
        static readonly Color[] Dawn = { new(0.3f, 0.46f, 0.72f, 0.16f), new(1f, 0.74f, 0.46f, 0.26f), new(0.24f, 0.24f, 0.34f, 0.1f), new(1f, 0.86f, 0.6f, 0.9f), new(1f, 0.82f, 0.68f, 0.4f), new(0.5f, 0.48f, 0.66f, 0.24f) };

        void Start()
        {
            if (!cam) cam = Camera.main;
            var sh = Shader.Find("Monster/DuskSky"); if (!sh) { enabled = false; return; }
            mat = new Material(sh) { name = "duskSky" };
            // 11-16 用户真机：天是压暗了，但还不够明显 ——“应该是黑的，但感觉不到”。上一版留了一成多到两成半的房间亮度；人眼对亮度的感觉是非线性的，
            // 两成的亮度看着还有一半亮。这一版把不透明度提到 0.93～0.965（脚下方向 0.89）：房间只剩 4%～7%（脚下约一成，桌沿和地面的轮廓还隐约在），
            // 并且加上星星（下雨的关没有）：天顶是真的黑下来的夜空，而不是一层灰色的滤镜。
            // 11-14 用户：夜空其实显示不出来（透视画面本身是亮的房间，盖一层半透明的天色只是发灰），干脆把环境压暗 —— 现在这样衬不出“万家灯火”。
            // 天幕的不透明度从 0.4～0.6 提到 0.74～0.88：真实房间只剩一成多到两成半的亮度（家具和地面的轮廓还在，脚下方向留得最多），
            // 颜色也压成很深的蓝黑，只在地平线留一线余晖 / 城市的光晕；城市的窗灯和霓虹成了画面里最亮的东西
            switch (time)
            {
                case "night":
                    mat.SetColor("_Zenith", new Color(0.006f, 0.008f, 0.03f, 0.965f)); mat.SetColor("_Horizon", new Color(0.07f, 0.055f, 0.16f, 0.95f));
                    mat.SetColor("_Ground", new Color(0.008f, 0.009f, 0.028f, 0.9f)); mat.SetColor("_Sun", new Color(0.55f, 0.65f, 1f, 0.35f));
                    mat.SetColor("_CloudLit", new Color(0.13f, 0.15f, 0.3f, 0.95f)); mat.SetColor("_CloudDark", new Color(0.015f, 0.018f, 0.05f, 0.97f)); break;
                case "rain":
                    mat.SetColor("_Zenith", new Color(0.018f, 0.02f, 0.038f, 0.96f)); mat.SetColor("_Horizon", new Color(0.085f, 0.09f, 0.14f, 0.945f));
                    mat.SetColor("_Ground", new Color(0.014f, 0.016f, 0.03f, 0.895f)); mat.SetColor("_Sun", new Color(0.6f, 0.65f, 0.8f, 0f));   // 下雨：没有太阳 / 月亮
                    mat.SetColor("_CloudLit", new Color(0.13f, 0.14f, 0.2f, 0.95f)); mat.SetColor("_CloudDark", new Color(0.028f, 0.03f, 0.05f, 0.97f)); break;
                default:
                    // 黄昏将入夜：深蓝紫的天，地平线只剩窄窄一线粉橙色的晚霞
                    mat.SetColor("_Zenith", new Color(0.012f, 0.013f, 0.06f, 0.955f)); mat.SetColor("_Horizon", new Color(0.42f, 0.14f, 0.26f, 0.93f));
                    mat.SetColor("_Ground", new Color(0.012f, 0.012f, 0.045f, 0.885f)); mat.SetColor("_Sun", new Color(1f, 0.5f, 0.4f, 0.5f));
                    mat.SetColor("_CloudLit", new Color(0.6f, 0.25f, 0.35f, 0.94f)); mat.SetColor("_CloudDark", new Color(0.035f, 0.03f, 0.1f, 0.965f)); break;
            }
            // 城市本身的受光也压下来（太阳光 ×0.4；环境光 ×0.48、偏冷）：楼体变成深蓝灰，暖黄的窗才跳得出来（现场对比过两组，见 editor_checks/lights.cs）。场景重新加载时这些值会还原
            {
                var sun = RenderSettings.sun; if (!sun) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) sun = l;
                if (sun) sun.intensity *= 0.4f;
                var cool = new Color(0.8f, 0.9f, 1.15f) * 0.48f;
                RenderSettings.ambientSkyColor *= cool; RenderSettings.ambientEquatorColor *= cool; RenderSettings.ambientGroundColor *= 0.48f;
            }
            var noise = Resources.Load<Texture2D>("UI/cloud_noise"); if (noise) { noise.wrapMode = TextureWrapMode.Repeat; mat.SetTexture("_Cloud", noise); }
            stars0 = time == "rain" ? 0 : time == "night" ? 1f : 0.7f; mat.SetFloat("_Stars", stars0);
            mat.SetVector("_SunDir", sunDir.normalized); mat.SetFloat("_Fade", 0);
            var m = new Mesh { name = "fullscreen_sky" };
            m.SetVertices(new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 1000);
            var go = new GameObject("DuskSky"); quad = go.transform; quad.SetParent(cam.transform, false); quad.localPosition = new Vector3(0, 0, 1);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }
        void OnDestroy() { if (quad) Destroy(quad.gameObject); }
        void LateUpdate()
        {
            if (!mat) return;
            if (dawn > 0)
            {
                if (!got) { got = true; for (int i = 0; i < 6; i++) c0[i] = mat.GetColor(Names[i]); sun0 = mat.GetVector("_SunDir"); }
                float k = Mathf.Clamp01(dawn);
                for (int i = 0; i < 6; i++) mat.SetColor(Names[i], Color.Lerp(c0[i], Dawn[i], k));
                var sd = sun0; sd.y = Mathf.Lerp(sun0.y, 0.11f, k); mat.SetVector("_SunDir", sd.normalized);
                mat.SetFloat("_Stars", stars0 * Mathf.Clamp01(1 - k * 2.2f));   // 天一亮星星先隐去
            }
            fade = Mathf.MoveTowards(fade, MonsterMR.CityShown ? 1 : 0, Time.unscaledDeltaTime / 2f);
            mat.SetFloat("_Fade", fade * fade * (3 - 2 * fade));
            if (quad.gameObject.activeSelf != fade > 0.001f) quad.gameObject.SetActive(fade > 0.001f);
        }
    }
}
