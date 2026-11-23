using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BtuAsistan.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "feedback",
                table: "messages",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "feedback_at",
                table: "messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "feedback_comment",
                table: "messages",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "feedback",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "feedback_at",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "feedback_comment",
                table: "messages");
        }
    }
}
