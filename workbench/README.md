# @flowamaz/workbench

Workbench screens for Vue 3 (MIT): the parts of the Flowamaz app for working with workflows, runs and approvals. They talk to the server through one interface, so they work with the Flowamaz engine API ([Flowamaz.Engine.Api](../dotnet/Flowamaz.Engine.Api/README.md)) or with your own backend.

**Status.** This is phase 5b of the Community Edition plan, and the package grows one PR at a time. It contains:

- the shared basics: `FmButton`, `FmBadge`, `FmEmptyState`, `FmErrorState`, `FmModal`, `FmSkeleton` and `FmSpinner`;
- the engine DTO types;
- the `WorkbenchApi` interface and its engine API client;
- date and run helpers (`runDuration`, `instanceStatusVariant`, `isRunEnded`, …);
- **runs:** `RunsPage` and `RunDetailPage` (see below), and the parts they're built from: `RunTable`, `RunDetail`, `FmRunTimeline` and `VariableInspector`;
- **approvals:** `GatesPage`, the approval inbox, with `GateApproveDialog`, `GateFormFields` and `FmAlert`, plus the gate form rules (`gateForm`) and `humanizeId`.

The workflow screens come next.

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

A failed call throws a `WorkbenchApiError` with the server's message, its code (e.g. `WORKFLOW_SLUG_TAKEN`) and the HTTP status. `describeError(e)` turns anything thrown into a message to show.

`createEngineApiClient` implements the interface for Flowamaz.Engine.Api:

- It sends your headers on every call.
- It refuses a run payload that isn't valid JSON before calling the server.
- An `idempotencyKey` is sent as the `Idempotency-Key` header.

## Develop

```sh
npm ci && npm test && npm run typecheck
```
