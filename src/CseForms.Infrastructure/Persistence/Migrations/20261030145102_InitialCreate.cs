using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CseForms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "form",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    current_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    opens_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closes_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    access_code_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    max_responses = table.Column<int>(type: "integer", nullable: true),
                    retention_days = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "setting",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_setting", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "site",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "form_version",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    schema_json = table.Column<string>(type: "jsonb", nullable: false),
                    ui_json = table.Column<string>(type: "jsonb", nullable: true),
                    pdf_template_html = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_form_version_form_form_id",
                        column: x => x.form_id,
                        principalTable: "form",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submission",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    form_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    purge_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submission", x => x.id);
                    table.ForeignKey(
                        name: "fk_submission_form_version_form_version_id",
                        column: x => x.form_version_id,
                        principalTable: "form_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_form_slug",
                table: "form",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_form_version_form_id_number",
                table: "form_version",
                columns: new[] { "form_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_site_label",
                table: "site",
                column: "label",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_submission_case_number",
                table: "submission",
                column: "case_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_submission_form_version_id",
                table: "submission",
                column: "form_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_submission_purge_at",
                table: "submission",
                column: "purge_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "setting");

            migrationBuilder.DropTable(
                name: "site");

            migrationBuilder.DropTable(
                name: "submission");

            migrationBuilder.DropTable(
                name: "form_version");

            migrationBuilder.DropTable(
                name: "form");
        }
    }
}
