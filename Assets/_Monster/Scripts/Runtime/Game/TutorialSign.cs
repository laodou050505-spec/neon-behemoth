// 玩中学的提示牌（场景里的实物，不是浮空面板）：一块立在城市近边路沿外的小霓虹路牌，低矮、不挡城市，始终转向玩家的头、略往后仰。
// 左上：这一步的名字（霓虹粉）；下面一行：这一步做什么（白字）；右边：进度（3 / 3）。完成一步：牌子轻弹一下、亮绿色“完成！”。
// 要操作的目标上方有一个上下跳动的黄色箭头（教学的 Focus：楼 / 警车 / 电站 / 塔）。
using TMPro;
using UnityEngine;

namespace Monster.Game
{
    public class TutorialSign : MonoBehaviour
    {
        public MonsterGame game; public GameView view; public Camera cam;
        Transform sign, arrow; TextMeshPro title, body, prog; Material board, edge, arrowMat;
        TMP_FontAsset fontZh, fontEn; Vector3 anchor; int lastStep = -2; float pop, passGlow;
        static readonly Color Ink = new(0.08f, 0.06f, 0.12f), Neon = new(1f, 0.32f, 0.62f), Done = new(0.36f, 1f, 0.55f);
        const float W = 0.60f, H = 0.19f;   // 11-11 用户：面板别小家子气
        // 11-11 用户：注意面板的排版边距。内圈灯管离板边 1.6cm，内容区再往里留一圈：左右 3.6cm、上下 3cm
        // （原来字离灯管不到 5mm，长句子的最大宽度没扣边距会压到右边灯管上，贴纸下沿贴着灯管）
        const float PadX = 0.036f, PadTop = 0.03f, PadBot = 0.03f, ChipH = 0.026f, ChipGap = 0.012f;
        static float Inner => W - 2 * PadX;
        readonly System.Collections.Generic.List<TextMeshPro> chips = new(); readonly System.Collections.Generic.List<Material> stickers = new();
        readonly System.Collections.Generic.List<Material> beacons = new(); TextMeshPro wantedLabel; int lastStars = -1; float flash;
        HpBar hpBar; float lift, baseScale = 1; Transform panel; readonly System.Collections.Generic.List<Transform> poles = new();

