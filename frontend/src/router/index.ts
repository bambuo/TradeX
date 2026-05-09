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
          component: () => import('../views/DashboardView.vue'),
          meta: { label: '仪表盘' }
        },
        {
          path: 'traders',
          name: 'Traders',
          component: () => import('../views/TradersView.vue'),
          meta: { label: '交易员' }
        },
        {
          path: 'traders/:traderId/strategies',
          name: 'Strategies',
          component: () => import('../views/StrategyView.vue'),
          meta: { label: '策略' }
        },
        {
          path: 'traders/:traderId/positions',
          name: 'Positions',
          component: () => import('../views/PositionsView.vue'),
          meta: { label: '持仓' }
        },
        {
          path: 'backtests',
          name: 'BacktestList',
          component: () => import('../views/BacktestListView.vue'),
          meta: { label: '回测' }
        },
        {
          path: 'backtests/tasks/:taskId',
          name: 'Backtest',
          component: () => import('../views/BacktestView.vue'),
          meta: { label: '详情' }
        },
        {
          path: 'traders/:traderId/orders',
          name: 'Orders',
          component: () => import('../views/OrdersView.vue'),
          meta: { label: '订单' }
        },
        {
          path: 'exchanges',
          name: 'Exchanges',
          component: () => import('../views/ExchangesView.vue'),
          meta: { label: '交易所' }
        },
        {
          path: 'strategies',
          name: 'GlobalStrategies',
          component: () => import('../views/StrategiesView.vue'),
          meta: { label: '策略模板' }
        },
        {
          path: 'audit-logs',
          name: 'AuditLogs',
          component: () => import('../views/AuditLogView.vue'),
          meta: { label: '审计日志' }
        },
        {
          path: 'notifications',
          name: 'Notifications',
          component: () => import('../views/NotificationChannelsView.vue'),
          meta: { label: '通知渠道' }
        },
        {
          path: 'users',
          name: 'Users',
          component: () => import('../views/UsersView.vue'),
          meta: { label: '用户管理' }
        },
        {
          path: 'settings',
          name: 'Settings',
          component: () => import('../views/SettingsView.vue'),
          meta: { label: '系统设置' }
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
