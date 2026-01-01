using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SysmonConfigPusher.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentAuthToken",
                table: "Computers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentId",
                table: "Computers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AgentLastHeartbeat",
                table: "Computers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentVersion",
                table: "Computers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAgentManaged",
                table: "Computers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AgentPendingCommands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ComputerId = table.Column<int>(type: "INTEGER", nullable: false),
                    CommandId = table.Column<string>(type: "TEXT", nullable: false),
                    CommandType = table.Column<string>(type: "TEXT", nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResultStatus = table.Column<string>(type: "TEXT", nullable: true),
                    ResultMessage = table.Column<string>(type: "TEXT", nullable: true),
                    ResultPayload = table.Column<string>(type: "TEXT", nullable: true),
                    InitiatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    DeploymentJobId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentPendingCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentPendingCommands_Computers_ComputerId",
                        column: x => x.ComputerId,
                        principalTable: "Computers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AgentPendingCommands_DeploymentJobs_DeploymentJobId",
                        column: x => x.DeploymentJobId,
                        principalTable: "DeploymentJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Computers_AgentId",
                table: "Computers",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_Computers_IsAgentManaged",
                table: "Computers",
                column: "IsAgentManaged");

            migrationBuilder.CreateIndex(
                name: "IX_AgentPendingCommands_CommandId",
                table: "AgentPendingCommands",
                column: "CommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentPendingCommands_CompletedAt",
                table: "AgentPendingCommands",
                column: "CompletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AgentPendingCommands_ComputerId",
                table: "AgentPendingCommands",
                column: "ComputerId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentPendingCommands_CreatedAt",
                table: "AgentPendingCommands",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AgentPendingCommands_DeploymentJobId",
                table: "AgentPendingCommands",
                column: "DeploymentJobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentPendingCommands");

            migrationBuilder.DropIndex(
                name: "IX_Computers_AgentId",
                table: "Computers");

            migrationBuilder.DropIndex(
                name: "IX_Computers_IsAgentManaged",
                table: "Computers");

            migrationBuilder.DropColumn(
                name: "AgentAuthToken",
                table: "Computers");

            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "Computers");

            migrationBuilder.DropColumn(
                name: "AgentLastHeartbeat",
                table: "Computers");

            migrationBuilder.DropColumn(
                name: "AgentVersion",
                table: "Computers");

            migrationBuilder.DropColumn(
                name: "IsAgentManaged",
                table: "Computers");
        }
    }
}
