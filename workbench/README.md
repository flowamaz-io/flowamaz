# @flowamaz/workbench

Workbench screens for Vue 3 (MIT): the parts of the Flowamaz app for working with workflows, runs and approvals. They talk to the server through one interface, so they work with the Flowamaz engine API ([Flowamaz.Engine.Api](../dotnet/Flowamaz.Engine.Api/README.md)) or with your own backend.

**Status.** This is phase 5b of the Community Edition plan, and the package grows one PR at a time. It contains:

- the shared basics: `FmButton`, `FmBadge`, `FmEmptyState`, `FmErrorState`, `FmModal`, `FmSkeleton` and `FmSpinner`;
- the engine DTO types;
- the `WorkbenchApi` interface and its engine API client;
- date and run helpers (`runDuration`, `instanceStatusVariant`, `isRunEnded`, …);
- **runs:** `RunsPage` and `RunDetailPage` (see below), and the parts they're built from: `RunTable`, `RunDetail`, `FmRunTimeline` and `VariableInspector`;
- **workflows:** `WorkflowsPage` and `WorkflowDetailPage`, with `CreateWorkflowDialog` and `StartRunDialog`, plus `workflowStatusVariant`, `starterWorkflowYaml` and `slugify`;
- **editing:** `WorkflowEditorPage` (the YAML on the left, a live picture of the workflow on the right, or with `canvas` the visual editor, Save and Publish), with `FmYamlEditor` and `WorkflowGraphPreview`, and the visual editor `SfgCanvas` (see below);
- **approvals:** `GatesPage`, the approval inbox, with `GateApproveDialog`, `GateFormFields` and `FmAlert`, plus the gate form rules (`gateForm`) and `humanizeId`.

## Workflows

`WorkflowsPage` and `WorkflowDetailPage` need only the installed API. They emit navigation instead of using a router:

```vue
<WorkflowsPage @open="(id) => router.push(`/workflows/${id}`)" @edit="(id) => router.push(`/workflows/${id}/edit`)"
  @run-started="(runId) => router.push(`/runs/${runId}`)" />
<WorkflowDetailPage :workflow-id="id" @edit="…" @open-run="…" @run-started="…" @back="…" />
```

The list has:

- search;
- Active, All and Archived filters (archived workflows are hidden by default);
- New workflow, which starts from a small workflow (trigger, approval, end) that publishes as it is;
- start a run (a draft runs as a test run);
- edit, archive or unarchive a published workflow, and delete a draft.

The detail page shows the workflow's published versions and its runs. Use `can-edit` and `can-run` to hide actions a user may not take.

## Editing

`<WorkflowEditorPage :workflow-id="id" @published="…" @back="…" />` needs only the installed API:

- **Save:** the Save button or Ctrl/Cmd+S.
- **Publish:** saves first, then shows the server's validation message as it is if publishing is refused.
- **Leaving:** the browser warns before you leave with unsaved changes.
- **`read-only`:** hides Save and Publish.
- **`canvas`:** the visual editor (`SfgCanvas`, loaded only then) in place of the picture, in step with the YAML.

### The visual editor

`SfgCanvas` (`@flowamaz/workbench/components/canvas/SfgCanvas.vue`, not in the package index, because it brings in
Cytoscape) is the full canvas editor: drag steps from the palette, connect them, edit each step's settings in its panel,
undo and redo, and keep the YAML in step.

`<SfgCanvas :workflow-id="id" :initial-yaml="yaml" @yaml-change="yaml = $event" @save="save" />`

- It loads the workflow with the API's `getWorkflow`. `initial-yaml` and `yaml-change` keep it in step with a YAML editor beside it.
- `state` shares the canvas state (selection, undo) with the rest of your page; without it the canvas keeps its own.
- `read-only` shows the workflow without editing. `collab` joins a shared editing session (`CanvasCollab`).
- The step settings list connectors, credentials and Library approval forms when the API has the optional
  `listConnectors`, `listCredentials`, `listLibraryForms` and `saveLibraryForm`. Without them, the settings still
  work; they just have no list to pick from.

The picture is drawn with [`@flowamaz/canvas`](../canvas/README.md). The workbench uses the canvas from its sibling folder's source, so install both (`npm ci --prefix canvas && npm ci --prefix workbench`).

## Approvals

`<GatesPage @decided="refreshBadge" />` is the whole inbox:

- **Tabs:** Pending, and All (which includes decided gates).
- **Include test runs:** a switch, remembered in localStorage (`showTestStorageKey`).
- **Deciding:** Approve or Reject with an optional note. A gate with a form asks for its answers first and checks them, using the same rules as the server.
- **After a decision:** the row stays, showing the decision. `decided` fires once each decision is recorded.
- **Links:** "Learn about human gates" goes to `docsUrl`.

## Runs

`RunsPage` and `RunDetailPage` need nothing but the installed API. They refresh by polling while a run is going (every 5 s for the list, 2 s for a run) and stop once it has ended. They emit navigation instead of using a router:

```vue
<RunsPage @open="(id) => router.push(`/runs/${id}`)" @go-to-workflows="router.push('/workflows')" />
<RunDetailPage :run-id="id" @retried="(newId) => router.push(`/runs/${newId}`)" @back="router.push('/runs')" />
```

To build your own page (live updates, your own roles), use `RunTable` and `RunDetail` directly. `RunDetail` has these slots:

- `back`: a link back to the list;
- `actions`: more buttons next to Cancel and Retry;
- `timeline-extra`: content under the timeline;
- `data-hidden`: the note shown when the viewer can't see run data;
- one `tab-<name>` slot for each entry in `extraTabs`.

## Setup

```ts
import { createApp } from 'vue';
import { createEngineApiClient, installWorkbench } from '@flowamaz/workbench';

const app = createApp(App);
installWorkbench(app, {
  api: createEngineApiClient({
    baseUrl: '/api/v1',
    workspaceId,
    headers: () => ({ Authorization: `Bearer ${getToken()}` }),
  }),
  // Optional: link errors to your help system.
  articleForError: (status, code) => null,
  openHelp: (article) => window.open(`https://docs.example.com/${article}`),
});
```

The package is published as TypeScript and `.vue` source, so your Vite build compiles it.

**Styles.** The components use Tailwind CSS v4 utility classes, including the colour scales `primary`, `accent`, `amber` and `danger`. Add the package to Tailwind's sources, and import the default colours unless your theme defines those scales:

```css
@import "tailwindcss";
@import "@flowamaz/workbench/theme.css";
@source "../node_modules/@flowamaz/workbench/src";
```

## The API

`WorkbenchApi` covers one workspace:

- **Workflows:** list, get, create, save, delete, publish, archive, unarchive and versions.
- **Runs:** start, list, get, events, timeline, cancel and retry.
- **Gates:** list pending and decide.
- **Canvas step settings (optional):** connectors, credentials, and Library approval forms (list and save). `createEngineApiClient` has `listConnectors` (the engine's `GET …/connectors`); credentials and Library forms belong to the host.

A failed call throws a `WorkbenchApiError` with the server's message, its code (e.g. `WORKFLOW_SLUG_TAKEN`) and the HTTP status. `describeError(e)` turns anything thrown into a message to show.

`createEngineApiClient` implements the interface for Flowamaz.Engine.Api:

- It sends your headers on every call.
- It refuses a run payload that isn't valid JSON before calling the server.
- An `idempotencyKey` is sent as the `Idempotency-Key` header.

## Develop

```sh
npm ci && npm test && npm run typecheck
```
