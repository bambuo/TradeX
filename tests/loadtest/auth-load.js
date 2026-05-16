import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

/**
 * 登录吞吐压测
 *
 * 模拟集中登录场景（如系统重启后用户重连）。重点验证：
 * - JwtService 生成 token 的吞吐
 * - BCrypt.Verify 不成为瓶颈（BCrypt 是 CPU 密集的）
 * - DB 连接池在登录高峰下是否撑得住
 *
 * 阈值：p95 < 300ms，错误率 < 5%（BCrypt 慢是预期）
 */

const API = __ENV.API || 'http://localhost';
const USERNAME = __ENV.LOAD_USER || 'admin';
const PASSWORD = __ENV.LOAD_PASS;
if (!PASSWORD) throw new Error('环境变量 LOAD_PASS 必填');

const loginFail = new Counter('login_fail');

export const options = {
  scenarios: {
    burst: {
      executor: 'ramping-arrival-rate',
      startRate: 5,                // 起 5 RPS
      timeUnit: '1s',
      preAllocatedVUs: 100,
      stages: [
        { duration: '30s', target: 20 },
        { duration: '1m',  target: 50 },
        { duration: '30s', target: 100 },  // 突发 100 RPS 登录
        { duration: '30s', target: 0 },
      ],
    },
  },
  thresholds: {
    'http_req_duration{name:login}': ['p(95)<300'],
    'http_req_failed{name:login}':   ['rate<0.05'],
  },
};

export default function () {
  const r = http.post(`${API}/api/auth/login`,
    JSON.stringify({ username: USERNAME, password: PASSWORD }),
    { headers: { 'Content-Type': 'application/json' }, tags: { name: 'login' } });
  if (!check(r, { 'login 200': r => r.status === 200 })) {
    loginFail.add(1);
  }
}
