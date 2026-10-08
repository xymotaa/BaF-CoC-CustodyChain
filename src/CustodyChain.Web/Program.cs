using System.Reflection;
using System.Threading.RateLimiting;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.CadastroVestigio;
using CustodyChain.Web.Application.Arquivo;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.Remessa;
using CustodyChain.Web.Application.Recebimento;
using CustodyChain.Web.Application.ProcessamentoPericial;
using CustodyChain.Web.Application.DestinacaoFinal;
using CustodyChain.Web.Application.Pericias;
using CustodyChain.Web.Application.VerifiableCredentials;
using CustodyChain.Web.Data;
using CustodyChain.Web.Security;
using CustodyChain.Web.Services.Armazenamento;
using CustodyChain.Web.Services.Autenticacao;
using CustodyChain.Web.Services.Ledger;
using CustodyChain.Web.Services.VerifiableCredentials;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
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
builder.Services.AddScoped<IDarEntradaArquivo, DarEntradaArquivoUseCase>();
builder.Services.AddScoped<IEntradaArquivoStore, EntradaArquivoStore>();
builder.Services.AddScoped<ICriarRemessa, CriarRemessaUseCase>();
builder.Services.AddScoped<ICriarRemessaStore, RemessaStore>();
builder.Services.AddScoped<IRemessaOpcoesQuery, RemessaOpcoesQuery>();
builder.Services.AddScoped<IConfirmarRecebimento, ConfirmarRecebimentoUseCase>();
builder.Services.AddScoped<IRecusarRecebimento, RecusarRecebimentoUseCase>();
builder.Services.AddScoped<IRecebimentoStore, RecebimentoStore>();
builder.Services.AddScoped<IRecebimentoPendentesQuery, RecebimentoPendentesQuery>();
builder.Services.AddScoped<IReceberPericia, ReceberPericiaUseCase>();
builder.Services.AddScoped<IRecebimentoPericiaStore, RecebimentoPericiaStore>();
builder.Services.AddScoped<IRomperLacre, RomperLacreUseCase>();
builder.Services.AddScoped<IRompimentoLacreStore, RompimentoLacreStore>();
builder.Services.AddScoped<IEmitirLaudo, EmitirLaudoUseCase>();
builder.Services.AddScoped<IEmissaoLaudoStore, EmissaoLaudoStore>();
builder.Services.AddScoped<IFracionarAmostra, FracionarAmostraUseCase>();
builder.Services.AddScoped<IFracionamentoAmostraStore, FracionamentoAmostraStore>();
builder.Services.AddScoped<IUnificarAmostras, UnificarAmostrasUseCase>();
builder.Services.AddScoped<IUnificacaoAmostrasStore, UnificacaoAmostrasStore>();
builder.Services.AddScoped<IRegistrarConsumoOuExaurimento, RegistrarConsumoOuExaurimentoUseCase>();
builder.Services.AddScoped<IConsumoOuExaurimentoStore, ConsumoOuExaurimentoStore>();
builder.Services.AddScoped<ISolicitarDestinacao, SolicitarDestinacaoUseCase>();
builder.Services.AddScoped<IAprovarDestinacao, AprovarDestinacaoUseCase>();
builder.Services.AddScoped<IDestinacaoFinalStore, DestinacaoFinalStore>();
builder.Services.AddScoped<ISolicitarDesignacaoPericia, SolicitarDesignacaoPericiaUseCase>();
builder.Services.AddScoped<IAprovarDesignacaoPericia, AprovarDesignacaoPericiaUseCase>();
builder.Services.AddScoped<IDesignacaoPericiaStore, DesignacaoPericiaStore>();
builder.Services.AddScoped<IArmazenamentoAutorizacao, ArmazenamentoAutorizacaoIpfs>();
builder.Services.AddScoped<IArmazenamentoEvidenciaColeta, ArmazenamentoEvidenciaColetaIpfs>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IGeradorIdentificadorCredencial, GeradorIdentificadorCredencial>();
builder.Services.AddScoped<CriarDesafioAutenticacaoUseCase>();
builder.Services.AddScoped<ConcluirAutenticacaoUseCase>();
builder.Services.AddSingleton<IDesafioAutenticacaoStore, MemoryDesafioAutenticacaoStore>();
builder.Services.AddSingleton<IEmissaoVcPendenteStore, MemoryEmissaoVcPendenteStore>();
builder.Services.AddScoped<CriarVcPermissao>();
builder.Services.AddSingleton<IVerificadorAssinaturaDid, VerificadorAssinaturaEd25519>();
builder.Services.AddSingleton<IGeradorNonce, GeradorNonceCriptografico>();
builder.Services.AddScoped<IIdentidadeAutenticacaoStore, IdentidadeAutenticacaoStore>();
builder.Services.AddScoped<RevalidacaoCookieEvents>();
builder.Services.AddSingleton(new ConfiguracaoAutenticacaoDid(
    builder.Configuration["AuthenticationDid:Audience"] ?? "custodychain-web",
    TimeSpan.FromMinutes(2)));

// Cada processo do gateway usa uma única identidade Fabric. O roteador abaixo
// escolhe o cliente por DID somente no servidor; a escolha nunca vem do HTTP
// do navegador e o chaincode ainda valida o MSP da transação.
var ledgerOrg1Url = builder.Configuration["Ledger:Organizations:Org1MSP:GatewayUrl"]
    ?? builder.Configuration["Ledger:GatewayUrl"]
    ?? "http://127.0.0.1:3000";
var ledgerOrg1Token = builder.Configuration["Ledger:Organizations:Org1MSP:ServiceToken"]
    ?? builder.Configuration["Ledger:ServiceToken"];
var ledgerOrg2Url = builder.Configuration["Ledger:Organizations:Org2MSP:GatewayUrl"];
var ledgerOrg2Token = builder.Configuration["Ledger:Organizations:Org2MSP:ServiceToken"];

ConfigurarGatewayFabric(OrganizacaoFabricDid.Org1Msp, ledgerOrg1Url, ledgerOrg1Token);
ConfigurarGatewayFabric(OrganizacaoFabricDid.Org2Msp, ledgerOrg2Url, ledgerOrg2Token);
builder.Services.AddScoped<IServicoLedger, ServicoLedgerPorOrganizacao>();
builder.Services.AddHttpClient<DidRegistryFabric>(client =>
{
    client.BaseAddress = new Uri(ledgerOrg1Url);
    client.Timeout = TimeSpan.FromSeconds(10);
    DidRegistryFabric.ConfigurarAutorizacao(client, ledgerOrg1Token);
});
builder.Services.AddScoped<IDidRegistry>(services => services.GetRequiredService<DidRegistryFabric>());

void ConfigurarGatewayFabric(string mspId, string? gatewayUrl, string? serviceToken)
{
    builder.Services.AddHttpClient(ServicoLedgerPorOrganizacao.NomeCliente(mspId), client =>
    {
        if (!string.IsNullOrWhiteSpace(gatewayUrl))
        {
            client.BaseAddress = new Uri(gatewayUrl);
        }
        client.Timeout = TimeSpan.FromSeconds(30);
        DidRegistryFabric.ConfigurarAutorizacao(client, serviceToken);
    });
}

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
        options.EventsType = typeof(RevalidacaoCookieEvents);
    });

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("AutenticacaoDid", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
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
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
