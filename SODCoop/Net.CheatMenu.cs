using System;
using System.Collections.Generic;
using UnityEngine;

namespace SodCoop
{
    internal static partial class Net
    {
        static bool cheatOpen, cheatBuilt;
        static int cheatPage, itemCat, itemPage;
        static string itemSearch = "";
        static UiKit.Win cheatWin;
        static string cheatMsg = "";
        static TMPro.TextMeshProUGUI cheatMsgLabel;
        static TMPro.TMP_InputField cheatMoney, cheatSocial, cheatSearch;
        static string searchShown = "";

        internal static bool CheatMenuOpen { get { return cheatOpen; } }

        static void ToggleCheatMenu()
        {
            if (cheatOpen) { CloseCheatMenu(); return; }
            if (NetMenu.Open || CharEditor.Open) return;
            try { var mm = MainMenuController.Instance; if (mm != null && mm.mainMenuActive) return; } catch (Exception __e) { Err.Swallow(__e); }
            cheatOpen = true; cheatBuilt = false; cheatMsg = ""; cheatPage = 0;
            CheatInput(false);
            Plugin.L.LogInfo("[CHEAT] меню читов открыто");
        }

        static void CloseCheatMenu()
        {
            CheatHarvest();
            cheatOpen = false;
            if (cheatWin != null && cheatWin.Alive) UiKit.Show(cheatWin, false);
            CheatInput(true);
        }

        static void CheatInput(bool game)
        {
            try
            {
                var p = Player.Instance;
                if (p != null) { p.EnablePlayerMouseLook(game, false); p.EnablePlayerMovement(game, true); }
                var ic = InputController.Instance;
                if (ic != null) { ic.SetCursorLock(game); ic.SetCursorVisible(!game); }
            }
            catch (Exception __e) { Err.Swallow(__e); }
            Cursor.lockState = game ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !game;
        }

        internal static void CheatMenuUiTick()
        {
            if (!cheatOpen) return;
            if (!UiKit.Init()) return;
            bool menu = false; try { var mm = MainMenuController.Instance; menu = mm != null && mm.mainMenuActive; } catch (Exception __e) { Err.Swallow(__e); }
            if (menu || Player.Instance == null || KeyDown(KeyCode.Escape)) { CloseCheatMenu(); return; }
            if (cheatWin == null || !cheatWin.Alive) { cheatWin = UiKit.MakeWindow("SodCoop CheatPanel", "", 1300f, 860f, true); cheatBuilt = false; }
            if (!cheatBuilt) { CheatHarvest(); if (cheatPage == 0) BuildCheatMain(); else BuildItemPage(); cheatBuilt = true; }
            if (!cheatWin.Root.activeSelf) UiKit.Show(cheatWin, true);
            if (Cursor.lockState != CursorLockMode.None || !Cursor.visible) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (cheatMsgLabel != null && cheatMsgLabel.text != cheatMsg) cheatMsgLabel.text = cheatMsg;
            if (cheatSearch != null && cheatSearch.text != searchShown) { itemSearch = cheatSearch.text; searchShown = itemSearch; itemPage = 0; cheatBuilt = false; refocusSearch = true; }
            if (refocusSearch && cheatBuilt && cheatSearch != null) { refocusSearch = false; try { cheatSearch.ActivateInputField(); cheatSearch.MoveTextEnd(false); } catch (Exception __e) { Err.Swallow(__e); } }
        }

        static bool refocusSearch;

        static void CheatHarvest()
        {
            int v;
            if (cheatMoney != null && int.TryParse(cheatMoney.text, out v)) Plugin.CfgMoneyAmount.Value = v;
            if (cheatSocial != null && int.TryParse(cheatSocial.text, out v)) Plugin.CfgSocialAmount.Value = v;
        }

        static void CheatRefresh() { CheatHarvest(); cheatBuilt = false; }

        static void CheatClear()
        {
            var w = cheatWin;
            UiKit.Clear(w.Content); UiKit.Clear(w.Buttons);
            cheatMoney = cheatSocial = cheatSearch = null;
        }

