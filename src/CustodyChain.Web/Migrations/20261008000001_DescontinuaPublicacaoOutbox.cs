using CustodyChain.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustodyChain.Web.Migrations;

[DbContext(typeof(CustodyChainDbContext))]
[Migration("20261008000001_DescontinuaPublicacaoOutbox")]
public partial class DescontinuaPublicacaoOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE `REGISTRO_LEDGER`
            SET `Estado` = 'FALHA',
                `ProcessandoEm` = NULL,
                `ProximaTentativaEm` = NULL,
                `Erro` = 'Publicação por outbox descontinuada; registro preservado sem reenvio automático.'
            WHERE `Estado` IN ('PENDENTE', 'PROCESSANDO');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
