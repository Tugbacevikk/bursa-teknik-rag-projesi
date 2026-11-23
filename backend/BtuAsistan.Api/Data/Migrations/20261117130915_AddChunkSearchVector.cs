using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace BtuAsistan.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChunkSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "chunks",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "to_tsvector('turkish'::regconfig, translate(content, 'Iİ', 'ıi'))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_chunks_search_vector",
                table: "chunks",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chunks_search_vector",
                table: "chunks");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "chunks");
        }
    }
}
