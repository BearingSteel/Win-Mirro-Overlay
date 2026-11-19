using System.IO;
using System.Text.Json;

namespace WinMirroOverlay.Services;

/// <summary>跨实例持久化的用户设置（%LOCALAPPDATA%\Win-Mirro-Overlay\settings.json）。</summary>
internal sealed class AppSettings
{
    public bool ShowOnAllDesktops { get; set; } = true;

    public double? Left { get; set; }

    public double? Top { get; set; }

    public double? Width { get; set; }

    public double? Height { get; set; }

    private static string FilePath => Path.Combine(Common.AppLog.Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (settings is not null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Common.AppLog.Warn("读取设置失败：" + ex.Message);
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Common.AppLog.Folder);
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Common.AppLog.Warn("保存设置失败：" + ex.Message);
        }
    }
}
