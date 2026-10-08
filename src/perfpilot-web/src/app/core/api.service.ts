import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AppConfig,
  ConnectionTestResult,
  Environment,
  ImportSpecFromUrlRequest,
  LoadPresetInfo,
  PagedResult,
  RunDetail,
  RunListItem,
  RunQuery,
  SaveEnvironmentRequest,
  SaveScenarioRequest,
  Scenario,
  ScenarioListItem,
  ScriptPreview,
  SpecDetail,
  SpecSummary,
  StartRunRequest,
} from './models';

/**
 * All calls go to the PerfPilot backend on the same origin. The browser never contacts target APIs
 * or spec URLs directly — the backend and k6 do that server-side, so no CORS is involved.
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  config(): Observable<AppConfig> {
    return this.http.get<AppConfig>('/api/config');
  }

  // Environments
  environments(): Observable<Environment[]> {
    return this.http.get<Environment[]>('/api/environments');
  }
  environment(id: string): Observable<Environment> {
    return this.http.get<Environment>(`/api/environments/${id}`);
  }
  createEnvironment(req: SaveEnvironmentRequest): Observable<Environment> {
    return this.http.post<Environment>('/api/environments', req);
  }
  updateEnvironment(id: string, req: SaveEnvironmentRequest): Observable<Environment> {
    return this.http.put<Environment>(`/api/environments/${id}`, req);
  }
  deleteEnvironment(id: string): Observable<void> {
    return this.http.delete<void>(`/api/environments/${id}`);
  }
  testConnection(environmentId: string | null, environment: SaveEnvironmentRequest): Observable<ConnectionTestResult> {
    return this.http.post<ConnectionTestResult>('/api/environments/test-connection', { environmentId, environment });
  }

  // Specs
  specVersions(environmentId: string): Observable<SpecSummary[]> {
    return this.http.get<SpecSummary[]>(`/api/environments/${environmentId}/specs`);
  }
  latestSpec(environmentId: string): Observable<SpecDetail | null> {
    return this.http.get<SpecDetail | null>(`/api/environments/${environmentId}/specs/latest`);
  }
  spec(specId: string): Observable<SpecDetail> {
    return this.http.get<SpecDetail>(`/api/specs/${specId}`);
  }
  importSpecFromUrl(environmentId: string, req: ImportSpecFromUrlRequest): Observable<SpecDetail> {
    return this.http.post<SpecDetail>(`/api/environments/${environmentId}/specs/import-url`, req);
  }
  uploadSpec(environmentId: string, file: File): Observable<SpecDetail> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<SpecDetail>(`/api/environments/${environmentId}/specs/upload`, form);
  }

  // Scenarios
  scenarios(environmentId?: string | null): Observable<ScenarioListItem[]> {
    const params = environmentId ? new HttpParams().set('environmentId', environmentId) : undefined;
    return this.http.get<ScenarioListItem[]>('/api/scenarios', { params });
  }
  scenario(id: string): Observable<Scenario> {
    return this.http.get<Scenario>(`/api/scenarios/${id}`);
  }
  createScenario(req: SaveScenarioRequest): Observable<Scenario> {
    return this.http.post<Scenario>('/api/scenarios', req);
  }
  updateScenario(id: string, req: SaveScenarioRequest): Observable<Scenario> {
    return this.http.put<Scenario>(`/api/scenarios/${id}`, req);
  }
  deleteScenario(id: string): Observable<void> {
    return this.http.delete<void>(`/api/scenarios/${id}`);
  }
  cloneScenario(id: string): Observable<Scenario> {
    return this.http.post<Scenario>(`/api/scenarios/${id}/clone`, {});
  }
  previewScript(req: SaveScenarioRequest): Observable<ScriptPreview> {
    return this.http.post<ScriptPreview>('/api/scenarios/preview-script', req);
  }
  loadPresets(): Observable<LoadPresetInfo[]> {
    return this.http.get<LoadPresetInfo[]>('/api/load-presets');
  }

  // Runs
  runs(query: RunQuery): Observable<PagedResult<RunListItem>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.scenarioId) params = params.set('scenarioId', query.scenarioId);
    if (query.environmentId) params = params.set('environmentId', query.environmentId);
    if (query.status) params = params.set('status', query.status);
    return this.http.get<PagedResult<RunListItem>>('/api/runs', { params });
  }
  run(id: string): Observable<RunDetail> {
    return this.http.get<RunDetail>(`/api/runs/${id}`);
  }
  startRun(req: StartRunRequest): Observable<RunDetail> {
    return this.http.post<RunDetail>('/api/runs', req);
  }
  cancelRun(id: string): Observable<RunDetail> {
    return this.http.post<RunDetail>(`/api/runs/${id}/cancel`, {});
  }

  // Download links (plain navigations; served as attachments by the backend)
  scenarioScriptUrl(id: string): string {
    return `/api/scenarios/${id}/script/download`;
  }
  runReportUrl(id: string): string {
    return `/api/runs/${id}/report`;
  }
  runScriptUrl(id: string): string {
    return `/api/runs/${id}/script`;
  }
  runSummaryUrl(id: string): string {
    return `/api/runs/${id}/summary`;
  }
  specRawUrl(id: string): string {
    return `/api/specs/${id}/raw`;
  }
}
