import http from 'k6/http';
import { check, sleep } from 'k6';

// Instance status polling — the highest-volume read path (cached). High VU count
// simulates many clients polling a run's status concurrently.
export const options = {
  vus: 100,
  duration: '2m',
  thresholds: {
    http_req_duration: ['p(95)<500'],
    http_req_failed: ['rate<0.01'],
  },
};

export default function () {
  const url = `${__ENV.BASE_URL}/api/v1/instances/${__ENV.INSTANCE_ID}`;
  const params = {
    headers: {
      Authorization: `Bearer ${__ENV.JWT}`,
    },
  };

  const res = http.get(url, params);
  check(res, {
    'status is 200': (r) => r.status === 200,
  });

  sleep(0.5);
}
