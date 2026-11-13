using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CseForms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditDetailAndRetentionFromProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "detail",
                table: "audit_log",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_target",
                table: "audit_log",
                column: "target");

            // La conservation court désormais à partir du traitement : une réponse jamais traitée n'a pas de date de purge.
            migrationBuilder.Sql("UPDATE submission SET purge_at = NULL WHERE status = 'new';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_log_target",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "detail",
                table: "audit_log");
        }
    }
}
