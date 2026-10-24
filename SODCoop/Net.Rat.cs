using System;
using System.Collections.Generic;
using UnityEngine;

namespace SodCoop
{
    internal static partial class Net
    {
        const float RatAvatarScale = 0.4f / 1.68f;
        static readonly Dictionary<long, float> ratReactAt = new Dictionary<long, float>();
        static float ratTickAt, ratDiagAt;
        static int ratDiagLogged;

        static bool LocalRat()
        {
            if (GhostMe) return false;
            var mc = ModifiersController.Instance;
            return mc != null && mc.ratDetectiveModifierEnabled;
        }

        static bool RpRat(RP rp) { return rp != null && rp.ratOn; }

        static readonly HashSet<string> ratIgnored = new HashSet<string>();
        static float ratColAt;
        static bool ratColOn;

        static void RatCollisionTick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < ratColAt) return;
            ratColAt = now + 1f;
            bool want = LocalRat() && manager != null && manager.ConnectedPeersCount > 0;
            if (!want && !ratColOn) return;
            var mine = new List<Collider>();
            var p = Player.Instance;
            var mc = ModifiersController.Instance;
            try { if (p != null && p.charController != null) mine.Add(p.charController); } catch (Exception __e) { Err.Swallow(__e); }
            try { if (mc != null && mc.playerCapsuleCollider != null) mine.Add(mc.playerCapsuleCollider); if (mc != null && mc.playerCameraCollider != null) mine.Add(mc.playerCameraCollider); } catch (Exception __e) { Err.Swallow(__e); }
            if (mine.Count == 0) return;
            int added = 0;
            foreach (var rp in players)
            {
                Citizen c = null;
                try
                {
                    if (IsHost) c = PuppetOf(rp);
                    else { int id = PuppetIdOf(rp); if (id >= 0 && npcById != null) npcById.TryGetValue(id, out c); }
                }
                catch (Exception __e) { Err.Swallow(__e); }
                var objs = new List<GameObject>();
                if ((object)c != null && c != null) objs.Add(c.gameObject);
                var av = rp == curRp ? avatar : rp.avatar;
                if (av != null) objs.Add(av);
                foreach (var go in objs)
                    foreach (var oc in go.GetComponentsInChildren<Collider>(true))
                    {
                        if (oc == null) continue;
                        foreach (var m in mine)
                        {
                            string key = oc.GetInstanceID() + ":" + m.GetInstanceID();
                            bool has = ratIgnored.Contains(key);
                            if (want == has) continue;
                            try { Physics.IgnoreCollision(m, oc, want); } catch (Exception __e) { Err.Swallow(__e); }
                            if (want) { ratIgnored.Add(key); added++; } else ratIgnored.Remove(key);
                        }
                    }
            }
            if (!want) { ratIgnored.Clear(); ratColOn = false; Plugin.L.LogInfo("[RAT] столкновения с другими игроками снова включены"); return; }
            if (!ratColOn) { ratColOn = true; Plugin.L.LogInfo("[RAT] я крыса: прохожу сквозь других игроков и их двойников"); }
            if (added > 0) Plugin.Dbg($"[RAT] выключил столкновений: {added}");
        }

        static void RatPose(Player p, ref Vector3 pos, ref float feet, out Vector3 up)
        {
            up = Vector3.up;
            try
            {
                var cc = p.charController;
                var t = cc != null ? cc.transform : p.transform;
                up = t.up;
                if (cc != null)
                {
                    Vector3 c = t.TransformPoint(cc.center);
                    Vector3 b = c - up * (cc.height * 0.5f * t.lossyScale.y);
                    pos = b; feet = RatGround(p, b);
                }
                float now = Time.realtimeSinceStartup;
                if (ratDiagLogged < 12 && now >= ratDiagAt)
                {
                    ratDiagAt = now + 10f; ratDiagLogged++;
                    var cam = Camera.main;
                    Plugin.L.LogInfo($"[RAT] я крыса: капсула h={(cc != null ? cc.height : -1f):F2} низ {pos.y:F2}, пол {feet:F2} (ниже на {ratGroundLast:F2}), камера над полом {(cam != null ? cam.transform.position.y - feet : -1f):F2}");
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
        }

        static float ratGroundLast;

        static float RatGround(Player p, Vector3 bottom)
        {
            var own = new HashSet<int>();
            try { if (p.charController != null) own.Add(p.charController.GetInstanceID()); } catch (Exception __e) { Err.Swallow(__e); }
            try { foreach (var oc in p.GetComponentsInChildren<Collider>(true)) if (oc != null) own.Add(oc.GetInstanceID()); } catch (Exception __e) { Err.Swallow(__e); }
            try { var mc = ModifiersController.Instance; if (mc != null) { if (mc.playerCapsuleCollider != null) own.Add(mc.playerCapsuleCollider.GetInstanceID()); if (mc.playerCameraCollider != null) own.Add(mc.playerCameraCollider.GetInstanceID()); } } catch (Exception __e) { Err.Swallow(__e); }
            float best = float.NaN;
            try
            {
                var hits = Physics.RaycastAll(bottom + Vector3.up * 0.15f, Vector3.down, 1.2f, ~0, QueryTriggerInteraction.Ignore);
                foreach (var h in hits)
                {
                    if (h.collider == null || own.Contains(h.collider.GetInstanceID())) continue;
                    if (float.IsNaN(best) || h.point.y > best) best = h.point.y;
                }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            float y = float.IsNaN(best) || best > bottom.y + 0.15f ? bottom.y : best;
            ratGroundLast = bottom.y - y;
            return y;
        }

        static readonly HashSet<int> ratDiagDone = new HashSet<int>();

        static void RatAvatarDiag(RP rp, GameObject av, Vector3 feet)
        {
            if (rp == null || av == null || ratDiagDone.Contains(rp.pid)) return;
            var rs = av.GetComponentsInChildren<Renderer>(false);
            if (rs.Length == 0) return;
            Bounds b = new Bounds(); bool any = false;
            foreach (var r in rs) { if (r == null || !r.enabled) continue; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            if (!any) return;
            ratDiagDone.Add(rp.pid);
            Plugin.L.LogInfo($"[RAT] гном игрока {rp.pid}: высота {b.size.y:F2} м, низ {b.min.y:F2}, ноги по сети {feet.y:F2}");
        }

        internal static bool fallCheck;
        static NewNode ratGoodNode;
        static float ratGoodAt;

        static void RatGoodTick(Player p)
        {
            if (p == null || !LocalRat() || p.playerKOInProgress) return;
            float now = Time.realtimeSinceStartup;
            if (now < ratGoodAt) return;
            ratGoodAt = now + 0.5f;
            try { var cc = p.charController; if (cc != null && cc.isGrounded && p.currentNode != null) ratGoodNode = p.currentNode; } catch (Exception __e) { Err.Swallow(__e); }
        }

        internal static bool FallRescue(string why)
        {
            var p = Player.Instance;
            if (p == null) return false;
            string info = "";
            try { var cc = p.charController; info = $"позиция {p.transform.position}, капсула h={(cc != null ? cc.height : -1f):F2}, здоровье {p.currentHealth:F2}, крыса={LocalRat()}"; } catch (Exception __e) { Err.Swallow(__e); }
            Plugin.L.LogWarning($"[RAT] игра вырубает игрока: {why}; {info}");
            if (!LocalRat()) return false;
            var node = ratGoodNode;
            try { if (node == null) node = p.currentNode; } catch (Exception __e) { Err.Swallow(__e); }
            if (node == null) return false;
            try
            {
                p.Teleport(node, null, true, false, true);
                Plugin.L.LogInfo($"[RAT] крыса провалилась ({why}) - вместо нокаута ставлю обратно на пол {node.position}");
                return true;
            }
            catch (Exception e) { Plugin.L.LogWarning("[RAT] вернуть на пол: " + e.Message); return false; }
        }

        static Quaternion RatTilt(RP rp, Quaternion yaw)
        {
            if (!RpRat(rp)) return yaw;
            Vector3 up = rp.ratUp.sqrMagnitude > 0.01f ? rp.ratUp.normalized : Vector3.up;
            return Quaternion.FromToRotation(Vector3.up, up) * yaw;
        }

        static bool RatTooFar(Actor observer, Actor target)
        {
            if (!IsHost || observer == null || target == null || players.Count == 0) return false;
            var mc = ModifiersController.Instance;
            if (mc == null || !mc.ratDetectiveModifierEnabled) return false;
            IntPtr ptr = target.Pointer;
            for (int i = 0; i < players.Count; i++)
            {
                var rp = players[i];
                var c = PuppetOf(rp);
                if ((object)c == null || c.Pointer != ptr) continue;
                if (!RpRat(rp)) return false;
                return Vector3.Distance(observer.transform.position, c.transform.position) > Mathf.Max(0.5f, mc.RatPlayerSpotDistance);
            }
            return false;
        }

        static void RatTick()
        {
            if (!IsHost || manager == null || manager.ConnectedPeersCount == 0 || players.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            if (now < ratTickAt) return;
            ratTickAt = now + 0.5f;
            var mc = ModifiersController.Instance;
            if (mc == null || !mc.ratDetectiveModifierEnabled) return;
            var dir = CityData.Instance != null ? CityData.Instance.citizenDirectory : null;
            if (dir == null) return;
            float spot = Mathf.Max(0.5f, mc.RatPlayerSpotDistance);
            float kickRange = 5f;
            try { var cc = CitizenControls.Instance; if (cc != null) kickRange = cc.punchedResponseRange; } catch (Exception __e) { Err.Swallow(__e); }
            foreach (var rp in players)
            {
                if (!RpRat(rp) || IsGhostRp(rp) || rp.downed) continue;
                var c = PuppetOf(rp);
                if ((object)c == null || c == null) continue;
                Vector3 rpos = c.transform.position;
                for (int i = 0; i < dir.Count; i++)
                {
                    var h = dir[i];
                    try
                    {
                        if (h == null || h.isDead || h.isStunned || h.isAsleep || h.ai == null || h.Pointer == c.Pointer) continue;
                        if (!h.hatesRats && !h.scaredOfRats) continue;
                        Vector3 hp = h.transform.position;
                        if (Mathf.Abs(hp.y - rpos.y) > 2.5f) continue;
                        Vector3 d = rpos - hp;
                        if (d.magnitude > spot) continue;
                        Vector3 f = h.transform.forward; d.y = 0f; f.y = 0f;
                        if (d.sqrMagnitude > 0.25f && Vector3.Dot(f.normalized, d.normalized) < 0.2f) continue;
                        long key = ((long)h.humanID << 8) | (uint)rp.pid;
                        float at;
                        if (ratReactAt.TryGetValue(key, out at) && now < at) continue;
                        ratReactAt[key] = now + 20f;
                        if (h.hatesRats)
                        {
                            try { h.speechController.TriggerBark(SpeechController.Bark.rat); } catch (Exception __e) { Err.Swallow(__e); }
                            h.ai.SetPersue(c, false, 2, true, kickRange);
                            Plugin.L.LogInfo($"[RAT] горожанин {h.humanID} ({h.citizenName}) ненавидит крыс - пинает игрока {rp.pid}");
                        }
                        else
                        {
                            h.AddBladder(0.2f);
                            try { h.speechController.TriggerBark(SpeechController.Bark.scared); } catch (Exception __e) { Err.Swallow(__e); }
                            Plugin.L.LogInfo($"[RAT] горожанин {h.humanID} ({h.citizenName}) боится крыс - испугался игрока {rp.pid}");
                        }
                    }
                    catch (Exception __e) { Err.Swallow(__e); }
                }
            }
        }
    }
}
