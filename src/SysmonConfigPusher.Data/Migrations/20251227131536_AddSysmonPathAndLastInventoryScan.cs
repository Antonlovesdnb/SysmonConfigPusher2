using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SysmonConfigPusher.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSysmonPathAndLastInventoryScan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastInventoryScan",
                table: "Computers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SysmonPath",
                table: "Computers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastInventoryScan",
                table: "Computers");

            migrationBuilder.DropColumn(
                name: "SysmonPath",
                table: "Computers");
        }
    }
}
