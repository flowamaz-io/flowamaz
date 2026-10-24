using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddComplianceTemplateSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000010"),
                column: "yaml_content",
                value: "apiVersion: flowamaz/v1\nkind: Workflow\nmetadata:\n  id: compliance-checklist\n  name: \"Compliance Checklist\"\n  description: \"Periodic compliance review workflow driven by due dates.\"\n  version: \"1.0.0\"\nspec:\n  trigger:\n    type: schedule\n    config:\n      cron: \"0 9 1 * *\"   # 09:00 on the 1st of every month — edit after installing\n      timezone: \"UTC\"\n  variables:\n    period:\n      type: string\n      required: true\n  nodes:\n    - id: start\n      type: trigger\n      label: \"Review period starts\"\n    - id: gather-evidence\n      type: action\n      label: \"Gather evidence\"\n    - id: reviewer-sign-off\n      type: human-gate\n      label: \"Reviewer sign-off\"\n    - id: file-report\n      type: action\n      label: \"File compliance report\"\n    - id: done\n      type: end\n      label: \"Complete\"\n  edges:\n    - id: e1\n      from: start\n      to: gather-evidence\n    - id: e2\n      from: gather-evidence\n      to: reviewer-sign-off\n    - id: e3\n      from: reviewer-sign-off\n      to: file-report\n    - id: e4\n      from: file-report\n      to: done");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "workflow_templates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000010"),
                column: "yaml_content",
                value: "apiVersion: flowamaz/v1\nkind: Workflow\nmetadata:\n  id: compliance-checklist\n  name: \"Compliance Checklist\"\n  description: \"Periodic compliance review workflow driven by due dates.\"\n  version: \"1.0.0\"\nspec:\n  trigger:\n    type: schedule\n  variables:\n    period:\n      type: string\n      required: true\n  nodes:\n    - id: start\n      type: trigger\n      label: \"Review period starts\"\n    - id: gather-evidence\n      type: action\n      label: \"Gather evidence\"\n    - id: reviewer-sign-off\n      type: human-gate\n      label: \"Reviewer sign-off\"\n    - id: file-report\n      type: action\n      label: \"File compliance report\"\n    - id: done\n      type: end\n      label: \"Complete\"\n  edges:\n    - id: e1\n      from: start\n      to: gather-evidence\n    - id: e2\n      from: gather-evidence\n      to: reviewer-sign-off\n    - id: e3\n      from: reviewer-sign-off\n      to: file-report\n    - id: e4\n      from: file-report\n      to: done");
        }
    }
}
