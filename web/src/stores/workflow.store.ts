import { defineStore } from 'pinia';
import { ref } from 'vue';
import { workflowService } from '@/services/workflow.service';
import { instanceService, type InstanceListFilters } from '@/services/instance.service';
import { useWorkspaceStore } from '@/stores/workspace.store';
import { toUserFacingError } from '@/utils/error.util';
import type {
  CreateWorkflowDefinitionRequest,
  InstanceDetailResponse,
  InstanceListItem,
  InstanceStatus,
  TimelineResponse,
  TriggerInstanceRequest,
  TriggerInstanceResponse,
  WorkflowDefinitionListItem,
  WorkflowDefinitionResponse,
  WorkflowVersionResponse,
} from '@/types';

/**
 * Workflow + instance state. Reads the active workspace id from the workspace store so every
 * call is correctly scoped. Loaders manage loading/error; mutating actions throw for the caller
 * to surface a toast.
 */
export const useWorkflowStore = defineStore('workflow', () => {
  const workspace = useWorkspaceStore();

  const workflows = ref<WorkflowDefinitionListItem[]>([]);
  const currentWorkflow = ref<WorkflowDefinitionResponse | null>(null);
  const versions = ref<WorkflowVersionResponse[]>([]);
  const instances = ref<InstanceListItem[]>([]);
  const currentInstance = ref<InstanceDetailResponse | null>(null);
  const timeline = ref<TimelineResponse | null>(null);
  const loading = ref(false);
  const error = ref<string | null>(null);
  const yamlVersion = ref(0);

  function requireWorkspace(): string {
    const id = workspace.currentWorkspaceId;
    if (!id) throw new Error('Select a workspace first.');
    return id;
  }

  async function run<T>(work: () => Promise<T>): Promise<T | null> {
    loading.value = true;
    error.value = null;
    try {
      return await work();
    } catch (err) {
      error.value = toUserFacingError(err).message;
      return null;
    } finally {
      loading.value = false;
    }
  }

  async function loadWorkflows(): Promise<void> {
    await run(async () => {
      const result = await workflowService.list(requireWorkspace());
      workflows.value = result.data;
    });
  }

  async function loadWorkflow(id: string): Promise<void> {
    await run(async () => {
      currentWorkflow.value = await workflowService.get(requireWorkspace(), id);
    });
  }

  async function createWorkflow(payload: CreateWorkflowDefinitionRequest): Promise<WorkflowDefinitionResponse> {
    const created = await workflowService.create(requireWorkspace(), payload);
    workflows.value.unshift({
      id: created.id,
      name: created.name,
      slug: created.slug,
      status: created.status,
      healthScore: created.healthScore,
      updatedAt: created.updatedAt,
    });
    return created;
  }

  async function publishWorkflow(id: string): Promise<WorkflowVersionResponse> {
    const version = await workflowService.publish(requireWorkspace(), id);
    if (currentWorkflow.value?.id === id) {
      currentWorkflow.value = { ...currentWorkflow.value, status: 'Published', currentVersion: version.commitSha };
    }
    return version;
  }

  async function loadVersions(id: string): Promise<void> {
    await run(async () => {
      versions.value = await workflowService.versions(requireWorkspace(), id);
    });
  }

  async function loadInstances(filters: InstanceListFilters = {}): Promise<void> {
    await run(async () => {
      const result = await instanceService.list(requireWorkspace(), filters);
      instances.value = result.data;
    });
  }

  async function triggerInstance(payload: TriggerInstanceRequest): Promise<TriggerInstanceResponse> {
    return instanceService.trigger(requireWorkspace(), payload);
  }

  async function loadInstanceDetail(id: string): Promise<void> {
    await run(async () => {
      currentInstance.value = await instanceService.get(requireWorkspace(), id);
    });
  }

  async function loadTimeline(id: string): Promise<void> {
    await run(async () => {
      timeline.value = await instanceService.timeline(requireWorkspace(), id);
    });
  }

  async function cancelInstance(id: string): Promise<void> {
    currentInstance.value = await instanceService.cancel(requireWorkspace(), id);
    syncListStatus(id, currentInstance.value.status);
  }

  async function retryInstance(id: string): Promise<TriggerInstanceResponse> {
    return instanceService.retry(requireWorkspace(), id);
  }

  /** Applied by the live WebSocket — updates the badge in the list + the open detail view. */
  function applyStatusUpdate(instanceId: string, status: InstanceStatus, nodeId: string | null): void {
    syncListStatus(instanceId, status);
    if (currentInstance.value?.id === instanceId) {
      currentInstance.value = { ...currentInstance.value, status, currentNodeId: nodeId };
    }
  }

  function syncListStatus(instanceId: string, status: InstanceStatus): void {
    const item = instances.value.find((i) => i.id === instanceId);
    if (item) item.status = status;
  }

  function bumpYamlVersion() { yamlVersion.value++; }

  return {
    workflows,
    currentWorkflow,
    versions,
    instances,
    currentInstance,
    timeline,
    loading,
    error,
    yamlVersion,
    bumpYamlVersion,
    loadWorkflows,
    loadWorkflow,
    createWorkflow,
    publishWorkflow,
    loadVersions,
    loadInstances,
    triggerInstance,
    loadInstanceDetail,
    loadTimeline,
    cancelInstance,
    retryInstance,
    applyStatusUpdate,
  };
});
