using Toolkit.Vault.Sqlite;
using Toolkit.Vault.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Static server rendering: components render to HTML on the server; nothing runs in the browser.
builder.Services.AddRazorComponents();

// The same vault the `vault` command and the Sandbox's auto-capture use, unless
// Vault:DatabasePath points somewhere else. Pages ask DI for IVaultStore, never for SQLite.
builder.Services.AddVaultSqlite(builder.Configuration["Vault:DatabasePath"]);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Local only: plain HTTP on localhost, so no HTTPS redirect or HSTS. Don't expose this on a
// network without adding authentication: the vault holds your private notes and conversations.
app.MapStaticAssets();
app.MapRazorComponents<App>();

app.Run();
