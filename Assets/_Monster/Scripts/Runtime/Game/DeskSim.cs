// 编辑器 / 电脑试玩（没有头显时）：鼠标当眼睛和手，照原型 src/input/desktop.js。
//   左键 = 往下拍；Shift + 左键 = 横推（上半截朝远离你的方向倒）；右键按住 = 抓追兵，松开 = 朝鼠标指的地方扔
//   E 按住 = 拔起鼠标下的楼，松开 = 扔出去；空格 = 吐息（怒气满时）；WASD 走动、QE 升降、方向键转头
using UnityEngine;
using UnityEngine.InputSystem;

namespace Monster.Game
{
    public class DeskSim : MonoBehaviour
    {
        public MonsterGame game; public Camera cam;
        public float moveSpeed = 0.8f, turnSpeed = 70f;
        readonly GameInput gi = new() { hands = new System.Collections.Generic.List<HandIn>() };
        Ray mouseRay;

        public GameInput Read()
        {
            if (!cam) cam = Camera.main;
            var kb = Keyboard.current; var ms = Mouse.current;
            float dt = Time.deltaTime;
            if (kb != null)
            {
                var t = cam.transform;
                var fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized; var right = Vector3.Cross(Vector3.up, fwd);
                var mv = Vector3.zero;
                if (kb.wKey.isPressed) mv += fwd; if (kb.sKey.isPressed) mv -= fwd;
                if (kb.dKey.isPressed) mv += right; if (kb.aKey.isPressed) mv -= right;
                if (kb.qKey.isPressed) mv -= Vector3.up * 0.5f; if (kb.rKey.isPressed) mv += Vector3.up * 0.5f;
                t.position += mv * (moveSpeed * dt);
                float yaw = (kb.rightArrowKey.isPressed ? 1 : 0) - (kb.leftArrowKey.isPressed ? 1 : 0);
                float pitch = (kb.downArrowKey.isPressed ? 1 : 0) - (kb.upArrowKey.isPressed ? 1 : 0);
                var e = t.eulerAngles; t.rotation = Quaternion.Euler(Mathf.Clamp(Mathf.DeltaAngle(0, e.x) + pitch * turnSpeed * dt, -80, 80), e.y + yaw * turnSpeed * dt, 0);
            }
            mouseRay = ms != null ? cam.ScreenPointToRay(ms.position.ReadValue()) : new Ray(cam.transform.position, cam.transform.forward);
            gi.hasHead = true; gi.headPos = cam.transform.position; gi.headFwd = cam.transform.forward;
            gi.hasGaze = true; gi.gaze = mouseRay;
            if (game == null || game.result != null) return gi;
            bool shift = kb != null && kb.leftShiftKey.isPressed;
            if (ms != null && ms.leftButton.wasPressedThisFrame) Click(shift);
            if (ms != null && ms.rightButton.wasPressedThisFrame) { var h = game.Raycast(mouseRay, 4); if (h.HasValue && h.Value.e != null) game.GrabEnemy("right", h.Value.e); }
            if (ms != null && ms.rightButton.isPressed) game.HoldAt("right", HoldPoint());
            if (ms != null && ms.rightButton.wasReleasedThisFrame) game.Release("right", mouseRay.direction * 2.5f, game.AimPoint(mouseRay));
            if (kb != null && kb.eKey.wasPressedThisFrame) { var h = game.Raycast(mouseRay, 4, false); if (h.HasValue && h.Value.b != null && h.Value.b.kind != BuildingKind.Station) game.Uproot("both", h.Value.b); }
            if (kb != null && kb.eKey.isPressed) game.HoldAt("both", HoldPoint());
            if (kb != null && kb.eKey.wasReleasedThisFrame) game.Release("both", mouseRay.direction * 2.5f, game.AimPoint(mouseRay));
            if (kb != null && kb.spaceKey.wasPressedThisFrame) game.StartBreath();
            return gi;
        }

        Vector3 HoldPoint() => cam.transform.position + cam.transform.forward * 0.35f - Vector3.up * 0.12f;

        void Click(bool shift)
        {
            // 先看鼠标附近有没有飞来的炮弹（拍开）
            for (int i = game.projectiles.Count - 1; i >= 0; i--)
            {
                var p = game.projectiles[i];
                float along = Vector3.Dot(p.pos - mouseRay.origin, mouseRay.direction);
                if (along > 0 && Vector3.Distance(p.pos, mouseRay.GetPoint(along)) < 0.04f) { game.Parry(i, mouseRay.direction * 2); return; }
            }
            var h = game.Raycast(mouseRay, 4);
            if (!h.HasValue) return;
            var hit = h.Value;
            if (hit.e != null) { game.HitEnemy(hit.e, Enemies.Types[hit.e.type].fly ? mouseRay.direction * 2 : Vector3.down * 2, Enemies.Types[hit.e.type].fly ? "swat" : "smash"); return; }
            if (hit.b == null) return;
            if (hit.b.kind == BuildingKind.Tower && game.IsShielded(hit.b)) { game.Hurt(Cfg.ShieldBounceDmg, hit.point, "shield"); game.Emit(new Ev { type = "shield_hit", id = hit.b.id, pos = hit.point }); return; }
            var flat = Vector3.ProjectOnPlane(mouseRay.direction, Vector3.up).normalized;
            game.SmashBuilding(hit.b, hit.point, shift ? flat * 1.5f + Vector3.down * 0.2f : Vector3.down * 2);
        }
    }
}