        static void BuildCheatMain()
        {
            var w = cheatWin; var c = w.Content;
            CheatClear();
            w.Title.text = UiKit.T("Cheats", "Читы");
            float W = c.rect.width > 100f ? c.rect.width : 1172f, H = c.rect.height > 100f ? c.rect.height : 604f;

            float x = 30f, y = 16f, bw = 520f;
            UiKit.Label(c, UiKit.T("Toggles", "Переключатели"), x, y, bw, 32f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, UiKit.HeadColor); y += 42f;
            Toggle(c, x, ref y, bw, UiKit.T("God mode", "Бессмертие"), Plugin.CfgGod.Value, () => { Plugin.CfgGod.Value = !Plugin.CfgGod.Value; cheatMsg = UiKit.T("God mode: ", "Бессмертие: ") + OnOff(Plugin.CfgGod.Value); });
            Toggle(c, x, ref y, bw, UiKit.T("Citizens can't see me", "Жители меня не видят"), Plugin.CfgInvisible.Value, () => { Plugin.CfgInvisible.Value = !Plugin.CfgInvisible.Value; cheatMsg = UiKit.T("Invisible: ", "Невидимость: ") + OnOff(Plugin.CfgInvisible.Value); });
            Toggle(c, x, ref y, bw, UiKit.T("Freeze citizens", "Остановить жителей"), Plugin.CfgPauseNpc.Value, () => { Plugin.CfgPauseNpc.Value = !Plugin.CfgPauseNpc.Value; ApplyPause(); cheatMsg = UiKit.T("Citizens frozen: ", "Жители остановлены: ") + OnOff(Plugin.CfgPauseNpc.Value); });
            Toggle(c, x, ref y, bw, UiKit.T("Case answers on screen", "Ответы по делам на экране"), caseAnswers != null, () => { ToggleCaseAnswers(); cheatMsg = UiKit.T("Case answers: ", "Ответы по делам: ") + OnOff(caseAnswers != null); });
            y += 14f;
            UiKit.Label(c, UiKit.T("Reveal", "Раскрыть"), x, y, bw, 32f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, UiKit.HeadColor); y += 42f;
            UiKit.Button(c, UiKit.T("Reveal all citizens", "Раскрыть всех жителей"), x, y, bw, 50f, () => { RevealAllCitizens(); cheatMsg = UiKit.T("All citizens revealed.", "Все жители раскрыты."); }); y += 60f;
            UiKit.Button(c, UiKit.T("Learn all passwords", "Узнать все пароли"), x, y, bw, 50f, () =>
            {
                int codes, pcs; RevealAllPasswords(out codes, out pcs);
                cheatMsg = UiKit.T($"Codes learned: {codes}, computer passwords: {pcs}.", $"Узнано кодов: {codes}, паролей от компьютеров: {pcs}.");
            });

            float rx = 620f, ry = 16f, rw = W - rx - 30f;
            UiKit.Label(c, UiKit.T("Give", "Выдать"), rx, ry, rw, 32f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, UiKit.HeadColor); ry += 42f;
            UiKit.Label(c, UiKit.T("Money", "Деньги"), rx, ry, 200f, 50f);
            cheatMoney = UiKit.Input(c, rx + 210f, ry + 2f, 160f, 46f, Plugin.CfgMoneyAmount.Value.ToString(), 9, false);
            UiKit.Button(c, UiKit.T("Add", "Добавить"), rx + 384f, ry, rw - 384f, 50f, () =>
            {
                CheatHarvest(); GiveMoney(Plugin.CfgMoneyAmount.Value);
                cheatMsg = UiKit.T($"Money added: {Plugin.CfgMoneyAmount.Value}.", $"Добавлено денег: {Plugin.CfgMoneyAmount.Value}.");
            });
            ry += 64f;
            UiKit.Label(c, UiKit.T("Social credit", "Соцрейтинг"), rx, ry, 200f, 50f);
            cheatSocial = UiKit.Input(c, rx + 210f, ry + 2f, 160f, 46f, Plugin.CfgSocialAmount.Value.ToString(), 9, false);
            UiKit.Button(c, UiKit.T("Add", "Добавить"), rx + 384f, ry, rw - 384f, 50f, () =>
            {
                CheatHarvest(); GiveSocial(Plugin.CfgSocialAmount.Value);
                cheatMsg = UiKit.T($"Social credit added: {Plugin.CfgSocialAmount.Value}.", $"Добавлено соцрейтинга: {Plugin.CfgSocialAmount.Value}.");
            });
            ry += 78f;
            UiKit.Label(c, UiKit.T("Items", "Предметы"), rx, ry, rw, 32f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, UiKit.HeadColor); ry += 42f;
            UiKit.Button(c, UiKit.T("Items and weapons...", "Предметы и оружие..."), rx, ry, rw, 50f, () => { CheatHarvest(); cheatPage = 1; cheatBuilt = false; });

            cheatMsgLabel = UiKit.Label(c, cheatMsg, 30f, H - 48f, W - 60f, 36f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, UiKit.HeadColor);
            UiKit.BarButton(w, UiKit.T("Close", "Закрыть"), 300f, () => CloseCheatMenu());
        }

