using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using NumDeck.Core.Models;

namespace NumDeck.Core.Config;

public sealed class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string FilePath { get; }

    public ConfigStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NumDeck", "config.json");
    }

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var cfg = Deserialize(File.ReadAllText(FilePath));
                if (cfg is not null) return Normalize(cfg);
            }
        }
        catch (Exception)
        {
            // Corrupted file: keep a copy and start fresh rather than crash.
            try { File.Copy(FilePath, FilePath + ".corrompido", overwrite: true); } catch { }
        }
        return AppConfig.CreateDefault();
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, Serialize(config));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public static string Serialize(AppConfig config) => JsonSerializer.Serialize(config, JsonOptions);

    public static AppConfig? Deserialize(string json) => JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);

    private static AppConfig Normalize(AppConfig cfg)
    {
        if (cfg.Profiles.Count == 0) cfg.Profiles.Add(new Profile { Name = "Principal" });
        if (cfg.Profiles.All(p => p.Id != cfg.ActiveProfileId)) cfg.ActiveProfileId = cfg.Profiles[0].Id;
        cfg.GlobalKeys ??= new Profile { Id = "global", Name = "Teclas globais" };
        return cfg;
    }
}
