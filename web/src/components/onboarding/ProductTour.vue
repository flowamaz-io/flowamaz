<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import TourStep, { type SpotlightRect } from './TourStep.vue';
import { preferencesService } from '@/services/preferences.service';

// Custom first-run product tour — no external tour library.
// Shows automatically once per workspace after the onboarding wizard completes,
// and never again (gated by the `tour_completed_{workspaceId}` user preference).
// Keyboard: Escape skips, ArrowRight advances.

const props = defineProps<{
  workspaceId: string;
  /** When true the tour shows immediately if not already completed (used after the wizard). */
  autoStart?: boolean;
}>();

const emit = defineEmits<{ finished: [] }>();

const router = useRouter();

interface TourStepDef {
  title: string;
  body: string;
  /** CSS selector of the element to spotlight; null centres the card. */
  selector: string | null;
  primaryActionLabel?: string;
  /** Called when the secondary "primary action" button is pressed. */
  onPrimaryAction?: () => void;
}

const STEPS: TourStepDef[] = [
  {
    title: 'Your workspace sidebar',
    body: 'This is your workspace sidebar. Navigate between workflows, instances, and settings from here.',
    selector: '[data-tour="sidebar"]',
  },
  {
    title: 'Create your first workflow',
    body: 'Create your first workflow in seconds. Describe what you want to automate in plain English.',
    selector: '[data-tour="creation-methods"]',
    primaryActionLabel: 'Try it now',
    onPrimaryAction: () => {
      void complete();
      void router.push({ name: 'workflow-new', query: { method: 'nl' } });
    },
  },
  {
    title: 'Browse the Library',
    body: 'The Library has 13 official connectors — Slack, GitHub, PostgreSQL, and more — ready to power your workflows.',
    selector: '[data-tour="library-nav"]',
  },
  {
    title: 'Create your first workflow',
    body: 'Choose how you want to build — describe it in plain English, upload a document, or start from a template.',
    selector: '[data-tour="creation-methods"]',
  },
  {
    title: 'Ready to automate',
    body: 'Start from a pre-built template or explore on your own.',
    selector: '[data-tour="creation-methods"]',
    primaryActionLabel: 'Start from template',
    onPrimaryAction: () => {
      void complete();
      void router.push({ name: 'workflow-new', query: { method: 'template' } });
    },
  },
];

const prefKey = computed(() => `tour_completed_${props.workspaceId}`);

const active = ref(false);
const stepIndex = ref(0);
const rect = ref<SpotlightRect | null>(null);

const current = computed(() => STEPS[stepIndex.value]);

function measure(): void {
  const sel = current.value?.selector;
  if (!sel) {
    rect.value = null;
    return;
  }
  const el = document.querySelector(sel);
  if (!el) {
    rect.value = null;
    return;
  }
  const r = el.getBoundingClientRect();
  rect.value = { top: r.top, left: r.left, width: r.width, height: r.height };
}

function goTo(index: number): void {
  stepIndex.value = index;
  // Allow the DOM/route to settle before measuring the target element.
  requestAnimationFrame(measure);
}

function next(): void {
  if (stepIndex.value >= STEPS.length - 1) {
    void complete();
    return;
  }
  goTo(stepIndex.value + 1);
}

function back(): void {
  if (stepIndex.value > 0) goTo(stepIndex.value - 1);
}

async function persistCompleted(): Promise<void> {
  try {
    await preferencesService.set<boolean>(prefKey.value, true);
  } catch {
    // Non-fatal: the tour simply may re-appear next session if persistence failed.
  }
}

async function complete(): Promise<void> {
  if (!active.value) return;
  active.value = false;
  await persistCompleted();
  emit('finished');
}

function onKeydown(event: KeyboardEvent): void {
  if (!active.value) return;
  if (event.key === 'Escape') {
    event.preventDefault();
    void complete();
  } else if (event.key === 'ArrowRight') {
    event.preventDefault();
    next();
  }
}

async function maybeStart(): Promise<void> {
  if (!props.autoStart || !props.workspaceId) return;
  try {
    const done = await preferencesService.get<boolean>(prefKey.value);
    if (done === true) return;
  } catch {
    // If we cannot read the preference, fail closed (do not nag the user with a tour).
    return;
  }
  active.value = true;
  goTo(0);
}

function onResize(): void {
  if (active.value) measure();
}

onMounted(() => {
  document.addEventListener('keydown', onKeydown);
  window.addEventListener('resize', onResize);
  window.addEventListener('scroll', onResize, true);
  void maybeStart();
});

onBeforeUnmount(() => {
  document.removeEventListener('keydown', onKeydown);
  window.removeEventListener('resize', onResize);
  window.removeEventListener('scroll', onResize, true);
});

// Exposed for tests and for an explicit "Take the tour" entry point.
defineExpose({ start: () => { active.value = true; goTo(0); } });
</script>

<template>
  <TourStep
    v-if="active && current"
    :title="current.title"
    :body="current.body"
    :rect="rect"
    :step-index="stepIndex"
    :step-count="STEPS.length"
    :is-first="stepIndex === 0"
    :is-last="stepIndex === STEPS.length - 1"
    :primary-action-label="current.primaryActionLabel"
    @next="next"
    @back="back"
    @skip="complete"
    @primary-action="current.onPrimaryAction?.()"
  />
</template>
