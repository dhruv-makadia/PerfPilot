using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerfPilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginHeaders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LoginHeaders",
                table: "Environments",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LoginHeaders",
                table: "Environments");
        }
    }
}
