using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarmonIQ.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Analyses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<string>(type: "TEXT", nullable: false),
                    PrincipleSet = table.Column<string>(type: "TEXT", nullable: false),
                    RulesVersion = table.Column<string>(type: "TEXT", nullable: false),
                    EngineVersion = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: true),
                    Grade = table.Column<string>(type: "TEXT", nullable: true),
                    InteriorsScore = table.Column<int>(type: "INTEGER", nullable: true),
                    SiteScore = table.Column<int>(type: "INTEGER", nullable: true),
                    NumerologyAdjustment = table.Column<double>(type: "REAL", nullable: true),
                    InteriorsCoverage = table.Column<double>(type: "REAL", nullable: true),
                    SiteCoverage = table.Column<double>(type: "REAL", nullable: true),
                    Confidence = table.Column<double>(type: "REAL", nullable: true),
                    CohortEvidencePath = table.Column<string>(type: "TEXT", nullable: true),
                    CohortOrientationPath = table.Column<string>(type: "TEXT", nullable: true),
                    ElementBalanceJson = table.Column<string>(type: "TEXT", nullable: true),
                    SummaryText = table.Column<string>(type: "TEXT", nullable: true),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", nullable: true),
                    InputFingerprint = table.Column<string>(type: "TEXT", nullable: true),
                    ReportUri = table.Column<string>(type: "TEXT", nullable: true),
                    ReportSha256 = table.Column<string>(type: "TEXT", nullable: true),
                    ComputedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Analyses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EngineVersions",
                columns: table => new
                {
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    RulesVersionFengshui = table.Column<string>(type: "TEXT", nullable: false),
                    RulesVersionVastu = table.Column<string>(type: "TEXT", nullable: false),
                    PromptVersion = table.Column<string>(type: "TEXT", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", nullable: false),
                    CalibrationJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineVersions", x => x.Version);
                });

            migrationBuilder.CreateTable(
                name: "InputSets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<string>(type: "TEXT", nullable: false),
                    EvidencePath = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceHashesJson = table.Column<string>(type: "TEXT", nullable: false),
                    EnvironmentJson = table.Column<string>(type: "TEXT", nullable: false),
                    OrientationJson = table.Column<string>(type: "TEXT", nullable: true),
                    NumbersJson = table.Column<string>(type: "TEXT", nullable: true),
                    InputFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InputSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Observations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<string>(type: "TEXT", nullable: false),
                    InputSetId = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceHash = table.Column<string>(type: "TEXT", nullable: false),
                    PromptVersion = table.Column<string>(type: "TEXT", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Observations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectionRows",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ListingId = table.Column<string>(type: "TEXT", nullable: false),
                    FloorPlanId = table.Column<string>(type: "TEXT", nullable: true),
                    PrincipleSet = table.Column<string>(type: "TEXT", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: true),
                    Grade = table.Column<string>(type: "TEXT", nullable: true),
                    Cohort = table.Column<string>(type: "TEXT", nullable: true),
                    Confidence = table.Column<double>(type: "REAL", nullable: true),
                    EngineVersion = table.Column<string>(type: "TEXT", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectionRows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScoringJobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<string>(type: "TEXT", nullable: false),
                    EngineVersion = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    QueuedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    InputTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    CostUsd = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoringJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubjectOrientations",
                columns: table => new
                {
                    SubjectId = table.Column<string>(type: "TEXT", nullable: false),
                    FacingDegrees = table.Column<double>(type: "REAL", nullable: true),
                    Cardinal = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubjectOrientations", x => x.SubjectId);
                });

            migrationBuilder.CreateTable(
                name: "Subjects",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    PropertyKey = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectType = table.Column<string>(type: "TEXT", nullable: false),
                    ExternalPlanKey = table.Column<string>(type: "TEXT", nullable: true),
                    PlanName = table.Column<string>(type: "TEXT", nullable: true),
                    Beds = table.Column<int>(type: "INTEGER", nullable: true),
                    Baths = table.Column<double>(type: "REAL", nullable: true),
                    SqftMin = table.Column<int>(type: "INTEGER", nullable: true),
                    SqftMax = table.Column<int>(type: "INTEGER", nullable: true),
                    PlanImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    PlanImageHash = table.Column<string>(type: "TEXT", nullable: true),
                    ContentSignature = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subjects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Analyses_SubjectId_PrincipleSet_RulesVersion",
                table: "Analyses",
                columns: new[] { "SubjectId", "PrincipleSet", "RulesVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InputSets_InputFingerprint",
                table: "InputSets",
                column: "InputFingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_InputSets_SubjectId",
                table: "InputSets",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Observations_SubjectId_EvidenceHash_PromptVersion_ModelId",
                table: "Observations",
                columns: new[] { "SubjectId", "EvidenceHash", "PromptVersion", "ModelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionRows_ListingId_FloorPlanId_PrincipleSet",
                table: "ProjectionRows",
                columns: new[] { "ListingId", "FloorPlanId", "PrincipleSet" });

            migrationBuilder.CreateIndex(
                name: "IX_ScoringJobs_Status",
                table: "ScoringJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ScoringJobs_SubjectId_EngineVersion",
                table: "ScoringJobs",
                columns: new[] { "SubjectId", "EngineVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_ExternalPlanKey",
                table: "Subjects",
                column: "ExternalPlanKey");

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_PropertyKey",
                table: "Subjects",
                column: "PropertyKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Analyses");

            migrationBuilder.DropTable(
                name: "EngineVersions");

            migrationBuilder.DropTable(
                name: "InputSets");

            migrationBuilder.DropTable(
                name: "Observations");

            migrationBuilder.DropTable(
                name: "ProjectionRows");

            migrationBuilder.DropTable(
                name: "ScoringJobs");

            migrationBuilder.DropTable(
                name: "SubjectOrientations");

            migrationBuilder.DropTable(
                name: "Subjects");
        }
    }
}
