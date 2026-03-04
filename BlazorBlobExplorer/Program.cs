using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using BlazorBlobExplorer;
using BlazorBlobExplorer.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// SWA built-in auth — no MSAL, no app registration needed.
// Auth is handled by Azure Static Web Apps via /.auth/login/aad
// User info is retrieved from /.auth/me
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<SwaAuthenticationStateProvider>();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(
    sp => sp.GetRequiredService<SwaAuthenticationStateProvider>());

// Register the Azure Blob service
builder.Services.AddScoped<AzureBlobService>();
builder.Services.AddScoped<SasTokenService>();

builder.Services.AddHttpClient("AzureStorage");
builder.Services.AddHttpClient("SwaAuth", client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});

await builder.Build().RunAsync();
