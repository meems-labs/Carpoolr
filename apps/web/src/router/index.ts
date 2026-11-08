import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(),
  scrollBehavior: () => ({ top: 0 }),
  routes: [
    {
      path: '/',
      component: () => import('../views/HomeView.vue'),
    },
    { path: '/profil', component: () => import('../views/ProfileView.vue') },
    { path: '/fahrten/neu', component: () => import('../views/CreateTripView.vue') },
    { path: '/fahrten/:id', component: () => import('../views/TripDetailView.vue') },
    { path: '/meine-fahrten', component: () => import('../views/MyTripsView.vue') },
    { path: '/meine-anfragen', component: () => import('../views/MyBookingsView.vue') },
    {
      path: '/eingegangene-anfragen',
      component: () => import('../views/IncomingBookingsView.vue'),
    },
    { path: '/kosten', component: () => import('../views/CostsView.vue') },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})

export default router
