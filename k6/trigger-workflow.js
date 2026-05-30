import http from 'k6/http';
import { check, sleep } from 'k6';

// Public workflow trigger — the platform's primary write-path hot endpoint.
export const options = {
  vus: 50,
  duration: '2m',
  thresholds: {
    http_req_duration: ['p(95)<500'],
    http_req_failed: ['rate<0.01'],
  },
};

export default function () {
  const url = `${__ENV.BASE_URL}/api/public/v1/workflows/${__ENV.WORKFLOW_SLUG}/trigger`;
  const payload = JSON.stringify({ test: true });
  const params = {
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${__ENV.API_KEY}`,
    },
  };

  const res = http.post(url, payload, params);
  check(res, {
    'status is 202': (r) => r.status === 202,
  });

  sleep(1);
}