        void Start()
        {
            if (!cam) cam = Camera.main;
            fontZh = Resources.Load<TMP_FontAsset>("Fonts/ComicZH SDF"); fontEn = Resources.Load<TMP_FontAsset>("Fonts/ComicEN SDF");
            var z = game.city.zones.Find(q => q.kind == ZoneKind.Table) ?? game.city.zones[0];
            // 桌子近边上离玩家最近的那一点、再往玩家这边 5cm（底座外），牌子不压城市
            var head = cam.transform.position;
            float a = Mathf.Clamp(Vector3.Dot(head - z.origin, z.u), W * 0.6f, z.w - W * 0.6f);
            // 站着玩（11-08 用户：注意 UI 位置）：牌子不再立在桌子近边（要低头 60° 才看得到），
            // 而是像球场记分牌一样架在桌子远边外面、高过这张桌上最高的楼 —— 低头看城市时它就在视野上方，不挡城市
            float back = CityEdge.Back(view.builder, z);
            float tall = 0;
            foreach (var b in game.city.buildings) if (b.zone == z) tall = Mathf.Max(tall, MonsterGame.TopOf(b).y - z.origin.y);
            lift = Mathf.Clamp(tall + 0.025f, 0.12f, 0.34f);
            anchor = z.Point(a, back - 0.035f, lift - 0.03f);   // 牌面从本地 y=0.03 开始
            var shader = Shader.Find("Monster/ComicCard");
            sign = new GameObject("TutorialSign").transform;
            // 牌面单独一层：按距离放大（大房间桌子深、牌子远，看起来要和近处一样大），弹跳只动牌面，立柱不跟着伸缩（不戳进桌面）
            panel = new GameObject("Panel").transform; panel.SetParent(sign, false);
            var flat = anchor - head; flat.y = 0;
            baseScale = Mathf.Clamp(flat.magnitude / 0.8f, 1f, 1.4f);
            // 两根细立柱 + 牌面（墨边 + 深色底）
            // 11-11：霓虹涂鸦面板（墨边 + 深底 + 粉青两圈灯管 + 喷漆 + 贴纸），替换原来的铁皮外壳
            var gp = NeonKit.GraffitiPanel(panel, W, H, 0.03f, 3151, 7, 1f, true);
            board = gp.Find("Board").GetComponent<MeshRenderer>().sharedMaterial; edge = gp.Find("Ink").GetComponent<MeshRenderer>().sharedMaterial;
            // 三行：标题（顶）/ 提示（正中）/ 招式贴纸（底），提示行放在标题下沿和贴纸上沿的正中间
            float top = 0.03f + H - PadTop, chipY = 0.03f + PadBot + ChipH / 2, bodyY = (top - 0.036f + chipY + ChipH / 2) / 2;
            title = Text(panel, new Vector3(-W / 2 + PadX, top, -0.004f), TextAlignmentOptions.TopLeft, 3152);
            body = Text(panel, new Vector3(-W / 2 + PadX, bodyY, -0.004f), TextAlignmentOptions.Left, 3152);
            float cell = Inner / game.tut.moves.Count;
            // 11-11 用户：看不到血量 —— 标题行中间放体力条（标题在左、招式进度在右）
            hpBar = new HpBar(panel, new Vector3(-0.115f, top - 0.019f, -0.004f), 0.23f, 0.012f, 3152);
            // 招式清单：一排 6 个标签（没学会 = 灰，学会 = 亮绿）
            for (int k = 0; k < game.tut.moves.Count; k++)
            {
                // 每张贴纸占一格，字在贴纸正中（原来靠左，左右留白不一样）
                var ch = Text(panel, new Vector3(-W / 2 + PadX + (k + 0.5f) * cell, chipY, -0.004f), TextAlignmentOptions.Center, 3152);
                ch.rectTransform.sizeDelta = new Vector2((cell - ChipGap) * 100, 3);
                chips.Add(ch);
                // 贴纸底：每张略歪
                var stM = NeonKit.Mat(new Color(0.22f, 0.2f, 0.3f), 3152 - 1);
                var st = NeonKit.Shape(panel, "Sticker" + k, NeonKit.RoundRect(cell - ChipGap, ChipH, 0.007f), ch.transform.localPosition + new Vector3(0, 0, 0.0012f), stM);
                st.localRotation = Quaternion.Euler(0, 0, (k % 2 == 0 ? 1 : -1) * (2 + k % 3));
                stickers.Add(stM);
            }
            prog = Text(panel, new Vector3(W / 2 - PadX, top - 0.005f, -0.004f), TextAlignmentOptions.TopRight, 3152);
            // 两根细钢管从桌面（远边外）立上来托住牌子
            var poleM = new Material(shader) { color = new Color(0.22f, 0.22f, 0.27f), renderQueue = 3149 };
            foreach (var sx in new[] { -1, 1 })
            {
                var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(pole.GetComponent<Collider>());
                pole.name = "Pole"; pole.transform.SetParent(sign, false);
                float pb = 0.03f - lift, pt = 0.03f * baseScale + 0.006f;   // 托到牌面底边（牌面绕底边往后仰，不会压到立柱）   // 从桌面立到牌子中间（Cylinder 图元高 2、中心在中间）
                pole.transform.localPosition = new Vector3(sx * W * 0.38f * baseScale, (pb + pt) * 0.5f, 0.006f);
                pole.transform.localScale = new Vector3(0.006f, (pt - pb) * 0.5f, 0.006f);
                var pr = pole.GetComponent<MeshRenderer>(); pr.sharedMaterial = poleM; pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                poles.Add(pole.transform);
            }
            // 实体外壳（铁皮背盒 + 钢框 + 铆钉）；顶框右侧装一排 5 盏通缉警灯（教学关通缉牌并到这里，不再单独立一块）
            // （铁皮外壳去掉，换成上面的霓虹涂鸦面板）
            var lab = Text(panel, new Vector3(W / 2 - 0.148f, 0.03f + H + 0.013f, -0.006f), TextAlignmentOptions.Right, 3154);
            lab.text = "通缉"; lab.fontSize = 9; lab.color = new Color(1, 1, 1, 0.8f); wantedLabel = lab;
            for (int i = 0; i < 5; i++)
            {
                var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(lamp.GetComponent<Collider>());
                lamp.name = "Beacon" + i; lamp.transform.SetParent(panel, false);
                lamp.transform.localPosition = new Vector3(W / 2 - 0.125f + i * 0.024f, 0.03f + H + 0.013f, -0.006f); lamp.transform.localScale = Vector3.one * 0.011f;   // 立在顶边外，不压灯管
                var m = new Material(shader) { color = new Color(0.25f, 0.22f, 0.3f), renderQueue = 3155 }; beacons.Add(m);
                var lr = lamp.GetComponent<MeshRenderer>(); lr.sharedMaterial = m; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // 牌面的转轴放在底边：子物体整体下移 3cm，牌面层放到底边高度
            foreach (Transform c in panel) c.localPosition -= new Vector3(0, 0.03f, 0);
            panel.localPosition = new Vector3(0, 0.03f * baseScale, 0);
            // 目标箭头：朝下的四棱锥
            arrow = new GameObject("TutorialArrow").transform;
            arrow.gameObject.AddComponent<MeshFilter>().sharedMesh = FxBatch.Cone(4);
            arrowMat = new Material(shader) { color = new Color(1f, 0.86f, 0.3f), renderQueue = 3153 };
            var mr = arrow.gameObject.AddComponent<MeshRenderer>(); mr.sharedMaterial = arrowMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Loc.Changed += Refresh;
            sign.gameObject.SetActive(false); arrow.gameObject.SetActive(false);
        }
        void OnDestroy() { Loc.Changed -= Refresh; if (sign) Destroy(sign.gameObject); if (arrow) Destroy(arrow.gameObject); }

        static void Quad(Transform p, string n, Vector3 pos, Vector2 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(go.GetComponent<Collider>());
            go.name = n; go.transform.SetParent(p, false); go.transform.localPosition = pos; go.transform.localScale = new Vector3(size.x, size.y, 1);
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }

        TextMeshPro Text(Transform p, Vector3 pos, TextAlignmentOptions align, int q)
        {
            var go = new GameObject("Text"); go.transform.SetParent(p, false);
            var t = go.AddComponent<TextMeshPro>();
            t.transform.localPosition = pos; t.transform.localScale = Vector3.one * 0.01f;
            t.alignment = align; t.enableWordWrapping = false;
            t.rectTransform.pivot = align == TextAlignmentOptions.TopRight ? new Vector2(1, 1) : align == TextAlignmentOptions.TopLeft ? new Vector2(0, 1)
                : align == TextAlignmentOptions.Center ? new Vector2(0.5f, 0.5f) : align == TextAlignmentOptions.Right ? new Vector2(1, 0.5f) : new Vector2(0, 0.5f);
            t.rectTransform.sizeDelta = new Vector2(Inner * 100, 3);
            t.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            t.fontMaterial.renderQueue = q;
            return t;
        }

        void Refresh()
        {
            var tut = game.tut; if (tut == null || tut.Step == null) return;
            var f = Loc.En ? fontEn : fontZh;
            foreach (var t in new[] { title, body, prog }) { t.font = f; t.fontMaterial.renderQueue = 3152; }
            foreach (var t in chips) { t.font = f; t.fontMaterial.renderQueue = 3152; }
            if (wantedLabel) { wantedLabel.font = f; wantedLabel.text = Loc.En ? "WANTED" : "通缉"; wantedLabel.fontMaterial.renderQueue = 3154; }
            bool passed = tut.passT > 0;
            title.text = Loc.En ? tut.Title.ToUpperInvariant() : tut.Title; title.fontSize = Loc.En ? 34 : 36; title.color = Neon;
            title.font = Resources.Load<TMP_FontAsset>(Loc.En ? "Fonts/ComicEN SDF" : "Fonts/GraffitiZH SDF"); title.fontMaterial.renderQueue = 3152;
            title.outlineWidth = 0.25f; title.outlineColor = new Color32(20, 12, 30, 255);
            title.transform.localRotation = Quaternion.Euler(0, 0, 3);
            body.text = Loc.En ? tut.Text.ToUpperInvariant() : tut.Text;
            body.fontSize = Loc.En ? 25 : 29; body.color = passed ? Done : Color.white;
            prog.text = $"{Loc.T("招式")} {tut.DoneCount} / {tut.Count}";
            prog.fontSize = 20; prog.color = new Color(1, 1, 1, 0.85f);
            for (int k = 0; k < chips.Count; k++)
            {
                var m = tut.moves[k];
                chips[k].text = Loc.En ? Loc.T(m.name).ToUpperInvariant() : m.name;
                chips[k].fontSize = Loc.En ? 15 : 19;
                // 字比贴纸宽（英文长词）就缩小，两边各留 6mm
                float cw = chips[k].GetPreferredValues(chips[k].text, 9999, 0).x, cmax = (Inner / chips.Count - ChipGap) * 100 - 1.2f;
                if (cw > cmax) chips[k].fontSize *= cmax / cw;
                // 学会的招式：贴纸变亮黄、字变深色（像刚贴上去的新贴纸）；没学会：暗紫贴纸、灰字
                chips[k].color = m.done ? new Color(0.1f, 0.06f, 0.14f) : new Color(1, 1, 1, 0.45f);
                if (k < stickers.Count) stickers[k].color = m.done ? new Color(1f, 0.86f, 0.22f) : new Color(0.22f, 0.2f, 0.3f);
            }
            // 整行（含补充提示）太长就缩小一点，保证一行放得下
            var pw = body.GetPreferredValues(body.text, 9999, 0).x; float max = Inner * 100;
            if (pw > max) body.fontSize *= max / pw;
        }

        void LateUpdate()
        {
            if (!sign || game == null) return;   // 组件是开局那一帧中途加上的：那一帧的 LateUpdate 会先于 Start 跑到
            var tut = game.tut;
            bool on = tut != null && tut.Step != null && !view.paused && game.result == null;
            if (sign.gameObject.activeSelf != on) sign.gameObject.SetActive(on);
            if (!on) { arrow.gameObject.SetActive(false); return; }
            // 换步 / 完成：重写文字、弹一下
            int key = tut.Version;
            if (key != lastStep) { if (tut.passT > 1.1f) pop = 1; lastStep = key; Refresh(); }
            pop = Mathf.Max(0, pop - Time.deltaTime * 3);
            hpBar?.Set(game.hp / Cfg.HpMax, view.hurtFlash);
            // 立在桌子近边外，转向玩家的头、略往后仰
            // 像讲台上的牌子：整块正对玩家的眼睛（站着往下看时也是正面，不会只看到一条边）
            var h = cam.transform.position;
            sign.position = anchor;
            // 只绕竖轴转向玩家（立柱保持竖直），牌面本身再朝眼睛仰一点
            var look = sign.position + Vector3.up * (H * 0.5f + 0.03f) - h; look.y = 0;
            if (look.sqrMagnitude > 1e-4f) sign.rotation = Quaternion.Slerp(sign.rotation, Quaternion.LookRotation(look.normalized, Vector3.up), 1 - Mathf.Exp(-Time.deltaTime * 6));
            panel.localScale = Vector3.one * baseScale * (1 + 0.08f * Mathf.Sin(pop * Mathf.PI));
            // 牌面朝眼睛仰起（绕底边往后仰，仰角取眼睛相对牌面中心高度的 80%）
            float dy = h.y - (anchor.y + (0.03f + H * 0.5f) * baseScale); float dh = new Vector2(look.x, look.z).magnitude;
            panel.localRotation = Quaternion.Euler(Mathf.Atan2(dy, Mathf.Max(0.05f, dh)) * Mathf.Rad2Deg * 0.8f, 0, 0);
            board.color = Color.Lerp(new Color(0.07f, 0.05f, 0.13f), new Color(0.2f, 0.06f, 0.18f), tut.passT > 0 ? 0.6f + 0.4f * Mathf.Sin(Time.time * 6) : 0);   // 学会一招：底板泛粉光
            // 通缉警灯（同 CityHud）：亮几盏 = 几星；刚升星时红蓝快闪
            if (game.stars != lastStars) { flash = lastStars >= 0 && game.stars > lastStars ? 1.2f : 0; lastStars = game.stars; }
            flash = Mathf.Max(0, flash - Time.deltaTime);
            for (int i = 0; i < beacons.Count; i++)
            {
                bool lit = i < game.stars, blue = ((int)(Time.time * 6) + i) % 2 == 0;
                beacons[i].color = lit ? (flash > 0 || i == game.stars - 1 ? (blue ? new Color(0.3f, 0.6f, 2f) : new Color(2f, 0.3f, 0.3f)) : new Color(1.6f, 0.35f, 0.35f)) : new Color(0.25f, 0.22f, 0.3f);
            }
            // 目标箭头
            var f = tut.Focus;
            arrow.gameObject.SetActive(f.HasValue);
            if (f.HasValue)
            {
                float bob = Mathf.Sin(Time.time * 4) * 0.008f;
                arrow.position = f.Value + Vector3.up * (0.03f + bob);
                arrow.rotation = Quaternion.Euler(180, Time.time * 90, 0);
                arrow.localScale = new Vector3(0.03f, 0.042f, 0.03f);
            }
        }
    }

