using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerfPilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Environments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    OpenApiUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    IsProduction = table.Column<bool>(type: "bit", nullable: false),
                    IgnoreTlsErrors = table.Column<bool>(type: "bit", nullable: false),
                    AuthType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProtectedSecret = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApiKeyHeaderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LoginUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ProtectedLoginBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LoginTokenJsonPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LoginTokenHeaderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LoginTokenPrefix = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LoginScope = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Environments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiSpecs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ApiVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SpecFormat = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceLocation = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ImportedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RawContent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Warnings = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EndpointCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiSpecs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiSpecs_Environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "Environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApiEndpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApiSpecId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    OperationId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Tag = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Deprecated = table.Column<bool>(type: "bit", nullable: false),
                    Parameters = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestBodyContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RequestBodySchema = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SampleRequestBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseCodes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiEndpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiEndpoints_ApiSpecs_ApiSpecId",
                        column: x => x.ApiSpecId,
                        principalTable: "ApiSpecs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Scenarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApiSpecId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Definition = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scenarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Scenarios_ApiSpecs_ApiSpecId",
                        column: x => x.ApiSpecId,
                        principalTable: "ApiSpecs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Scenarios_Environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "Environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TestRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ScenarioName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EnvironmentName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EnvironmentBaseUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    EnvironmentIsProduction = table.Column<bool>(type: "bit", nullable: false),
                    ScenarioSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Script = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ThresholdPlan = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ThresholdsPassed = table.Column<bool>(type: "bit", nullable: true),
                    StartedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    QueuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExitCode = table.Column<int>(type: "int", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WorkDirectory = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    SummaryJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Count = table.Column<long>(type: "bigint", nullable: false),
                    FailedCount = table.Column<long>(type: "bigint", nullable: false),
                    AvgMs = table.Column<double>(type: "float", nullable: false),
                    MinMs = table.Column<double>(type: "float", nullable: false),
                    P50Ms = table.Column<double>(type: "float", nullable: false),
                    P90Ms = table.Column<double>(type: "float", nullable: false),
                    P95Ms = table.Column<double>(type: "float", nullable: false),
                    P99Ms = table.Column<double>(type: "float", nullable: false),
                    MaxMs = table.Column<double>(type: "float", nullable: false),
                    ErrorRate = table.Column<double>(type: "float", nullable: false),
                    Rps = table.Column<double>(type: "float", nullable: false),
                    ChecksPassed = table.Column<long>(type: "bigint", nullable: false),
                    ChecksFailed = table.Column<long>(type: "bigint", nullable: false),
                    TimeSeries = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestRuns_Environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "Environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TestRuns_Scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "Scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RunEndpointResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(2200)", maxLength: 2200, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Count = table.Column<long>(type: "bigint", nullable: false),
                    FailedCount = table.Column<long>(type: "bigint", nullable: false),
                    AvgMs = table.Column<double>(type: "float", nullable: false),
                    MinMs = table.Column<double>(type: "float", nullable: false),
                    P50Ms = table.Column<double>(type: "float", nullable: false),
                    P90Ms = table.Column<double>(type: "float", nullable: false),
                    P95Ms = table.Column<double>(type: "float", nullable: false),
                    P99Ms = table.Column<double>(type: "float", nullable: false),
                    MaxMs = table.Column<double>(type: "float", nullable: false),
                    ErrorRate = table.Column<double>(type: "float", nullable: false),
                    Rps = table.Column<double>(type: "float", nullable: false),
                    ChecksPassed = table.Column<long>(type: "bigint", nullable: false),
                    ChecksFailed = table.Column<long>(type: "bigint", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunEndpointResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunEndpointResults_TestRuns_TestRunId",
                        column: x => x.TestRunId,
                        principalTable: "TestRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunThresholdResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EndpointKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ScopeName = table.Column<string>(type: "nvarchar(2200)", maxLength: 2200, nullable: false),
                    Metric = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Limit = table.Column<double>(type: "float", nullable: false),
                    Actual = table.Column<double>(type: "float", nullable: true),
                    Passed = table.Column<bool>(type: "bit", nullable: false),
                    K6Metric = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Expression = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunThresholdResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunThresholdResults_TestRuns_TestRunId",
                        column: x => x.TestRunId,
                        principalTable: "TestRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiEndpoints_ApiSpecId_SortOrder",
                table: "ApiEndpoints",
                columns: new[] { "ApiSpecId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ApiSpecs_EnvironmentId_Version",
                table: "ApiSpecs",
                columns: new[] { "EnvironmentId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Environments_Name",
                table: "Environments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunEndpointResults_TestRunId",
                table: "RunEndpointResults",
                column: "TestRunId");

            migrationBuilder.CreateIndex(
                name: "IX_RunThresholdResults_TestRunId",
                table: "RunThresholdResults",
                column: "TestRunId");

            migrationBuilder.CreateIndex(
                name: "IX_Scenarios_ApiSpecId",
                table: "Scenarios",
                column: "ApiSpecId");

            migrationBuilder.CreateIndex(
                name: "IX_Scenarios_EnvironmentId",
                table: "Scenarios",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Scenarios_Name",
                table: "Scenarios",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_EnvironmentId_QueuedAt",
                table: "TestRuns",
                columns: new[] { "EnvironmentId", "QueuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_QueuedAt",
                table: "TestRuns",
                column: "QueuedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_ScenarioId_QueuedAt",
                table: "TestRuns",
                columns: new[] { "ScenarioId", "QueuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_Status",
                table: "TestRuns",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiEndpoints");

            migrationBuilder.DropTable(
                name: "RunEndpointResults");

            migrationBuilder.DropTable(
                name: "RunThresholdResults");

            migrationBuilder.DropTable(
                name: "TestRuns");

            migrationBuilder.DropTable(
                name: "Scenarios");

            migrationBuilder.DropTable(
                name: "ApiSpecs");

            migrationBuilder.DropTable(
                name: "Environments");
        }
    }
}
