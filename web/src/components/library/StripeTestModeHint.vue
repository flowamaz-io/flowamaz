<script setup lang="ts">
import { FlaskConical } from 'lucide-vue-next';
import { isStripeTestMode } from '@/utils/marketplace.util';

const show = isStripeTestMode();
</script>

<template>
  <p
    v-if="show"
    class="inline-flex items-center gap-1.5 rounded-md bg-amber-50 px-2 py-1 text-xs text-amber-800"
    data-test="stripe-test-hint"
  >
    <FlaskConical class="h-3.5 w-3.5 shrink-0" />
    Stripe test mode — use card 4242 4242 4242 4242, any future expiry and any CVC.
  </p>
</template>
