<script setup lang="ts">
import FmToast from '@/components/common/FmToast.vue';
</script>

<template>
  <a
    href="#main-content"
    class="sr-only focus:not-sr-only focus:fixed focus:top-4 focus:left-4 focus:z-50 focus:rounded-lg focus:bg-white focus:px-4 focus:py-2 focus:text-primary-700 focus:shadow-lg"
  >Skip to main content</a>
  <RouterView />
  <FmToast />
</template>
