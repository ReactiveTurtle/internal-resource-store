using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InternalResourceStore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "internal_resource_store");

            migrationBuilder.CreateTable(
                name: "api_keys",
                schema: "internal_resource_store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "resources",
                schema: "internal_resource_store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    owner_api_key_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    image_width = table.Column<int>(type: "integer", nullable: false),
                    image_height = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    purged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_variables",
                schema: "internal_resource_store",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_variables", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_key_hash",
                schema: "internal_resource_store",
                table: "api_keys",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_resources_deleted_at_purged_at",
                schema: "internal_resource_store",
                table: "resources",
                columns: new[] { "deleted_at", "purged_at" });

            migrationBuilder.CreateIndex(
                name: "IX_resources_owner_api_key_hash",
                schema: "internal_resource_store",
                table: "resources",
                column: "owner_api_key_hash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys",
                schema: "internal_resource_store");

            migrationBuilder.DropTable(
                name: "resources",
                schema: "internal_resource_store");

            migrationBuilder.DropTable(
                name: "system_variables",
                schema: "internal_resource_store");
        }
    }
}
