using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SysmonConfigPusher.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "TimeRangeHours",
                table: "NoiseAnalysisRuns",
                type: "REAL",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.CreateIndex(
                name: "IX_NoiseResults_NoiseScore",
                table: "NoiseResults",
                column: "NoiseScore");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentJobs_CompletedAt",
                table: "DeploymentJobs",
                column: "CompletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentJobs_StartedAt",
                table: "DeploymentJobs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentJobs_Status",
                table: "DeploymentJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Configs_Hash",
                table: "Configs",
                column: "Hash");

            migrationBuilder.CreateIndex(
                name: "IX_Configs_IsActive",
                table: "Configs",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Computers_ConfigHash",
                table: "Computers",
                column: "ConfigHash");

            migrationBuilder.CreateIndex(
                name: "IX_Computers_LastInventoryScan",
                table: "Computers",
                column: "LastInventoryScan");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_User",
                table: "AuditLogs",
                column: "User");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NoiseResults_NoiseScore",
                table: "NoiseResults");

            migrationBuilder.DropIndex(
                name: "IX_DeploymentJobs_CompletedAt",
                table: "DeploymentJobs");

            migrationBuilder.DropIndex(
                name: "IX_DeploymentJobs_StartedAt",
                table: "DeploymentJobs");

            migrationBuilder.DropIndex(
                name: "IX_DeploymentJobs_Status",
                table: "DeploymentJobs");

            migrationBuilder.DropIndex(
                name: "IX_Configs_Hash",
                table: "Configs");

            migrationBuilder.DropIndex(
                name: "IX_Configs_IsActive",
                table: "Configs");

            migrationBuilder.DropIndex(
                name: "IX_Computers_ConfigHash",
                table: "Computers");

            migrationBuilder.DropIndex(
                name: "IX_Computers_LastInventoryScan",
                table: "Computers");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_User",
                table: "AuditLogs");

            migrationBuilder.AlterColumn<int>(
                name: "TimeRangeHours",
                table: "NoiseAnalysisRuns",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "REAL");
        }
    }
}
