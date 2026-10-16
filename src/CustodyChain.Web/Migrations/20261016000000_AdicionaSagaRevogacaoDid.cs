using CustodyChain.Web.Data;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustodyChain.Web.Migrations;

[DbContext(typeof(CustodyChainDbContext))]
[Migration("20261016000000_AdicionaSagaRevogacaoDid")]
public partial class AdicionaSagaRevogacaoDid : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SAGA_REVOGACAO_DID",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                CommandId = table.Column<string>(type: "varchar(45)", maxLength: 45, nullable: false),
                IntervenienteId = table.Column<long>(type: "bigint", nullable: false),
                AdministradorId = table.Column<long>(type: "bigint", nullable: false),
                DidAlvo = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                CommandJson = table.Column<string>(type: "longtext", nullable: false),
                KeyId = table.Column<string>(type: "varchar(250)", maxLength: 250, nullable: false),
                Signature = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                Estado = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false),
                Tentativas = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                Erro = table.Column<string>(type: "text", nullable: true),
                CriadaEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UltimaTentativaEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ConfirmadaEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ProximaTentativaEm = table.Column<DateTime>(type: "datetime(6)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SAGA_REVOGACAO_DID", x => x.Id);
                table.ForeignKey("FK_SAGA_REVOGACAO_DID_INTERVENIENTE_AdministradorId", x => x.AdministradorId,
                    "INTERVENIENTE", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SAGA_REVOGACAO_DID_INTERVENIENTE_IntervenienteId", x => x.IntervenienteId,
                    "INTERVENIENTE", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "IX_SAGA_REVOGACAO_DID_CommandId", table: "SAGA_REVOGACAO_DID", column: "CommandId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_SAGA_REVOGACAO_DID_AdministradorId", table: "SAGA_REVOGACAO_DID", column: "AdministradorId");
        migrationBuilder.CreateIndex(name: "IX_SAGA_REVOGACAO_DID_IntervenienteId", table: "SAGA_REVOGACAO_DID", column: "IntervenienteId");
        migrationBuilder.CreateIndex(name: "IX_SAGA_REVOGACAO_DID_Estado_ProximaTentativaEm", table: "SAGA_REVOGACAO_DID", columns: new[] { "Estado", "ProximaTentativaEm" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "SAGA_REVOGACAO_DID");
}
