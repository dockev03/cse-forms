using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CseForms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "form_draft",
                columns: table => new
                {
                    form_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_json = table.Column<string>(type: "text", nullable: false),
                    pdf_template_html = table.Column<string>(type: "text", nullable: true),
                    saved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    saved_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_draft", x => x.form_id);
                    table.ForeignKey(
                        name: "fk_form_draft_form_form_id",
                        column: x => x.form_id,
                        principalTable: "form",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_draft");
        }
    }
}
