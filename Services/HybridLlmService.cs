namespace BlazorApp1.Services;

public enum LlmProviderType
{
    WebLLM,
    Ollama
}

public class HybridLlmService : ILlmService
{
    private readonly WebLlmService _webLlm;
    private readonly OllamaService _ollama;
    private LlmProviderType _activeProvider = LlmProviderType.WebLLM;

    public HybridLlmService(WebLlmService webLlm, OllamaService ollama)
    {
        _webLlm = webLlm;
        _ollama = ollama;
    }

    public LlmProviderType ActiveProvider => _activeProvider;
    public WebLlmService WebLlm => _webLlm;
    public OllamaService Ollama => _ollama;

    public void SetProvider(LlmProviderType provider)
    {
        _activeProvider = provider;
    }

    public string DefaultModel => _activeProvider == LlmProviderType.WebLLM 
        ? _webLlm.DefaultModel 
        : _ollama.DefaultModel;

    public ILlmService ActiveService => _activeProvider == LlmProviderType.WebLLM 
        ? (ILlmService)_webLlm 
        : (ILlmService)_ollama;

    public Task<LlmHealthResult> CheckHealthAsync(string? modelName = null, CancellationToken ct = default)
    {
        return ActiveService.CheckHealthAsync(modelName, ct);
    }

    public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default)
    {
        return ActiveService.GenerateTextAsync(systemPrompt, userPrompt, model, ct);
    }

    public Task<T?> GenerateStructuredAsync<T>(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default) where T : class
    {
        return ActiveService.GenerateStructuredAsync<T>(systemPrompt, userPrompt, model, ct);
    }
}

