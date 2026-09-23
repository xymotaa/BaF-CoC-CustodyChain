using CustodyChain.Web.Data;
using CustodyChain.Web.Services.Ledger;
using Microsoft.EntityFrameworkCore;

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
