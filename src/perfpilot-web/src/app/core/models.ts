// Types mirror the PerfPilot API contracts (enums are serialized as strings).

export type AuthType = 'None' | 'BearerToken' | 'ApiKey' | 'LoginFlow';
export type LoginScope = 'PerVu' | 'Shared';
export type CheckSeverity = 'Ok' | 'Warning' | 'Error';
export type SpecSource = 'Url' | 'Upload';
export type ParameterLocation = 'Path' | 'Query' | 'Header';
export type ParameterValueMode = 'Static' | 'CsvColumn';
export type LoadPreset = 'Smoke' | 'Load' | 'Stress' | 'Spike' | 'Soak' | 'Custom';
export type RunStatus = 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';
export type SlaMetric = 'P95' | 'P99' | 'ErrorRate' | 'MinRps';

export interface K6Status {
  available: boolean;
  binaryPath: string;
  version: string | null;
  error: string | null;
}

export interface AppConfig {
  maxVus: number;
  maxDurationMinutes: number;
  identityMode: 'Header' | 'Windows';
  currentUser: string;
  k6: K6Status;
}

export interface AuthConfig {
  type: AuthType;
  hasSecret: boolean;
  secretMask: string | null;
  apiKeyHeaderName: string | null;
  loginUrl: string | null;
  hasLoginBody: boolean;
  loginTokenJsonPath: string | null;
  loginTokenHeaderName: string;
  loginTokenPrefix: string;
  loginScope: LoginScope;
  loginHeaderNames: string[];
}

export interface Environment {
  id: string;
  name: string;
  baseUrl: string;
  openApiUrl: string | null;
  isProduction: boolean;
  ignoreTlsErrors: boolean;
  auth: AuthConfig;
  createdAt: string;
  updatedAt: string;
  latestSpec: SpecSummary | null;
}

export interface SaveAuthConfig {
  type: AuthType;
  secret: string | null;
  clearSecret: boolean;
  apiKeyHeaderName: string | null;
  loginUrl: string | null;
  loginBody: string | null;
  loginTokenJsonPath: string | null;
  loginTokenHeaderName: string | null;
  loginTokenPrefix: string | null;
  loginScope: LoginScope;
  /** null keeps stored headers; an entry with a null value keeps that header's stored value. */
  loginHeaders: { name: string; value: string | null }[] | null;
}

export interface SaveEnvironmentRequest {
  name: string;
  baseUrl: string;
  openApiUrl: string | null;
  isProduction: boolean;
  ignoreTlsErrors: boolean;
  auth: SaveAuthConfig;
}

export interface ConnectionCheck {
  name: string;
  url: string;
  severity: CheckSeverity;
  statusCode: number | null;
  latencyMs: number;
  errorKind: string | null;
  message: string;
}

export interface ConnectionTestResult {
  success: boolean;
  checks: ConnectionCheck[];
}

export interface SpecSummary {
  id: string;
  environmentId: string;
  version: number;
  title: string;
  apiVersion: string | null;
  specFormat: string;
  source: SpecSource;
  sourceLocation: string;
  importedAt: string;
  importedBy: string | null;
  endpointCount: number;
  warnings: string[];
}

export interface EndpointParameter {
  name: string;
  in: string;
  required: boolean;
  type: string | null;
  format: string | null;
  description: string | null;
  example: string | null;
  enum: string[] | null;
}

export interface ApiEndpoint {
  id: string;
  method: string;
  path: string;
  operationId: string | null;
  summary: string | null;
  description: string | null;
  tag: string;
  deprecated: boolean;
  parameters: EndpointParameter[];
  requestBodyContentType: string | null;
  requestBodySchema: string | null;
  sampleRequestBody: string | null;
  responseCodes: string[];
}

export interface EndpointGroup {
  tag: string;
  endpoints: ApiEndpoint[];
}

export interface SpecDetail {
  spec: SpecSummary;
  groups: EndpointGroup[];
}

export interface ImportSpecFromUrlRequest {
  url: string | null;
  authHeaderName: string | null;
  authHeaderValue: string | null;
  useEnvironmentAuth: boolean;
  timeoutSeconds: number | null;
  ignoreTlsErrors: boolean;
}

export interface SlaThresholds {
  p95Ms: number | null;
  p99Ms: number | null;
  maxErrorRatePercent: number | null;
  minRps: number | null;
}

export interface ParameterValue {
  name: string;
  in: ParameterLocation;
  mode: ParameterValueMode;
  value: string | null;
  csvColumn: string | null;
}

