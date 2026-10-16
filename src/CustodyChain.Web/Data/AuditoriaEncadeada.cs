using System.Data;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

/// <summary>Serializa a criação de registros do log encadeado em todos os escritores MySQL.</summary>
public static class AuditoriaEncadeada
{
    private const string NomeBloqueio = "custodychain:log-auditoria";

    public static async Task<Bloqueio> AdquirirAsync(CustodyChainDbContext db, CancellationToken cancellationToken)
    {
        var conexao = db.Database.GetDbConnection();
        var deveFechar = conexao.State != ConnectionState.Open;
        if (deveFechar) await db.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var comando = conexao.CreateCommand();
            comando.CommandText = "SELECT GET_LOCK('custodychain:log-auditoria', 10)";
            var adquirido = Convert.ToInt32(await comando.ExecuteScalarAsync(cancellationToken));
            if (adquirido == 1) return new Bloqueio(db, deveFechar);
        }
        catch
        {
            if (deveFechar) await db.Database.CloseConnectionAsync();
            throw;
        }

        if (deveFechar) await db.Database.CloseConnectionAsync();
        throw new InvalidOperationException("Não foi possível adquirir o bloqueio da auditoria encadeada.");
    }

    public static async Task AdicionarAsync(
        CustodyChainDbContext db,
        Func<string, LogAuditoria> criarRegistro,
        CancellationToken cancellationToken)
    {
        var hashAnterior = await db.LogsAuditoria.OrderByDescending(log => log.Id)
            .Select(log => log.HashRegistro).FirstOrDefaultAsync(cancellationToken) ?? new string('0', 64);
        db.LogsAuditoria.Add(criarRegistro(hashAnterior));
    }

    public sealed class Bloqueio(CustodyChainDbContext db, bool deveFechar) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var comando = db.Database.GetDbConnection().CreateCommand();
                comando.CommandText = "SELECT RELEASE_LOCK('custodychain:log-auditoria')";
                await comando.ExecuteScalarAsync();
            }
            finally
            {
                if (deveFechar) await db.Database.CloseConnectionAsync();
            }
        }
    }
}
