using BlazorApp1;
using BlazorApp1.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// In-Browser WebGPU LLM Engine (Default: Qwen2.5-0.5B-Instruct ~350MB, zero server required)
builder.Services.AddSingleton<WebLlmService>(sp =>
{
    var js = sp.GetRequiredService<IJSRuntime>();
    return new WebLlmService(js, "Qwen2.5-0.5B-Instruct-q4f16_1-MLC");
});
builder.Services.AddSingleton<ILlmService>(sp => sp.GetRequiredService<WebLlmService>());

builder.Services.AddSingleton<DocumentService>();
builder.Services.AddSingleton<SqliteStorageService>();
builder.Services.AddSingleton<VectorService>();
builder.Services.AddSingleton<ConceptService>();
builder.Services.AddSingleton<ChatService>();
builder.Services.AddSingleton<StudyService>();
builder.Services.AddSingleton<AppStateService>();

await builder.Build().RunAsync();
