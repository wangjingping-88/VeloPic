using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using VeloPic.Core;
using Windows.Graphics.Imaging;

namespace VeloPic.App;

public sealed record SmartClassificationResult(
    string Category,
    float Confidence,
    string Evidence);

public sealed class SmartClassificationService : IDisposable
{
    private const int InputSize = 224;
    private const float MinimumConfidence = 0.28f;
    private readonly InferenceSession _session;
    private readonly string[] _labels;
    private readonly string _inputName;
    private readonly string _outputName;

    public SmartClassificationService()
    {
        var modelPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "squeezenet_model.onnx");
        var labelsPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "imagenet_classes.txt");

        if (!File.Exists(modelPath) || !File.Exists(labelsPath))
        {
            throw new FileNotFoundException("智能分类模型文件未随程序发布。", modelPath);
        }

        _session = new InferenceSession(modelPath);
        _labels = File.ReadAllLines(labelsPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        _inputName = _session.InputMetadata.Keys.First();
        _outputName = _session.OutputMetadata.Keys.First();
    }

    public async Task<SmartClassificationResult?> ClassifyAsync(ImageRecord image, CancellationToken cancellationToken = default)
    {
        var tensor = await CreateInputTensorAsync(image.FullPath, cancellationToken);
        if (tensor is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var input = NamedOnnxValue.CreateFromTensor(_inputName, tensor);
        using var outputs = _session.Run([input]);
        var scores = outputs.FirstOrDefault(x => string.Equals(x.Name, _outputName, StringComparison.Ordinal))?.AsEnumerable<float>().ToArray();
        if (scores is null || scores.Length == 0)
        {
            return null;
        }

        var probabilities = ToProbabilities(scores);
        return PickCategory(probabilities, image.FileName);
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    private static async Task<DenseTensor<float>?> CreateInputTensorAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            using var randomAccessStream = stream.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Rgba8,
                BitmapAlphaMode.Ignore,
                new BitmapTransform
                {
                    ScaledWidth = InputSize,
                    ScaledHeight = InputSize,
                    InterpolationMode = BitmapInterpolationMode.Fant,
                },
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);
            var pixels = pixelData.DetachPixelData();
            var tensor = new DenseTensor<float>([1, 3, InputSize, InputSize]);

            for (var y = 0; y < InputSize; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < InputSize; x++)
                {
                    var sourceIndex = (y * InputSize + x) * 4;
                    tensor[0, 0, y, x] = (pixels[sourceIndex] / 255f - 0.485f) / 0.229f;
                    tensor[0, 1, y, x] = (pixels[sourceIndex + 1] / 255f - 0.456f) / 0.224f;
                    tensor[0, 2, y, x] = (pixels[sourceIndex + 2] / 255f - 0.406f) / 0.225f;
                }
            }

            return tensor;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private SmartClassificationResult? PickCategory(float[] probabilities, string fileName)
    {
        var candidates = probabilities
            .Select((probability, index) => new LabelScore(GetLabel(index), probability))
            .Where(x => x.Label.Length > 0)
            .OrderByDescending(x => x.Score)
            .Take(24)
            .ToArray();

        var categoryScores = new Dictionary<string, (float Score, string Evidence)>(StringComparer.Ordinal)
        {
            ["人物"] = (0, string.Empty),
            ["城市"] = (0, string.Empty),
            ["风景"] = (0, string.Empty),
            ["自然"] = (0, string.Empty),
        };

        foreach (var candidate in candidates)
        {
            foreach (var category in CategoryKeywords)
            {
                var match = category.Value.FirstOrDefault(keyword => candidate.Label.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    continue;
                }

                var weightedScore = candidate.Score * category.Value.Count(keyword => candidate.Label.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                if (weightedScore > categoryScores[category.Key].Score)
                {
                    categoryScores[category.Key] = (weightedScore, candidate.Label);
                }
            }
        }

        var best = categoryScores.OrderByDescending(x => x.Value.Score).First();
        if (best.Value.Score >= MinimumConfidence)
        {
            return new SmartClassificationResult(best.Key, best.Value.Score, best.Value.Evidence);
        }

        var fileHint = GetFileNameHint(fileName);
        return fileHint is null ? null : new SmartClassificationResult(fileHint, 0.3f, "文件名提示");
    }

    private string GetLabel(int index)
    {
        return index >= 0 && index < _labels.Length ? _labels[index] : string.Empty;
    }

    private static float[] ToProbabilities(float[] values)
    {
        var max = values.Max();
        var exponentials = values.Select(value => MathF.Exp(value - max)).ToArray();
        var total = exponentials.Sum();
        return total <= 0 ? values : exponentials.Select(value => value / total).ToArray();
    }

    private static string? GetFileNameHint(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        if (ContainsAny(lower, ["person", "portrait", "people", "human", "girl", "boy", "woman", "man"]))
        {
            return "人物";
        }

        if (ContainsAny(lower, ["city", "street", "building", "urban", "town", "skyscraper"]))
        {
            return "城市";
        }

        if (ContainsAny(lower, ["mountain", "lake", "beach", "forest", "landscape", "sunset", "nature"]))
        {
            return "风景";
        }

        return null;
    }

    private static bool ContainsAny(string value, IEnumerable<string> keywords)
    {
        return keywords.Any(value.Contains);
    }

    private sealed record LabelScore(string Label, float Score);

    private static readonly IReadOnlyDictionary<string, string[]> CategoryKeywords = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["人物"] =
        [
            "person", "man", "woman", "boy", "girl", "bride", "groom", "athlete", "ballplayer",
            "swimmer", "diver", "skier", "boxer", "cowboy", "guitarist", "violinist", "barber",
            "scuba", "bikini", "military uniform", "jersey", "maillot"
        ],
        ["城市"] =
        [
            "street", "building", "skyscraper", "traffic light", "bridge", "church", "theater", "cinema",
            "restaurant", "store", "shop", "bus", "taxi", "car", "train", "subway", "tram", "airliner",
            "mosque", "palace", "castle", "library", "stadium", "parking meter", "streetcar", "tower",
            "fountain", "lighthouse", "trolleybus", "pickup"
        ],
        ["风景"] =
        [
            "mountain", "valley", "lake", "seashore", "cliff", "forest", "beach", "desert", "alp", "volcano",
            "geyser", "promontory", "lakeside", "sandbar", "coral reef", "rainforest", "dam", "sunset"
        ],
        ["自然"] =
        [
            "tree", "flower", "plant", "mushroom", "bird", "fish", "dog", "cat", "horse", "deer", "bear",
            "rabbit", "fox", "wolf", "tiger", "lion", "elephant", "monkey", "snake", "lizard", "turtle",
            "butterfly", "bee", "spider", "snail", "frog", "caterpillar", "coral", "sea snake", "jellyfish",
            "lobster", "crab", "starfish", "oyster", "pine", "oak", "daisy", "rose", "sunflower"
        ],
    };
}
