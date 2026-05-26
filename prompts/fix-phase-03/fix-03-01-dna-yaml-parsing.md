---
prompt-id: fix-03-01-dna-yaml-parsing
phase: fix-phase-03
sequence: 1
roles: [Executor, Verifier, Testing]
type: fix
source-issue: Verifier W1 — Phase 03 report
severity: Medium
depends-on: []
---

# Fix: WorkflowDnaService Uses Regex Instead of YamlDotNet

## Issue Being Fixed

From phase-03-report.md:

```
W1 | WorkflowDnaService uses regex line-scanning for YAML node type extraction
   instead of YamlDotNet deserialisation
   Deferred to fix-03 — functional, non-blocking
```

Foundation rule from CLAUDE.md:
"YAML always via YamlDotNet — never raw string manipulation."

## Objective
Replace regex line-scanning in WorkflowDnaService with proper YamlDotNet
deserialisation using the existing SfgParser. Extend tests to cover the fixed path.

## Scope

### What to Fix

**WorkflowDnaService.ComputeDnaAsync — current (wrong):**
```csharp
// WRONG — regex line scanning
var nodeTypes = yamlContent
    .Split('\n')
    .Where(l => l.TrimStart().StartsWith("type:"))
    .Select(l => l.Split(':')[1].Trim())
    .Distinct()
    .OrderBy(t => t)
    .ToList();
```

**WorkflowDnaService.ComputeDnaAsync — correct:**
```csharp
// CORRECT — YamlDotNet via existing SfgParser
var graph = await _sfgParser.ParseAsync(yamlContent, ct);

var nodeTypes = graph.Nodes
    .Select(n => n.Type.ToString().ToLowerInvariant())
    .Distinct()
    .OrderBy(t => t)
    .ToList();

var connectorIds = graph.Nodes
    .Where(n => n.Type == NodeType.Action)
    .Select(n => n.Config.RootElement
        .TryGetProperty("connector_id", out var c) ? c.GetString() : null)
    .Where(id => id != null)
    .Distinct()
    .OrderBy(id => id)
    .ToList()!;

var hasAiNodes = graph.Nodes.Any(n => n.Type == NodeType.Ai);
var hasHumanGates = graph.Nodes.Any(n => n.Type == NodeType.HumanGate);
var hasParallelBranches = graph.Nodes.Any(n => n.Type == NodeType.Parallel);
var nodeCount = graph.Nodes.Count;
var edgeCount = graph.Edges.Count;
```

**What also needs checking:**
- Any other place in WorkflowDnaService that parses YAML via string manipulation
- Any regex or Split/Contains patterns on yamlContent strings — replace all
- SopParsingService — verify it does not do any YAML string manipulation
  (it should produce NlWorkflowRequest, not YAML directly)

**Extended tests:**

Add to WorkflowDnaServiceTests.cs:
- ComputeDnaAsync_ValidYaml_UsesGraphNotRegex
  Verify: DNA node types match graph.Nodes types exactly
  Use a YAML with mixed case in labels — regex would fail, YamlDotNet succeeds

- ComputeDnaAsync_InvalidYaml_ThrowsSfgParseException
  Regex-based approach silently returns empty. YamlDotNet throws correctly.
  Verify: malformed YAML → SfgParseException propagates from ComputeDnaAsync.

- ComputeDnaAsync_ActionNodeWithConnectorId_IncludedInDna
  Verify: connector_id extracted from Action node config correctly.

- FindSimilarAsync_SameNodeTypesAndConnectors_HighScore
  Regression test — ensure similarity scoring still works after the refactor.

- DnaHash_SameWorkflow_Deterministic
  Same YAML twice → same DnaHash. Verify SHA256 is stable after refactor.

### What NOT to Change
- No entity or migration changes
- No API changes
- No frontend changes
- WorkflowDnaService logic only + tests

## Technical Requirements
- [ ] WorkflowDnaService: zero string manipulation of YAML content
- [ ] WorkflowDnaService: uses _sfgParser.ParseAsync for all structural extraction
- [ ] Invalid YAML: throws SfgParseException (not silently returns empty DNA)
- [ ] All existing WorkflowDnaService tests still pass
- [ ] 5 new tests added and passing
- [ ] dotnet build — 0 errors, 0 warnings
- [ ] dotnet test — all pass (will be 329+ total)

## Acceptance Criteria
- [ ] grep -r "Split\|\.Lines\|Regex\|\.Contains(" WorkflowDnaService.cs → 0 results
- [ ] Malformed YAML → SfgParseException (not empty DNA object)
- [ ] connector_id from Action node config → included in DNA.ConnectorIds
- [ ] DnaHash deterministic: same YAML → same hash after refactor

## Output Expected
```
backend/Flowamaz.Application/Workflow/Dna/WorkflowDnaService.cs (refactored)
backend/Flowamaz.Tests.Unit/Workflow/Dna/WorkflowDnaServiceTests.cs (extended)
```
