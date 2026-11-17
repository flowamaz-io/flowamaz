// @flowamaz/workbench — Flowamaz workbench screens for Vue 3 (MIT).
// Shared basics, the workflow list and detail, the YAML editor, the runs screens and the approval inbox
// (docs/COMMUNITY-EDITION.md §10, phase 5b).

export * from './types';
export * from './api';
export * from './engineClient';
export * from './host';
export * from './date';
export * from './runs';
export * from './gateForm';
export * from './strings';
export * from './workflows';
export * from './connectors';
export * from './canvasCollab';

export { default as FmBadge } from './components/FmBadge.vue';
export { default as FmButton } from './components/FmButton.vue';
export { default as FmEmptyState } from './components/FmEmptyState.vue';
export { default as FmErrorState } from './components/FmErrorState.vue';
export { default as FmModal } from './components/FmModal.vue';
export { default as FmSkeleton } from './components/FmSkeleton.vue';
export { default as FmSpinner } from './components/FmSpinner.vue';

export { default as FmRunTimeline } from './components/FmRunTimeline.vue';
export { default as VariableInspector } from './components/VariableInspector.vue';
export { default as RunTable } from './components/RunTable.vue';
export { default as RunDetail } from './components/RunDetail.vue';
export { default as RunsPage } from './components/RunsPage.vue';
export { default as RunDetailPage } from './components/RunDetailPage.vue';
export { default as FmAlert } from './components/FmAlert.vue';
export { default as GateApproveDialog } from './components/GateApproveDialog.vue';
export { default as GateFormFields } from './components/GateFormFields.vue';
export { default as GatesPage } from './components/GatesPage.vue';
export { default as FmYamlEditor } from './components/FmYamlEditor.vue';
export { default as WorkflowGraphPreview } from './components/WorkflowGraphPreview.vue';
export { default as WorkflowEditorPage } from './components/WorkflowEditorPage.vue';
export { default as StartRunDialog } from './components/StartRunDialog.vue';
export { default as CreateWorkflowDialog } from './components/CreateWorkflowDialog.vue';
export { default as WorkflowsPage } from './components/WorkflowsPage.vue';
export { default as WorkflowDetailPage } from './components/WorkflowDetailPage.vue';
