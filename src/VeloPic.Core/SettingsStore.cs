using System.Text.Json;
using System.Text.Json.Serialization;

namespace VeloPic.Core;

public static class SettingsStore
{
    private static readonly object SyncRoot = new();
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string DefaultPath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "VeloPic", "settings.json");
        }
    }

    public static AppSettings Load(string? path = null)
    {
        var actualPath = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
        if (!File.Exists(actualPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(actualPath);
            return Normalize(JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings());
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public static void Save(string? path, AppSettings settings)
    {
        var actualPath = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
        lock (SyncRoot)
        {
            var directory = Path.GetDirectoryName(actualPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(Normalize(settings), SerializerOptions);
            var temporaryPath = actualPath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, actualPath, overwrite: true);
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.Appearance ??= new AppearanceSettings();
        settings.Sorting ??= new SortingSettings();
        settings.Favorites ??= [];
        settings.WallpaperFavorites ??= [];
        settings.Albums ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        settings.Categories ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        settings.CustomCategories ??= [];
        return settings;
    }
}
