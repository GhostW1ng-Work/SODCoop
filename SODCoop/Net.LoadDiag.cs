using System;
using System.Collections.Generic;
using UnityEngine;

namespace SodCoop
{
    internal static partial class Net
    {
        static IntPtr loadDiagRoom;
        static int loadDiagStuck, loadDiagLines, loadDiagStage, loadDiagFarStuck, loadTrendTick;

        static int LoadTrendCount(Il2CppSystem.Type t) { return Resources.FindObjectsOfTypeAll(t).Length; }

        static void LoadTrend(NewRoom room)
        {
            if (++loadTrendTick < 120) return;
            loadTrendTick = 0;
            var pool = ObjectPoolingController.Instance;
            int vis = 0, cit = 0;
            var dir = CityData.Instance != null ? CityData.Instance.citizenDirectory : null;
            if (dir != null) for (int i = 0; i < dir.Count; i++) { var c = dir[i]; if (c == null) continue; cit++; if (c.visible) vis++; }
            string counts = $"объектов {LoadTrendCount(Il2CppInterop.Runtime.Il2CppType.Of<GameObject>())}, рендереров {LoadTrendCount(Il2CppInterop.Runtime.Il2CppType.Of<Renderer>())}, света {LoadTrendCount(Il2CppInterop.Runtime.Il2CppType.Of<Light>())}, коллайдеров {LoadTrendCount(Il2CppInterop.Runtime.Il2CppType.Of<Collider>())}, аниматоров {LoadTrendCount(Il2CppInterop.Runtime.Il2CppType.Of<Animator>())}";
            string q = pool == null ? "пула нет" : $"предметов загружено {pool.interactablesLoaded}, в пуле {pool.interactablesCurrentlyPooled}, на загрузку {pool.interactablesToLoadCount}, диапазон {pool.interactablesRangeCount}; мебель: ждёт проверки {pool.furnitureCheckingPool.Count}, под контролем {pool.furnitureRangeToEnableDisableList.Count}, за кадр {pool.maximumRangeCheckingPerFrame}; комнат загружено {pool.roomsLoaded}, в очереди {pool.roomStuffToLoad}";
            long ws; using (var pr = System.Diagnostics.Process.GetCurrentProcess()) ws = pr.WorkingSet64;
            Plugin.L.LogInfo($"[TREND] память процесса {ws / 1048576} МБ, Unity {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576} МБ, .NET {GC.GetTotalMemory(false) / 1048576} МБ; {counts}; жителей видно {vis}/{cit}; {q}; к/с {1f / Mathf.Max(Time.unscaledDeltaTime, 0.001f):F0}");
        }

        static void LoadDiagScan(NewRoom room, Vector3 from, List<FurnitureLocation> missing, ref int total, ref int outOfRange)
        {
            var pool = ObjectPoolingController.Instance;
            var all = new List<FurnitureLocation>();
            foreach (var f in room.individualFurniture) if (f != null) all.Add(f);
            foreach (var cl in room.furniture)
            {
                if (cl == null || cl.clusterList == null) continue;
                foreach (var f in cl.clusterList) if (f != null) all.Add(f);
            }
            foreach (var f in all)
            {
                total++;
                if (f.spawnedObject != null) continue;
                float d;
                if (pool != null && !pool.SpawnRangeCheck(f, out d)) { outOfRange++; continue; }
                missing.Add(f);
            }
        }

        static string LoadDiagState(NewRoom room, int total, int missing, int outOfRange)
        {
            var pool = ObjectPoolingController.Instance;
            string q = pool == null ? "пула нет" : $"на загрузку {pool.furnitureToLoadCount}, в проверке диапазона {pool.furnitureRangeCount}, в очереди проверки {pool.furntiureCheckCount}, комнат в очереди {pool.roomStuffToLoad}";
            return $"комната '{room.name}' id={room.roomID} видна={room.isVisible} геометрия={room.geometryLoaded}; мебели {total}, не появилось {missing}, вне диапазона {outOfRange}; {q}; к/с {1f / Mathf.Max(Time.unscaledDeltaTime, 0.001f):F0}";
        }

