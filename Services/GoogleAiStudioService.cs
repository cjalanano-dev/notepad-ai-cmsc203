using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorApp1.Services;

public class GoogleAiStudioService : ILlmService
{
    private readonly HttpClient _http;
    private string _apiKey;
    private string _selectedModel;

    public static readonly List<string> AvailableModels = new()
    {
        "gemini-3.8-flash",
        "gemini-3.5-flash",
        "gemini-3-flash-preview",
        "gemma-4-31b-it",
        "gemma-4-26b-a4b-it"
    };

    private static readonly string[] FallbackChain = new[]
    {
        "gemini-3.8-flash",
        "gemini-3.5-flash",
        "gemini-3-flash-preview",
        "gemma-4-31b-it",
        "gemma-4-26b-a4b-it"
    };

    public string DefaultModel => _selectedModel;
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);
    public bool IsStandardApiKey => _apiKey.StartsWith("AIza", StringComparison.Ordinal);
    public string StatusMessage { get; private set; } = "Ready";

    public GoogleAiStudioService(HttpClient http, string apiKey = "", string defaultModel = "gemini-3.8-flash")
    {
        _http = http;
        _apiKey = apiKey?.Trim() ?? string.Empty;
        _selectedModel = NormalizeModelId(defaultModel);
        UpdateInitialStatus();
    }

    private void UpdateInitialStatus()
    {
        if (!HasApiKey)
        {
            StatusMessage = "Missing GEMINI_API_KEY in .env";
        }
        else if (!IsStandardApiKey)
        {
            StatusMessage = $"Warning: Key starts with '{(_apiKey.Length > 4 ? _apiKey[..4] : _apiKey)}...' instead of 'AIzaSy...' (Google AI Studio keys start with AIzaSy)";
        }
        else
        {
            StatusMessage = $"Connected to Google AI Studio ({_selectedModel})";
        }
    }

    public void SetModel(string modelName)
    {
        if (!string.IsNullOrWhiteSpace(modelName))
        {
            _selectedModel = NormalizeModelId(modelName);
            UpdateInitialStatus();
        }
    }

    public void SetApiKey(string apiKey)
    {
        _apiKey = apiKey?.Trim() ?? string.Empty;
        UpdateInitialStatus();
    }

    public static string NormalizeModelId(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return "gemma-4-31b-it";

        var clean = model.Trim();
        if (clean.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            clean = clean.Substring("models/".Length);

        // Map Ollama-style gemma4 tags to Google AI Studio API model codes
        return clean.ToLowerInvariant() switch
        {
            "gemma4:31b-cloud" or "gemma4:31b" or "gemma-4-31b" => "gemma-4-31b-it",
            "gemma4:26b-cloud" or "gemma4:26b" or "gemma-4-26b" => "gemma-4-26b-a4b-it",
            "gemma4:e4b" or "gemma-4-e4b" => "gemma-4-e4b-it",
            "gemma3:27b" or "gemma-3-27b" => "gemma-3-27b-it",
            _ => clean
        };
    }

    public async Task<LlmHealthResult> CheckHealthAsync(string? modelName = null, CancellationToken ct = default)
    {
        var targetModel = NormalizeModelId(modelName ?? _selectedModel);

        if (!HasApiKey)
        {
            StatusMessage = "Missing GEMINI_API_KEY in .env";
            return new LlmHealthResult
            {
                Connected = false,
                Model = targetModel,
                Available = false,
                ModelsList = AvailableModels,
                Provider = "Google AI Studio (Cloud)",
                StatusMessage = StatusMessage
            };
        }

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{targetModel}?key={Uri.EscapeDataString(_apiKey)}";
            using var response = await _http.GetAsync(url, ct);

            if (response.IsSuccessStatusCode)
            {
                StatusMessage = $"Online • {targetModel} ready on Google AI Studio";
                return new LlmHealthResult
                {
                    Connected = true,
                    Model = targetModel,
                    Available = true,
                    ModelsList = AvailableModels,
                    Provider = "Google AI Studio (Cloud)",
                    StatusMessage = StatusMessage
                };
            }

            var errBody = await response.Content.ReadAsStringAsync(ct);
            StatusMessage = $"API Error ({(int)response.StatusCode}): {ExtractErrorMessage(errBody)}";
            return new LlmHealthResult
            {
                Connected = false,
                Model = targetModel,
                Available = false,
                ModelsList = AvailableModels,
                Provider = "Google AI Studio (Cloud)",
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
                Provider = "Google AI Studio (Cloud)",
                StatusMessage = StatusMessage
            };
        }
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default)
    {
        return await SendGenerateContentAsync(systemPrompt, userPrompt, model, forceJson: false, ct);
    }

    public async Task<T?> GenerateStructuredAsync<T>(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default) where T : class
    {
        var jsonPrompt = systemPrompt + "\nReturn strictly valid JSON. Do not include markdown codeblocks or extra explanations.";
        var text = await SendGenerateContentAsync(jsonPrompt, userPrompt, model, forceJson: true, ct);
        return WebLlmService.CleanAndParseJson<T>(text);
    }

    private async Task<string> SendGenerateContentAsync(
        string systemPrompt,
        string userPrompt,
        string? model,
        bool forceJson,
        CancellationToken ct)
    {
        if (!HasApiKey)
        {
            throw new InvalidOperationException("Google AI Studio API key is not configured. Add GEMINI_API_KEY to your .env file.");
        }

        var primaryModel = NormalizeModelId(model ?? _selectedModel);

        // Build candidate list starting with the requested model, followed by fallback models
        var candidateModels = new List<string> { primaryModel };
        foreach (var fb in FallbackChain)
        {
            if (!candidateModels.Contains(fb, StringComparer.OrdinalIgnoreCase))
            {
                candidateModels.Add(fb);
            }
        }

        string lastError = "Unknown error";

        foreach (var targetModel in candidateModels)
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{targetModel}:generateContent?key={Uri.EscapeDataString(_apiKey)}";
            var isGemma = targetModel.StartsWith("gemma", StringComparison.OrdinalIgnoreCase);

            var combinedText = string.IsNullOrWhiteSpace(systemPrompt)
                ? userPrompt
                : $"System Instructions:\n{systemPrompt.Trim()}\n\nUser Request:\n{userPrompt.Trim()}";

            object payload;
            if (isGemma)
            {
                payload = new
                {
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new[] { new { text = combinedText } }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.2,
                        maxOutputTokens = 4096
                    }
                };
            }
            else
            {
                payload = new
                {
                    system_instruction = new
                    {
                        parts = new[] { new { text = systemPrompt } }
                    },
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new[] { new { text = userPrompt } }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.2,
                        maxOutputTokens = 4096,
                        responseMimeType = forceJson ? "application/json" : "text/plain"
                    }
                };
            }

            var jsonPayload = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(url, content, ct);
            var responseText = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                StatusMessage = targetModel == primaryModel
                    ? $"Ready ({targetModel})"
                    : $"Ready ({targetModel} fallback; {primaryModel} busy)";
                return ExtractCandidateText(responseText);
            }

            lastError = ExtractErrorMessage(responseText);
            var statusCode = (int)response.StatusCode;

            // If 401/403 (invalid API key), fail immediately instead of retrying other models
            if (statusCode == 401 || statusCode == 403 || responseText.Contains("ACCESS_TOKEN_TYPE_UNSUPPORTED", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = $"Invalid API key ({statusCode}): Use a Google AI Studio key starting with 'AIzaSy...'";
                throw new InvalidOperationException(StatusMessage);
            }

            // If 500 / 503 / 404 / 429 on this model, try the next model in FallbackChain
            Console.WriteLine($"[GoogleAiStudio] {targetModel} returned HTTP {statusCode} ({lastError}). Trying next fallback model...");
        }

        StatusMessage = $"Error: {lastError}";
        throw new InvalidOperationException($"Google AI Studio ({primaryModel}) error: {lastError}");
    }

    private static string ExtractCandidateText(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.ValueKind == JsonValueKind.Array &&
                candidates.GetArrayLength() > 0)
            {
                var first = candidates[0];
                if (first.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var part in parts.EnumerateArray())
                    {
                        // Skip internal thought parts if thinking mode is returned separately
                        if (part.TryGetProperty("thought", out var thoughtProp) && thoughtProp.ValueKind == JsonValueKind.True)
                        {
                            continue;
                        }

                        if (part.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                        {
                            sb.Append(textProp.GetString());
                        }
                    }
                    return sb.ToString().Trim();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GoogleAiStudioService] Failed to parse response JSON: {ex.Message}");
        }

        return string.Empty;
    }

    private static string ExtractErrorMessage(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return "Empty response from Google AI Studio API.";
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