export interface ScenarioEndpoint {
  name: string | null;
  method: string;
  path: string;
  weight: number;
  parameters: ParameterValue[];
  body: string | null;
  contentType: string | null;
  expectedStatusCodes: number[];
  thresholds: SlaThresholds;
}

export interface LoadStage {
  duration: string;
  target: number;
}

export interface LoadProfile {
  preset: LoadPreset;
  stages: LoadStage[];
  thinkTimeMinSeconds: number;
  thinkTimeMaxSeconds: number;
}

export interface ScenarioDefinition {
  endpoints: ScenarioEndpoint[];
  loadProfile: LoadProfile;
  globalThresholds: SlaThresholds;
  csvData: string | null;
}

export interface Scenario {
  id: string;
  name: string;
  description: string | null;
  environmentId: string;
  environmentName: string;
  environmentIsProduction: boolean;
  apiSpecId: string | null;
  definition: ScenarioDefinition;
  createdAt: string;
  updatedAt: string;
  createdBy: string | null;
}

export interface LastRun {
  id: string;
  status: RunStatus;
  thresholdsPassed: boolean | null;
  queuedAt: string;
}

export interface ScenarioListItem {
  id: string;
  name: string;
  description: string | null;
  environmentId: string;
  environmentName: string;
  environmentIsProduction: boolean;
  endpointCount: number;
  preset: LoadPreset;
  maxVus: number;
  totalDurationSeconds: number;
  updatedAt: string;
  lastRun: LastRun | null;
}

export interface SaveScenarioRequest {
  name: string;
  description: string | null;
  environmentId: string;
  apiSpecId: string | null;
  definition: ScenarioDefinition;
}

export interface ScriptPreview {
  script: string;
  requiredEnvironmentVariables: string[];
  fileName: string;
}

export interface LoadPresetInfo {
  preset: LoadPreset;
  description: string;
  stages: LoadStage[];
  thinkTimeMinSeconds: number;
  thinkTimeMaxSeconds: number;
}

export interface RunMetrics {
  count: number;
  failed: number;
  avgMs: number;
  minMs: number;
  p50Ms: number;
  p90Ms: number;
  p95Ms: number;
  p99Ms: number;
  maxMs: number;
  errorRate: number;
  rps: number;
}

export interface RunEndpointResult {
  id: string;
  key: string;
  name: string;
  method: string;
  path: string;
  metrics: RunMetrics;
  checksPassed: number;
  checksFailed: number;
  sortOrder: number;
}

export interface RunThresholdResult {
  id: string;
  endpointKey: string | null;
  scopeName: string;
  metric: SlaMetric;
  limit: number;
  actual: number | null;
  passed: boolean;
  k6Metric: string;
  expression: string;
  sortOrder: number;
}

export interface TimeSeriesPoint {
  t: number;
  vus: number;
  requests: number;
  rps: number;
  p50Ms: number;
  p95Ms: number;
  p99Ms: number;
  errorRate: number;
}

export interface RunListItem {
  id: string;
  scenarioId: string | null;
  scenarioName: string;
  environmentId: string | null;
  environmentName: string;
  environmentIsProduction: boolean;
  status: RunStatus;
  thresholdsPassed: boolean | null;
  startedBy: string;
  queuedAt: string;
  startedAt: string | null;
  endedAt: string | null;
  durationSeconds: number | null;
  preset: LoadPreset;
  overall: RunMetrics;
}

export interface RunDetail extends RunListItem {
  environmentBaseUrl: string;
  exitCode: number | null;
  errorMessage: string | null;
  cancelledBy: string | null;
  scenarioSnapshot: ScenarioDefinition;
  checksPassed: number;
  checksFailed: number;
  endpointResults: RunEndpointResult[];
  thresholdResults: RunThresholdResult[];
  timeSeries: TimeSeriesPoint[];
  hasSummary: boolean;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface RunQuery {
  scenarioId?: string | null;
  environmentId?: string | null;
  status?: RunStatus | null;
  page: number;
  pageSize: number;
}

export interface StartRunRequest {
  scenarioId: string;
  confirmEnvironmentName: string | null;
}

export interface RunStatusMessage {
  runId: string;
  status: RunStatus;
  thresholdsPassed: boolean | null;
  errorMessage: string | null;
}

export interface RunProgressMessage {
  runId: string;
  elapsedSeconds: number;
  vus: number;
  rps: number;
  p95Ms: number;
  errorRate: number;
  totalRequests: number;
  totalFailed: number;
  point: TimeSeriesPoint;
}

export const emptySla = (): SlaThresholds => ({ p95Ms: null, p99Ms: null, maxErrorRatePercent: null, minRps: null });
