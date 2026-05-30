<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue';
import { EditorState } from '@codemirror/state';
import { EditorView, keymap, lineNumbers, hoverTooltip } from '@codemirror/view';
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands';
import { yaml } from '@codemirror/lang-yaml';
import { linter, lintGutter, type Diagnostic } from '@codemirror/lint';
import {
  autocompletion,
  completionKeymap,
  type CompletionContext,
} from '@codemirror/autocomplete';
import { oneDark } from '@codemirror/theme-one-dark';

const props = defineProps<{
  modelValue: string;
  diagnostics?: Array<{ from: number; to: number; message: string; severity: 'error' | 'warning' }>;
  darkMode?: boolean;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: string];
  ready: [];
}>();

const containerRef = ref<HTMLDivElement | null>(null);
let view: EditorView | null = null;

// Field hover tooltips for known YAML spec fields
const FIELD_DOCS: Record<string, string> = {
  type: 'Node type. Valid values: action, ai, human-gate, router, end, annotation, trigger, parallel, foreach, trycatch.',
  retry: 'Retry policy. Applies to action and ai nodes. Fields: maxAttempts, backoffSeconds.',
  timeout: 'Timeout in seconds for this node. Overrides workflow-level timeout.',
  condition: 'CEL expression evaluated at runtime. Must return boolean.',
  label: 'Human-readable display name for this node on the canvas.',
  id: 'Unique identifier for this node. Used in edges (from/to).',
  config: 'Node-type-specific configuration block.',
  edges: 'List of directed connections between nodes. Each edge has id, from, to.',
  nodes: 'List of workflow nodes. Each node has id, type, label, config.',
  trigger: 'Workflow trigger configuration. Defines how the workflow is started.',
  metadata: 'Workflow metadata: id, name, version, description, tags.',
  apiVersion: 'Must be "flowamaz/v1".',
  kind: 'Must be "Workflow".',
  spec: 'Top-level workflow specification block.',
  variables: 'Input/output variable declarations for the workflow.',
  sla: 'Service level agreement. Fields: warningThresholdSeconds, criticalThresholdSeconds.',
};

function buildHoverTooltip() {
  return hoverTooltip((view, pos) => {
    const line = view.state.doc.lineAt(pos);
    const lineText = line.text;
    const colonIdx = lineText.indexOf(':');
    if (colonIdx < 0) return null;
    const fieldName = lineText.slice(0, colonIdx).trim().replace(/^-\s*/, '');
    const doc = FIELD_DOCS[fieldName];
    if (!doc) return null;
    return {
      pos: line.from,
      end: line.from + colonIdx,
      above: true,
      create() {
        const dom = document.createElement('div');
        dom.className = 'cm-field-tooltip';
        dom.textContent = doc;
        return { dom };
      },
    };
  });
}

// Snippet completions on Ctrl+Space
const SNIPPETS = [
  { label: 'action', detail: 'action node', apply: `- id: action-1\n  type: action\n  label: "Action"\n  config:\n    url: ""\n    method: POST` },
  { label: 'ai', detail: 'AI node', apply: `- id: ai-1\n  type: ai\n  label: "AI Step"\n  config:\n    prompt: ""\n    outputVariable: result` },
  { label: 'gate', detail: 'human-gate node', apply: `- id: gate-1\n  type: human-gate\n  label: "Approval"\n  config:\n    assignees: []\n    timeoutSeconds: 86400` },
  { label: 'router', detail: 'router node', apply: `- id: router-1\n  type: router\n  label: "Route"\n  config:\n    conditions:\n      - condition: ""\n        target: ""` },
  { label: 'foreach', detail: 'forEach block', apply: `- id: foreach-1\n  type: foreach\n  label: "For Each"\n  config:\n    collection: "$items"\n    bodyNode: ""` },
  { label: 'trycatch', detail: 'try-catch block', apply: `- id: try-1\n  type: trycatch\n  label: "Try"\n  config:\n    tryNode: ""\n    catchNode: ""` },
];

function buildCompletion() {
  return autocompletion({
    override: [
      (ctx: CompletionContext) => {
        const word = ctx.matchBefore(/\w*/);
        if (!word || (word.from === word.to && !ctx.explicit)) return null;
        return {
          from: word.from,
          options: SNIPPETS.map(s => ({ label: s.label, detail: s.detail, apply: s.apply, type: 'keyword' })),
        };
      },
    ],
  });
}

function buildLinter() {
  return linter(() => {
    return (props.diagnostics ?? []).map(d => ({
      from: d.from,
      to: d.to,
      message: d.message,
      severity: d.severity,
    } as Diagnostic));
  });
}

function initEditor() {
  if (!containerRef.value) return;
  const extensions = [
    lineNumbers(),
    history(),
    yaml(),
    buildHoverTooltip(),
    buildCompletion(),
    keymap.of(completionKeymap),
    buildLinter(),
    lintGutter(),
    keymap.of([...defaultKeymap, ...historyKeymap, indentWithTab]),
    EditorView.updateListener.of(update => {
      if (update.docChanged) emit('update:modelValue', update.state.doc.toString());
    }),
    EditorView.theme({
      '&': { height: '100%', fontSize: '13px' },
      '.cm-scroller': { overflow: 'auto', fontFamily: '"Fira Code", "JetBrains Mono", monospace' },
      '.cm-field-tooltip': { background: '#ffffff', color: '#111827', border: '1px solid #e5e7eb', padding: '6px 10px', borderRadius: '6px', fontSize: '12px', maxWidth: '320px', lineHeight: '1.5', boxShadow: '0 1px 4px rgba(0,0,0,0.08)' },
    }),
  ];
  if (props.darkMode !== false) extensions.push(oneDark);

  view = new EditorView({
    state: EditorState.create({
      doc: props.modelValue,
      extensions,
    }),
    parent: containerRef.value,
  });
  emit('ready');
}

// Sync external value changes into the editor without clobbering undo history
watch(() => props.modelValue, (newVal) => {
  if (!view) return;
  const current = view.state.doc.toString();
  if (current === newVal) return;
  view.dispatch({
    changes: { from: 0, to: current.length, insert: newVal },
  });
});

onMounted(initEditor);
onUnmounted(() => { view?.destroy(); view = null; });

function setCursor(pos: number) {
  view?.dispatch({ selection: { anchor: pos } });
  view?.focus();
}

defineExpose({ setCursor });
</script>

<template>
  <div
    ref="containerRef"
    class="h-full w-full overflow-hidden"
  />
</template>
