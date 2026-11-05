import http from 'k6/http';
import { check, sleep } from 'k6';
import type { Options } from 'k6/options';

const baseUrl = __ENV.BASE_URL ?? 'http://127.0.0.1:8080';
const query = encodeURIComponent('cobranca de divida');

export const options: Options = {
  stages: [
    { duration: '30s', target: 1000 },
    { duration: '1m', target: 1000 },
    { duration: '15s', target: 0 },
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'],
    http_req_failed: ['rate<0.01'],
    checks: ['rate>0.99'],
  },
};

export default function (): void {
  const search = http.get(`${baseUrl}/api/themes?q=${query}`, { tags: { name: 'search' } });
  check(search, {
    'search returns themes': (r) => r.status === 200 && (r.json('themes') as unknown[]).length > 0,
  });

  const theme = http.get(`${baseUrl}/api/themes/1`, { tags: { name: 'theme' } });
  check(theme, {
    'theme returns its name': (r) => r.status === 200 && Boolean(r.json('name')),
  });

  const panels = http.get(`${baseUrl}/api/themes/1/panels`, { tags: { name: 'panels' } });
  check(panels, {
    'panels return the theme': (r) => r.status === 200 && r.json('themeKey') === 1,
  });

  sleep(1);
}
