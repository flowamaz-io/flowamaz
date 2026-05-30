using Flowamaz.Core.Entities.Library;

namespace Flowamaz.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotent seed data for the 10 official workflow templates. Applied via HasData in
/// <see cref="Configurations.WorkflowTemplateConfiguration"/> — the migration is the seed.
/// Fixed Guid IDs and a fixed seed timestamp keep migrations deterministic. Every YAML here is
/// valid SFG workflow YAML (flowamaz/v1) and is asserted against the real validator in the unit
/// tests before it can ship (see TemplateServiceTests.AllOfficialTemplates_PassValidator).
/// </summary>
public static class WorkflowTemplateSeeder
{
    private static readonly DateTime SeedTimestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static IReadOnlyList<WorkflowTemplate> OfficialTemplates { get; } =
    [
        Template(
            id: "00000000-0000-0000-0002-000000000001",
            slug: "purchase-approval",
            name: "Purchase Approval",
            category: "Finance",
            description: "Two-level purchase approval with a resubmission loop for rejected requests.",
            tags: ["finance", "approval", "purchase"],
            yaml: PurchaseApprovalYaml),

        Template(
            id: "00000000-0000-0000-0002-000000000002",
            slug: "employee-onboarding",
            name: "Employee Onboarding",
            category: "HR",
            description: "Multi-step new-hire onboarding checklist with HR approval gates.",
            tags: ["hr", "onboarding", "checklist"],
            yaml: EmployeeOnboardingYaml),

        Template(
            id: "00000000-0000-0000-0002-000000000003",
            slug: "leave-request",
            name: "Leave Request",
            category: "HR",
            description: "Manager-approved leave request with a calendar update step.",
            tags: ["hr", "leave", "approval"],
            yaml: LeaveRequestYaml),

        Template(
            id: "00000000-0000-0000-0002-000000000004",
            slug: "it-support-ticket",
            name: "IT Support Ticket",
            category: "IT",
            description: "Triage, assign, resolve and close an IT support ticket.",
            tags: ["it", "support", "ticket"],
            yaml: ItSupportTicketYaml),

        Template(
            id: "00000000-0000-0000-0002-000000000005",
            slug: "contract-review",
            name: "Contract Review",
            category: "Legal",
            description: "Legal review gate followed by an e-signature placeholder step.",
            tags: ["legal", "contract", "review"],
            yaml: ContractReviewYaml),

        Template(
            id: "00000000-0000-0000-0002-000000000006",
            slug: "expense-claim",
            name: "Expense Claim",
            category: "Finance",
            description: "Receipt upload, manager approval, then finance approval.",
            tags: ["finance", "expense", "approval"],
            yaml: ExpenseClaimYaml),

        Template(
            id: "00000000-0000-0000-0002-000000000007",
            slug: "vendor-onboarding",
            name: "Vendor Onboarding",
            category: "Operations",
            description: "Procurement workflow with three sequential approval levels.",
            tags: ["operations", "vendor", "procurement"],
            yaml: VendorOnboardingYaml),

        Template(
            id: "00000000-0000-0000-0002-000000000008",
            slug: "incident-response",
            name: "Incident Response",
            category: "IT",
            description: "Detect, triage, escalate, resolve and run a post-mortem.",
            tags: ["it", "incident", "response"],
            yaml: IncidentResponseYaml),

        Template(
            id: "00000000-0000-0000-0002-000000000009",
            slug: "customer-refund",
            name: "Customer Refund",
            category: "Finance",
            description: "Customer-support review, finance approval, then payment trigger.",
            tags: ["finance", "refund", "approval"],
            yaml: CustomerRefundYaml),

        Template(
            id: "00000000-0000-0000-0002-000000000010",
            slug: "compliance-checklist",
            name: "Compliance Checklist",
            category: "Operations",
            description: "Periodic compliance review workflow driven by due dates.",
            tags: ["operations", "compliance", "review"],
            yaml: ComplianceChecklistYaml),
    ];

    private static WorkflowTemplate Template(
        string id, string slug, string name, string category, string description, string[] tags, string yaml) => new()
    {
        Id = Guid.Parse(id),
        Slug = slug,
        Name = name,
        Category = category,
        Description = description,
        Tags = tags,
        YamlContent = yaml,
        IsOfficial = true,
        OrgId = null,
        InstallCount = 0,
        AverageRating = 0m,
        Version = "1.0.0",
        IsActive = true,
        ReviewStatus = "approved",
        CreatedAt = SeedTimestamp,
        UpdatedAt = SeedTimestamp,
    };

    // ── Official template YAML (flowamaz/v1) ────────────────────────────────────

