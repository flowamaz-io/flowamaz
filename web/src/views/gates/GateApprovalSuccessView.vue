<script setup lang="ts">
import { computed } from 'vue';
import { useRoute } from 'vue-router';
import { CheckCircle, XCircle } from 'lucide-vue-next';

const route = useRoute();
const isApproved = computed(() => route.query.decision !== 'rejected');
</script>

<template>
  <div class="flex min-h-screen items-center justify-center bg-slate-50 px-4">
    <div class="max-w-md w-full rounded-2xl border border-slate-200 bg-white p-10 text-center shadow-sm">
      <div class="mb-6 flex justify-center">
        <CheckCircle
          v-if="isApproved"
          class="h-16 w-16 text-emerald-500"
        />
        <XCircle
          v-else
          class="h-16 w-16 text-slate-400"
        />
      </div>

      <h1 class="text-xl font-semibold text-slate-900">
        {{ isApproved ? 'Approval Recorded' : 'Rejection Recorded' }}
      </h1>
      <p class="mt-3 text-sm text-slate-500">
        {{
          isApproved
            ? 'Your approval has been recorded. The workflow will resume shortly.'
            : 'Your rejection has been recorded. The workflow owner will be notified.'
        }}
      </p>

      <div class="mt-8">
        <a
          href="/"
          class="inline-flex items-center gap-2 rounded-lg bg-primary-600 px-5 py-2.5 text-sm font-semibold text-white hover:bg-primary-700 transition-colors"
        >
          Go to Flowamaz Portal
        </a>
      </div>
    </div>
  </div>
</template>
