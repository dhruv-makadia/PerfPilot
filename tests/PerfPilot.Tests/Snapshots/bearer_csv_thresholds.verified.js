// =============================================================================
// PerfPilot generated k6 script
// Scenario:    Pet store load
// Environment: Staging (https://petstore.staging.local/api/v3/)
// Load:        Load profile, max 10 VUs, 1m45s total
//
// Secrets are never embedded in this file. Provide them as environment variables:
//   PERFPILOT_BEARER_TOKEN  Static bearer token
// Optional: PERFPILOT_BASE_URL overrides the target base URL.
//
// Run it yourself:
//   k6 run -e PERFPILOT_BEARER_TOKEN=<value> script.js
// =============================================================================
import http from 'k6/http';
import { check, sleep } from 'k6';
import { SharedArray } from 'k6/data';

const BASE_URL = (__ENV.PERFPILOT_BASE_URL || 'https://petstore.staging.local/api/v3').replace(/\/+$/, '');

export const options = {
  stages: [
    { duration: '30s', target: 10 },
    { duration: '1m', target: 10 },
    { duration: '15s', target: 0 },
  ],
  thresholds: {
    'http_req_duration{kind:test}': ['p(95)<=800'],
    'http_req_failed{kind:test}': ['rate<=0.01'],
    'http_reqs{kind:test}': ['rate>=5'],
    'http_req_duration{endpoint:ep1}': ['p(95)<=300', 'p(99)<=600'],
    'http_req_failed{endpoint:ep1}': ['rate>=0'],
    'http_reqs{endpoint:ep1}': ['count>=0'],
    'http_req_duration{endpoint:ep2}': ['max>=0'],
    'http_req_failed{endpoint:ep2}': ['rate<=0.005'],
    'http_reqs{endpoint:ep2}': ['rate>=1.5'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)', 'count'],
  insecureSkipTLSVerify: false,
  discardResponseBodies: true,
  userAgent: 'PerfPilot/1.0 (k6)',
};

// CSV test data (2 rows), shared read-only across VUs.
const CSV_DATA = new SharedArray('perfpilot-csv', function () {
  return [
    {"petId":"1","status":"available"},
    {"petId":"2","status":"sold, out"},
  ];
});

// Endpoints with traffic weight (%). One is picked at random per iteration.
const ENDPOINTS = [
  {
    "key": "ep1",
    "name": "GET /pet/{petId}",
    "method": "GET",
    "path": "/pet/{petId}",
    "weight": 70,
    "params": [
      {
        "name": "petId",
        "in": "path",
        "mode": "csv",
        "value": null,
        "column": "petId"
      },
      {
        "name": "X-Trace",
        "in": "header",
        "mode": "static",
        "value": "{{random.guid}}",
        "column": null
      }
    ],
    "body": null,
    "contentType": null,
    "expected": [
      200,
      404
    ],
    "check": "ep1 GET /pet/{petId} status in [200,404]"
  },
  {
    "key": "ep2",
    "name": "Create pet",
    "method": "POST",
    "path": "/pet",
    "weight": 30,
    "params": [
      {
        "name": "source",
        "in": "query",
        "mode": "static",
        "value": "perf's test",
        "column": null
      }
    ],
    "body": "{\n  \"id\": {{random.int}},\n  \"name\": \"{{csv.status}}\",\n  \"owner\": \"{{random.email}}\"\n}",
    "contentType": "application/json",
    "expected": [
      200
    ],
    "check": "ep2 Create pet status in [200]"
  }
];
for (const ep of ENDPOINTS) {
  // Requests with an unexpected status count as failed (http_req_failed).
  ep.responseCallback = http.expectedStatuses(...ep.expected);
}
const TOTAL_WEIGHT = ENDPOINTS.reduce(function (sum, ep) { return sum + ep.weight; }, 0);

const THINK_TIME_MIN = 0.5;
const THINK_TIME_MAX = 1.5;

// ---- Authentication ----------------------------------------------------------
const BEARER_TOKEN = __ENV.PERFPILOT_BEARER_TOKEN;
if (!BEARER_TOKEN) {
  throw new Error('Environment variable PERFPILOT_BEARER_TOKEN is required (static bearer token).');
}

function applyAuth(headers, data) {
  headers['Authorization'] = 'Bearer ' + BEARER_TOKEN;
}

// ---- Helpers -------------------------------------------------------------------
function pickEndpoint() {
  let r = Math.random() * TOTAL_WEIGHT;
  for (let i = 0; i < ENDPOINTS.length; i++) {
    r -= ENDPOINTS[i].weight;
    if (r < 0) { return ENDPOINTS[i]; }
  }
  return ENDPOINTS[ENDPOINTS.length - 1];
}

function randomInt() {
  return Math.floor(Math.random() * 1000000) + 1;
}

function uuidv4() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
    const r = Math.random() * 16 | 0;
    const v = c === 'x' ? r : (r & 0x3 | 0x8);
    return v.toString(16);
  });
}

function randomEmail() {
  return 'perfpilot.' + uuidv4().substring(0, 12) + '@example.com';
}

// Replaces {{random.int}}, {{random.guid}}, {{random.email}} and {{csv.column}} placeholders.
function fill(template, row) {
  if (template === null || template === undefined) { return template; }
  return String(template).replace(/\{\{\s*([A-Za-z]+)\.([^{}]+?)\s*\}\}/g, function (all, ns, name) {
    if (ns === 'random') {
      if (name === 'int') { return String(randomInt()); }
      if (name === 'guid' || name === 'uuid') { return uuidv4(); }
      if (name === 'email') { return randomEmail(); }
      return all;
    }
    if (ns === 'csv') {
      const v = row[name];
      return v === undefined || v === null ? '' : String(v);
    }
    return all;
  });
}

function paramValue(p, row) {
  if (p.mode === 'csv') {
    const v = row[p.column];
    return v === undefined || v === null ? '' : String(v);
  }
  return fill(p.value, row);
}

function buildRequest(ep, row) {
  let path = ep.path;
  const query = [];
  const headers = {};
  for (const p of ep.params) {
    const value = paramValue(p, row);
    if (p.in === 'path') {
      path = path.split('{' + p.name + '}').join(encodeURIComponent(value));
    } else if (p.in === 'query') {
      if (value !== '') { query.push(encodeURIComponent(p.name) + '=' + encodeURIComponent(value)); }
    } else if (p.in === 'header') {
      headers[p.name] = value;
    }
  }
  let body = null;
  if (ep.body !== null) {
    body = fill(ep.body, row);
    headers['Content-Type'] = ep.contentType;
  }
  const separator = path.indexOf('?') >= 0 ? '&' : '?';
  const url = BASE_URL + path + (query.length > 0 ? separator + query.join('&') : '');
  return { url: url, body: body, headers: headers };
}

// ---- Test ------------------------------------------------------------------------
export default function (data) {
  const row = CSV_DATA.length > 0 ? CSV_DATA[Math.floor(Math.random() * CSV_DATA.length)] : {};
  const ep = pickEndpoint();
  const req = buildRequest(ep, row);
  applyAuth(req.headers, data);

  const res = http.request(ep.method, req.url, req.body, {
    headers: req.headers,
    tags: { endpoint: ep.key, name: ep.name, kind: 'test' },
    responseCallback: ep.responseCallback,
  });

  check(res, {
    [ep.check]: function (r) { return ep.expected.indexOf(r.status) >= 0; },
  }, { endpoint: ep.key });

  sleep(THINK_TIME_MIN + Math.random() * (THINK_TIME_MAX - THINK_TIME_MIN));
}