    private const string PurchaseApprovalYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: purchase-approval
          name: "Purchase Approval"
          description: "Two-level purchase approval with a resubmission loop."
          version: "1.0.0"
        spec:
          trigger:
            type: form
          variables:
            amount:
              type: number
              required: true
            requester:
              type: string
              required: true
          nodes:
            - id: start
              type: trigger
              label: "Purchase requested"
            - id: manager-approval
              type: human-gate
              label: "Manager approval"
            - id: route-decision
              type: router
              label: "Approved?"
            - id: finance-approval
              type: human-gate
              label: "Finance approval"
            - id: create-po
              type: action
              label: "Create purchase order"
            - id: notify-rejected
              type: action
              label: "Notify requester of rejection"
            - id: done
              type: end
              label: "Complete"
          edges:
            - id: e1
              from: start
              to: manager-approval
            - id: e2
              from: manager-approval
              to: route-decision
            - id: e3
              from: route-decision
              to: finance-approval
              via: approved
            - id: e4
              from: route-decision
              to: notify-rejected
              via: rejected
            - id: e5
              from: finance-approval
              to: create-po
            - id: e6
              from: create-po
              to: done
            - id: e7
              from: notify-rejected
              to: done
        """;

    private const string EmployeeOnboardingYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: employee-onboarding
          name: "Employee Onboarding"
          description: "Multi-step new-hire onboarding checklist with HR gates."
          version: "1.0.0"
        spec:
          trigger:
            type: form
          variables:
            employeeName:
              type: string
              required: true
            startDate:
              type: string
              required: true
          nodes:
            - id: start
              type: trigger
              label: "New hire created"
            - id: provision-accounts
              type: action
              label: "Provision accounts"
            - id: assign-equipment
              type: action
              label: "Assign equipment"
            - id: hr-paperwork
              type: human-gate
              label: "HR confirms paperwork"
            - id: manager-welcome
              type: action
              label: "Schedule manager welcome"
            - id: done
              type: end
              label: "Onboarding complete"
          edges:
            - id: e1
              from: start
              to: provision-accounts
            - id: e2
              from: provision-accounts
              to: assign-equipment
            - id: e3
              from: assign-equipment
              to: hr-paperwork
            - id: e4
              from: hr-paperwork
              to: manager-welcome
            - id: e5
              from: manager-welcome
              to: done
        """;

    private const string LeaveRequestYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: leave-request
          name: "Leave Request"
          description: "Manager-approved leave request with a calendar update."
          version: "1.0.0"
        spec:
          trigger:
            type: form
          variables:
            days:
              type: number
              required: true
          nodes:
            - id: start
              type: trigger
              label: "Leave requested"
            - id: manager-approval
              type: human-gate
              label: "Manager approval"
            - id: decision
              type: router
              label: "Approved?"
            - id: update-calendar
              type: action
              label: "Update calendar"
            - id: notify-declined
              type: action
              label: "Notify declined"
            - id: done
              type: end
              label: "Complete"
          edges:
            - id: e1
              from: start
              to: manager-approval
            - id: e2
              from: manager-approval
              to: decision
            - id: e3
              from: decision
              to: update-calendar
              via: approved
            - id: e4
              from: decision
              to: notify-declined
              via: declined
            - id: e5
              from: update-calendar
              to: done
            - id: e6
              from: notify-declined
              to: done
        """;

    private const string ItSupportTicketYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: it-support-ticket
          name: "IT Support Ticket"
          description: "Triage, assign, resolve and close an IT support ticket."
          version: "1.0.0"
        spec:
          trigger:
            type: webhook
          variables:
            priority:
              type: string
              required: true
          nodes:
            - id: start
              type: trigger
              label: "Ticket created"
            - id: triage
              type: action
              label: "Triage ticket"
            - id: assign
              type: action
              label: "Assign to engineer"
            - id: resolve
              type: human-gate
              label: "Engineer resolves"
            - id: close
              type: action
              label: "Close ticket"
            - id: done
              type: end
              label: "Closed"
          edges:
            - id: e1
              from: start
              to: triage
            - id: e2
              from: triage
              to: assign
            - id: e3
              from: assign
              to: resolve
            - id: e4
              from: resolve
              to: close
            - id: e5
              from: close
              to: done
        """;

