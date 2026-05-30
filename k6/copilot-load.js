import http from 'k6/http';
import { check, sleep } from 'k6';

// Co-pilot (F1) load. The semantic cache (Redis, 24h TTL) plus pattern matching should
// absorb most of these calls at zero AI cost — a repeated command exercises that cache path.
export const options = {
  vus: 5,
  duration: '1m',
};

export default function () {
  const url = `${__ENV.BASE_URL}/api/v1/ai/copilot`;
  const payload = JSON.stringify({ command: 'add a slack node' });
  const params = {
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${__ENV.JWT}`,
    },
  };

  const res = http.post(url, payload, params);
  check(res, {
    'status is 200': (r) => r.status === 200,
  });

  sleep(2);
}
