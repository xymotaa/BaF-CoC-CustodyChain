using CustodyChain.Web.Application.RevogacaoDid;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Xunit.Sdk;

namespace CustodyChain.Web.Tests;

[Trait("Category", "MySqlIntegration")]
public sealed class RevogacaoDidMySqlIntegrationTests
{
    [Fact]
    public async Task Store_serializa_criacao_idempotente_lease_e_auditoria()
    {
        var connectionString = ObterConnectionStringDeTeste();
        var id = Guid.NewGuid().ToString("N");
        var perfilId = (byte)240;
        var administradorId = 9_000_000_000L + Random.Shared.Next(1_000_000);
        var alvoId = administradorId + 1;
        var commandId = $"urn:uuid:{Guid.NewGuid()}";

        try
        {
            await using (var contexto = CriarContexto(connectionString))
            {
                contexto.Perfis.Add(new Perfil { Id = perfilId, Codigo = $"TEST_{id[..8]}", Nome = "Teste", MetodoDid = "did:legal:admin" });
                contexto.Intervenientes.AddRange(
                    new Interveniente { Id = administradorId, PerfilId = perfilId, Did = $"did:legal:admin:{id}", Nome = "Administrador", Situacao = SituacaoInterveniente.ATIVO, CriadoEm = DateTime.UtcNow },
                    new Interveniente { Id = alvoId, PerfilId = perfilId, Did = $"did:legal:expert:{id}", Nome = "Alvo", Situacao = SituacaoInterveniente.ATIVO, CriadoEm = DateTime.UtcNow });
                await contexto.SaveChangesAsync();
            }

            var nova = new NovaSagaRevogacaoDid(commandId, alvoId, administradorId, $"did:legal:expert:{id}",
                "{\"commandId\":\"teste\"}", $"did:legal:admin:{id}#key-1", "assinatura", new ContextoAuditoriaRevogacaoDid(null, null), DateTime.UtcNow);
            var criacoes = await Task.WhenAll(CriarSagaAsync(connectionString, nova), CriarSagaAsync(connectionString, nova));
            Assert.All(criacoes, saga => Assert.Equal(commandId, saga.CommandId));

            await Assert.ThrowsAsync<RevogacaoDidInvalidaException>(() =>
                CriarSagaAsync(connectionString, nova with { CommandJson = "{\"commandId\":\"divergente\"}" }));
            await using (var conflito = CriarContexto(connectionString))
                Assert.Contains(await conflito.LogsAuditoria.Where(log => log.RegistroId == alvoId).Select(log => log.Acao).ToListAsync(),
                    acao => acao == "DID_REVOGACAO_REJ_COMANDO");

            var agora = DateTime.UtcNow.AddMinutes(1);
            var leases = await Task.WhenAll(AssumirAsync(connectionString, commandId, agora), AssumirAsync(connectionString, commandId, agora));
            Assert.Single(leases, lease => lease is not null);

            await Task.WhenAll(RegistrarRejeicaoAsync(connectionString, alvoId, administradorId), RegistrarRejeicaoAsync(connectionString, alvoId, administradorId));
            await using var verificacao = CriarContexto(connectionString);
            Assert.Equal(1, await verificacao.SagasRevogacaoDid.CountAsync(s => s.CommandId == commandId));
            var logs = await verificacao.LogsAuditoria.Where(log => log.RegistroId == alvoId).OrderBy(log => log.Id).ToListAsync();
            Assert.All(logs.Skip(1), (log, indice) => Assert.Equal(logs[indice].HashRegistro, log.HashAnterior));
        }
        finally
        {
            await LimparAsync(connectionString, commandId, alvoId, administradorId, perfilId);
        }
    }

    [Fact]
    public async Task Store_audita_interveniente_inativo_apos_rollback()
    {
        var connectionString = ObterConnectionStringDeTeste();
        var id = Guid.NewGuid().ToString("N");
        var perfilId = (byte)238;
        var administradorId = 7_000_000_000L + Random.Shared.Next(1_000_000);
        var alvoId = administradorId + 1;
        var commandId = $"urn:uuid:{Guid.NewGuid()}";

        try
        {
            await using (var contexto = CriarContexto(connectionString))
            {
                contexto.Perfis.Add(new Perfil { Id = perfilId, Codigo = $"TEST_{id[..8]}", Nome = "Teste", MetodoDid = "did:legal:admin" });
                contexto.Intervenientes.AddRange(
                    new Interveniente { Id = administradorId, PerfilId = perfilId, Did = $"did:legal:admin:{id}", Nome = "Administrador", Situacao = SituacaoInterveniente.ATIVO, CriadoEm = DateTime.UtcNow },
                    new Interveniente { Id = alvoId, PerfilId = perfilId, Did = $"did:legal:expert:{id}", Nome = "Alvo", Situacao = SituacaoInterveniente.GERADO, CriadoEm = DateTime.UtcNow });
                await contexto.SaveChangesAsync();
            }

            var nova = new NovaSagaRevogacaoDid(commandId, alvoId, administradorId, $"did:legal:expert:{id}",
                "{\"commandId\":\"inativo\"}", $"did:legal:admin:{id}#key-1", "assinatura", new ContextoAuditoriaRevogacaoDid(null, null), DateTime.UtcNow);
            await Assert.ThrowsAsync<RevogacaoDidInvalidaException>(() => CriarSagaAsync(connectionString, nova));

            await using var verificacao = CriarContexto(connectionString);
            Assert.Contains(await verificacao.LogsAuditoria.Where(log => log.RegistroId == alvoId).Select(log => log.Acao).ToListAsync(),
                acao => acao == "DID_REVOGACAO_REJ_INDISP");
            Assert.Equal(0, await verificacao.SagasRevogacaoDid.CountAsync(s => s.CommandId == commandId));
        }
        finally
        {
            await LimparAsync(connectionString, commandId, alvoId, administradorId, perfilId);
        }
    }