    private const string ContractReviewYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: contract-review
          name: "Contract Review"
          description: "Legal review gate followed by an e-signature placeholder."
          version: "1.0.0"
        spec:
          trigger:
            type: form
          variables:
            counterparty:
              type: string
              required: true
          nodes:
            - id: start
              type: trigger
              label: "Contract submitted"
            - id: legal-review
              type: human-gate
              label: "Legal review"
            - id: decision
              type: router
              label: "Approved?"
            - id: esign
              type: action
              label: "Send for e-signature"
            - id: request-changes
              type: action
              label: "Request changes"
            - id: done
              type: end
              label: "Complete"
          edges:
            - id: e1
              from: start
              to: legal-review
            - id: e2
              from: legal-review
              to: decision
            - id: e3
              from: decision
              to: esign
              via: approved
            - id: e4
              from: decision
              to: request-changes
              via: changes
            - id: e5
              from: esign
              to: done
            - id: e6
              from: request-changes
              to: done
        """;

    private const string ExpenseClaimYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: expense-claim
          name: "Expense Claim"
          description: "Receipt upload, manager approval, then finance approval."
          version: "1.0.0"
        spec:
          trigger:
            type: form
          variables:
            amount:
              type: number
              required: true
          nodes:
            - id: start
              type: trigger
              label: "Claim submitted"
            - id: upload-receipt
              type: action
              label: "Attach receipt"
            - id: manager-approval
              type: human-gate
              label: "Manager approval"
            - id: finance-approval
              type: human-gate
              label: "Finance approval"
            - id: reimburse
              type: action
              label: "Trigger reimbursement"
            - id: done
              type: end
              label: "Complete"
          edges:
            - id: e1
              from: start
              to: upload-receipt
            - id: e2
              from: upload-receipt
              to: manager-approval
            - id: e3
              from: manager-approval
              to: finance-approval
            - id: e4
              from: finance-approval
              to: reimburse
            - id: e5
              from: reimburse
              to: done
        """;

    private const string VendorOnboardingYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: vendor-onboarding
          name: "Vendor Onboarding"
          description: "Procurement workflow with three sequential approval levels."
          version: "1.0.0"
        spec:
          trigger:
            type: form
          variables:
            vendorName:
              type: string
              required: true
          nodes:
            - id: start
              type: trigger
              label: "Vendor submitted"
            - id: procurement-review
              type: human-gate
              label: "Procurement review"
            - id: compliance-review
              type: human-gate
              label: "Compliance review"
            - id: finance-review
              type: human-gate
              label: "Finance review"
            - id: activate-vendor
              type: action
              label: "Activate vendor record"
            - id: done
              type: end
              label: "Complete"
          edges:
            - id: e1
              from: start
              to: procurement-review
            - id: e2
              from: procurement-review
              to: compliance-review
            - id: e3
              from: compliance-review
              to: finance-review
            - id: e4
              from: finance-review
              to: activate-vendor
            - id: e5
              from: activate-vendor
              to: done
        """;

    private const string IncidentResponseYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: incident-response
          name: "Incident Response"
          description: "Detect, triage, escalate, resolve and run a post-mortem."
          version: "1.0.0"
        spec:
          trigger:
            type: webhook
          variables:
            severity:
              type: string
              required: true
          nodes:
            - id: start
              type: trigger
              label: "Incident detected"
            - id: triage
              type: action
              label: "Triage severity"
            - id: severity-router
              type: router
              label: "Escalate?"
            - id: escalate
              type: action
              label: "Escalate to on-call"
            - id: resolve
              type: human-gate
              label: "Resolve incident"
            - id: post-mortem
              type: action
              label: "Schedule post-mortem"
            - id: done
              type: end
              label: "Resolved"
          edges:
            - id: e1
              from: start
              to: triage
            - id: e2
              from: triage
              to: severity-router
            - id: e3
              from: severity-router
              to: escalate
              via: high
            - id: e4
              from: severity-router
              to: resolve
              via: low
            - id: e5
              from: escalate
              to: resolve
            - id: e6
              from: resolve
              to: post-mortem
            - id: e7
              from: post-mortem
              to: done
        """;

    private const string CustomerRefundYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: customer-refund
          name: "Customer Refund"
          description: "Customer-support review, finance approval, then payment trigger."
          version: "1.0.0"
        spec:
          trigger:
            type: form
          variables:
            amount:
              type: number
              required: true
          nodes:
            - id: start
              type: trigger
              label: "Refund requested"
            - id: cs-review
              type: human-gate
              label: "Customer support review"
            - id: finance-approval
              type: human-gate
              label: "Finance approval"
            - id: issue-refund
              type: action
              label: "Trigger payment refund"
            - id: done
              type: end
              label: "Refunded"
          edges:
            - id: e1
              from: start
              to: cs-review
            - id: e2
              from: cs-review
              to: finance-approval
            - id: e3
              from: finance-approval
              to: issue-refund
            - id: e4
              from: issue-refund
              to: done
        """;

    private const string ComplianceChecklistYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: compliance-checklist
          name: "Compliance Checklist"
          description: "Periodic compliance review workflow driven by due dates."
          version: "1.0.0"
        spec:
          trigger:
            type: schedule
          variables:
            period:
              type: string
              required: true
          nodes:
            - id: start
              type: trigger
              label: "Review period starts"
            - id: gather-evidence
              type: action
              label: "Gather evidence"
            - id: reviewer-sign-off
              type: human-gate
              label: "Reviewer sign-off"
            - id: file-report
              type: action
              label: "File compliance report"
            - id: done
              type: end
              label: "Complete"
          edges:
            - id: e1
              from: start
              to: gather-evidence
            - id: e2
              from: gather-evidence
              to: reviewer-sign-off
            - id: e3
              from: reviewer-sign-off
              to: file-report
            - id: e4
              from: file-report
              to: done
        """;
}
