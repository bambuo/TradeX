import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import client from '../api/client'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/setup',
      name: 'Setup',
      component: () => import('../views/SetupWizardView.vue')
    },
    {
      path: '/login',
      name: 'Login',
      component: () => import('../views/LoginView.vue')
    },
    {
      path: '/mfa/setup',
      name: 'MfaSetup',
      component: () => import('../views/MfaSetupView.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/',
      component: () => import('../layouts/AppLayout.vue'),
      meta: { requiresAuth: true },
      children: [
        {
          path: '',
          name: 'Dashboard',
          component: () => import('../views/DashboardView.vue')
        },
        {
          path: 'traders',
          name: 'Traders',
          component: () => import('../views/TradersView.vue')
        },
        {
          path: 'traders/:traderId/exchanges',
          name: 'ExchangeAccounts',
          component: () => import('../views/ExchangeAccountsView.vue')
        },
        {
          path: 'traders/:traderId/strategies',
          name: 'Strategies',
          component: () => import('../views/StrategyView.vue')
        },
        {
          path: 'traders/:traderId/positions',
          name: 'Positions',
          component: () => import('../views/PositionsView.vue')
        },
        {
          path: 'traders/:traderId/orders',
          name: 'Orders',
          component: () => import('../views/OrdersView.vue')
        },
        {
          path: 'traders/:traderId/strategies/:strategyId/backtest',
          name: 'Backtest',
          component: () => import('../views/BacktestView.vue')
        },
        {
          path: 'audit-logs',
          name: 'AuditLogs',
          component: () => import('../views/AuditLogView.vue')
        },
        {
          path: 'notifications',
          name: 'Notifications',
          component: () => import('../views/NotificationChannelsView.vue')
        },
        {
          path: 'users',
          name: 'Users',
          component: () => import('../views/UsersView.vue')
        }
      ]
    }
  ]
})

let _initChecked = false
let _isInitialized = true

router.beforeEach(async (to, _from, next) => {
  if (!_initChecked && to.name !== 'Setup') {
    try {
      const { data } = await client.get('/setup/status')
      _isInitialized = data.isInitialized
    } catch {
      _isInitialized = true
    }
    _initChecked = true
  }

  if (!_isInitialized && to.name !== 'Setup') {
    next('/setup')
    return
  }

  const auth = useAuthStore()
  if (to.meta.requiresAuth && !auth.isAuthenticated) {
    next('/login')
  } else {
    next()
  }
})

export function resetInitCheck() {
  _initChecked = false
  _isInitialized = true
}

export default router
