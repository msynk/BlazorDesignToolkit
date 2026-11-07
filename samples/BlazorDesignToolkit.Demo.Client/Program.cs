using BlazorDesignToolkit.Demo.Client;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddDemoServices();
await builder.Build().RunAsync();