        static void Toggle(Transform c, float x, ref float y, float w, string label, bool on, Action flip)
        {
            var b = UiKit.Button(c, label, x, y, w, 50f, () => { flip(); CheatRefresh(); });
            UiKit.SetSelected(b, on);
            y += 60f;
        }

        static string OnOff(bool v) { return v ? UiKit.T("on", "вкл") : UiKit.T("off", "выкл"); }

        sealed class ItemEntry { public InteractablePreset preset; public string name; public int cat; }
        static List<ItemEntry> itemCatalog;

        static string[] ItemCats()
        {
            return new[]
            {
                UiKit.T("All", "Всё"), UiKit.T("Weapons", "Оружие"), UiKit.T("Food & drink", "Еда и напитки"), UiKit.T("Medical", "Медицина"),
                UiKit.T("Equipment", "Снаряжение"), UiKit.T("Electronics", "Электроника"), UiKit.T("Documents", "Документы"), UiKit.T("Other", "Разное")
            };
        }

        static List<ItemEntry> ItemCatalog()
        {
            if (itemCatalog != null) return itemCatalog;
            itemCatalog = new List<ItemEntry>();
            var seen = new HashSet<string>();
            foreach (var p in Resources.FindObjectsOfTypeAll<InteractablePreset>())
            {
                try
                {
                    if (p == null || string.IsNullOrEmpty(p.name) || !p.spawnable || p.physicsProfile == null || !seen.Add(p.name)) continue;
                    int cat;
                    if (p.weapon != null) cat = 1;
                    else
                    {
                        var ic = p.itemClass;
                        cat = ic == InteractablePreset.ItemClass.consumable ? 2 : ic == InteractablePreset.ItemClass.medical ? 3 : ic == InteractablePreset.ItemClass.equipment ? 4
                            : ic == InteractablePreset.ItemClass.electronics ? 5 : ic == InteractablePreset.ItemClass.document ? 6 : 7;
                        if (cat == 7 && p.retailItem != null && p.retailItem.isConsumable) cat = 2;
                    }
                    string nm = "";
                    try { nm = Strings.Get("evidence.names", p.name, Strings.Casing.firstLetterCaptial, false, true, false, null); } catch (Exception __e) { Err.Swallow(__e); }
                    if (string.IsNullOrWhiteSpace(nm) || nm == p.name) nm = p.name;
                    itemCatalog.Add(new ItemEntry { preset = p, name = nm, cat = cat });
                }
                catch (Exception __e) { Err.Swallow(__e); }
            }
            itemCatalog.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.CurrentCultureIgnoreCase));
            Plugin.L.LogInfo($"[CHEAT] предметов для выдачи: {itemCatalog.Count}");
            return itemCatalog;
        }

        static void BuildItemPage()
        {
            var w = cheatWin; var c = w.Content;
            CheatClear();
            w.Title.text = UiKit.T("Items and weapons", "Предметы и оружие");
            float W = c.rect.width > 100f ? c.rect.width : 1172f, H = c.rect.height > 100f ? c.rect.height : 604f;

            var cats = ItemCats();
            float tx = 20f, tw = (W - 40f - 6f * (cats.Length - 1)) / cats.Length;
            for (int i = 0; i < cats.Length; i++)
            {
                int k = i;
                var b = UiKit.Button(c, cats[i], tx + i * (tw + 6f), 14f, tw, 44f, () => { itemCat = k; itemPage = 0; cheatBuilt = false; }, 18f);
                UiKit.SetSelected(b, i == itemCat);
            }
            UiKit.Label(c, UiKit.T("Search", "Поиск"), 20f, 70f, 120f, 46f);
            cheatSearch = UiKit.Input(c, 140f, 70f, 520f, 46f, itemSearch, 40, false);
            searchShown = itemSearch;

            string q = (itemSearch ?? "").Trim();
            var list = new List<ItemEntry>();
            foreach (var e in ItemCatalog())
            {
                if (itemCat != 0 && e.cat != itemCat) continue;
                if (q != "" && e.name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0 && e.preset.name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                list.Add(e);
            }
            const int cols = 3, rows = 7, per = cols * rows;
            int pages = Math.Max(1, (list.Count + per - 1) / per);
            itemPage = Mathf.Clamp(itemPage, 0, pages - 1);
            UiKit.Label(c, UiKit.T($"Found: {list.Count}", $"Найдено: {list.Count}"), 680f, 70f, 260f, 46f, 20f);
            UiKit.Button(c, "<", W - 260f, 70f, 60f, 46f, () => { itemPage--; cheatBuilt = false; });
            UiKit.Label(c, (itemPage + 1) + " / " + pages, W - 196f, 70f, 112f, 46f, 20f, TMPro.TextAlignmentOptions.Midline);
            UiKit.Button(c, ">", W - 80f, 70f, 60f, 46f, () => { itemPage++; cheatBuilt = false; });

            float gy = 130f, gw = (W - 40f - 12f * (cols - 1)) / cols, gh = 48f;
            for (int i = 0; i < per; i++)
            {
                int idx = itemPage * per + i;
                if (idx >= list.Count) break;
                var e = list[idx];
                float bx = 20f + (i % cols) * (gw + 12f), by = gy + (i / cols) * (gh + 8f);
                var b = UiKit.Button(c, e.name, bx, by, gw, gh, () =>
                {
                    SpawnPreset(e.preset);
                    cheatMsg = UiKit.T("Spawned in front of you: ", "Появилось перед тобой: ") + e.name;
                }, 18f);
                if (e.cat == 1) UiKit.SetSelected(b, true);
            }

            cheatMsgLabel = UiKit.Label(c, cheatMsg, 30f, H - 48f, W - 60f, 36f, 22f, TMPro.TextAlignmentOptions.MidlineLeft, UiKit.HeadColor);
            UiKit.BarButton(w, UiKit.T("Back", "Назад"), 300f, () => { cheatPage = 0; cheatBuilt = false; });
            UiKit.BarButton(w, UiKit.T("Close", "Закрыть"), 300f, () => CloseCheatMenu());
        }

        static void SpawnPreset(InteractablePreset pr)
        {
            try
            {
                var cam = Camera.main;
                Vector3 pos = cam != null ? cam.transform.position + cam.transform.forward * 1.0f : Player.Instance.transform.position + Vector3.up;
                var it = InteractableCreator.Instance.CreateWorldInteractable(pr, null, null, null, pos, Vector3.zero, null, null, "");
                Plugin.L.LogInfo(it != null ? $"[CHEAT] создан предмет '{pr.name}' (id={it.id})" : $"[CHEAT] не удалось создать '{pr.name}'");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[CHEAT] предмет {pr.name}: {e.Message}"); }
        }

        static void RevealAllPasswords(out int codesOut, out int pcsOut)
        {
            codesOut = 0; pcsOut = 0;
            int codes = 0, pcs = 0;
            var gc = GameplayController.Instance; var city = CityData.Instance;
            if (gc == null || city == null) return;
            var known = new HashSet<string>();
            if (gc.acquiredPasscodes != null)
                foreach (var p in gc.acquiredPasscodes) if (p != null) known.Add((int)p.type + ":" + p.id);
            Action<GameplayController.Passcode> learn = p =>
            {
                if (p == null || p.digits == null || p.digits.Count == 0) return;
                if (!known.Add((int)p.type + ":" + p.id)) return;
                try { gc.AddPasscode(p, false); codes++; } catch (Exception __e) { Err.Swallow(__e); }
            };
            var kp = gc.playerKnowsPasswords;
            foreach (var kv in city.addressDictionary)
            {
                var a = kv.Value; if (a == null) continue;
                try { learn(a.passcode); } catch (Exception __e) { Err.Swallow(__e); }
                try { if (kp == null || !kp.Contains(a.id)) { gc.SetPlayerKnowsPassword(a); pcs++; } } catch (Exception __e) { Err.Swallow(__e); }
            }
            foreach (var kv in city.roomDictionary) { try { if (kv.Value != null) learn(kv.Value.passcode); } catch (Exception __e) { Err.Swallow(__e); } }
            foreach (var kv in city.citizenDictionary) { try { if (kv.Value != null) learn(kv.Value.passcode); } catch (Exception __e) { Err.Swallow(__e); } }
            var items = city.interactableDirectory;
            if (items != null)
                for (int i = 0; i < items.Count; i++) { try { var it = items[i]; if (it != null && it.passcode != null) learn(it.passcode); } catch (Exception __e) { Err.Swallow(__e); } }
            codesOut = codes; pcsOut = pcs;
            Plugin.L.LogInfo($"[CHEAT] пароли: кодов {codes}, паролей от компьютеров {pcs}");
        }
    }
}
