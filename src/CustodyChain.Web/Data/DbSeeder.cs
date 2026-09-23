using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

/// <summary>
/// Popula perfis, tipos de vestígio e os cinco usuários de teste (um por
/// perfil), conforme previsto na Seção 8.5 do documento técnico. Só roda
/// se as tabelas estiverem vazias — idempotente entre reinícios da app.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(CustodyChainDbContext db)
    {
        if (!await db.Perfis.AnyAsync())
        {
            db.Perfis.AddRange(
                new Perfil { Id = 1, Codigo = "ADMIN", Nome = "Administrador", MetodoDid = "did:legal:admin" },
                new Perfil { Id = 2, Codigo = "CUSTODIA", Nome = "Central de Custódia", MetodoDid = "did:legal:custodian" },
                new Perfil { Id = 3, Codigo = "COLETOR", Nome = "Agente Coletor", MetodoDid = "did:legal:delegate" },
                new Perfil { Id = 4, Codigo = "PERITO", Nome = "Perito", MetodoDid = "did:legal:expert" },
                new Perfil { Id = 5, Codigo = "EXTERNO", Nome = "Órgão Externo", MetodoDid = "did:legal:judge" }
            );
            await db.SaveChangesAsync();
        }

        if (!await db.TiposVestigio.AnyAsync())
        {
            db.TiposVestigio.AddRange(
                new TipoVestigio { Categoria = CategoriaVestigio.BALISTICO, Descricao = "Arma de fogo", AreaPericial = "Balística", ExigeHash = false },
                new TipoVestigio { Categoria = CategoriaVestigio.VEICULO, Descricao = "Veículo automotor", AreaPericial = "Engenharia legal", ExigeHash = false },
                new TipoVestigio { Categoria = CategoriaVestigio.MATERIAL_GENERICO, Descricao = "Material genérico", AreaPericial = "Criminalística geral", ExigeHash = false },
                new TipoVestigio { Categoria = CategoriaVestigio.ANEXO_DIGITAL, Descricao = "Mídia digital", AreaPericial = "Computação forense", ExigeHash = true },
                new TipoVestigio { Categoria = CategoriaVestigio.DROGA, Descricao = "Substância entorpecente", AreaPericial = "Química forense", ExigeHash = false },
                new TipoVestigio { Categoria = CategoriaVestigio.PADRAO_CONFRONTO, Descricao = "Padrão de confronto", AreaPericial = "Papiloscopia", ExigeHash = false },
                new TipoVestigio { Categoria = CategoriaVestigio.SUPORTE, Descricao = "Suporte de coleta", AreaPericial = "Criminalística geral", ExigeHash = false }
            );
            await db.SaveChangesAsync();
        }

        if (!await db.Intervenientes.AnyAsync())
        {
            var agora = DateTime.UtcNow;

            db.Intervenientes.AddRange(
                new Interveniente
                {
                    Did = "did:legal:admin:teste-001",
                    PerfilId = 1,
                    Nome = "Admin de Teste",
                    Matricula = "0001",
                    Orgao = "Unifesspa",
                    Situacao = SituacaoInterveniente.ATIVO,
                    CriadoEm = agora,
                    AtivadoEm = agora
                },
                new Interveniente
                {
                    Did = "did:legal:custodian:teste-001",
                    PerfilId = 2,
                    Nome = "Custodiante de Teste",
                    Matricula = "0002",
                    Orgao = "Central de Custódia",
                    Situacao = SituacaoInterveniente.ATIVO,
                    CriadoEm = agora,
                    AtivadoEm = agora
                },
                new Interveniente
                {
                    Did = "did:legal:delegate:teste-001",
                    PerfilId = 3,
                    Nome = "Agente Coletor de Teste",
                    Matricula = "0003",
                    Orgao = "Polícia Científica",
                    Situacao = SituacaoInterveniente.ATIVO,
                    CriadoEm = agora,
                    AtivadoEm = agora
                },
                new Interveniente
                {
                    Did = "did:legal:expert:teste-001",
                    PerfilId = 4,
                    Nome = "Perito de Teste",
                    Matricula = "0004",
                    Orgao = "Instituto de Criminalística",
                    Situacao = SituacaoInterveniente.ATIVO,
                    CriadoEm = agora,
                    AtivadoEm = agora
                },
                new Interveniente
                {
                    Did = "did:legal:judge:teste-001",
                    PerfilId = 5,
                    Nome = "Órgão Externo de Teste",
                    Matricula = "0005",
                    Orgao = "Poder Judiciário",
                    Situacao = SituacaoInterveniente.ATIVO,
                    CriadoEm = agora,
                    AtivadoEm = agora
                }
            );
            await db.SaveChangesAsync();
        }

        // Não previsto no documento técnico, mas necessário: sem ao menos
        // um processo ATIVO, a T-03 (cadastro de vestígio) não tem NC
        // (Número do Caso) para associar.
        if (!await db.Processos.AnyAsync())
        {
            db.Processos.Add(new Processo
            {
                Numero = "0000001-00.2026.8.14.0000",
                NomeOperacao = "Processo de teste",
                OrgaoOrigem = "Polícia Científica",
                DataAbertura = DateOnly.FromDateTime(DateTime.UtcNow),
                Situacao = SituacaoProcesso.ATIVO,
            });
            await db.SaveChangesAsync();
        }
    }
}
