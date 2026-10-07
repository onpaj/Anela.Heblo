using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Anela.Heblo.Persistence.Ads.Migrations
{
    /// <inheritdoc />
    public partial class InitialAdsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ads");

            migrationBuilder.CreateTable(
                name: "ad_accounts",
                schema: "ads",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    platform = table.Column<string>(type: "text", nullable: false),
                    external_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    time_zone = table.Column<string>(type: "text", nullable: false),
                    is_managed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ad_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_state",
                schema: "ads",
                columns: table => new
                {
                    platform = table.Column<string>(type: "text", nullable: false),
                    account_external_id = table.Column<string>(type: "text", nullable: false),
                    stream = table.Column<string>(type: "text", nullable: false),
                    watermark = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: true),
                    last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_state", x => new { x.platform, x.account_external_id, x.stream });
                });

            migrationBuilder.CreateTable(
                name: "ad_entities",
                schema: "ads",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    level = table.Column<string>(type: "text", nullable: false),
                    external_id = table.Column<string>(type: "text", nullable: false),
                    parent_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    attributes = table.Column<string>(type: "jsonb", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ad_entities", x => x.id);
                    table.ForeignKey(
                        name: "FK_ad_entities_ad_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "ads",
                        principalTable: "ad_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ad_entities_ad_entities_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "ads",
                        principalTable: "ad_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ad_change_events",
                schema: "ads",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    external_event_id = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "text", nullable: true),
                    actor_kind = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<long>(type: "bigint", nullable: true),
                    entity_external_ref = table.Column<string>(type: "text", nullable: true),
                    change_type = table.Column<string>(type: "text", nullable: false),
                    old_value = table.Column<string>(type: "jsonb", nullable: true),
                    new_value = table.Column<string>(type: "jsonb", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    matched_execution_id = table.Column<string>(type: "text", nullable: true),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ad_change_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_ad_change_events_ad_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "ads",
                        principalTable: "ad_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ad_change_events_ad_entities_entity_id",
                        column: x => x.entity_id,
                        principalSchema: "ads",
                        principalTable: "ad_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ad_daily_facts",
                schema: "ads",
                columns: table => new
                {
                    entity_id = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    impressions = table.Column<long>(type: "bigint", nullable: false),
                    clicks = table.Column<long>(type: "bigint", nullable: false),
                    cost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    conversions = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    conversion_value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ad_daily_facts", x => new { x.entity_id, x.date });
                    table.ForeignKey(
                        name: "FK_ad_daily_facts_ad_entities_entity_id",
                        column: x => x.entity_id,
                        principalSchema: "ads",
                        principalTable: "ad_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ad_search_term_daily",
                schema: "ads",
                columns: table => new
                {
                    ad_group_entity_id = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    search_term = table.Column<string>(type: "text", nullable: false),
                    match_type = table.Column<string>(type: "text", nullable: false),
                    impressions = table.Column<long>(type: "bigint", nullable: false),
                    clicks = table.Column<long>(type: "bigint", nullable: false),
                    cost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    conversions = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    conversion_value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ad_search_term_daily", x => new { x.ad_group_entity_id, x.date, x.search_term, x.match_type });
                    table.ForeignKey(
                        name: "FK_ad_search_term_daily_ad_entities_ad_group_entity_id",
                        column: x => x.ad_group_entity_id,
                        principalSchema: "ads",
                        principalTable: "ad_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_ad_accounts_platform_external_id",
                schema: "ads",
                table: "ad_accounts",
                columns: new[] { "platform", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ad_change_events_entity_id",
                schema: "ads",
                table: "ad_change_events",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_ad_change_events_occurred_at",
                schema: "ads",
                table: "ad_change_events",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ux_ad_change_events_account_source_external_event_id",
                schema: "ads",
                table: "ad_change_events",
                columns: new[] { "account_id", "source", "external_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ad_daily_facts_date",
                schema: "ads",
                table: "ad_daily_facts",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_ad_entities_parent_id",
                schema: "ads",
                table: "ad_entities",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ux_ad_entities_account_level_external_id",
                schema: "ads",
                table: "ad_entities",
                columns: new[] { "account_id", "level", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ad_search_term_daily_date",
                schema: "ads",
                table: "ad_search_term_daily",
                column: "date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ad_change_events",
                schema: "ads");

            migrationBuilder.DropTable(
                name: "ad_daily_facts",
                schema: "ads");

            migrationBuilder.DropTable(
                name: "ad_search_term_daily",
                schema: "ads");

            migrationBuilder.DropTable(
                name: "sync_state",
                schema: "ads");

            migrationBuilder.DropTable(
                name: "ad_entities",
                schema: "ads");

            migrationBuilder.DropTable(
                name: "ad_accounts",
                schema: "ads");
        }
    }
}
