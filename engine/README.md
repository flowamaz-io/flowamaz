# Flowamaz.Engine.Core

The open core of the Flowamaz workflow engine (MIT):

- **SFG parser** (`SfgParser`, `WorkflowGraph`): reads workflow YAML (`apiVersion: flowamaz/v1`) into a graph.
- **Validator** (`WorkflowValidator`): JSON Schema (`workflow-v1.schema.json`) plus graph checks, with actionable messages.
- **Condition evaluator** (`ConditionEvaluator`): the router condition rules the engine uses.
- **Gate forms** (`GateForms`): human-gate form definitions and answer validation.
- **Workflow Twin** (`TwinCaseGenerator`, `TwinSimulator`, `TwinAnalyzer`): simulates a workflow against generated inputs and reports where it breaks.
- **Output shape drift** (`OutputShapeDrift`): detects when a connector's responses change shape.

The YAML format is documented in [`../docs/yaml-spec.md`](../docs/yaml-spec.md).

```bash
dotnet test Flowamaz.Engine.slnx
dotnet pack Flowamaz.Engine.Core -c Release
```
