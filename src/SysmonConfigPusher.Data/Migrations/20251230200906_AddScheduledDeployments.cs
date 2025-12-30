using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SysmonConfigPusher.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledDeployments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledDeployments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Operation = table.Column<string>(type: "TEXT", nullable: false),
                    ConfigId = table.Column<int>(type: "INTEGER", nullable: true),
                    ScheduledAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    DeploymentJobId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledDeployments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledDeployments_Configs_ConfigId",
                        column: x => x.ConfigId,
                        principalTable: "Configs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ScheduledDeployments_DeploymentJobs_DeploymentJobId",
                        column: x => x.DeploymentJobId,
                        principalTable: "DeploymentJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledDeploymentComputers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScheduledDeploymentId = table.Column<int>(type: "INTEGER", nullable: false),
                    ComputerId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledDeploymentComputers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledDeploymentComputers_Computers_ComputerId",
                        column: x => x.ComputerId,
                        principalTable: "Computers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScheduledDeploymentComputers_ScheduledDeployments_ScheduledDeploymentId",
                        column: x => x.ScheduledDeploymentId,
                        principalTable: "ScheduledDeployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledDeploymentComputers_ComputerId",
                table: "ScheduledDeploymentComputers",
                column: "ComputerId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledDeploymentComputers_ScheduledDeploymentId_ComputerId",
                table: "ScheduledDeploymentComputers",
                columns: new[] { "ScheduledDeploymentId", "ComputerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledDeployments_ConfigId",
                table: "ScheduledDeployments",
                column: "ConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledDeployments_DeploymentJobId",
                table: "ScheduledDeployments",
                column: "DeploymentJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledDeployments_ScheduledAt",
                table: "ScheduledDeployments",
                column: "ScheduledAt");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledDeployments_Status",
                table: "ScheduledDeployments",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledDeploymentComputers");

            migrationBuilder.DropTable(
                name: "ScheduledDeployments");
        }
    }
}