        static void LoadSnapRoom(string tag, NewRoom room, Vector3 from)
        {
            if (room == null) { Plugin.L.LogInfo($"[SNAP] {tag}: комнаты нет"); return; }
            var all = new List<FurnitureLocation>();
            int clusters = 0, clustersLoaded = 0;
            foreach (var f in room.individualFurniture) if (f != null) all.Add(f);
            foreach (var cl in room.furniture)
            {
                if (cl == null) continue;
                clusters++; if (cl.loadedGeometry) clustersLoaded++;
                if (cl.clusterList != null) foreach (var f in cl.clusterList) if (f != null) all.Add(f);
            }
            int noObj = 0, inactive = 0, noRender = 0;
            var lines = new List<string>();
            var pool = ObjectPoolingController.Instance;
            foreach (var f in all)
            {
                var go = f.spawnedObject;
                string st;
                if (go == null)
                {
                    noObj++;
                    float d = 0f; bool inR = pool != null && pool.SpawnRangeCheck(f, out d);
                    st = $"нет объекта, в диапазоне={inR}, дист={d:F1}";
                }
                else
                {
                    var rs = go.GetComponentsInChildren<Renderer>(true);
                    int on = 0; foreach (var r in rs) if (r != null && r.enabled && r.gameObject.activeInHierarchy) on++;
                    if (!go.activeInHierarchy) inactive++;
                    if (on == 0) noRender++;
                    st = $"объект активен={go.activeInHierarchy}, рендереров вкл {on}/{rs.Length}, поз={go.transform.position}";
                }
                if (lines.Count < 30) lines.Add((f.furniture != null ? f.furniture.name : "?") + ": " + st);
            }
            int batchKeys = -1, batchMeshes = -1;
            try { if (room.staticBatchDictionary != null) batchKeys = room.staticBatchDictionary.Count; if (room.staticBatchedGeneratedMeshes != null) batchMeshes = room.staticBatchedGeneratedMeshes.Count; } catch (Exception __e) { Err.Swallow(__e); }
            Plugin.L.LogInfo($"[SNAP] {tag}: объединение мешей: ключей {batchKeys}, готовых мешей {batchMeshes}");
            Plugin.L.LogInfo($"[SNAP] {tag}: '{room.name}' id={room.roomID} активна={room.gameObject.activeInHierarchy} видна={room.isVisible} геометрия={room.geometryLoaded}; групп {clusters}, загружено {clustersLoaded}; мебели {all.Count}, без объекта {noObj}, неактивно {inactive}, без рендера {noRender}");
            foreach (var l in lines) Plugin.L.LogInfo("[SNAP]   " + l);
        }

        static void LoadForceRenderers(Player p)
        {
            var room = p.currentRoom;
            if (room == null) return;
            var all = new List<FurnitureLocation>();
            foreach (var f in room.individualFurniture) if (f != null) all.Add(f);
            foreach (var cl in room.furniture)
            {
                if (cl == null || cl.clusterList == null) continue;
                foreach (var f in cl.clusterList) if (f != null) all.Add(f);
            }
            int on = 0, seen = 0;
            foreach (var f in all)
            {
                var go = f.spawnedObject;
                if (go == null) continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    seen++;
                    if (!r.enabled) { r.enabled = true; on++; }
                }
            }
            Toast($"Renderers enabled: {on}/{seen}.");
            Plugin.L.LogInfo($"[SNAP] F9: в комнате '{room.name}' id={room.roomID} включил рендереров {on} из {seen}");
        }

