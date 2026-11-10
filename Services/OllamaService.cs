using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorApp1.Models;

namespace BlazorApp1.Services;

public class OllamaService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _defaultModel;

    public OllamaService(HttpClient httpClient, string baseUrl = "http://127.0.0.1:11434", string defaultModel = "qwen2.5:3b")
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _defaultModel = defaultModel;
    }

    public string DefaultModel => _defaultModel;
    public string BaseUrl => _baseUrl;

    public async Task<OllamaHealthResult> CheckHealthAsync(string? modelName = null, CancellationToken ct = default)
    {
        var targetModel = modelName ?? _defaultModel;
        try
        {
            var res = await _httpClient.GetAsync($"{_baseUrl}/api/tags", ct);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var modelsList = new List<string>();
                if (doc.RootElement.TryGetProperty("models", out var modelsElem))
                {
                    foreach (var m in modelsElem.EnumerateArray())
                    {
                        if (m.TryGetProperty("name", out var nameProp))
                        {
                            var n = nameProp.GetString();
                            if (!string.IsNullOrEmpty(n)) modelsList.Add(n);
                        }
                    }
                }

                var available = modelsList.Any(m => m == targetModel || m.StartsWith(targetModel + ":") || m.StartsWith(targetModel));
                return new OllamaHealthResult
                {
                    Connected = true,
                    Model = targetModel,
                    Available = available,
                    ModelsList = modelsList
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OllamaService] CheckHealthAsync warning: {ex.Message}");
        }

        return new OllamaHealthResult
        {
            Connected = false,
            Model = targetModel,
            Available = false,
            ModelsList = new()
        };
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default)
    {
        var targetModel = model ?? _defaultModel;
        var payload = new
        {
            model = targetModel,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            stream = false,
            options = new
            {
                temperature = 0.2,
                num_ctx = 4096
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", content, ct);
        response.EnsureSuccessStatusCode();

        var resJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(resJson);
        var reply = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        return reply.Trim();
    }

    public async Task<T?> GenerateStructuredAsync<T>(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default) where T : class
    {
        var targetModel = model ?? _defaultModel;
        var jsonFormatInstruction = "\nReturn strictly valid JSON conforming to the requested schema. Do not enclose in markdown explanation.";
        var fullSystemPrompt = systemPrompt + jsonFormatInstruction;

        var text = await GenerateTextAsync(fullSystemPrompt, userPrompt, targetModel, ct);
        return CleanAndParseJson<T>(text);
    }

    public static T? CleanAndParseJson<T>(string raw) where T : class
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var clean = raw.Trim();

        // Extract markdown code blocks ```json ... ``` or ``` ... ```
        var match = System.Text.RegularExpressions.Regex.Match(clean, @"```(?:json)?\s*([\s\S]*?)\s*```");
        if (match.Success)
        {
            clean = match.Groups[1].Value.Trim();
        }
        else
        {
            var braceMatch = System.Text.RegularExpressions.Regex.Match(clean, @"(\{[\s\S]*\}|\[[\s\S]*\])");
            if (braceMatch.Success)
            {
                clean = braceMatch.Groups[1].Value.Trim();
            }
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        return JsonSerializer.Deserialize<T>(clean, options);
    }
}

public class OllamaHealthResult
{
    public bool Connected { get; set; }
    public string Model { get; set; } = string.Empty;
    public bool Available { get; set; }
    public List<string> ModelsList { get; set; } = new();
}
