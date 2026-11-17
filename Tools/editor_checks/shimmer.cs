// 闪烁量测试：按头显单眼的分辨率（1832×1920、视场 96°，4 倍抗锯齿）从玩家视角渲染 6 帧，每帧相机多转 0.012°（约四分之一个像素，模拟头的微动），
// 统计相邻两帧之间亮度变化的平均值（静止的画面这个数应该很小；越大 = 越闪）。参数：变体名 [off = 关掉全部描边]
var a = System.IO.File.ReadAllText("/tmp/monster_game/arg.txt").Trim().Split(' ');
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); v.paused = true;
var ov = GameObject.Find("HurtOverlay"); if (ov) ov.SetActive(false);
bool off = a.Length > 1 && a[1] == "off";
var toggled = new System.Collections.Generic.List<Material>();
if (off) foreach (var r in v.builder.root.GetComponentsInChildren<MeshRenderer>(true)) foreach (var m in r.sharedMaterials) if (m && m.shader && m.shader.name == "Monster/CityToon" && m.GetShaderPassEnabled("SRPDefaultUnlit")) { m.SetShaderPassEnabled("SRPDefaultUnlit", false); toggled.Add(m); }
int W = 1832, H = 1920; var go = new GameObject("__ShimmerCam") { hideFlags = HideFlags.HideAndDontSave }; var cam = go.AddComponent<Camera>(); cam.CopyFrom(Camera.main);
cam.fieldOfView = 96; cam.nearClipPlane = 0.06f; cam.farClipPlane = 12; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.05f, 0.05f, 0.06f, 1);
var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); data.renderPostProcessing = false; data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;
var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; cam.targetTexture = rt;
var tex = new Texture2D(W, H, TextureFormat.RGB24, false); Color32[] prev = null; double sum = 0; long big = 0; int pairs = 0; long npx = 0;
var sb = new System.Text.StringBuilder();
foreach (var pose in new[] { (new Vector3(0, 1.6f, 0), new Vector3(35, 0, 0)), (new Vector3(0, 1.5f, 0), new Vector3(28, -28, 0)) })
{
    prev = null;
    for (int f = 0; f < 6; f++)
    {
        go.transform.SetPositionAndRotation(pose.Item1, Quaternion.Euler(pose.Item2.x + f * 0.005f, pose.Item2.y + f * 0.012f, 0));
        cam.Render();
        var old = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); RenderTexture.active = old;
        var px = tex.GetPixels32();
        if (prev != null)
        {
            // 只看画面中间 60%（头显里看得最清楚的区域）
            for (int y = H / 5; y < H * 4 / 5; y += 1) for (int x = W / 5; x < W * 4 / 5; x += 1)
            { int i = y * W + x; int d = System.Math.Abs((px[i].r * 2 + px[i].g * 5 + px[i].b) / 8 - (prev[i].r * 2 + prev[i].g * 5 + prev[i].b) / 8); sum += d; if (d > 40) big++; npx++; }
            pairs++;
        }
        prev = px;
    }
}
if (a.Length > 2) System.IO.File.WriteAllBytes("/tmp/monster_game/shots/" + a[2] + ".png", tex.EncodeToPNG());
foreach (var m in toggled) m.SetShaderPassEnabled("SRPDefaultUnlit", true);
cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
result.Log(a[0] + (off ? "（描边关）" : "") + "：平均变化 " + (sum / npx).ToString("F3") + " / 255，变化超过 40 的像素 " + (big * 1000.0 / npx).ToString("F2") + "‰（" + pairs + " 对帧）");
