using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace BlazorApp1.Services;

public class WebLlmService : ILlmService, IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private DotNetObjectReference<WebLlmService>? _dotNetRef;
    private readonly string _defaultModel;

    public string DefaultModel => _defaultModel;
    public string StatusMessage { get; private set; } = "Not initialized";
    public double Progress { get; private set; } = 0;
    public bool IsReady { get; private set; } = false;

    public event Action? OnProgressChanged;

    public WebLlmService(IJSRuntime js, string defaultModel = "Qwen2.5-0.5B-Instruct-q4f16_1-MLC")
    {
        _js = js;
        _defaultModel = defaultModel;
    }

    private async Task EnsureModuleLoadedAsync()
    {
        if (_module == null)
        {
            _module = await _js.InvokeAsync<IJSObjectReference>("import", "./webllm-runner.js");
            _dotNetRef = DotNetObjectReference.Create(this);
        }
    }

    [JSInvokable]
    public void OnProgressUpdate(string text, double progress)
    {
        StatusMessage = text;
        Progress = progress;
        OnProgressChanged?.Invoke();
    }

    public async Task<bool> IsWebGpuSupportedAsync()
    {
        try
        {
            await EnsureModuleLoadedAsync();
            return await _module!.InvokeAsync<bool>("isWebGpuAvailable");
        }
        catch
        {
            return false;
        }
    }

    public async Task<LlmHealthResult> CheckHealthAsync(string? modelName = null, CancellationToken ct = default)
    {
        var targetModel = modelName ?? _defaultModel;
        try
        {
            await EnsureModuleLoadedAsync();
            var ready = await _module!.InvokeAsync<bool>("isReady");
            IsReady = ready;

            return new LlmHealthResult
            {
                Connected = true,
                Model = targetModel,
                Available = ready,
                ModelsList = new() { targetModel },
                Provider = "WebLLM (In-Browser WebGPU)",
                StatusMessage = ready ? "Model ready in browser memory" : StatusMessage
            };
        }
        catch (Exception ex)
        {
            return new LlmHealthResult
            {
                Connected = false,
                Model = targetModel,
                Available = false,
                ModelsList = new(),
                Provider = "WebLLM (In-Browser WebGPU)",
                StatusMessage = ex.Message
            };
        }
    }

    public async Task InitializeModelAsync(string? modelName = null)
    {
        var targetModel = modelName ?? _defaultModel;
        await EnsureModuleLoadedAsync();
        var result = await _module!.InvokeAsync<InitResult>("initWebLlm", _dotNetRef, targetModel);
        if (result.Success)
        {
            IsReady = true;
            StatusMessage = "Ready";
            OnProgressChanged?.Invoke();
        }
        else
        {
            StatusMessage = $"Init failed: {result.Message}";
            OnProgressChanged?.Invoke();
            throw new InvalidOperationException(result.Message);
        }
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default)
    {
        await EnsureModuleLoadedAsync();
        if (!IsReady)
        {
            await InitializeModelAsync(model ?? _defaultModel);
        }

        var messages = new object[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userPrompt }
        };

        var json = JsonSerializer.Serialize(messages);
        var reply = await _module!.InvokeAsync<string>("generateChat", json, 0.2, 2048);
        return (reply ?? string.Empty).Trim();
    }

    public async Task<T?> GenerateStructuredAsync<T>(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default) where T : class
    {
        var jsonPrompt = systemPrompt + "\nReturn strictly valid JSON. Do not include markdown codeblocks or extra explanations.";
        var text = await GenerateTextAsync(jsonPrompt, userPrompt, model, ct);
        return CleanAndParseJson<T>(text);
    }

    public static T? CleanAndParseJson<T>(string raw) where T : class
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var clean = raw.Trim();

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

    public async ValueTask DisposeAsync()
    {
        _dotNetRef?.Dispose();
        if (_module != null)
        {
            await _module.DisposeAsync();
        }
    }

    private class InitResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
    }
}
