import { createRouter, createWebHistory } from 'vue-router'

// Placeholder route — feature routes (Trips, recurring rides) come in later passes.
const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      component: () => import('../views/HomeView.vue'),
    },
  ],
})

export default router