<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import { createCanvasModel, parseWorkflowGraph, useCytoscapeCanvas } from '@flowamaz/canvas';

// A read-only picture of a workflow's YAML, drawn with @flowamaz/canvas. Keeps the last graph that parsed, so a
// half-typed edit doesn't blank it; `valid` says whether the current text could be drawn.
const props = defineProps<{ yaml: string }>();
const emit = defineEmits<{ valid: [ok: boolean] }>();

const container = ref<HTMLElement | null>(null);
const canvas = useCytoscapeCanvas(createCanvasModel(), container);

function draw(yaml: string): void {
  const graph = parseWorkflowGraph(yaml);
  emit('valid', graph !== null);
  if (!graph || !canvas.cy.value) return;
  canvas.loadFromNodes(graph.nodes, graph.edges);
  canvas.runLayout();
  canvas.fitView();
}

let pending: ReturnType<typeof setTimeout> | null = null;
watch(() => props.yaml, (yaml) => {
  if (pending) clearTimeout(pending);
  pending = setTimeout(() => draw(yaml), 300);
});

onMounted(() => {
  canvas.setEditable(false);
  draw(props.yaml);
});
</script>

<template>
  <div
    ref="container"
    class="h-full min-h-64 w-full bg-slate-50"
    data-testid="workflow-graph-preview"
  />
</template>
