using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceOneActiveSessionPerComputer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sessions_Computer_Active",
                table: "Sessions");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Computer_Active",
                table: "Sessions",
                columns: new[] { "ComputerId", "EndedAt" },
                unique: true,
                filter: "EndedAt IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sessions_Computer_Active",
                table: "Sessions");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Computer_Active",
                table: "Sessions",
                columns: new[] { "ComputerId", "EndedAt" },
                filter: "EndedAt IS NULL");
        }
    }
}