    /// <summary>坐着玩的提醒：头的高度连续 3 秒低于 1.15m，就在眼前浮出“请站起来玩”（站起来后消失）</summary>
    public class StandReminder : MonoBehaviour
    {
        public Camera cam;
        Transform card; TextMeshPro text; float low;
        void Start()
        {
            if (!cam) cam = Camera.main;
            var shader = Shader.Find("Monster/ComicCard");
            card = new GameObject("StandReminder").transform;
            var ink = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(ink.GetComponent<Collider>()); ink.transform.SetParent(card, false); ink.transform.localScale = new Vector3(0.3f, 0.07f, 1); ink.transform.localPosition = new Vector3(0, 0, 0.001f);
            ink.GetComponent<MeshRenderer>().sharedMaterial = new Material(shader) { color = new Color(0.08f, 0.06f, 0.12f), renderQueue = 3300 };
            var box = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(box.GetComponent<Collider>()); box.transform.SetParent(card, false); box.transform.localScale = new Vector3(0.294f, 0.064f, 1);
            box.GetComponent<MeshRenderer>().sharedMaterial = new Material(shader) { color = new Color(1f, 0.86f, 0.3f), renderQueue = 3301 };
            var go = new GameObject("Text"); go.transform.SetParent(card, false); go.transform.localPosition = new Vector3(0, 0, -0.002f); go.transform.localScale = Vector3.one * 0.01f;
            text = go.AddComponent<TextMeshPro>(); text.alignment = TextAlignmentOptions.Center; text.enableWordWrapping = false; text.color = new Color(0.08f, 0.06f, 0.12f);
            text.rectTransform.sizeDelta = new Vector2(29, 6);
            card.gameObject.SetActive(false);
        }
        void OnDestroy() { if (card) Destroy(card.gameObject); }
        void LateUpdate()
        {
            if (!UnityEngine.XR.XRSettings.isDeviceActive) return;
            var h = cam.transform;
            low = h.position.y < 1.15f ? low + Time.deltaTime : 0;
            bool on = low > 3f || (card.gameObject.activeSelf && h.position.y < 1.25f);
            if (card.gameObject.activeSelf != on) card.gameObject.SetActive(on);
            if (!on) return;
            text.font = Resources.Load<TMP_FontAsset>(Loc.En ? "Fonts/ComicEN SDF" : "Fonts/ComicZH SDF"); text.fontMaterial.renderQueue = 3302;
            text.fontSize = 22; text.text = Loc.En ? Loc.T("请站起来玩").ToUpperInvariant() : "请站起来玩";
            var f = Vector3.ProjectOnPlane(h.forward, Vector3.up).normalized;
            card.position = Vector3.Lerp(card.position, h.position + f * 0.7f, 1 - Mathf.Exp(-Time.deltaTime * 4));
            card.rotation = Quaternion.LookRotation(f, Vector3.up);
        }
    }

