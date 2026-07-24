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
        Converters =
        {
            new CategoryMapJsonConverter(),
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
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
        settings.Categories = (settings.Categories ?? new Dictionary<string, List<string>>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(
                pair => pair.Key,
                pair => (pair.Value ?? [])
                    .Where(category => !string.IsNullOrWhiteSpace(category))
                    .Select(category => category.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
        settings.CustomCategories ??= [];
        return settings;
    }

    private sealed class CategoryMapJsonConverter : JsonConverter<Dictionary<string, List<string>>>
    {
        public override Dictionary<string, List<string>> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("categories 必须是对象。");
            }

            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("categories 包含无效属性。");
                }

                var path = reader.GetString() ?? string.Empty;
                if (!reader.Read())
                {
                    throw new JsonException("categories 属性缺少值。");
                }

                var categories = new List<string>();
                if (reader.TokenType == JsonTokenType.String)
                {
                    var category = reader.GetString();
                    if (!string.IsNullOrWhiteSpace(category))
                    {
                        categories.Add(category);
                    }
                }
                else if (reader.TokenType == JsonTokenType.StartArray)
                {
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType == JsonTokenType.String)
                        {
                            var category = reader.GetString();
                            if (!string.IsNullOrWhiteSpace(category))
                            {
                                categories.Add(category);
                            }
                        }
                        else
                        {
                            reader.Skip();
                        }
                    }
                }
                else if (reader.TokenType != JsonTokenType.Null)
                {
                    reader.Skip();
                }

                result[path] = categories;
            }

            return result;
        }

        public override void Write(
            Utf8JsonWriter writer,
            Dictionary<string, List<string>> value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (path, categories) in value)
            {
                writer.WritePropertyName(path);
                writer.WriteStartArray();
                foreach (var category in categories)
                {
                    writer.WriteStringValue(category);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
    }
}
