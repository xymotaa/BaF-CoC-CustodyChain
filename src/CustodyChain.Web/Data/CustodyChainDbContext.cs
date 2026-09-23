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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
    }
}
