using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SysmonConfigPusher.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigTagColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfigTag",
                table: "Computers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfigTag",
                table: "Computers");
        }
    }
}
