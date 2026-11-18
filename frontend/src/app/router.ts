import { createRouter, createWebHistory } from 'vue-router'

import { useAuthStore } from '@/features/auth/store'

declare module 'vue-router' {
  interface RouteMeta {
    /** Reachable without signing in. */
    public?: boolean
    /** Only for signed-out visitors; signed-in users go home. */
    guestOnly?: boolean
    /** Platform admins only; others go home. */
    admin?: boolean
  }
}

export const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/sign-in',
      name: 'sign-in',
      component: () => import('@/features/auth/views/SignInView.vue'),
      meta: { public: true, guestOnly: true },
    },
    {
      path: '/register',
      name: 'register',
      component: () => import('@/features/auth/views/RegisterView.vue'),
      meta: { public: true, guestOnly: true },
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
        {
          path: 'invite/:token',
          name: 'invite',
          component: () => import('@/features/boards/views/AcceptInviteView.vue'),
          props: true,
        },
        {
          path: 'admin/users',
          name: 'admin-users',
          component: () => import('@/features/admin/views/UsersView.vue'),
          meta: { admin: true },
        },
        {
          path: 'profile',
          name: 'profile',
          component: () => import('@/features/profile/views/ProfileView.vue'),
        },
      ],
    },
  ],
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()

  if (to.meta.guestOnly) {
    await auth.ensureLoaded()
    return auth.isSignedIn ? { name: 'home' } : true
  }
  if (to.meta.public) return true

  await auth.ensureLoaded()
  if (!auth.isSignedIn) return { name: 'sign-in', query: { next: to.fullPath } }
  if (to.meta.admin && !auth.user?.isPlatformAdmin) return { name: 'home' }
  return true
})