    [Fact]
    public async Task AuditoriaEncadeada_serializa_append_concorrente()
    {
        var connectionString = ObterConnectionStringDeTeste();
        var id = Guid.NewGuid().ToString("N");
        var perfilId = (byte)239;
        var administradorId = 8_000_000_000L + Random.Shared.Next(1_000_000);
        var alvoId = administradorId + 1;
        var prefixoAcao = $"TEST_AUD_{id[..16]}";

        try
        {
            await using (var contexto = CriarContexto(connectionString))
            {
                contexto.Perfis.Add(new Perfil { Id = perfilId, Codigo = $"TEST_{id[..8]}", Nome = "Teste", MetodoDid = "did:legal:admin" });
                contexto.Intervenientes.Add(new Interveniente { Id = administradorId, PerfilId = perfilId, Did = $"did:legal:admin:{id}", Nome = "Administrador", Situacao = SituacaoInterveniente.ATIVO, CriadoEm = DateTime.UtcNow });
                await contexto.SaveChangesAsync();
            }

            await Task.WhenAll(
                AcrescentarAuditoriaAsync(connectionString, alvoId, administradorId, $"{prefixoAcao}_A"),
                AcrescentarAuditoriaAsync(connectionString, alvoId, administradorId, $"{prefixoAcao}_B"));

            await using var verificacao = CriarContexto(connectionString);
            var logs = await verificacao.LogsAuditoria.Where(log => log.Acao.StartsWith(prefixoAcao))
                .OrderBy(log => log.Id).ToListAsync();
            Assert.Equal(2, logs.Count);
            Assert.Equal(logs[0].HashRegistro, logs[1].HashAnterior);
        }
        finally
        {
            await LimparAsync(connectionString, null, alvoId, administradorId, perfilId);
        }
    }

    private static async Task<SagaRevogacaoDidDto> CriarSagaAsync(string connectionString, NovaSagaRevogacaoDid nova)
    {
        await using var contexto = CriarContexto(connectionString);
        return await new RevogacaoDidStore(contexto).CriarOuObterAsync(nova);
    }

    private static async Task<SagaRevogacaoDidDto?> AssumirAsync(string connectionString, string commandId, DateTime agora)
    {
        await using var contexto = CriarContexto(connectionString);
        return await new RevogacaoDidStore(contexto).TentarAssumirAsync(commandId, agora, TimeSpan.FromMinutes(2));
    }

    private static async Task RegistrarRejeicaoAsync(string connectionString, long alvoId, long administradorId)
    {
        await using var contexto = CriarContexto(connectionString);
        await new RevogacaoDidStore(contexto).RegistrarRejeicaoAsync(new RejeicaoRevogacaoDid(alvoId, administradorId,
            MotivoRejeicaoRevogacaoDid.ComandoInvalido, new ContextoAuditoriaRevogacaoDid(null, null), DateTime.UtcNow));
    }

    private static async Task AcrescentarAuditoriaAsync(string connectionString, long registroId, long administradorId, string acao)
    {
        await using var contexto = CriarContexto(connectionString);
        await using var bloqueio = await AuditoriaEncadeada.AdquirirAsync(contexto, CancellationToken.None);
        await using var transacao = await contexto.Database.BeginTransactionAsync();
        var agora = DateTime.UtcNow;
        await AuditoriaEncadeada.AdicionarAsync(contexto, hashAnterior =>
        {
            var conteudo = string.Join('|', hashAnterior, acao, "INTERVENIENTE", registroId, administradorId, agora.Ticks);
            return new LogAuditoria
            {
                IntervenienteId = administradorId, Acao = acao, Entidade = "INTERVENIENTE", RegistroId = registroId,
                DataHora = agora, HashAnterior = hashAnterior,
                HashRegistro = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo)))
            };
        }, CancellationToken.None);
        await contexto.SaveChangesAsync();
        await transacao.CommitAsync();
    }

    private static CustodyChainDbContext CriarContexto(string connectionString) => new(new DbContextOptionsBuilder<CustodyChainDbContext>()
        .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)).Options);

    private static string ObterConnectionStringDeTeste()
    {
        var connectionString = Environment.GetEnvironmentVariable("CUSTODYCHAIN_TEST_MYSQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw SkipException.ForSkip("Defina CUSTODYCHAIN_TEST_MYSQL_CONNECTION para um banco MySQL descartável já migrado.");
        if (!connectionString.Contains("database=", StringComparison.OrdinalIgnoreCase)
            || !connectionString.Contains("test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A conexão de integração deve apontar explicitamente para um banco descartável com 'test' no nome.");
        return connectionString;
    }

    private static async Task LimparAsync(string connectionString, string? commandId, long alvoId, long administradorId, byte perfilId)
    {
        await using var contexto = CriarContexto(connectionString);
        await contexto.LogsAuditoria.Where(log => log.RegistroId == alvoId).ExecuteDeleteAsync();
        if (commandId is not null)
            await contexto.SagasRevogacaoDid.Where(saga => saga.CommandId == commandId).ExecuteDeleteAsync();
        await contexto.Intervenientes.Where(i => i.Id == alvoId || i.Id == administradorId).ExecuteDeleteAsync();
        await contexto.Perfis.Where(perfil => perfil.Id == perfilId).ExecuteDeleteAsync();
    }
}
