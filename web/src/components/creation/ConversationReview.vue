<template>
  <div class="space-y-5">
    <!-- What the AI read, and how sure it is -->
    <div class="flex flex-wrap items-center gap-2 text-xs">
      <span
        :class="[
          'rounded-full px-2 py-0.5 font-medium',
          lowConfidence ? 'bg-amber-100 text-amber-700' : 'bg-emerald-100 text-emerald-700',
        ]"
        data-test="conversation-confidence"
      >
        {{ Math.round(model.confidence * 100) }}% confident
      </span>
      <span
        v-for="p in model.participants"
        :key="p.name"
        class="rounded-full bg-white px-2 py-0.5 text-gray-700 ring-1 ring-gray-200"
      >
        {{ p.name }} · {{ p.messageCount }} {{ p.messageCount === 1 ? 'message' : 'messages' }}
      </span>
    </div>

    <div
      v-if="warnings.length > 0"
      role="status"
      class="rounded border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800"
    >
      <p class="mb-1 font-medium">
        Check before generating
      </p>
      <ul class="list-disc space-y-0.5 pl-5">
        <li
          v-for="w in warnings"
          :key="w"
        >
          {{ w }}
        </li>
      </ul>
    </div>

    <!-- Basics -->
    <div class="grid gap-3 sm:grid-cols-2">
      <div class="space-y-1">
        <label
          for="conv-name"
          class="text-xs font-medium text-gray-600"
        >Workflow name</label>
        <input
          id="conv-name"
          v-model="model.workflowName"
          type="text"
          :class="inputClass"
        >
      </div>
      <div class="space-y-1">
        <label
          for="conv-trigger"
          class="text-xs font-medium text-gray-600"
        >What starts it</label>
        <input
          id="conv-trigger"
          v-model="model.trigger"
          type="text"
          placeholder="e.g. A purchase request is submitted"
          :class="inputClass"
        >
      </div>
      <div class="space-y-1 sm:col-span-2">
        <label
          for="conv-summary"
          class="text-xs font-medium text-gray-600"
        >Summary</label>
        <input
          id="conv-summary"
          v-model="model.summary"
          type="text"
          :class="inputClass"
        >
      </div>
    </div>

    <!-- Steps -->
    <section aria-labelledby="conv-steps-heading">
      <h3
        id="conv-steps-heading"
        class="mb-2 text-sm font-semibold text-gray-800"
      >
        Steps, in order
      </h3>
      <ol class="space-y-2">
        <li
          v-for="(step, i) in model.steps"
          :key="i"
          class="rounded border border-gray-200 bg-gray-50 p-2"
          data-test="conversation-step"
        >
          <div class="flex items-start gap-2">
            <span class="mt-2 w-5 shrink-0 text-right text-xs text-gray-500">{{ i + 1 }}.</span>
            <div class="grid flex-1 gap-2 sm:grid-cols-[2fr_1fr_1fr]">
              <input
                v-model="step.title"
                type="text"
                :aria-label="`Step ${i + 1}`"
                placeholder="What happens"
                :class="inputClass"
              >
              <input
                :value="step.owner ?? ''"
                type="text"
                :aria-label="`Step ${i + 1} owner`"
                placeholder="Who does it"
                :class="inputClass"
                @input="step.owner = ($event.target as HTMLInputElement).value"
              >
              <input
                :value="step.system ?? ''"
                type="text"
                :aria-label="`Step ${i + 1} system`"
                placeholder="System (optional)"
                :class="inputClass"
                @input="step.system = ($event.target as HTMLInputElement).value"
              >
            </div>
            <div class="flex shrink-0 gap-1">
              <button
                type="button"
                :aria-label="`Move step ${i + 1} up`"
                :disabled="i === 0"
                :class="iconButtonClass"
                @click="move(model.steps, i, -1)"
              >
                <ArrowUp class="h-3.5 w-3.5" />
              </button>
              <button
                type="button"
                :aria-label="`Move step ${i + 1} down`"
                :disabled="i === model.steps.length - 1"
                :class="iconButtonClass"
                @click="move(model.steps, i, 1)"
              >
                <ArrowDown class="h-3.5 w-3.5" />
              </button>
              <button
                type="button"
                :aria-label="`Remove step ${i + 1}`"
                :class="iconButtonClass"
                @click="model.steps.splice(i, 1)"
              >
                <X class="h-3.5 w-3.5" />
              </button>
            </div>
          </div>
          <p
            v-if="step.saidBy"
            class="mt-1 pl-7 text-xs text-gray-500"
          >
            From {{ step.saidBy }}
          </p>
        </li>
      </ol>
      <button
        type="button"
        :class="addButtonClass"
        @click="model.steps.push(emptyStep())"
      >
        <Plus class="h-3.5 w-3.5" /> Add step
      </button>
    </section>

    <!-- Conditions -->
    <section aria-labelledby="conv-decisions-heading">
      <h3
        id="conv-decisions-heading"
        class="mb-2 text-sm font-semibold text-gray-800"
      >
        Conditions
      </h3>
      <p
        v-if="model.decisions.length === 0"
        class="mb-2 text-xs text-gray-500"
      >
        No conditions were found in the thread. Add one if a step depends on something (e.g. "amount above $5,000" → "CFO approves").
      </p>
      <ul class="space-y-2">
        <li
          v-for="(d, i) in model.decisions"
          :key="i"
          class="flex items-center gap-2"
        >
          <span class="text-xs text-gray-500">If</span>
          <input
            v-model="d.condition"
            type="text"
            :aria-label="`Condition ${i + 1}`"
            placeholder="amount above $5,000"
            :class="inputClass"
          >
          <span class="text-xs text-gray-500">then</span>
          <input
            v-model="d.outcome"
            type="text"
            :aria-label="`Condition ${i + 1} outcome`"
            placeholder="CFO approves"
            :class="inputClass"
          >
          <button
            type="button"
            :aria-label="`Remove condition ${i + 1}`"
            :class="iconButtonClass"
            @click="model.decisions.splice(i, 1)"
          >
            <X class="h-3.5 w-3.5" />
          </button>
        </li>
      </ul>
      <button
        type="button"
        :class="addButtonClass"
        @click="model.decisions.push(emptyDecision())"
      >
        <Plus class="h-3.5 w-3.5" /> Add condition
      </button>
    </section>

    <!-- Approvers -->
    <section aria-labelledby="conv-approvers-heading">
      <h3
        id="conv-approvers-heading"
        class="mb-2 text-sm font-semibold text-gray-800"
      >
        Approvers
      </h3>
      <p
        v-if="model.approvers.length === 0"
        class="mb-2 text-xs text-gray-500"
      >
        Nobody was named as an approver. Add one if a step needs sign-off.
      </p>
      <ul class="space-y-2">
        <li
          v-for="(a, i) in model.approvers"
          :key="i"
          class="flex items-center gap-2"
        >
          <input
            v-model="a.who"
            type="text"
            :aria-label="`Approver ${i + 1}`"
            placeholder="Person or role"
            :class="inputClass"
          >
          <input
            :value="a.when ?? ''"
            type="text"
            :aria-label="`Approver ${i + 1} when`"
            placeholder="When (e.g. above $5,000)"
            :class="inputClass"
            @input="a.when = ($event.target as HTMLInputElement).value"
          >
          <button
            type="button"
            :aria-label="`Remove approver ${i + 1}`"
            :class="iconButtonClass"
            @click="model.approvers.splice(i, 1)"
          >
            <X class="h-3.5 w-3.5" />
          </button>
        </li>
      </ul>
      <button
        type="button"
        :class="addButtonClass"
        @click="model.approvers.push(emptyApprover())"
      >
        <Plus class="h-3.5 w-3.5" /> Add approver
      </button>
    </section>

    <!-- Systems -->
    <div class="space-y-1">
      <label
        for="conv-systems"
        class="text-xs font-medium text-gray-600"
      >Systems (comma separated)</label>
      <input
        id="conv-systems"
        :value="model.systems.join(', ')"
        type="text"
        placeholder="e.g. SAP, Slack"
        :class="inputClass"
        @change="model.systems = parseList(($event.target as HTMLInputElement).value)"
      >
    </div>

    <!-- Agreements -->
    <section
      v-if="model.agreements.length > 0"
      aria-labelledby="conv-agreed-heading"
    >
      <h3
        id="conv-agreed-heading"
        class="mb-2 text-sm font-semibold text-gray-800"
      >
        What was agreed
      </h3>
      <ul class="space-y-1">
        <li
          v-for="(text, i) in model.agreements"
          :key="i"
          class="flex items-center gap-2 text-sm text-gray-700"
        >
          <Check class="h-3.5 w-3.5 shrink-0 text-emerald-600" />
          <span class="flex-1">{{ text }}</span>
          <button
            type="button"
            :aria-label="`Remove agreement ${i + 1}`"
            :class="iconButtonClass"
            @click="model.agreements.splice(i, 1)"
          >
            <X class="h-3.5 w-3.5" />
          </button>
        </li>
      </ul>
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { ArrowDown, ArrowUp, Check, Plus, X } from 'lucide-vue-next';
import type { ConversationExtraction } from '@/services/creation.service';
import { emptyApprover, emptyDecision, emptyStep, move, parseList } from '@/utils/conversationReview.util';

defineProps<{ warnings: string[] }>();
const model = defineModel<ConversationExtraction>({ required: true });

const lowConfidence = computed(() => model.value.confidence < 0.6);

const inputClass =
  'w-full rounded border border-gray-300 bg-white px-2 py-1.5 text-sm text-gray-900 placeholder-gray-400 outline-none transition-colors focus:border-teal-500';
const iconButtonClass =
  'rounded p-1.5 text-gray-600 hover:bg-gray-100 hover:text-gray-900 disabled:cursor-not-allowed disabled:opacity-30';
const addButtonClass =
  'mt-2 inline-flex items-center gap-1 rounded px-2 py-1 text-xs font-medium text-teal-700 hover:bg-gray-100';
</script>
