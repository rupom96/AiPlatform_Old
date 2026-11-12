using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiPlatform.Infrastructure.Persistence.ErpMigrations
{
    /// <inheritdoc />
    public partial class AddProductWarranty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductWarranties",
                columns: table => new
                {
                    ProductWarrantyId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    WarrantyType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DurationMonths = table.Column<int>(type: "int", nullable: true),
                    Terms = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductWarranties", x => x.ProductWarrantyId);
                    table.ForeignKey(
                        name: "FK_ProductWarranties_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductWarranties_CreatedBy",
                table: "ProductWarranties",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ProductWarranties_ProductId_WarrantyType",
                table: "ProductWarranties",
                columns: new[] { "ProductId", "WarrantyType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductWarranties");
        }
    }
}
