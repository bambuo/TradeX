import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';

/**
 * Dashboard 读路径压测
 *
 * 模拟用户登录后频繁刷新 dashboard 与持仓订单列表。
 * 这些是 API 端最热的端点，反映 DB 查询 + JSON 序列化的整体性能。
 *
 * 阈值：p99 < 500ms，错误率 < 1%；满足才能称"读路径可用"
 */

const API = __ENV.API || 'http://localhost';
const TOKEN = __ENV.TOKEN;
if (!TOKEN) throw new Error('环境变量 TOKEN 必填（运行前 export TOKEN=...）');

const errors = new Counter('biz_errors');
const dashboardLatency = new Trend('dashboard_latency_ms');

export const options = {
  stages: [
    { duration: '30s', target: 10 },   // 升温
    { duration: '2m',  target: 50 },   // 加压
    { duration: '2m',  target: 100 },  // 满负荷
    { duration: '30s', target: 0 },    // 降温
  ],
  thresholds: {
    http_req_duration: ['p(99)<500'],         // P99 < 500ms
    http_req_failed:   ['rate<0.01'],          // 错误率 < 1%
    biz_errors:        ['count<50'],           // 业务错误总数
    dashboard_latency_ms: ['p(95)<400'],      // dashboard 端点单独阈值
  },
};

const headers = {
  Authorization: `Bearer ${TOKEN}`,
  'Content-Type': 'application/json',
};

export default function () {
  // 单个虚拟用户的一轮行为：刷新 dashboard → 查持仓 → 查订单
  const tag = { name: 'dashboard' };

  const r1 = http.get(`${API}/api/dashboard`, { headers, tags: tag });
  dashboardLatency.add(r1.timings.duration);
  check(r1, {
    'dashboard 200': r => r.status === 200,
    'dashboard has body': r => r.body && r.body.length > 0,
  }) || errors.add(1);

  const r2 = http.get(`${API}/api/positions`, { headers, tags: { name: 'positions' } });
  check(r2, { 'positions 200': r => r.status === 200 }) || errors.add(1);

  const r3 = http.get(`${API}/api/orders?limit=20`, { headers, tags: { name: 'orders' } });
  check(r3, { 'orders 200': r => r.status === 200 }) || errors.add(1);

  sleep(Math.random() * 2 + 1);  // 1-3s 模拟用户思考时间
}

export function handleSummary(data) {
  console.log('\n=== TradeX Dashboard 压测结果 ===');
  console.log(`总请求: ${data.metrics.http_reqs.values.count}`);
  console.log(`错误率: ${(data.metrics.http_req_failed.values.rate * 100).toFixed(2)}%`);
  console.log(`P50:    ${data.metrics.http_req_duration.values['p(50)'].toFixed(0)}ms`);
  console.log(`P95:    ${data.metrics.http_req_duration.values['p(95)'].toFixed(0)}ms`);
  console.log(`P99:    ${data.metrics.http_req_duration.values['p(99)'].toFixed(0)}ms`);
  console.log(`Max:    ${data.metrics.http_req_duration.values.max.toFixed(0)}ms`);
  return { stdout: '' };  // 抑制 k6 默认输出
}
