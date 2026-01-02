using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ChatWithLLM.Models;

namespace ChatWithLLM.Services;

public sealed class GeminiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly double _temperature;
    private readonly int _maxOutputTokens;

    public GeminiClient(HttpClient http, string apiKey, string model = "gemini-2.0-flash", double temperature = 0.7, int maxOutputTokens = 2048)
    {
        _http = http;
        _apiKey = apiKey;
        _model = model;
        _temperature = temperature;
        _maxOutputTokens = maxOutputTokens;
    }

    public static GeminiClient CreateDefault(string model = "gemini-2.0-flash")
    {
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("GEMINI_API_KEY ortam değişkeni tanımlı değil.");

        return new GeminiClient(new HttpClient(), apiKey, model);
    }

    public async Task<string> GenerateAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

        var request = new GenerateContentRequest
        {
            SystemInstruction = new SystemInstruction
            {
                Parts =
                [
                    new Part
                    {
                        Text =
                            "Sen yardımsever ve samimi bir asistansın. Kullanıcıyla sıcak ve arkadaşça bir şekilde konuş. " +
                            "Resmi olmaktan kaçın, doğal ve içten ol. Emoji kullanabilirsin ama abartma. " +
                            "Türkçe yanıt ver. Gerektiğinde Markdown kullan (başlık, kalın/italik, kod bloğu, liste). " +
                            "Kullanıcıya ismiyle hitap edebilirsin eğer paylaşırsa. Kısa ve öz cevaplar ver ama gerektiğinde detaylı açıkla."
                    }
                ]
            },
            Contents = history
                .Where(m => !string.IsNullOrWhiteSpace(m.Text))
                .Select(m => new Content
                {
                    Role = NormalizeRole(m.Role),
                    Parts = [new Part { Text = m.Text }]
                })
                .ToList(),
            GenerationConfig = new GenerationConfig
            {
                Temperature = _temperature,
                MaxOutputTokens = _maxOutputTokens,
            }
        };

        using var response = await _http.PostAsJsonAsync(url, request, JsonOptions, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<GenerateContentResponse>(JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = body?.Error?.Message;
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(message)
                    ? $"Gemini API hatası: {response.StatusCode}"
                    : $"Gemini API hatası: {message}");
        }

        var text = body?.Candidates?
            .FirstOrDefault()?
            .Content?
            .Parts?
            .Select(p => p.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .DefaultIfEmpty(string.Empty)
            .Aggregate((a, b) => a + "\n" + b);

        return text ?? string.Empty;
    }

    private static string NormalizeRole(string role)
    {
        if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
            return "model";

        if (string.Equals(role, "model", StringComparison.OrdinalIgnoreCase))
            return "model";

        return "user";
    }

    private sealed class GenerateContentRequest
    {
        [JsonPropertyName("systemInstruction")]
        public SystemInstruction? SystemInstruction { get; set; }

        [JsonPropertyName("contents")]
        public List<Content> Contents { get; set; } = [];

        [JsonPropertyName("generationConfig")]
        public GenerationConfig? GenerationConfig { get; set; }
    }

    private sealed class SystemInstruction
    {
        [JsonPropertyName("parts")]
        public List<Part> Parts { get; set; } = [];
    }

    private sealed class Content
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [JsonPropertyName("parts")]
        public List<Part> Parts { get; set; } = [];
    }

    private sealed class Part
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private sealed class GenerationConfig
    {
        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("maxOutputTokens")]
        public int? MaxOutputTokens { get; set; }
    }

    private sealed class GenerateContentResponse
    {
        [JsonPropertyName("candidates")]
        public List<Candidate>? Candidates { get; set; }

        [JsonPropertyName("error")]
        public ApiError? Error { get; set; }
    }

    private sealed class Candidate
    {
        [JsonPropertyName("content")]
        public Content? Content { get; set; }
    }

    private sealed class ApiError
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
