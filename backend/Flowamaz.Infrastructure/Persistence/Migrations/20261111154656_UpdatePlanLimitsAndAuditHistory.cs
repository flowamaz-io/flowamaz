using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePlanLimitsAndAuditHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "plans",
                keyColumn: "id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000001"),
                columns: new[] { "features", "limits" },
                values: new object[] { "{\"sso\":false,\"scim\":false,\"customConnectors\":false,\"auditLog\":true,\"auditExport\":false,\"apiAccess\":true,\"byom\":false,\"dataResidency\":false,\"cmek\":false,\"processIntelligence\":false,\"roiAnalytics\":false}", "{\"maxWorkspaces\":1,\"maxMembersPerWorkspace\":1,\"maxWorkflowDefinitions\":5,\"maxRunsPerMonth\":500,\"maxAiCallsPerMonth\":0,\"maxStorageGb\":1,\"runRetentionDays\":7,\"apiRateLimitPerMinute\":30,\"auditHistoryDays\":7,\"allowedAiProviders\":[\"anthropic\"]}" });

            migrationBuilder.UpdateData(
                table: "plans",
                keyColumn: "id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000002"),
                columns: new[] { "features", "limits" },
                values: new object[] { "{\"sso\":false,\"scim\":false,\"customConnectors\":true,\"auditLog\":true,\"auditExport\":true,\"apiAccess\":true,\"byom\":false,\"dataResidency\":false,\"cmek\":false,\"processIntelligence\":false,\"roiAnalytics\":false}", "{\"maxWorkspaces\":3,\"maxMembersPerWorkspace\":10,\"maxWorkflowDefinitions\":20,\"maxRunsPerMonth\":5000,\"maxAiCallsPerMonth\":0,\"maxStorageGb\":10,\"runRetentionDays\":90,\"apiRateLimitPerMinute\":120,\"auditHistoryDays\":90,\"allowedAiProviders\":[\"anthropic\",\"azure-openai\",\"google\"]}" });

            migrationBuilder.UpdateData(
                table: "plans",
                keyColumn: "id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000003"),
                columns: new[] { "features", "limits" },
                values: new object[] { "{\"sso\":false,\"scim\":false,\"customConnectors\":true,\"auditLog\":true,\"auditExport\":true,\"apiAccess\":true,\"byom\":false,\"dataResidency\":false,\"cmek\":false,\"processIntelligence\":true,\"roiAnalytics\":true}", "{\"maxWorkspaces\":10,\"maxMembersPerWorkspace\":50,\"maxWorkflowDefinitions\":0,\"maxRunsPerMonth\":50000,\"maxAiCallsPerMonth\":0,\"maxStorageGb\":50,\"runRetentionDays\":365,\"apiRateLimitPerMinute\":600,\"auditHistoryDays\":365,\"allowedAiProviders\":[\"anthropic\",\"azure-openai\",\"google\",\"kimi\",\"mistral\"]}" });

            migrationBuilder.UpdateData(
                table: "plans",
                keyColumn: "id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000004"),
                columns: new[] { "features", "limits" },
                values: new object[] { "{\"sso\":true,\"scim\":true,\"customConnectors\":true,\"auditLog\":true,\"auditExport\":true,\"apiAccess\":true,\"byom\":true,\"dataResidency\":true,\"cmek\":true,\"processIntelligence\":true,\"roiAnalytics\":true}", "{\"maxWorkspaces\":0,\"maxMembersPerWorkspace\":0,\"maxWorkflowDefinitions\":0,\"maxRunsPerMonth\":0,\"maxAiCallsPerMonth\":0,\"maxStorageGb\":0,\"runRetentionDays\":0,\"apiRateLimitPerMinute\":1200,\"auditHistoryDays\":0,\"allowedAiProviders\":[\"anthropic\",\"azure-openai\",\"google\",\"kimi\",\"mistral\",\"byom\"]}" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "plans",
                keyColumn: "id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000001"),
                columns: new[] { "features", "limits" },
                values: new object[] { "{\"sso\":false,\"scim\":false,\"customConnectors\":false,\"auditLog\":false,\"apiAccess\":false,\"byom\":false,\"dataResidency\":false,\"cmek\":false,\"processIntelligence\":false,\"roiAnalytics\":false}", "{\"maxWorkspaces\":1,\"maxMembersPerWorkspace\":1,\"maxWorkflowDefinitions\":5,\"maxRunsPerMonth\":500,\"maxAiCallsPerMonth\":0,\"maxStorageGb\":1,\"runRetentionDays\":7,\"apiRateLimitPerMinute\":10,\"allowedAiProviders\":[\"anthropic\"]}" });

            migrationBuilder.UpdateData(
                table: "plans",
                keyColumn: "id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000002"),
                columns: new[] { "features", "limits" },
                values: new object[] { "{\"sso\":false,\"scim\":false,\"customConnectors\":true,\"auditLog\":false,\"apiAccess\":true,\"byom\":false,\"dataResidency\":false,\"cmek\":false,\"processIntelligence\":false,\"roiAnalytics\":false}", "{\"maxWorkspaces\":3,\"maxMembersPerWorkspace\":10,\"maxWorkflowDefinitions\":20,\"maxRunsPerMonth\":5000,\"maxAiCallsPerMonth\":0,\"maxStorageGb\":10,\"runRetentionDays\":90,\"apiRateLimitPerMinute\":60,\"allowedAiProviders\":[\"anthropic\",\"azure-openai\",\"google\"]}" });

            migrationBuilder.UpdateData(
                table: "plans",
                keyColumn: "id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000003"),
                columns: new[] { "features", "limits" },
                values: new object[] { "{\"sso\":false,\"scim\":false,\"customConnectors\":true,\"auditLog\":true,\"apiAccess\":true,\"byom\":false,\"dataResidency\":false,\"cmek\":false,\"processIntelligence\":true,\"roiAnalytics\":true}", "{\"maxWorkspaces\":10,\"maxMembersPerWorkspace\":50,\"maxWorkflowDefinitions\":0,\"maxRunsPerMonth\":50000,\"maxAiCallsPerMonth\":0,\"maxStorageGb\":50,\"runRetentionDays\":365,\"apiRateLimitPerMinute\":300,\"allowedAiProviders\":[\"anthropic\",\"azure-openai\",\"google\",\"kimi\",\"mistral\"]}" });

            migrationBuilder.UpdateData(
                table: "plans",
                keyColumn: "id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000004"),
                columns: new[] { "features", "limits" },
                values: new object[] { "{\"sso\":true,\"scim\":true,\"customConnectors\":true,\"auditLog\":true,\"apiAccess\":true,\"byom\":true,\"dataResidency\":true,\"cmek\":true,\"processIntelligence\":true,\"roiAnalytics\":true}", "{\"maxWorkspaces\":0,\"maxMembersPerWorkspace\":0,\"maxWorkflowDefinitions\":0,\"maxRunsPerMonth\":0,\"maxAiCallsPerMonth\":0,\"maxStorageGb\":0,\"runRetentionDays\":0,\"apiRateLimitPerMinute\":0,\"allowedAiProviders\":[\"anthropic\",\"azure-openai\",\"google\",\"kimi\",\"mistral\",\"byom\"]}" });
        }
    }
}
