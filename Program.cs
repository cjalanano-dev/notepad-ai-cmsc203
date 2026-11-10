using BlazorApp1;
using BlazorApp1.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton<OllamaService>(sp =>
{
    var client = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
    var nav = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
    var hostUri = new Uri(nav.BaseUri);
    var host = string.IsNullOrWhiteSpace(hostUri.Host) ? "127.0.0.1" : hostUri.Host;
    var ollamaUrl = $"http://{host}:11434";
    return new OllamaService(client, ollamaUrl, "qwen2.5:3b");
});
builder.Services.AddSingleton<DocumentService>();
builder.Services.AddSingleton<SqliteStorageService>();
builder.Services.AddSingleton<VectorService>();
builder.Services.AddSingleton<ConceptService>();
builder.Services.AddSingleton<ChatService>();
builder.Services.AddSingleton<StudyService>();
builder.Services.AddSingleton<AppStateService>();

await builder.Build().RunAsync();
