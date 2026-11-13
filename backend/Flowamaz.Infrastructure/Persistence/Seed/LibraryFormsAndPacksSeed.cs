using System.Text.Json;
using Flowamaz.Core.Entities.Library;
using Flowamaz.Core.Workflow;

namespace Flowamaz.Infrastructure.Persistence.Seed;

/// <summary>Official Library forms and packs (FUNCTIONAL.md §23.2), built from the seeded connectors and templates.</summary>
public static class LibraryFormsAndPacksSeed
{
    private static readonly DateTime SeedTimestamp = new(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static FormDefinition[] OfficialForms =>
    [
        Form("00000000-0000-0000-0005-000000000001", "purchase-details", "Purchase details", "Finance",
            "What the approver confirms on a purchase request: the amount, cost centre and budget line.",
            [
                new GateFormField("approved_amount", "Approved amount", "number", Required: true, Min: 0, Help: "The amount you approve; it can be lower than requested."),
                new GateFormField("cost_centre", "Cost centre", "text", Required: true),
                new GateFormField("budget_line", "Budget line", "select", Options: ["Operating", "Capital", "Project"]),
                new GateFormField("conditions", "Conditions", "textarea", Help: "Anything the requester must do, e.g. get two quotes."),
            ]),
        Form("00000000-0000-0000-0005-000000000002", "expense-check", "Expense check", "Finance",
            "Checks before paying an expense claim: receipts, category and the amount to reimburse.",
            [
                new GateFormField("receipts_checked", "I checked the receipts", "checkbox", Required: true),
                new GateFormField("category", "Category", "select", Required: true, Options: ["Travel", "Meals", "Equipment", "Training", "Other"]),
                new GateFormField("reimburse_amount", "Amount to reimburse", "number", Required: true, Min: 0),
            ]),
        Form("00000000-0000-0000-0005-000000000003", "leave-approval", "Leave approval", "HR",
            "What a manager records when approving leave: the dates and who covers.",
            [
                new GateFormField("first_day", "First day of leave", "date", Required: true),
                new GateFormField("last_day", "Last day of leave", "date", Required: true),
                new GateFormField("cover", "Who covers (email)", "email"),
            ]),
    ];

    public static PackDefinition[] OfficialPacks =>
    [
        Pack("00000000-0000-0000-0006-000000000001", "finance-approvals", "Finance approvals", "Finance",
            "Purchase approval, expense claims and refunds, with Slack and email notices and the purchase and expense forms.",
            connectors: ["slack", "email-smtp"],
            templates: ["purchase-approval", "expense-claim", "customer-refund"],
            forms: ["purchase-details", "expense-check"]),
        Pack("00000000-0000-0000-0006-000000000002", "people-operations", "People operations", "HR",
            "Onboarding and leave requests with Microsoft 365 and email, and the leave approval form.",
            connectors: ["microsoft-365", "email-smtp"],
            templates: ["employee-onboarding", "leave-request"],
            forms: ["leave-approval"]),
    ];

    private static FormDefinition Form(string id, string slug, string name, string category, string description, GateFormField[] fields) => new()
    {
        Id = Guid.Parse(id), Slug = slug, Name = name, Category = category, Description = description, Version = "1.0.0",
        PublisherId = "flowamaz-io", IsOfficial = true, FieldsJson = JsonSerializer.Serialize(fields, Json),
        CreatedAt = SeedTimestamp, UpdatedAt = SeedTimestamp,
    };

    private static PackDefinition Pack(string id, string slug, string name, string category, string description,
        string[] connectors, string[] templates, string[] forms) => new()
    {
        Id = Guid.Parse(id), Slug = slug, Name = name, Category = category, Description = description, Version = "1.0.0",
        PublisherId = "flowamaz-io", IsOfficial = true, Connectors = connectors, Templates = templates, Forms = forms,
        CreatedAt = SeedTimestamp, UpdatedAt = SeedTimestamp,
    };
}
