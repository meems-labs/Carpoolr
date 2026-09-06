<script setup lang="ts">
// Root shell — feature routes render inside <router-view>.
</script>

<template>
  <main class="app">
    <router-view />
  </main>
</template>

<style scoped>
.app {
  font-family: system-ui, sans-serif;
  max-width: 40rem;
  margin: 2rem auto;
  padding: 0 1rem;
}
</style>