using System;
using System.IO;
using System.IO.Compression;
using BepInEx;
using UnityEngine;

namespace SodCoop
{
    internal static class LogPack
    {
        static void Add(ZipArchive z, string path, string name)
        {
            try
            {
                if (!File.Exists(path)) return;
                using var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var dst = z.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal).Open();
                src.CopyTo(dst);
            }
            catch (Exception e) { Plugin.L.LogWarning("[LOGS] " + name + ": " + e.Message); }
        }

        internal static void Collect()
        {
            try
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Paths.BepInExRootPath;
                string zip = Path.Combine(dir, "SodCoop-logs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
                Plugin.L.LogInfo("[LOGS] собираю логи в " + zip);
                using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
                {
                    Add(z, Path.Combine(Paths.BepInExRootPath, "LogOutput.log"), "LogOutput.log");
                    Add(z, Path.Combine(Paths.BepInExRootPath, "UnityErrors.log"), "UnityErrors.log");
                    Add(z, Path.Combine(Paths.ConfigPath, "com.flyingfrogs.sodcoop.cfg"), "com.flyingfrogs.sodcoop.cfg");
                    Add(z, Path.Combine(Application.persistentDataPath, "Player.log"), "Player.log");
                    Add(z, Path.Combine(Application.persistentDataPath, "Player-prev.log"), "Player-prev.log");
                    var info = z.CreateEntry("info.txt");
                    using var w = new StreamWriter(info.Open());
                    w.WriteLine("SodCoop " + Plugin.ModVersion);
                    w.WriteLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    w.WriteLine("Game: " + Application.version + ", Unity " + Application.unityVersion);
                    w.WriteLine("OS: " + SystemInfo.operatingSystem + "; CPU: " + SystemInfo.processorType + "; GPU: " + SystemInfo.graphicsDeviceName + "; RAM: " + SystemInfo.systemMemorySize + " MB");
                    w.WriteLine("Game folder: " + Paths.GameRootPath);
                }
                try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + zip + "\""); } catch (Exception e) { Err.Swallow(e); }
                UiKit.ShowMessage(UiKit.T("Logs collected", "Логи собраны"), UiKit.T("The file is on your desktop:\n" + Path.GetFileName(zip) + "\nSend it to the mod's Discord together with a description of the problem.", "Файл лежит на рабочем столе:\n" + Path.GetFileName(zip) + "\nОтправь его в Discord мода вместе с описанием проблемы."));
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[LOGS] " + e.Message);
                UiKit.ShowMessage(UiKit.T("Could not collect logs", "Не удалось собрать логи"), e.Message);
            }
        }
    }
}
