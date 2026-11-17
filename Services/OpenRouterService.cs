using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BlazorApp1.Services;

public class OpenRouterService : ILlmService
{
    private readonly HttpClient _http;
    private string _apiKey;
    private string _selectedModel;

    public static readonly List<string> AvailableModels = new()
    {
        "baidu/ernie-5.5-flash-preview:free",
        "particle-ai/particle-1:free",
        "nvidia/nemotron-3.5-super-vl:free",
        "google/gemma-4-26b-a4b-it:free",
        "google/gemma-4-31b-it:free",
        "nvidia/nemotron-3-ultra-550b-a55b:free"
    };

    private static readonly string[] FastFreeFallbacks = new[]
    {
        "baidu/ernie-5.5-flash-preview:free",
        "particle-ai/particle-1:free",
        "nvidia/nemotron-3.5-super-vl:free",
        "google/gemma-4-26b-a4b-it:free"
    };

    public string DefaultModel => _selectedModel;
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);
    public string StatusMessage { get; private set; } = "Ready";

    public OpenRouterService(HttpClient http, string apiKey = "", string defaultModel = "baidu/ernie-5.5-flash-preview:free")
    {
        _http = http;
        _apiKey = apiKey?.Trim() ?? string.Empty;
        _selectedModel = NormalizeModelId(defaultModel);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (!HasApiKey)
        {
            StatusMessage = "Missing OPENROUTER_API_KEY in .env";
        }
        else
        {
            StatusMessage = $"Connected to OpenRouter ({_selectedModel})";
        }
    }

    public void SetModel(string modelName)
    {
        if (!string.IsNullOrWhiteSpace(modelName))
        {
            _selectedModel = NormalizeModelId(modelName);
            UpdateStatus();
        }
    }

    public void SetApiKey(string apiKey)
    {
        _apiKey = apiKey?.Trim() ?? string.Empty;
        UpdateStatus();
    }

    public static string NormalizeModelId(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return "google/gemma-4-26b-a4b-it:free";

        var clean = model.Trim();
        return clean.ToLowerInvariant() switch
        {
            "gemma-4-31b-it" or "gemma4:31b-cloud" or "gemma4:31b" => "google/gemma-4-31b-it:free",
            "gemma-4-26b-a4b-it" or "gemma4:26b-cloud" or "gemma4:26b" => "google/gemma-4-26b-a4b-it:free",
            _ => clean
        };
    }

    public async Task<LlmHealthResult> CheckHealthAsync(string? modelName = null, CancellationToken ct = default)
    {
        var targetModel = NormalizeModelId(modelName ?? _selectedModel);

        if (!HasApiKey)
        {
            StatusMessage = "Missing OPENROUTER_API_KEY in .env";
            return new LlmHealthResult
            {
                Connected = false,
                Model = targetModel,
                Available = false,
                ModelsList = AvailableModels,
                Provider = "OpenRouter (Free Cloud)",
                StatusMessage = StatusMessage
            };
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/auth/key");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            using var response = await _http.SendAsync(req, ct);

            if (response.IsSuccessStatusCode)
            {
                StatusMessage = $"Online • {targetModel} ready on OpenRouter";
                return new LlmHealthResult
                {
                    Connected = true,
                    Model = targetModel,
                    Available = true,
                    ModelsList = AvailableModels,
                    Provider = "OpenRouter (Free Cloud)",
                    StatusMessage = StatusMessage
                };
            }

            var errBody = await response.Content.ReadAsStringAsync(ct);
            StatusMessage = $"Auth Error ({(int)response.StatusCode}): {ExtractErrorMessage(errBody)}";
            return new LlmHealthResult
            {
                Connected = false,
                Model = targetModel,
                Available = false,
                ModelsList = AvailableModels,
                Provider = "OpenRouter (Free Cloud)",
                StatusMessage = StatusMessage
            };
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
            return new LlmHealthResult
            {
                Connected = false,
                Model = targetModel,
                Available = false,
                ModelsList = AvailableModels,
                Provider = "OpenRouter (Free Cloud)",
                StatusMessage = StatusMessage
            };
        }
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default)
    {
        return await SendChatCompletionAsync(systemPrompt, userPrompt, model, maxTokens: 1600, ct);
    }

    public async Task<T?> GenerateStructuredAsync<T>(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default) where T : class
    {
        var jsonPrompt = systemPrompt + "\nReturn strictly valid JSON. Do not include markdown codeblocks or extra explanations.";
        var text = await SendChatCompletionAsync(jsonPrompt, userPrompt, model, maxTokens: 2800, ct);
        return WebLlmService.CleanAndParseJson<T>(text);
    }

    private async Task<string> SendChatCompletionAsync(
        string systemPrompt,
        string userPrompt,
        string? model,
        int maxTokens,
        CancellationToken ct)
    {
        if (!HasApiKey)
        {
            throw new InvalidOperationException("OpenRouter API key is not configured. Add OPENROUTER_API_KEY to your .env file.");
        }

        var primaryModel = NormalizeModelId(model ?? _selectedModel);

        // OpenRouter supports up to 3 models in the 'models' array for automatic server-side fallback in a single HTTP request!
        var routeModels = new List<string> { primaryModel };
        foreach (var fb in FastFreeFallbacks)
        {
            if (routeModels.Count >= 3) break;
            if (!routeModels.Contains(fb, StringComparer.OrdinalIgnoreCase))
            {
                routeModels.Add(fb);
            }
        }

        var payload = new
        {
            model = primaryModel,
            models = routeModels.ToArray(),
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.2,
            max_tokens = maxTokens
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await _http.SendAsync(req, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var errMsg = ExtractErrorMessage(responseText);
            StatusMessage = $"Error: {errMsg}";
            throw new InvalidOperationException($"OpenRouter ({primaryModel}) error: {errMsg}");
        }

        var (replyText, actualModel) = ExtractCompletionText(responseText, primaryModel);
        StatusMessage = string.Equals(actualModel, primaryModel, StringComparison.OrdinalIgnoreCase)
            ? $"Ready ({actualModel})"
            : $"Ready (routed to {actualModel})";

        return replyText;
    }

    private static (string Text, string ModelUsed) ExtractCompletionText(string rawJson, string defaultModel)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return (string.Empty, defaultModel);

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            var modelUsed = root.TryGetProperty("model", out var mProp) && mProp.ValueKind == JsonValueKind.String
                ? mProp.GetString() ?? defaultModel
                : defaultModel;

            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var msg))
                {
                    string content = string.Empty;
                    if (msg.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
                    {
                        content = contentProp.GetString() ?? string.Empty;
                    }

                    // For reasoning models (e.g. Ernie/DeepSeek/Nemotron), if 'content' was null/empty due to token boundaries,
                    // check if reasoning or reasoning_details produced the JSON structure.
                    if (string.IsNullOrWhiteSpace(content) &&
                        msg.TryGetProperty("reasoning", out var reasonProp) &&
                        reasonProp.ValueKind == JsonValueKind.String)
                    {
                        var reasonText = reasonProp.GetString() ?? string.Empty;
                        if (reasonText.Contains("```json") || (reasonText.Contains("{") && reasonText.Contains("concepts")))
                        {
                            content = reasonText;
                        }
                    }

                    return (content.Trim(), modelUsed);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OpenRouterService] Failed to parse response JSON: {ex.Message}");
        }

        return (string.Empty, defaultModel);
    }

    private static string ExtractErrorMessage(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return "Empty response from OpenRouter API.";
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg) &&
                msg.ValueKind == JsonValueKind.String)
            {
                return msg.GetString() ?? rawJson;
            }
        }
        catch
        {
            // Ignore parse errors
        }
        return rawJson.Length > 200 ? rawJson.Substring(0, 200) + "..." : rawJson;
    }
}
