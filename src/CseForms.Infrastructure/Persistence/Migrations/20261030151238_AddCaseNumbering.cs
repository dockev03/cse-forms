using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CseForms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseNumbering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "case_number_prefix",
                table: "form",
                type: "character varying(6)",
                maxLength: 6,
                nullable: false,
                defaultValue: "DOS");

            migrationBuilder.CreateTable(
                name: "case_number_counter",
                columns: table => new
                {
                    prefix = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_number_counter", x => new { x.prefix, x.year });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "case_number_counter");

            migrationBuilder.DropColumn(
                name: "case_number_prefix",
                table: "form");
        }
    }
}
