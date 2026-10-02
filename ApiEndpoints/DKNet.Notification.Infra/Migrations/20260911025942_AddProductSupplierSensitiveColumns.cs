using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DKNet.Notification.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddProductSupplierSensitiveColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SupplierCostPrice",
                schema: "sample",
                table: "Products",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierReferenceCode",
                schema: "sample",
                table: "Products",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SupplierCostPrice",
                schema: "sample",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SupplierReferenceCode",
                schema: "sample",
                table: "Products");
        }
    }
}
