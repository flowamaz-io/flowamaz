import http from 'k6/http';
import { check, sleep } from 'k6';

// Login throughput — rate-limited fast path. Keep VU count modest to stay under limits.
export const options = {
  vus: 10,
  duration: '1m',
  thresholds: {
    http_req_duration: ['p(95)<500'],
  },
};

export default function () {
  const url = `${__ENV.BASE_URL}/api/v1/auth/token`;
  const payload = JSON.stringify({
    email: __ENV.LOGIN_EMAIL,
    password: __ENV.LOGIN_PASSWORD,
  });
  const params = {
    headers: {
      'Content-Type': 'application/json',
    },
  };

  const res = http.post(url, payload, params);
  check(res, {
    'status is 200': (r) => r.status === 200,
  });

  sleep(1);
}
