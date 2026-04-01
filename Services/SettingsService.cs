using System.Diagnostics;
using System.IO;
using MusicDownloader.Models;
using Newtonsoft.Json;

namespace MusicDownloader.Services;

/// <summary>
/// 设置持久化服务：加载/保存用户偏好设置到 settings.json
/// </summary>
public static class SettingsService
{
    private static readonly string SettingsPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    /// <summary>加载用户偏好（文件不存在时返回默认）</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonConvert.DeserializeObject<AppSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[SettingsService] 加载设置失败: {ex.Message}"); }
        return new AppSettings();
    }

    /// <summary>保存用户偏好到文件（静默失败，不打扰用户）</summary>
    public static void Save(AppSettings settings)
    {
        try
        {
            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex) { Debug.WriteLine($"[SettingsService] 保存设置失败: {ex.Message}"); }
    }
}
