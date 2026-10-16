using System;
using System.IO;
using System.Text.Json;

namespace BUMirror.Models
{
    public class AppSettings
    {
        public string FolderA { get; set; } = string.Empty;
        public string FolderB { get; set; } = string.Empty;
        public bool Recursive { get; set; } = false;
        public string FilterSide { get; set; } = "すべて";

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BUMirror",
            "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }
    }
}
