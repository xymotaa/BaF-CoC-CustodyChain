using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public class CustodyChainDbContext(DbContextOptions<CustodyChainDbContext> options) : DbContext(options)
{
    public DbSet<Perfil> Perfis => Set<Perfil>();
    public DbSet<Interveniente> Intervenientes => Set<Interveniente>();
    public DbSet<Processo> Processos => Set<Processo>();
    public DbSet<TipoVestigio> TiposVestigio => Set<TipoVestigio>();
    public DbSet<Vestigio> Vestigios => Set<Vestigio>();
    public DbSet<Anexo> Anexos => Set<Anexo>();
    public DbSet<Lacre> Lacres => Set<Lacre>();
    public DbSet<Movimentacao> Movimentacoes => Set<Movimentacao>();
    public DbSet<Armazenamento> Armazenamentos => Set<Armazenamento>();
    public DbSet<Pericia> Pericias => Set<Pericia>();
    public DbSet<OperacaoAmostra> OperacoesAmostra => Set<OperacaoAmostra>();
    public DbSet<Laudo> Laudos => Set<Laudo>();
    public DbSet<Descarte> Descartes => Set<Descarte>();
    public DbSet<Credencial> Credenciais => Set<Credencial>();
    public DbSet<RegistroLedger> RegistrosLedger => Set<RegistroLedger>();
    public DbSet<LogAuditoria> LogsAuditoria => Set<LogAuditoria>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identidade

        modelBuilder.Entity<Perfil>(e =>
        {
            e.ToTable("PERFIL");
            e.HasKey(p => p.Id);
            e.Property(p => p.Codigo).HasMaxLength(20).IsRequired();
            e.HasIndex(p => p.Codigo).IsUnique();
            e.Property(p => p.Nome).HasMaxLength(60).IsRequired();
            e.Property(p => p.MetodoDid).HasMaxLength(40).IsRequired();
        });

        modelBuilder.Entity<Interveniente>(e =>
        {
            e.ToTable("INTERVENIENTE");
            e.HasKey(i => i.Id);
            e.Property(i => i.Did).HasMaxLength(200).IsRequired();
            e.HasIndex(i => i.Did).IsUnique();
            e.Property(i => i.Nome).HasMaxLength(120).IsRequired();
            e.Property(i => i.Matricula).HasMaxLength(30);
            e.Property(i => i.Orgao).HasMaxLength(80);
            e.Property(i => i.Lotacao).HasMaxLength(80);
            e.Property(i => i.Situacao).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.DidEmissor).HasMaxLength(200);
            e.Property(i => i.CriadoEm).HasColumnType("datetime(6)").IsRequired();
            e.Property(i => i.AtivadoEm).HasColumnType("datetime(6)");

            e.HasOne(i => i.Perfil)
                .WithMany(p => p.Intervenientes)
                .HasForeignKey(i => i.PerfilId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Processo e vestígio

        modelBuilder.Entity<Processo>(e =>
        {
            e.ToTable("PROCESSO");
            e.HasKey(p => p.Id);
            e.Property(p => p.Numero).HasMaxLength(40).IsRequired();
            e.HasIndex(p => p.Numero).IsUnique();
            e.Property(p => p.NomeOperacao).HasMaxLength(120);
            e.Property(p => p.OrgaoOrigem).HasMaxLength(80);
            e.Property(p => p.Situacao).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<TipoVestigio>(e =>
        {
            e.ToTable("TIPO_VESTIGIO");
            e.HasKey(t => t.Id);
            e.Property(t => t.Categoria).HasConversion<string>().HasMaxLength(30);
            e.Property(t => t.Descricao).HasMaxLength(80).IsRequired();
            e.Property(t => t.AreaPericial).HasMaxLength(60);
        });

        modelBuilder.Entity<Vestigio>(e =>
        {
            e.ToTable("VESTIGIO");
            e.HasKey(v => v.Id);
            e.Property(v => v.RotuloEvidencia).HasMaxLength(40).IsRequired();
            e.HasIndex(v => v.RotuloEvidencia).IsUnique();
            e.Property(v => v.RotuloConjunto).HasMaxLength(40).IsRequired();
            e.HasIndex(v => v.RotuloConjunto);
            e.Property(v => v.NumeroEvidencia).HasMaxLength(40);
            e.Property(v => v.Descricao).HasColumnType("text").IsRequired();
            e.Property(v => v.LocalColeta).HasMaxLength(200);
            e.Property(v => v.DataHoraColeta).HasColumnType("datetime(6)");
            e.Property(v => v.MetodoColeta).HasColumnType("text");
            e.Property(v => v.DescricaoIntercorrencia).HasColumnType("text");
            e.Property(v => v.FaseAtual).HasConversion<string>().HasMaxLength(10);
            e.Property(v => v.Estado).HasConversion<string>().HasMaxLength(30);
            e.Property(v => v.CriadoEm).HasColumnType("datetime(6)").IsRequired();
            e.Property(v => v.AtualizadoEm).HasColumnType("datetime(6)");

            e.HasOne(v => v.Processo)
                .WithMany(p => p.Vestigios)
                .HasForeignKey(v => v.ProcessoId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(v => v.TipoVestigio)
                .WithMany(t => t.Vestigios)
                .HasForeignKey(v => v.TipoVestigioId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(v => v.Criador)
                .WithMany()
                .HasForeignKey(v => v.CriadorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(v => v.CustodianteAtual)
                .WithMany()
                .HasForeignKey(v => v.CustodianteAtualId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Anexo>(e =>
        {
            e.ToTable("ANEXO");
            e.HasKey(a => a.Id);
            e.Property(a => a.Tipo).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.NomeArquivo).HasMaxLength(255).IsRequired();
            e.Property(a => a.CaminhoRelativo).HasMaxLength(500);
            e.Property(a => a.HashSha256).HasColumnType("char(64)").IsRequired();
            e.HasIndex(a => a.HashSha256);
            e.Property(a => a.Algoritmo).HasMaxLength(12);
            e.Property(a => a.EnviadoEm).HasColumnType("datetime(6)").IsRequired();

            e.HasOne(a => a.Vestigio)
                .WithMany()
                .HasForeignKey(a => a.VestigioId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(a => a.EnviadoPor)
                .WithMany()
                .HasForeignKey(a => a.EnviadoPorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Custódia

        modelBuilder.Entity<Lacre>(e =>
        {
            e.ToTable("LACRE");
            e.HasKey(l => l.Id);
            e.Property(l => l.Numero).HasMaxLength(40).IsRequired();
            e.HasIndex(l => l.Numero).IsUnique();
            e.Property(l => l.Situacao).HasConversion<string>().HasMaxLength(20);
            e.Property(l => l.AplicadoEm).HasColumnType("datetime(6)");
            e.Property(l => l.RompidoEm).HasColumnType("datetime(6)");
            e.Property(l => l.JustificativaRompimento).HasColumnType("text");

            e.HasOne(l => l.Vestigio)
                .WithMany()
                .HasForeignKey(l => l.VestigioId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(l => l.LacreAnterior)
                .WithMany()
                .HasForeignKey(l => l.LacreAnteriorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(l => l.AplicadoPor)
                .WithMany()
                .HasForeignKey(l => l.AplicadoPorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(l => l.RompidoPor)
                .WithMany()
                .HasForeignKey(l => l.RompidoPorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(l => l.FotoAnexo)
                .WithMany()
                .HasForeignKey(l => l.FotoAnexoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Movimentacao>(e =>
        {
            e.ToTable("MOVIMENTACAO");
            e.HasKey(m => m.Id);
            e.Property(m => m.Tipo).HasConversion<string>().HasMaxLength(20);
            e.Property(m => m.DataHoraSaida).HasColumnType("datetime(6)");
            e.Property(m => m.DataHoraChegada).HasColumnType("datetime(6)");
            e.Property(m => m.DescricaoIntercorrencia).HasColumnType("text");
            e.Property(m => m.CodigoRastreamento).HasMaxLength(40);
            e.Property(m => m.Situacao).HasConversion<string>().HasMaxLength(20);
            e.Property(m => m.MotivoRecusa).HasColumnType("text");

            e.HasOne(m => m.Vestigio)
                .WithMany()
                .HasForeignKey(m => m.VestigioId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(m => m.Origem)
                .WithMany()
                .HasForeignKey(m => m.OrigemId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(m => m.Destino)
                .WithMany()
                .HasForeignKey(m => m.DestinoId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(m => m.CriadoPor)
                .WithMany()
                .HasForeignKey(m => m.CriadoPorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(m => m.AprovadoPor)
                .WithMany()
                .HasForeignKey(m => m.AprovadoPorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Armazenamento>(e =>
        {
            e.ToTable("ARMAZENAMENTO");
            e.HasKey(a => a.Id);
            e.Property(a => a.Central).HasMaxLength(80);
            e.Property(a => a.Posicao).HasMaxLength(40);
            e.Property(a => a.EntradaEm).HasColumnType("datetime(6)").IsRequired();
            e.Property(a => a.SaidaEm).HasColumnType("datetime(6)");
            e.Property(a => a.Situacao).HasConversion<string>().HasMaxLength(20);

            e.HasOne(a => a.Vestigio)
                .WithMany()
                .HasForeignKey(a => a.VestigioId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(a => a.RecebidoPor)
                .WithMany()
                .HasForeignKey(a => a.RecebidoPorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Perícia

        modelBuilder.Entity<Pericia>(e =>
        {
            e.ToTable("PERICIA");
            e.HasKey(p => p.Id);
            e.Property(p => p.AreaPericial).HasMaxLength(60);
            e.Property(p => p.Prioridade).HasConversion<string>().HasMaxLength(10);
            e.Property(p => p.SolicitadaEm).HasColumnType("datetime(6)").IsRequired();
            e.Property(p => p.RecebidaEm).HasColumnType("datetime(6)");
            e.Property(p => p.ConcluidaEm).HasColumnType("datetime(6)");
            e.Property(p => p.Situacao).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.MotivoRecusa).HasColumnType("text");

            e.HasOne(p => p.Vestigio)
                .WithMany()
                .HasForeignKey(p => p.VestigioId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(p => p.Processo)
                .WithMany()
                .HasForeignKey(p => p.ProcessoId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(p => p.Perito)
                .WithMany()
                .HasForeignKey(p => p.PeritoId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(p => p.Credencial)
                .WithMany()
                .HasForeignKey(p => p.CredencialId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OperacaoAmostra>(e =>
        {
            e.ToTable("OPERACAO_AMOSTRA");
            e.HasKey(o => o.Id);
            e.Property(o => o.Tipo).HasConversion<string>().HasMaxLength(20);
            e.Property(o => o.QuantidadeDescrita).HasMaxLength(80);
            e.Property(o => o.Justificativa).HasColumnType("text").IsRequired();
            e.Property(o => o.ExecutadoEm).HasColumnType("datetime(6)").IsRequired();

            e.HasOne(o => o.Pericia)
                .WithMany(p => p.OperacoesAmostra)
                .HasForeignKey(o => o.PericiaId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(o => o.VestigioOrigem)
                .WithMany()
                .HasForeignKey(o => o.VestigioOrigemId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(o => o.VestigioResultante)
                .WithMany()
                .HasForeignKey(o => o.VestigioResultanteId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(o => o.ExecutadoPor)
                .WithMany()
                .HasForeignKey(o => o.ExecutadoPorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Laudo>(e =>
        {
            e.ToTable("LAUDO");
            e.HasKey(l => l.Id);
            e.Property(l => l.Numero).HasMaxLength(40).IsRequired();
            e.HasIndex(l => new { l.Numero, l.Versao }).IsUnique();
            e.Property(l => l.Conteudo).HasColumnType("longtext");
            e.Property(l => l.HashVestigios).HasColumnType("char(64)").IsRequired();
            e.Property(l => l.HashLaudo).HasColumnType("char(64)").IsRequired();
            e.Property(l => l.AssinaturaEd25519).HasMaxLength(128);
            e.Property(l => l.AssinadoEm).HasColumnType("datetime(6)");

            e.HasOne(l => l.Pericia)
                .WithMany(p => p.Laudos)
                .HasForeignKey(l => l.PericiaId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(l => l.LaudoAnterior)
                .WithMany()
                .HasForeignKey(l => l.LaudoAnteriorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(l => l.AssinadoPor)
                .WithMany()
                .HasForeignKey(l => l.AssinadoPorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(l => l.AnexoPdf)
                .WithMany()
                .HasForeignKey(l => l.AnexoPdfId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Encerramento

        modelBuilder.Entity<Descarte>(e =>
        {
            e.ToTable("DESCARTE");
            e.HasKey(d => d.Id);
            e.Property(d => d.Tipo).HasConversion<string>().HasMaxLength(20);
            e.Property(d => d.DidMagistrado).HasMaxLength(200).IsRequired();
            e.Property(d => d.ExecutadoEm).HasColumnType("datetime(6)");
            e.Property(d => d.Observacao).HasColumnType("text");

            e.HasOne(d => d.Vestigio)
                .WithMany()
                .HasForeignKey(d => d.VestigioId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(d => d.AutorizacaoAnexo)
                .WithMany()
                .HasForeignKey(d => d.AutorizacaoAnexoId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(d => d.SolicitadoPor)
                .WithMany()
                .HasForeignKey(d => d.SolicitadoPorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(d => d.AprovadoPor)
                .WithMany()
                .HasForeignKey(d => d.AprovadoPorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Integridade e ledger

        modelBuilder.Entity<Credencial>(e =>
        {
            e.ToTable("CREDENCIAL");
            e.HasKey(c => c.Id);
            e.Property(c => c.Tipo).HasConversion<string>().HasMaxLength(15);
            e.Property(c => c.Identificador).HasMaxLength(120).IsRequired();
            e.HasIndex(c => c.Identificador).IsUnique();
            e.Property(c => c.EmitidaEm).HasColumnType("datetime(6)").IsRequired();
            e.Property(c => c.ValidaAte).HasColumnType("datetime(6)");
            e.Property(c => c.Situacao).HasConversion<string>().HasMaxLength(15);

            e.HasOne(c => c.Titular)
                .WithMany()
                .HasForeignKey(c => c.TitularId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(c => c.Emissor)
                .WithMany()
                .HasForeignKey(c => c.EmissorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(c => c.Processo)
                .WithMany()
                .HasForeignKey(c => c.ProcessoId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(c => c.Vestigio)
                .WithMany()
                .HasForeignKey(c => c.VestigioId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RegistroLedger>(e =>
        {
            e.ToTable("REGISTRO_LEDGER");
            e.HasKey(r => r.Id);
            e.Property(r => r.EntidadeOrigem).HasMaxLength(30).IsRequired();
            e.Property(r => r.Evento).HasMaxLength(40).IsRequired();
            e.Property(r => r.PayloadJson).HasColumnType("json");
            e.Property(r => r.Estado).HasConversion<string>().HasMaxLength(15);
            e.Property(r => r.TxHash).HasMaxLength(128);
            e.Property(r => r.Erro).HasColumnType("text");
            e.Property(r => r.CriadoEm).HasColumnType("datetime(6)").IsRequired();
            e.Property(r => r.AncoradoEm).HasColumnType("datetime(6)");

            // FK polimórfica (entidade_origem + registro_origem_id): não é
            // representável como FK no MySQL. Integridade fica a cargo da
            // aplicação; a unicidade abaixo garante idempotência.
            e.HasIndex(r => new { r.EntidadeOrigem, r.RegistroOrigemId, r.Evento }).IsUnique();

            // Sustenta a fila de reprocessamento assíncrono (D-10).
            e.HasIndex(r => new { r.Estado, r.CriadoEm });

            e.HasOne(r => r.Vestigio)
                .WithMany()
                .HasForeignKey(r => r.VestigioId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LogAuditoria>(e =>
        {
            e.ToTable("LOG_AUDITORIA");
            e.HasKey(l => l.Id);
            e.Property(l => l.Acao).HasMaxLength(40).IsRequired();
            e.Property(l => l.Entidade).HasMaxLength(30).IsRequired();
            e.Property(l => l.DataHora).HasColumnType("datetime(6)").IsRequired();
            e.Property(l => l.Ip).HasMaxLength(45);
            e.Property(l => l.UserAgent).HasMaxLength(255);
            e.Property(l => l.HashAnterior).HasColumnType("char(64)").IsRequired();
            e.Property(l => l.HashRegistro).HasColumnType("char(64)").IsRequired();
            e.HasIndex(l => l.HashRegistro).IsUnique();

            e.HasOne(l => l.Interveniente)
                .WithMany()
                .HasForeignKey(l => l.IntervenienteId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
