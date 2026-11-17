using BlazorApp1;
using BlazorApp1.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// OpenRouter Free Cloud LLM Engine (Default: google/gemma-4-26b-a4b-it:free via .env / appsettings)
builder.Services.AddSingleton<OpenRouterService>(sp =>
{
    var http = new HttpClient();
    var config = sp.GetRequiredService<IConfiguration>();
    var apiKey = config["OpenRouter:ApiKey"] ?? config["OPENROUTER_API_KEY"] ?? "";
    var model = config["OpenRouter:Model"] ?? config["OPENROUTER_MODEL"] ?? "google/gemma-4-26b-a4b-it:free";
    return new OpenRouterService(http, apiKey, model);
});
builder.Services.AddSingleton<ILlmService>(sp => sp.GetRequiredService<OpenRouterService>());

builder.Services.AddSingleton<DocumentService>();
builder.Services.AddSingleton<SqliteStorageService>();
builder.Services.AddSingleton<VectorService>();
builder.Services.AddSingleton<ConceptService>();
builder.Services.AddSingleton<ChatService>();
builder.Services.AddSingleton<StudyService>();
builder.Services.AddSingleton<AppStateService>();

await builder.Build().RunAsync();
