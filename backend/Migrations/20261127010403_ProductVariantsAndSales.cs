using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class ProductVariantsAndSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "StockMovements",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SaleId",
                table: "StockMovements",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VariantId",
                table: "StockMovements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VariantLabel",
                table: "StockMovements",
                type: "character varying(63)",
                maxLength: 63,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Brand",
                table: "Products",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductVariants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Size = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Material = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    MinStock = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariants_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sales",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PaymentMethod = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    UndoneAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UndoneBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sales", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_SaleId",
                table: "StockMovements",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_VariantId",
                table: "StockMovements",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId",
                table: "ProductVariants",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CreatedAt",
                table: "Sales",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CreatedBy_CreatedAt",
                table: "Sales",
                columns: new[] { "CreatedBy", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_ProductVariants_VariantId",
                table: "StockMovements",
                column: "VariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Sales_SaleId",
                table: "StockMovements",
                column: "SaleId",
                principalTable: "Sales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // produtos existentes: uma variacao unica com o saldo e o minimo atuais; o historico passa a apontar para ela
            migrationBuilder.Sql("""
                INSERT INTO "ProductVariants" ("ProductId", "Size", "Material", "Quantity", "MinStock", "SortOrder")
                SELECT "Id", 'Único', '', "Quantity", "MinStock", 0 FROM "Products";
                UPDATE "StockMovements" m SET "VariantId" = v."Id", "VariantLabel" = v."Size"
                FROM "ProductVariants" v WHERE v."ProductId" = m."ProductId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_ProductVariants_VariantId",
                table: "StockMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Sales_SaleId",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "ProductVariants");

            migrationBuilder.DropTable(
                name: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_SaleId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_VariantId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "SaleId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "VariantLabel",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "Brand",
                table: "Products");
        }
    }
}
