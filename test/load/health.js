// health.js — anonymous availability/latency baseline against /api/health.
// Safe on any environment (health is excluded from Easy Auth and returns the
// downstream-dependency check results). Validates SLO 2's availability floor
// and gives a serverless cold/warm latency envelope.
//
// Defaults are deliberately gentle; override via k6 flags:
//   run.sh health test --vus 20 --duration 120s
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  vus: Number(__ENV.VUS || 5),
  duration: __ENV.DURATION || '60s',
  thresholds: {
    // /api/health may legitimately 503 during a real downstream outage;
    // during a planned load window we expect it healthy.
    http_req_failed: ['rate<0.01'],
    // P95 under 2s: generous for a Function App behind VNet integration.
    http_req_duration: ['p(95)<2000'],
  },
};

export default function () {
  const res = http.get(`${__ENV.BASE_URL}/api/health`);
  check(res, {
    'status 200': (r) => r.status === 200,
    'reports healthy': (r) => {
      try { return JSON.parse(r.body).status === 'healthy'; } catch { return false; }
    },
  });
  sleep(1);
}
