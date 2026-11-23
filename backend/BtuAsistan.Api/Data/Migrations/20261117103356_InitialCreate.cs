using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BtuAsistan.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            // The app account reads and writes rows but cannot change the schema. Default
            // privileges cover every table and sequence later migrations create, because
            // migrations always run as the same owner account.
            migrationBuilder.Sql("""
                GRANT USAGE ON SCHEMA public TO btu_asistan_app;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO btu_asistan_app;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    GRANT USAGE, SELECT ON SEQUENCES TO btu_asistan_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    REVOKE USAGE, SELECT ON SEQUENCES FROM btu_asistan_app;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM btu_asistan_app;
                REVOKE USAGE ON SCHEMA public FROM btu_asistan_app;
                """);
        }
    }
}
