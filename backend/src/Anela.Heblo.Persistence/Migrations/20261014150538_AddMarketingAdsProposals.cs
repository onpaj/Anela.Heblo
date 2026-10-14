using System;
using Anela.Heblo.Persistence.MarketingAds;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Anela.Heblo.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingAdsProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdAutonomySettings",
                schema: "public",
                columns: table => new
                {
                    Platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActionType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdAutonomySettings", x => new { x.Platform, x.ActionType });
                });

            migrationBuilder.CreateTable(
                name: "AdKillSwitches",
                schema: "public",
                columns: table => new
                {
                    Scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExecutionEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdKillSwitches", x => x.Scope);
                });

            migrationBuilder.CreateTable(
                name: "AdLimitSettings",
                schema: "public",
                columns: table => new
                {
                    Platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActionType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdLimitSettings", x => new { x.Platform, x.ActionType, x.Key });
                });

            migrationBuilder.CreateTable(
                name: "AdProposals",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CurrentVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AccountExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActionType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByPrincipal = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedByKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AgentRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    ExecutionStartedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    ExecutionFinishedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    RevertedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdProposals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentRuns",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Principal = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    DataScopesReadJson = table.Column<string>(type: "jsonb", nullable: false),
                    ProposalsCreated = table.Column<int>(type: "integer", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    CostCzk = table.Column<decimal>(type: "numeric(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdAuditEvents",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProposalId = table.Column<int>(type: "integer", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: true),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorPrincipal = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActorKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdAuditEvents_AdProposals_ProposalId",
                        column: x => x.ProposalId,
                        principalSchema: "public",
                        principalTable: "AdProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdProposalActionExecutions",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProposalId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ActionIndex = table.Column<int>(type: "integer", nullable: false),
                    Platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AccountExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActionType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BeforeValue = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BeforeRawJson = table.Column<string>(type: "text", nullable: true),
                    AfterValue = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PlatformResourceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PlatformResponseJson = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp", nullable: false),
                    RevertState = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RevertedAt = table.Column<DateTime>(type: "timestamp", nullable: true),
                    RevertResponseJson = table.Column<string>(type: "text", nullable: true),
                    RevertError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdProposalActionExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdProposalActionExecutions_AdProposals_ProposalId",
                        column: x => x.ProposalId,
                        principalSchema: "public",
                        principalTable: "AdProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdProposalApprovals",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProposalId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Principal = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    At = table.Column<DateTime>(type: "timestamp", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdProposalApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdProposalApprovals_AdProposals_ProposalId",
                        column: x => x.ProposalId,
                        principalSchema: "public",
                        principalTable: "AdProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdProposalVersions",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProposalId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ActionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ActionCount = table.Column<int>(type: "integer", nullable: false),
                    Reasoning = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdProposalVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdProposalVersions_AdProposals_ProposalId",
                        column: x => x.ProposalId,
                        principalSchema: "public",
                        principalTable: "AdProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "public",
                table: "AdAutonomySettings",
                columns: new[] { "ActionType", "Platform", "Mode", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { "AddNegativeKeyword", "GoogleAds", "ProposeOnly", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed" },
                    { "PauseAd", "GoogleAds", "ProposeOnly", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed" },
                    { "AddNegativeKeyword", "MetaAds", "ProposeOnly", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed" },
                    { "PauseAd", "MetaAds", "ProposeOnly", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed" },
                    { "AddNegativeKeyword", "Sklik", "ProposeOnly", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed" },
                    { "PauseAd", "Sklik", "ProposeOnly", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed" }
                });

            migrationBuilder.InsertData(
                schema: "public",
                table: "AdKillSwitches",
                columns: new[] { "Scope", "ExecutionEnabled", "UpdatedAt", "UpdatedBy" },
                values: new object[] { "Global", false, new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed" });

            migrationBuilder.InsertData(
                schema: "public",
                table: "AdLimitSettings",
                columns: new[] { "ActionType", "Key", "Platform", "UpdatedAt", "UpdatedBy", "Value" },
                values: new object[,]
                {
                    { "AddNegativeKeyword", "Enabled", "GoogleAds", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 1 },
                    { "AddNegativeKeyword", "MaxActionsPerProposal", "GoogleAds", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 50 },
                    { "AddNegativeKeyword", "McpMaxActionsPerProposal", "GoogleAds", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 20 },
                    { "AddNegativeKeyword", "SecondApproverAbove", "GoogleAds", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 50 },
                    { "PauseAd", "Enabled", "GoogleAds", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 1 },
                    { "PauseAd", "MaxActionsPerProposal", "GoogleAds", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 10 },
                    { "PauseAd", "MaxPausedAdsPer7Days", "GoogleAds", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 20 },
                    { "PauseAd", "McpMaxActionsPerProposal", "GoogleAds", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 3 },
                    { "PauseAd", "SecondApproverAbove", "GoogleAds", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 10 },
                    { "AddNegativeKeyword", "Enabled", "MetaAds", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 0 },
                    { "AddNegativeKeyword", "MaxActionsPerProposal", "MetaAds", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 50 },
                    { "AddNegativeKeyword", "McpMaxActionsPerProposal", "MetaAds", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 20 },
                    { "AddNegativeKeyword", "SecondApproverAbove", "MetaAds", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 50 },
                    { "PauseAd", "Enabled", "MetaAds", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 1 },
                    { "PauseAd", "MaxActionsPerProposal", "MetaAds", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 10 },
                    { "PauseAd", "MaxPausedAdsPer7Days", "MetaAds", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 20 },
                    { "PauseAd", "McpMaxActionsPerProposal", "MetaAds", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 3 },
                    { "PauseAd", "SecondApproverAbove", "MetaAds", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 10 },
                    { "AddNegativeKeyword", "Enabled", "Sklik", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 1 },
                    { "AddNegativeKeyword", "MaxActionsPerProposal", "Sklik", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 50 },
                    { "AddNegativeKeyword", "McpMaxActionsPerProposal", "Sklik", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 20 },
                    { "AddNegativeKeyword", "SecondApproverAbove", "Sklik", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 50 },
                    { "PauseAd", "Enabled", "Sklik", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 1 },
                    { "PauseAd", "MaxActionsPerProposal", "Sklik", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 10 },
                    { "PauseAd", "MaxPausedAdsPer7Days", "Sklik", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 20 },
                    { "PauseAd", "McpMaxActionsPerProposal", "Sklik", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 3 },
                    { "PauseAd", "SecondApproverAbove", "Sklik", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "seed", 10 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdAuditEvents_OccurredAt",
                schema: "public",
                table: "AdAuditEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AdAuditEvents_ProposalId",
                schema: "public",
                table: "AdAuditEvents",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_AdProposalActionExecutions_Platform_AccountExternalId_Execu~",
                schema: "public",
                table: "AdProposalActionExecutions",
                columns: new[] { "Platform", "AccountExternalId", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdProposalActionExecutions_ProposalId_Version_ActionIndex",
                schema: "public",
                table: "AdProposalActionExecutions",
                columns: new[] { "ProposalId", "Version", "ActionIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdProposalApprovals_ProposalId_Version_Principal",
                schema: "public",
                table: "AdProposalApprovals",
                columns: new[] { "ProposalId", "Version", "Principal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdProposals_AgentRunId",
                schema: "public",
                table: "AdProposals",
                column: "AgentRunId");

            migrationBuilder.CreateIndex(
                name: "IX_AdProposals_CreatedByPrincipal",
                schema: "public",
                table: "AdProposals",
                column: "CreatedByPrincipal");

            migrationBuilder.CreateIndex(
                name: "IX_AdProposals_Status_ExpiresAt",
                schema: "public",
                table: "AdProposals",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdProposalVersions_ProposalId_Version",
                schema: "public",
                table: "AdProposalVersions",
                columns: new[] { "ProposalId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_StartedAt",
                schema: "public",
                table: "AgentRuns",
                column: "StartedAt");

            migrationBuilder.Sql(MarketingAdsSql.AppendOnlyTriggersUp);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MarketingAdsSql.AppendOnlyTriggersDown);

            migrationBuilder.DropTable(
                name: "AdAuditEvents",
                schema: "public");

            migrationBuilder.DropTable(
                name: "AdAutonomySettings",
                schema: "public");

            migrationBuilder.DropTable(
                name: "AdKillSwitches",
                schema: "public");

            migrationBuilder.DropTable(
                name: "AdLimitSettings",
                schema: "public");

            migrationBuilder.DropTable(
                name: "AdProposalActionExecutions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "AdProposalApprovals",
                schema: "public");

            migrationBuilder.DropTable(
                name: "AdProposalVersions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "AgentRuns",
                schema: "public");

            migrationBuilder.DropTable(
                name: "AdProposals",
                schema: "public");
        }
    }
}
