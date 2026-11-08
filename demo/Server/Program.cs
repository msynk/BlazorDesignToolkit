using BlazorDesignToolkit.Demo.Client;
using BlazorDesignToolkit.Demo.Components;

var builder = WebApplication.CreateBuilder(args);

var razorComponents = builder.Services.AddRazorComponents();
razorComponents.AddInteractiveServerComponents()
    // The import page accepts token documents of up to DemoLimits.MaxImportBytes. In Interactive Server mode a pasted
    // document reaches the circuit in one SignalR message, and the default 32 KB limit would drop the connection.
    .AddHubOptions(o => o.MaximumReceiveMessageSize = DemoLimits.MaxImportBytes + 64 * 1024);
razorComponents.AddInteractiveWebAssemblyComponents();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDemoServices();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

// Unknown addresses render the demo's "not found" page with a 404 status instead of an empty response.
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(DemoTokens).Assembly);

app.Run();

/// <summary>Entry point marker for WebApplicationFactory in browser tests.</summary>
public partial class Program;
