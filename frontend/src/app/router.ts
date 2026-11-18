import { createRouter, createWebHistory } from 'vue-router'

import { useAuthStore } from '@/features/auth/store'

export const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/sign-in',
      name: 'sign-in',
      component: () => import('@/features/auth/views/SignInView.vue'),
      meta: { public: true },
    },
    {
      path: '/',
      component: () => import('./layouts/AppLayout.vue'),
      children: [
        {
          path: '',
          name: 'home',
          component: () => import('@/features/boards/views/HomeView.vue'),
        },
        {
          path: 'boards/:boardId',
          name: 'board',
          component: () => import('@/features/boards/views/BoardView.vue'),
          props: true,
        },
      ],
    },
  ],
})

router.beforeEach(async (to) => {
  if (to.meta.public) return true

  const auth = useAuthStore()
  await auth.ensureLoaded()
  return auth.isSignedIn ? true : { name: 'sign-in', query: { next: to.fullPath } }
})
