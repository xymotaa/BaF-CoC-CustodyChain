using System.Reflection;
using CustodyChain.Web.Application.CadastroVestigio;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.Remessa;
using CustodyChain.Web.Application.Recebimento;
using CustodyChain.Web.Data;
using CustodyChain.Web.Security;
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

builder.Services.AddScoped<ICadastrarVestigio, CadastrarVestigioUseCase>();
builder.Services.AddScoped<ICadastroVestigioStore, CadastroVestigioStore>();
builder.Services.AddScoped<ICadastroVestigioOpcoesQuery, CadastroVestigioOpcoesQuery>();
builder.Services.AddScoped<ICriarRemessa, CriarRemessaUseCase>();
builder.Services.AddScoped<ICriarRemessaStore, RemessaStore>();
builder.Services.AddScoped<IRemessaOpcoesQuery, RemessaOpcoesQuery>();
builder.Services.AddScoped<IConfirmarRecebimento, ConfirmarRecebimentoUseCase>();
builder.Services.AddScoped<IRecusarRecebimento, RecusarRecebimentoUseCase>();
builder.Services.AddScoped<IRecebimentoStore, RecebimentoStore>();
builder.Services.AddScoped<IRecebimentoPendentesQuery, RecebimentoPendentesQuery>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IGeradorIdentificadorCredencial, GeradorIdentificadorCredencial>();

// Ledger real: chama o gateway HTTP (fabric/gateway/), que fala com o
// chaincode CustodyChain via Fabric Gateway (fabric/chaincode/). Troca o
// LedgerFake usado durante o desenvolvimento das telas, sem alterar
// nenhum controller — ambos implementam o mesmo IServicoLedger.
var ledgerGatewayUrl = builder.Configuration["Ledger:GatewayUrl"] ?? "http://127.0.0.1:3000";
builder.Services.AddHttpClient<IServicoLedger, ServicoLedgerFabric>(client =>
{
    client.BaseAddress = new Uri(ledgerGatewayUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<IProcessadorOutboxLedger, ProcessadorOutboxLedger>();
builder.Services.AddHostedService<PublicadorOutboxLedgerService>();

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
        options.AccessDeniedPath = "/acesso-negado";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PoliticasAutorizacao.CadastrarVestigio, policy =>
        policy.RequireRole("COLETOR"));
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
