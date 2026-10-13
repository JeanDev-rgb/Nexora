using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionModesAndPaymentState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ContractedDurationSeconds",
                table: "Sessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "Sessions",
                type: "TEXT",
                nullable: false,
                defaultValue: "OpenEnded");

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAtUtc",
                table: "Sessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "Sessions",
                type: "TEXT",
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Sessions",
                type: "TEXT",
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.Sql("""
                UPDATE "Sessions"
                SET "Mode" = 'OpenEnded',
                    "State" = CASE WHEN "EndedAt" IS NULL THEN 'Active' ELSE 'Completed' END,
                    "PaymentStatus" = CASE WHEN EXISTS (SELECT 1 FROM "Charges" c WHERE c."SessionId" = "Sessions"."Id") THEN 'Paid' ELSE 'Pending' END,
                    "PaidAtUtc" = (SELECT c."ChargedAtUtc" FROM "Charges" c WHERE c."SessionId" = "Sessions"."Id")
                WHERE "Mode" = 'OpenEnded' OR "Mode" = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContractedDurationSeconds",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "PaidAtUtc",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Sessions");
        }
    }
}
