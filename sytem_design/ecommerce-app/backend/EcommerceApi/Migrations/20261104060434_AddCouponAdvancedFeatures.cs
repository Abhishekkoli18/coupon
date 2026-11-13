using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcommerceApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCouponAdvancedFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignedUserId",
                table: "Coupons",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NotifiedAt",
                table: "Coupons",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Coupons",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_AssignedUserId",
                table: "Coupons",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_IsActive_ExpiresAt",
                table: "Coupons",
                columns: new[] { "IsActive", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_RedeemedAt",
                table: "CouponRedemptions",
                column: "RedeemedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_UserId",
                table: "CouponRedemptions",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Coupons_Users_AssignedUserId",
                table: "Coupons",
                column: "AssignedUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Coupons_Users_AssignedUserId",
                table: "Coupons");

            migrationBuilder.DropIndex(
                name: "IX_Coupons_AssignedUserId",
                table: "Coupons");

            migrationBuilder.DropIndex(
                name: "IX_Coupons_IsActive_ExpiresAt",
                table: "Coupons");

            migrationBuilder.DropIndex(
                name: "IX_CouponRedemptions_RedeemedAt",
                table: "CouponRedemptions");

            migrationBuilder.DropIndex(
                name: "IX_CouponRedemptions_UserId",
                table: "CouponRedemptions");

            migrationBuilder.DropColumn(
                name: "AssignedUserId",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "NotifiedAt",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Coupons");
        }
    }
}
