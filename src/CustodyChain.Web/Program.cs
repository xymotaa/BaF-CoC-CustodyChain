using System.Reflection;
using CustodyChain.Web.Data;
using CustodyChain.Web.Services.Armazenamento;
using CustodyChain.Web.Services.Ledger;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

// Licença Community: uso gratuito para o TCC (projeto sem fins
// comerciais). Exigida pelo QuestPDF desde a versão 2023.
QuestPDF.Settings.License = LicenseType.Community;

// Fontes embutidas como recurso (não dependem de fontes do sistema
// operacional em nenhum ambiente onde a aplicação rodar).
var assembly = Assembly.GetExecutingAssembly();
foreach (var nomeRecurso in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf")))
{
    using var streamFonte = assembly.GetManifestResourceStream(nomeRecurso)!;
    FontManager.RegisterFontFromStream(streamFonte);
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

var connectionString = builder.Configuration.GetConnectionString("CustodyChainDb")
    ?? throw new InvalidOperationException("Connection string 'CustodyChainDb' não configurada.");

builder.Services.AddDbContext<CustodyChainDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0))));

// LedgerFake destrava o desenvolvimento das telas sem depender do Fabric.
// Trocar por um IServicoLedger real (gateway) sem alterar controllers.
builder.Services.AddSingleton<IServicoLedger, LedgerFake>();

// Armazenamento off-chain de anexos (P-01): IPFS privado local via
// docker-compose. A API HTTP roda em 127.0.0.1:5001, não exposta fora
// do host de desenvolvimento.
var ipfsApiUrl = builder.Configuration["Ipfs:ApiUrl"] ?? "http://127.0.0.1:5001";
builder.Services.AddHttpClient<IServicoArmazenamentoArquivos, ServicoArmazenamentoIpfs>(client =>
{
    client.BaseAddress = new Uri(ipfsApiUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Sessão do interveniente autenticado. Não há senha centralizada (a senha
// protege a wallet no dispositivo); o cookie guarda só a sessão pós-login.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/entrar";
        options.LogoutPath = "/sair";
        options.AccessDeniedPath = "/entrar";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CustodyChainDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
