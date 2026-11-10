namespace BlazorApp1.Services;

public interface ILlmService
{
    string DefaultModel { get; }
    Task<LlmHealthResult> CheckHealthAsync(string? modelName = null, CancellationToken ct = default);
    Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default);
    Task<T?> GenerateStructuredAsync<T>(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default) where T : class;
}

public class LlmHealthResult
{
    public bool Connected { get; set; }
    public string Model { get; set; } = string.Empty;
    public bool Available { get; set; }
    public List<string> ModelsList { get; set; } = new();
    public string Provider { get; set; } = "Local";
    public string? StatusMessage { get; set; }
}