        internal static void LoadSnapKey(Player p)
        {
            if (p == null) return;
            if (KeyDown(KeyCode.F9)) LoadForceRenderers(p);
            if (!KeyDown(KeyCode.F8)) return;
            Toast("Load snapshot written to the log.");
            var cam = Camera.main;
            Plugin.L.LogInfo($"[SNAP] ==== снимок: игрок {p.transform.position}, камера {(cam != null ? cam.transform.position.ToString() : "нет")}, дальность {(cam != null ? cam.farClipPlane : 0f):F0}, маска {(cam != null ? cam.cullingMask : 0)}, окклюзия {(cam != null && cam.useOcclusionCulling)}; режим {RtMode}");
            var pool = ObjectPoolingController.Instance;
            try { var g = Game.Instance; if (g != null) Plugin.L.LogInfo($"[SNAP] настройки игры: статическое объединение={g.enableRuntimeStaticBatching}, объединение комнат={g.combineRoomMeshes}"); } catch (Exception __e) { Err.Swallow(__e); }
            if (pool != null) Plugin.L.LogInfo($"[SNAP] пул: мебель загрузка {pool.furnitureToLoadCount}, диапазон {pool.furnitureRangeCount}, проверка {pool.furntiureCheckCount}; предметы загрузка {pool.interactablesToLoadCount}, диапазон {pool.interactablesRangeCount}; комнат загружено {pool.roomsLoaded}, в очереди {pool.roomStuffToLoad}, кэш {pool.maxRoomCache}; мебели ждёт проверки {pool.furnitureCheckingPool.Count}, за кадр {pool.maximumRangeCheckingPerFrame}, всего под контролем {pool.furnitureRangeToEnableDisableList.Count}");
            LoadSnapRoom("текущая комната", p.currentRoom, p.transform.position);
            NewRoom nodeRoom = null;
            try { if (p.currentNode != null) nodeRoom = p.currentNode.room; } catch (Exception __e) { Err.Swallow(__e); }
            if (nodeRoom == null || p.currentRoom == null || nodeRoom.Pointer != p.currentRoom.Pointer) LoadSnapRoom("комната по точке пути", nodeRoom, p.transform.position);
            var gl = p.currentGameLocation;
            Plugin.L.LogInfo($"[SNAP] локация: {(gl != null ? gl.name : "нет")}, точка {(p.currentNode != null ? p.currentNode.position.ToString() : "нет")}");
            if (gl != null && gl.rooms != null)
                foreach (var r in gl.rooms)
                    if (r != null && (p.currentRoom == null || r.Pointer != p.currentRoom.Pointer)) LoadSnapRoom("в этой локации", r, p.transform.position);
        }

        static int loadFixLines;

        static void LoadFixRoom(NewRoom room)
        {
            var pool = ObjectPoolingController.Instance;
            if (pool == null) return;
            var all = new List<FurnitureLocation>();
            foreach (var f in room.individualFurniture) if (f != null) all.Add(f);
            foreach (var cl in room.furniture)
            {
                if (cl == null || cl.clusterList == null) continue;
                foreach (var f in cl.clusterList) if (f != null) all.Add(f);
            }
            int on = 0, items = 0;
            foreach (var f in all)
            {
                if (f.spawnedObject == null || f.meshes == null) continue;
                float d;
                if (!pool.SpawnRangeCheck(f, out d)) continue;
                bool fixedHere = false;
                foreach (var r in f.meshes) if (r != null && !r.enabled) { r.enabled = true; on++; fixedHere = true; }
                if (fixedHere) items++;
            }
            if (on > 0 && loadFixLines < 100)
            {
                loadFixLines++;
                Plugin.Dbg($"[LOADFIX] игра не включила мебель в диапазоне: комната '{room.name}' id={room.roomID}, предметов {items}, рендереров {on}; ждут проверки {pool.furnitureCheckingPool.Count}, за кадр {pool.maximumRangeCheckingPerFrame}, к/с {1f / Mathf.Max(Time.unscaledDeltaTime, 0.001f):F0}");
            }
        }

        static bool imageHitLogged;
        static int eofStuck;
        static bool eofProbePending;
        static Il2CppSystem.Action eofProbe;

        internal static bool SkipImageHitTest(UnityEngine.UI.Image img)
        {
            var sp = img != null ? img.sprite : null;
            var t = sp != null ? sp.texture : null;
            if (t == null || t.isReadable || t.format.ToString().Contains("Crunched")) return false;
            if (!imageHitLogged) { imageHitLogged = true; Plugin.L.LogInfo($"[EOF] пропускаю alphaHitTest у '{img.name}': текстура недоступна для чтения, игра упала бы здесь с ошибкой"); }
            return true;
        }

