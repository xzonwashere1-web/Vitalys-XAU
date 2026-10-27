using Newtonsoft.Json;
using System.IO;

namespace XAU.Services;

/// <summary>Remembers titles for which this installation successfully unlocked an achievement.</summary>
public static class XauUnlockHistory
{
    private static readonly object Sync = new();
    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU", "UnlockedGames.json");

    public static void Record(string titleId, string gameName)
    {
        if (string.IsNullOrWhiteSpace(titleId)) return;
        lock (Sync)
        {
            var titles = LoadUnsafe();
            titles[titleId] = gameName;
            Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
            File.WriteAllText(HistoryPath, JsonConvert.SerializeObject(titles, Formatting.Indented));
        }
    }

    public static HashSet<string> GetTitleIds()
    {
        lock (Sync)
            return LoadUnsafe().Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> LoadUnsafe()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return new Dictionary<string, string>();
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(HistoryPath))
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}
