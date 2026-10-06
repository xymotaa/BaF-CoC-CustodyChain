using CustodyChain.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustodyChain.Web.Migrations;

[DbContext(typeof(CustodyChainDbContext))]
[Migration("20261002000000_AdicionaInscricaoDid")]
public partial class AdicionaInscricaoDid : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "INSCRICAO_DID",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                IntervenienteId = table.Column<long>(type: "bigint", nullable: false),
                EnrollmentId = table.Column<string>(type: "varchar(45)", maxLength: 45, nullable: false),
                CodigoHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                Situacao = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                ExpiraEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                CriadaEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                RegistradaEm = table.Column<DateTime>(type: "datetime(6)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_INSCRICAO_DID", x => x.Id);
                table.ForeignKey(
                    name: "FK_INSCRICAO_DID_INTERVENIENTE_IntervenienteId",
                    column: x => x.IntervenienteId,
                    principalTable: "INTERVENIENTE",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(name: "IX_INSCRICAO_DID_EnrollmentId", table: "INSCRICAO_DID", column: "EnrollmentId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_INSCRICAO_DID_IntervenienteId", table: "INSCRICAO_DID", column: "IntervenienteId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "INSCRICAO_DID");
}