    /// <summary>城区靠玩家那条边的实际位置（城区坐标 b）：虚拟桌按家具模型包围盒（桌面那一层），真家具就是城区边</summary>
    public static class CityEdge
    {
        /// <summary>桌子（含虚拟桌模型）离玩家最远那条边，在 z.v 方向上的坐标（远边在 0 附近或负数）</summary>
        public static float Back(CityBuilder builder, Zone z)
        {
            float back = -0.02f;
            var furn = builder.root.Find("Furniture_" + z.id) ?? builder.root.Find(z.id + "/Furniture");
            if (!furn) return back;
            foreach (var r in furn.GetComponentsInChildren<Renderer>())
            {
                var bb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? bb.min.x : bb.max.x, (i & 2) == 0 ? bb.min.y : bb.max.y, (i & 4) == 0 ? bb.min.z : bb.max.z);
                    if (c.y > z.origin.y - 0.08f) back = Mathf.Min(back, Vector3.Dot(c - z.origin, z.v));
                }
            }
            return back;
        }
        public static float Front(CityBuilder builder, Zone z)
        {
            float front = z.h + 0.02f;
            var furn = builder.root.Find("Furniture_" + z.id) ?? builder.root.Find(z.id + "/Furniture");
            if (!furn) return front;
            foreach (var r in furn.GetComponentsInChildren<Renderer>())
            {
                var bb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? bb.min.x : bb.max.x, (i & 2) == 0 ? bb.min.y : bb.max.y, (i & 4) == 0 ? bb.min.z : bb.max.z);
                    if (c.y > z.origin.y - 0.08f) front = Mathf.Max(front, Vector3.Dot(c - z.origin, z.v));
                }
            }
            return front;
        }
    }
}
