using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;

namespace SodCoop
{
    internal static class Updater
    {
        internal static string Latest = "", PageUrl = "", ZipUrl = "";
        static volatile int state;
        static volatile string error = "";
        static bool offered, started;

        static string PluginDir { get { return Path.GetDirectoryName(typeof(Updater).Assembly.Location); } }

        internal static bool Newer { get { return Latest != "" && Net.CompareVersions(Latest, Plugin.ModVersion) > 0; } }

        static HttpClient Client()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("SodCoop/" + Plugin.ModVersion);
            return c;
        }

        internal static void Start()
        {
            if (started) return;
            started = true;
            foreach (var n in new[] { "SODCoop.dll.old", "LiteNetLib.dll.old" })
            {
                try { string p = Path.Combine(PluginDir, n); if (File.Exists(p)) File.Delete(p); } catch (Exception e) { Err.Swallow(e); }
            }
            if (!Plugin.CfgUpdateCheck.Value || string.IsNullOrWhiteSpace(Plugin.CfgUpdateRepo.Value)) return;
            string repo = Plugin.CfgUpdateRepo.Value.Trim();
            Task.Run(async () =>
            {
                try
                {
                    using var c = Client();
                    c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                    string json = await c.GetStringAsync("https://api.github.com/repos/" + repo + "/releases/latest");
                    using var doc = JsonDocument.Parse(json);
                    var r = doc.RootElement;
                    string tag = r.GetProperty("tag_name").GetString() ?? "";
                    PageUrl = r.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
                    if (r.TryGetProperty("assets", out var assets))
                        foreach (var a in assets.EnumerateArray())
                        {
                            string name = a.GetProperty("name").GetString() ?? "", url = a.GetProperty("browser_download_url").GetString() ?? "";
                            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && name.IndexOf("cheat", StringComparison.OrdinalIgnoreCase) < 0) { ZipUrl = url; break; }
                        }
                    Latest = tag.TrimStart('v', 'V').Trim();
                    state = 1;
                }
                catch (Exception e) { error = e.Message; state = -1; }
            });
        }

        internal static void Tick()
        {
            if (state == -1) { state = 0; Plugin.L.LogWarning("[UPDATE] не удалось проверить обновления: " + error); }
            else if (state == 1)
            {
                state = 0;
                Plugin.L.LogInfo($"[UPDATE] последняя версия на GitHub: {Latest}, у меня {Plugin.ModVersion}");
                if (Newer && !offered) { offered = true; Offer(UiKit.T("A new mod version is available", "Вышла новая версия мода")); }
            }
            else if (state == 2) { state = 0; UiKit.ShowMessage(UiKit.T("Mod updated", "Мод обновлён"), UiKit.T($"Version {Latest} is installed.\nRestart the game to use it.", $"Установлена версия {Latest}.\nПерезапусти игру, чтобы она заработала."), (UiKit.T("Quit game", "Выйти из игры"), () => Application.Quit()), ("OK", null)); }
            else if (state == 3)
            {
                state = 0;
                Plugin.L.LogWarning("[UPDATE] обновление не удалось: " + error);
                UiKit.ShowMessage(UiKit.T("Update failed", "Обновить не удалось"), UiKit.T("Download the archive from the release page and extract it into the game folder.\n", "Скачай архив со страницы релиза и распакуй в папку игры.\n") + error, (UiKit.T("Open page", "Открыть страницу"), OpenPage), ("OK", null));
            }
        }

        internal static void Offer(string title)
        {
            string text = UiKit.T($"Version {Latest} is out, you have {Plugin.ModVersion}.", $"Вышла версия {Latest}, у тебя {Plugin.ModVersion}.");
            if (ZipUrl != "") UiKit.ShowMessage(title, text, (UiKit.T("Update", "Обновить"), Download), (UiKit.T("Open page", "Открыть страницу"), OpenPage), (UiKit.T("Later", "Позже"), null));
            else UiKit.ShowMessage(title, text, (UiKit.T("Open page", "Открыть страницу"), OpenPage), (UiKit.T("Later", "Позже"), null));
        }

        internal static void OpenPage()
        {
            string url = PageUrl != "" ? PageUrl : "https://github.com/" + Plugin.CfgUpdateRepo.Value.Trim() + "/releases/latest";
            Plugin.L.LogInfo("[UPDATE] открываю " + url);
            Application.OpenURL(url);
        }

        static void Download()
        {
            string url = ZipUrl, dir = PluginDir;
            Net.Toast(UiKit.T("Downloading the update...", "Скачиваю обновление..."));
            Plugin.L.LogInfo("[UPDATE] скачиваю " + url);
            Task.Run(async () =>
            {
                try
                {
                    using var c = Client();
                    byte[] data = await c.GetByteArrayAsync(url);
                    int n = 0;
                    using (var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read))
                        foreach (var e in zip.Entries)
                        {
                            string full = e.FullName.Replace('\\', '/');
                            if (!full.EndsWith("BepInEx/plugins/" + e.Name, StringComparison.OrdinalIgnoreCase) || !e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                            string target = Path.Combine(dir, e.Name), old = target + ".old";
                            string tmp = target + ".new";
                            e.ExtractToFile(tmp, true);
                            if (File.Exists(old)) File.Delete(old);
                            if (File.Exists(target)) File.Move(target, old);
                            File.Move(tmp, target);
                            n++;
                        }
                    if (n == 0) throw new Exception("no plugin dll in the archive");
                    state = 2;
                }
                catch (Exception e) { error = e.Message; state = 3; }
            });
        }
    }
}
