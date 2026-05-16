import ws from 'k6/ws';
import { check } from 'k6';

/**
 * SignalR 广播压测
 *
 * 模拟 N 个用户同时连接 /hubs/trading 并加入各自的 trader group。
 * 验证 SignalR Hub + Redis backplane 在多连接下的稳定性。
 *
 * 注意：此脚本走 WebSocket 原生协议，不解析 SignalR 协议帧。
 * 仅验证连接生命周期，不模拟交易事件。
 *
 * 真正端到端的事件验证用 RUNBOOK §5 的故障演练。
 */

const API = __ENV.API || 'http://localhost';
const WS_URL = API.replace(/^http/, 'ws') + '/hubs/trading';
const TOKEN = __ENV.TOKEN;
if (!TOKEN) throw new Error('环境变量 TOKEN 必填');

export const options = {
  vus: 200,           // 200 并发 WebSocket 连接
  duration: '3m',
  thresholds: {
    ws_session_duration: ['p(95)>120000'],  // 95% 连接稳定保持 ≥2 分钟
  },
};

export default function () {
  const res = ws.connect(`${WS_URL}?access_token=${TOKEN}`, null, function (socket) {
    socket.on('open', () => {
      // SignalR 握手帧（JSON 协议）
      socket.send('{"protocol":"json","version":1}\x1e');
    });

    socket.on('message', (msg) => {
      // 收到任何消息（包括 ping）即视为连接正常
    });

    socket.on('error', (e) => {
      console.error(`ws error: ${e.error()}`);
    });

    // 保持连接 2 分钟，间歇发送 ping
    socket.setInterval(() => {
      socket.send('{"type":6}\x1e');  // SignalR ping
    }, 15000);

    socket.setTimeout(() => socket.close(), 120000);
  });

  check(res, { 'ws connected': r => r && r.status === 101 });
}