        static void EndOfFrameWatch()
        {
            var tb = Toolbox.Instance;
            if (tb == null || tb.invokeEndOfFrame == null || tb.debugInvokeEndOfFrame == null) return;
            if (!eofProbePending)
            {
                if (eofProbe == null) eofProbe = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() => { eofProbePending = false; }));
                eofProbePending = true; eofStuck = 0;
                tb.InvokeEndOfFrame(eofProbe, "SodCoop probe");
                return;
            }
            if (++eofStuck < 3) return;
            eofStuck = 0;
            if (!tb.endOfFrameInvoke) { tb.invokeEndOfFrame.Remove(eofProbe); tb.debugInvokeEndOfFrame.Remove("SodCoop probe"); eofProbePending = false; return; }
            string first = tb.debugInvokeEndOfFrame.Count > 0 ? tb.debugInvokeEndOfFrame[0] : "?";
            Il2CppSystem.Action stuck = null;
            foreach (var a in tb.invokeEndOfFrame) { stuck = a; break; }
            Plugin.L.LogWarning($"[EOF] очередь конца кадра стоит на '{first}', заданий {tb.invokeEndOfFrame.Count}: убираю его и запускаю очередь заново");
            if (stuck != null) tb.invokeEndOfFrame.Remove(stuck);
            if (tb.debugInvokeEndOfFrame.Count > 0) tb.debugInvokeEndOfFrame.RemoveAt(0);
            tb.endOfFrameInvoke = false;
            tb.InvokeEndOfFrame(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() => { })), "SodCoop restart");
        }

        internal static void LoadDiagTick(Player p)
        {
            if (p == null) return;
            try { EndOfFrameWatch(); } catch (Exception e) { Plugin.L.LogWarning("[EOF] " + e.Message); }
            if (p.currentRoom != null) { try { LoadFixRoom(p.currentRoom); } catch (Exception e) { Plugin.L.LogWarning("[LOADFIX] " + e.Message); } }
            NewRoom room = p.currentRoom;
            if (room == null) { loadDiagRoom = IntPtr.Zero; loadDiagStuck = 0; loadDiagStage = 0; loadDiagFarStuck = 0; return; }
            if (room.Pointer != loadDiagRoom) { loadDiagRoom = room.Pointer; loadDiagStuck = 0; loadDiagStage = 0; loadDiagFarStuck = 0; }
            try { LoadTrend(room); } catch (Exception e) { loadTrendTick = 0; Plugin.L.LogWarning("[TREND] " + e.Message); }
            var missing = new List<FurnitureLocation>();
            int total = 0, outOfRange = 0;
            LoadDiagScan(room, p.transform.position, missing, ref total, ref outOfRange);
            if (outOfRange > 0 && ++loadDiagFarStuck == 5 && loadDiagLines < 60) { loadDiagLines++; Plugin.L.LogInfo($"[LOADDIAG] 5 с есть мебель вне диапазона загрузки: {LoadDiagState(room, total, missing.Count, outOfRange)}"); }
            if (missing.Count == 0)
            {
                if (loadDiagStuck >= 5) { loadDiagLines++; Plugin.L.LogInfo($"[LOADDIAG] мебель появилась через {loadDiagStuck} с, этап {loadDiagStage}: {LoadDiagState(room, total, 0, outOfRange)}"); }
                loadDiagStuck = 0; loadDiagStage = 0;
                return;
            }
            loadDiagStuck++;
            if (loadDiagLines >= 60) return;
            if (loadDiagStuck == 5)
            {
                var names = new List<string>();
                for (int i = 0; i < missing.Count && i < 6; i++) names.Add(missing[i].furniture != null ? missing[i].furniture.name : "?");
                loadDiagLines++;
                Plugin.L.LogWarning($"[LOADDIAG] мебель не появляется 5 с: {LoadDiagState(room, total, missing.Count, outOfRange)}; например: {string.Join(", ", names)}");
            }
            else if (loadDiagStuck == 20)
            {
                loadDiagStage = 1; loadDiagLines++;
                Plugin.L.LogWarning($"[LOADDIAG] 20 с: запускаю ExecuteUpdateObjectRanges(true): {LoadDiagState(room, total, missing.Count, outOfRange)}");
                var pool = ObjectPoolingController.Instance;
                if (pool != null) pool.ExecuteUpdateObjectRanges(true);
            }
            else if (loadDiagStuck == 30)
            {
                loadDiagStage = 2; loadDiagLines++;
                Plugin.L.LogWarning($"[LOADDIAG] 30 с: вызываю SpawnObject(true) у {missing.Count} предметов: {LoadDiagState(room, total, missing.Count, outOfRange)}");
                foreach (var f in missing) f.SpawnObject(true);
            }
            else if (loadDiagStuck == 35)
            {
                loadDiagLines++;
                Plugin.L.LogWarning($"[LOADDIAG] 35 с: всё ещё нет мебели: {LoadDiagState(room, total, missing.Count, outOfRange)}");
            }
        }
    }
}
