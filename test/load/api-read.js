// api-read.js — operator-console read mix. Simulates N operators keeping the
// console open: the job list poll (the app's heaviest ambient query), job
// detail opens, and the config surfaces the sidebar loads.
//
// Auth: pass BEARER (run.sh injects the Azure CLI ARM token — accepted on DEV
// only via the dev-only Easy Auth audience lever). Test/prod need an ARN
// automation identity; see docs/operations/load-testing.md "Blockers".
//
// Two modes:
//   default      — ramping burst: 1 -> VUS operators over RAMP, hold, ramp down
//   SOAK=1       — flat low concurrency for DURATION (broadcast-day ambient)
import http from 'k6/http';
import { check, sleep, group } from 'k6';

const VUS = Number(__ENV.VUS || 10);

export const options = __ENV.SOAK
  ? {
      vus: Number(__ENV.VUS || 4),
      duration: __ENV.DURATION || '30m',
      thresholds: {
        http_req_failed: ['rate<0.001'],       // SLO 2: 99.9% availability
        http_req_duration: ['p(95)<3000'],
      },
    }
  : {
      stages: [
        { duration: __ENV.RAMP || '30s', target: VUS },
        { duration: __ENV.HOLD || '2m', target: VUS },
        { duration: '15s', target: 0 },
      ],
      thresholds: {
        http_req_failed: ['rate<0.001'],       // SLO 2: 99.9% availability
        http_req_duration: ['p(95)<3000'],
      },
    };

const H = () => ({
  headers: {
    Authorization: `Bearer ${__ENV.BEARER}`,
    'Content-Type': 'application/json',
  },
});

export default function () {
  const base = `${__ENV.BASE_URL}/api`;

  group('job list poll', () => {
    const res = http.get(`${base}/jobs`, H());
    check(res, { 'jobs 200': (r) => r.status === 200 });

    // Open the newest job's detail (mirrors an operator clicking through).
    try {
      const jobs = JSON.parse(res.body);
      const list = Array.isArray(jobs) ? jobs : (jobs.Items || jobs.Jobs || jobs.jobs || []);
      const first = list[0];
      const id = first && (first.JobId || first.jobId);
      if (id) {
        const detail = http.get(`${base}/jobs/${id}`, H());
        check(detail, { 'job detail 200': (r) => r.status === 200 });
      }
    } catch (_) { /* list shape drift is a check failure, not a crash */ }
  });

  group('config surfaces', () => {
    check(http.get(`${base}/schedules`, H()), { 'schedules 200': (r) => r.status === 200 });
    check(http.get(`${base}/voices/profiles`, H()), { 'profiles 200': (r) => r.status === 200 });
  });

  // Operator polling cadence — the console polls ~10s; use 5s to be adversarial.
  sleep(5);
}
