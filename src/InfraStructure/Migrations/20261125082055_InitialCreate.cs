using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SeiRjMonitor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Numero = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DataGeracao = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Interessados = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AcessoRestrito = table.Column<bool>(type: "boolean", nullable: false),
                    PrimeiraConsultaUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UltimaConsultaUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UnidadeAtual = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "consultas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProcessoId = table.Column<long>(type: "bigint", nullable: false),
                    ConsultadoEmUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Fonte = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ProtocolosDeclarados = table.Column<int>(type: "integer", nullable: true),
                    AndamentosDeclarados = table.Column<int>(type: "integer", nullable: true),
                    QuantidadeProtocolos = table.Column<int>(type: "integer", nullable: false),
                    QuantidadeMovimentacoes = table.Column<int>(type: "integer", nullable: false),
                    UnidadeAtual = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consultas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_consultas_processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "movimentacoes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProcessoId = table.Column<long>(type: "bigint", nullable: false),
                    ConsultaId = table.Column<long>(type: "bigint", nullable: false),
                    Chave = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DataHora = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Unidade = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UnidadeRelacionada = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TipoEvento = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DetectadaEmUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movimentacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_movimentacoes_processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "protocolos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProcessoId = table.Column<long>(type: "bigint", nullable: false),
                    Chave = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Numero = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Natureza = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Data = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DataInclusao = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Unidade = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AcessoRestrito = table.Column<bool>(type: "boolean", nullable: false),
                    PrimeiraDeteccaoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UltimaDeteccaoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_protocolos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_protocolos_processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consultas_processo",
                table: "consultas",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "ix_consultas_processo_data",
                table: "consultas",
                columns: new[] { "ProcessoId", "ConsultadoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_movimentacoes_processo",
                table: "movimentacoes",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "ix_movimentacoes_processo_data",
                table: "movimentacoes",
                columns: new[] { "ProcessoId", "DataHora" });

            migrationBuilder.CreateIndex(
                name: "ux_movimentacoes_processo_chave",
                table: "movimentacoes",
                columns: new[] { "ProcessoId", "Chave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_processos_numero",
                table: "processos",
                column: "Numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_protocolos_processo",
                table: "protocolos",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "ux_protocolos_processo_chave",
                table: "protocolos",
                columns: new[] { "ProcessoId", "Chave" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consultas");

            migrationBuilder.DropTable(
                name: "movimentacoes");

            migrationBuilder.DropTable(
                name: "protocolos");

            migrationBuilder.DropTable(
                name: "processos");
        }
    }
}
