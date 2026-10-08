import type { ChartData } from 'chart.js';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ChartModule } from 'primeng/chart';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { SelectButtonModule } from 'primeng/selectbutton';
import { TabsModule } from 'primeng/tabs';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { TooltipModule } from 'primeng/tooltip';
import { ApiService } from '../../core/api.service';
import { downloadText, formatDuration, methodClass, parseDuration } from '../../core/format';
import { ApiError, toApiError } from '../../core/http';
import {
  ApiEndpoint,
  Environment,
  LoadPreset,
  LoadPresetInfo,
  ParameterLocation,
  SaveScenarioRequest,
  ScenarioDefinition,
  ScenarioEndpoint,
  ScriptPreview,
  SpecDetail,
  emptySla,
} from '../../core/models';
import { NotifyService } from '../../core/notify.service';
import { SessionService } from '../../core/session.service';
import { parseCsv } from '../../shared/csv';
import { ErrorListComponent } from '../../shared/error-list';
import { JsonEditorComponent } from '../../shared/json-editor';
import { RunLauncher } from '../../shared/run-launcher';

interface BuilderState {
  name: string;
  description: string;
  environmentId: string | null;
  apiSpecId: string | null;
  definition: ScenarioDefinition;
}

const BODY_METHODS = ['POST', 'PUT', 'PATCH', 'DELETE'];

const newState = (): BuilderState => ({
  name: '',
  description: '',
  environmentId: null,
  apiSpecId: null,
  definition: {
    endpoints: [],
    loadProfile: { preset: 'Smoke', stages: [{ duration: '1m', target: 1 }], thinkTimeMinSeconds: 1, thinkTimeMaxSeconds: 2 },
    globalThresholds: emptySla(),
    csvData: null,
  },
});

@Component({
  selector: 'app-scenario-builder',
  imports: [
    FormsModule, RouterLink, ButtonModule, ChartModule, CheckboxModule, DialogModule, IconFieldModule, InputIconModule, InputNumberModule,
    InputTextModule, MessageModule, SelectModule, SelectButtonModule, TabsModule, TagModule, TextareaModule, TooltipModule,
    ErrorListComponent, JsonEditorComponent,
  ],
  templateUrl: './scenario-builder.html',
})
export class ScenarioBuilderComponent implements OnInit {
  readonly id = input<string>();

  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly notify = inject(NotifyService);
  private readonly confirm = inject(ConfirmationService);
  private readonly launcher = inject(RunLauncher);
  protected readonly session = inject(SessionService);

  private readonly state = signal<BuilderState>(newState());
  private readonly savedSnapshot = signal<string>(JSON.stringify(newState()));
  protected get s(): BuilderState {
    return this.state();
  }

  protected readonly environments = signal<Environment[]>([]);
  protected readonly spec = signal<SpecDetail | null>(null);
  protected readonly presets = signal<LoadPresetInfo[]>([]);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly saving = signal(false);
  protected readonly activeTab = signal<string>('endpoints');
  protected readonly expanded = signal<number | null>(0);
  protected readonly script = signal<ScriptPreview | null>(null);
  protected readonly scriptError = signal<ApiError | null>(null);
  protected readonly scriptLoading = signal(false);

  // Endpoint picker
  protected readonly pickerOpen = signal(false);
  protected readonly pickerFilter = signal('');
  protected readonly picked = signal<Set<string>>(new Set());
  protected readonly customMethod = signal('GET');
  protected readonly customPath = signal('/');

  protected readonly methodClass = methodClass;
  protected readonly formatDuration = formatDuration;
  /** Renders a {{placeholder}} token in templates without clashing with Angular interpolation. */
  protected readonly ph = (name: string): string => '{{' + name + '}}';
  protected readonly methods = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS'].map((m) => ({ label: m, value: m }));
  protected readonly locations: { label: string; value: ParameterLocation }[] = [
    { label: 'Path', value: 'Path' },
    { label: 'Query', value: 'Query' },
    { label: 'Header', value: 'Header' },
  ];
  protected readonly modes = [
    { label: 'Static / placeholder', value: 'Static' },
    { label: 'CSV column', value: 'CsvColumn' },
  ];
  protected readonly presetOptions: { label: string; value: LoadPreset }[] = (['Smoke', 'Load', 'Stress', 'Spike', 'Soak', 'Custom'] as LoadPreset[]).map(
    (p) => ({ label: p, value: p }),
  );

  protected readonly selectedEnvironment = computed(() => this.environments().find((e) => e.id === this.state().environmentId) ?? null);

  protected readonly pickerGroups = computed(() => {
    const d = this.spec();
    if (!d) return [];
    const q = this.pickerFilter().trim().toLowerCase();
    return d.groups
      .map((g) => ({
        ...g,
        endpoints: g.endpoints.filter((e) => !q || `${e.method} ${e.path} ${e.summary ?? ''} ${g.tag}`.toLowerCase().includes(q)),
      }))
      .filter((g) => g.endpoints.length > 0);
  });

  ngOnInit(): void {
    this.api.environments().subscribe({ next: (list) => this.environments.set(list) });
    this.api.loadPresets().subscribe({ next: (p) => this.presets.set(p) });

    const id = this.id();
    if (id) {
      this.api.scenario(id).subscribe({
        next: (sc) => {
          const loaded: BuilderState = {
            name: sc.name,
            description: sc.description ?? '',
            environmentId: sc.environmentId,
            apiSpecId: sc.apiSpecId,
            definition: sc.definition,
          };
          this.state.set(loaded);
          this.savedSnapshot.set(JSON.stringify(loaded));
          this.loadSpec(sc.environmentId);
        },
        error: (e) => this.notify.error(e, 'Could not load scenario'),
      });
    } else {
      const envId = this.route.snapshot.queryParamMap.get('environmentId');
      if (envId) {
        this.state.set({ ...newState(), environmentId: envId });
        this.savedSnapshot.set(JSON.stringify(this.state()));
        this.loadSpec(envId);
      }
    }
  }

  // ---- Derived values (methods, because the model is edited in place by ngModel) ----

  protected isDirty(): boolean {
    return JSON.stringify(this.state()) !== this.savedSnapshot();
  }

  protected totalWeight(): number {
    return Math.round(this.s.definition.endpoints.reduce((sum, e) => sum + (e.weight || 0), 0) * 100) / 100;
  }

  protected totalDurationSeconds(): number {
    return this.s.definition.loadProfile.stages.reduce((sum, st) => sum + (parseDuration(st.duration) ?? 0), 0);
  }

  protected maxVus(): number {
    return Math.max(0, ...this.s.definition.loadProfile.stages.map((st) => st.target || 0));
  }

  protected capWarnings(): string[] {
    const cfg = this.session.config();
    if (!cfg) return [];
    const warnings: string[] = [];
    if (this.maxVus() > cfg.maxVus) warnings.push(`Max VUs ${this.maxVus()} exceeds the cap of ${cfg.maxVus}.`);
    if (this.totalDurationSeconds() > cfg.maxDurationMinutes * 60) warnings.push(`Duration exceeds the cap of ${cfg.maxDurationMinutes} minutes.`);
    return warnings;
  }

  protected csvColumns(): string[] {
    return parseCsv(this.s.definition.csvData).columns.filter((c) => !!c);
  }

  protected csvPreview() {
    const table = parseCsv(this.s.definition.csvData);
    return { columns: table.columns, rows: table.rows.slice(0, 5), total: table.rows.length };
  }

  private stageChartCache: { key: string; data: ChartData<'line', { x: number; y: number }[]> } | null = null;

  /** Memoized so the chart only re-renders when the stages actually change. */
  protected stageChart(): ChartData<'line', { x: number; y: number }[]> {
    const key = JSON.stringify(this.s.definition.loadProfile.stages);
    if (this.stageChartCache?.key !== key) {
      this.stageChartCache = { key, data: this.buildStageChart() };
    }
    return this.stageChartCache.data;
  }

  private buildStageChart(): ChartData<'line', { x: number; y: number }[]> {
    let t = 0;
    const points: { x: number; y: number }[] = [{ x: 0, y: 0 }];
    for (const st of this.s.definition.loadProfile.stages) {
      t += parseDuration(st.duration) ?? 0;
      points.push({ x: t, y: st.target || 0 });
    }
    return {
      datasets: [{ label: 'Target VUs', data: points, fill: true, borderColor: '#6366f1', backgroundColor: 'rgba(99,102,241,0.15)', tension: 0, pointRadius: 3 }],
    };
  }

  protected readonly stageChartOptions = {
    animation: false as const,
    maintainAspectRatio: false,
    plugins: { legend: { display: false } },
    scales: {
      x: { type: 'linear' as const, min: 0, ticks: { callback: (v: number | string) => formatDuration(Number(v)) } },
      y: { beginAtZero: true, title: { display: true, text: 'VUs' } },
    },
  };

  protected stageInvalid(duration: string): boolean {
    return parseDuration(duration) === null;
  }

  protected hasBodyEditor(ep: ScenarioEndpoint): boolean {
    return BODY_METHODS.includes(ep.method) || !!ep.body;
  }

  protected sampleFor(ep: ScenarioEndpoint): string | null {
    return this.findSpecEndpoint(ep.method, ep.path)?.sampleRequestBody ?? null;
  }

  protected specParamHint(ep: ScenarioEndpoint, name: string): string {
    const p = this.findSpecEndpoint(ep.method, ep.path)?.parameters.find((x) => x.name === name);
    if (!p) return '';
    const parts = [p.type, p.format, p.required ? 'required' : null, p.enum ? `one of: ${p.enum.join(', ')}` : null, p.description];
    return parts.filter(Boolean).join(' · ');
  }

  private findSpecEndpoint(method: string, path: string): ApiEndpoint | undefined {
    for (const g of this.spec()?.groups ?? []) {
      const found = g.endpoints.find((e) => e.method === method && e.path === path);
      if (found) return found;
    }
    return undefined;
  }

  // ---- Environment & spec ----

  protected onEnvironmentChange(envId: string | null): void {
    this.s.environmentId = envId;
    this.spec.set(null);
    if (envId) this.loadSpec(envId);
  }

  private loadSpec(envId: string): void {
    this.api.latestSpec(envId).subscribe({
      next: (d) => this.spec.set(d),
      error: (e) => this.notify.error(e, 'Could not load the API spec'),
    });
  }

  // ---- Endpoints ----

  protected openPicker(): void {
    this.picked.set(new Set());
    this.pickerFilter.set('');
    this.pickerOpen.set(true);
  }

  protected togglePick(id: string, checked: boolean): void {
    const next = new Set(this.picked());
    if (checked) next.add(id);
    else next.delete(id);
    this.picked.set(next);
  }

  protected isAlreadyAdded(ep: ApiEndpoint): boolean {
    return this.s.definition.endpoints.some((e) => e.method === ep.method && e.path === ep.path);
  }

  protected addPicked(): void {
    const d = this.spec();
    if (!d) return;
    const ids = this.picked();
    const chosen = d.groups.flatMap((g) => g.endpoints).filter((e) => ids.has(e.id));
    for (const ep of chosen) this.s.definition.endpoints.push(this.fromSpec(ep));
    this.s.apiSpecId = d.spec.id;
    this.distributeEvenly();
    this.expanded.set(this.s.definition.endpoints.length - chosen.length);
    this.pickerOpen.set(false);
  }

  protected addCustom(): void {
    const path = this.customPath().trim();
    if (!path.startsWith('/')) {
      this.notify.info('Path must start with /');
      return;
    }
    const params = [...path.matchAll(/\{([^{}/]+)\}/g)].map((m) => ({
      name: m[1],
      in: 'Path' as ParameterLocation,
      mode: 'Static' as const,
      value: '',
      csvColumn: null,
    }));
    this.s.definition.endpoints.push({
      name: null,
      method: this.customMethod(),
      path,
      weight: 0,
      parameters: params,
      body: null,
      contentType: null,
      expectedStatusCodes: [200],
      thresholds: emptySla(),
    });
    this.distributeEvenly();
    this.expanded.set(this.s.definition.endpoints.length - 1);
    this.pickerOpen.set(false);
  }

  private fromSpec(ep: ApiEndpoint): ScenarioEndpoint {
    const parameters = ep.parameters
      .filter((p) => p.in === 'path' || p.in === 'query' || (p.in === 'header' && p.required))
      .map((p) => ({
        name: p.name,
        in: (p.in === 'path' ? 'Path' : p.in === 'query' ? 'Query' : 'Header') as ParameterLocation,
        mode: 'Static' as const,
        value: p.example ?? p.enum?.[0] ?? (p.in === 'path' && p.type === 'integer' ? '{{random.int}}' : ''),
        csvColumn: null,
      }));
    const success = ep.responseCodes.map(Number).filter((c) => c >= 200 && c < 300);
    return {
      name: null,
      method: ep.method,
      path: ep.path,
      weight: 0,
      parameters,
      body: BODY_METHODS.includes(ep.method) ? ep.sampleRequestBody : null,
      contentType: ep.requestBodyContentType,
      expectedStatusCodes: success.length > 0 ? [success[0]] : [200],
      thresholds: emptySla(),
    };
  }

  protected distributeEvenly(): void {
    const eps = this.s.definition.endpoints;
    if (eps.length === 0) return;
    const base = Math.floor((100 / eps.length) * 100) / 100;
    eps.forEach((e) => (e.weight = base));
    eps[0].weight = Math.round((100 - base * (eps.length - 1)) * 100) / 100;
  }

  protected removeEndpoint(index: number): void {
    this.s.definition.endpoints.splice(index, 1);
    this.distributeEvenly();
    this.expanded.set(null);
  }

  protected moveEndpoint(index: number, delta: number): void {
    const eps = this.s.definition.endpoints;
    const target = index + delta;
    if (target < 0 || target >= eps.length) return;
    [eps[index], eps[target]] = [eps[target], eps[index]];
    this.expanded.set(target);
  }

  protected toggleExpanded(index: number): void {
    this.expanded.set(this.expanded() === index ? null : index);
  }

  protected addParameter(ep: ScenarioEndpoint, location: ParameterLocation): void {
    ep.parameters.push({ name: '', in: location, mode: 'Static', value: '', csvColumn: null });
  }

  protected removeParameter(ep: ScenarioEndpoint, index: number): void {
    ep.parameters.splice(index, 1);
  }

  protected statusText(ep: ScenarioEndpoint): string {
    return ep.expectedStatusCodes.join(', ');
  }

  protected setStatuses(ep: ScenarioEndpoint, text: string): void {
    const codes = text
      .split(/[\s,;]+/)
      .map((s) => Number(s))
      .filter((n) => Number.isInteger(n) && n >= 100 && n <= 599);
    ep.expectedStatusCodes = [...new Set(codes)];
  }

  protected enableBody(ep: ScenarioEndpoint): void {
    ep.body = this.sampleFor(ep) ?? '{\n  \n}';
    ep.contentType = ep.contentType ?? 'application/json';
  }

  // ---- Load profile ----

  protected applyPreset(preset: LoadPreset): void {
    const profile = this.s.definition.loadProfile;
    profile.preset = preset;
    const info = this.presets().find((p) => p.preset === preset);
    if (info) {
      profile.stages = info.stages.map((st) => ({ ...st }));
      profile.thinkTimeMinSeconds = info.thinkTimeMinSeconds;
      profile.thinkTimeMaxSeconds = info.thinkTimeMaxSeconds;
    }
  }

  protected presetDescription(): string {
    return this.presets().find((p) => p.preset === this.s.definition.loadProfile.preset)?.description ?? 'Custom stages.';
  }

  protected onStagesEdited(): void {
    this.s.definition.loadProfile.preset = 'Custom';
  }

  protected addStage(): void {
    this.s.definition.loadProfile.stages.push({ duration: '1m', target: this.maxVus() || 1 });
    this.onStagesEdited();
  }

  protected removeStage(index: number): void {
    this.s.definition.loadProfile.stages.splice(index, 1);
    this.onStagesEdited();
  }

  // ---- Script, save, run ----

  private toRequest(): SaveScenarioRequest {
    const s = this.s;
    return {
      name: s.name,
      description: s.description || null,
      environmentId: s.environmentId ?? '00000000-0000-0000-0000-000000000000',
      apiSpecId: s.apiSpecId,
      definition: s.definition,
    };
  }

  protected onTabChange(value: string | number | undefined): void {
    this.activeTab.set(String(value));
    if (value === 'script') this.refreshScript();
  }

  protected refreshScript(): void {
    this.scriptLoading.set(true);
    this.scriptError.set(null);
    this.api.previewScript(this.toRequest()).subscribe({
      next: (p) => {
        this.scriptLoading.set(false);
        this.script.set(p);
      },
      error: (e) => {
        this.scriptLoading.set(false);
        this.script.set(null);
        this.scriptError.set(toApiError(e));
      },
    });
  }

  protected copyScript(): void {
    const s = this.script();
    if (s) void navigator.clipboard.writeText(s.script).then(() => this.notify.success('Script copied to clipboard'));
  }

  protected downloadScript(): void {
    const s = this.script();
    if (s) downloadText(s.script, s.fileName, 'text/javascript');
  }

  protected save(then?: (id: string) => void): void {
    this.saving.set(true);
    this.error.set(null);
    const id = this.id();
    const call = id ? this.api.updateScenario(id, this.toRequest()) : this.api.createScenario(this.toRequest());
    call.subscribe({
      next: (sc) => {
        this.saving.set(false);
        const saved: BuilderState = {
          name: sc.name,
          description: sc.description ?? '',
          environmentId: sc.environmentId,
          apiSpecId: sc.apiSpecId,
          definition: sc.definition,
        };
        this.state.set(saved);
        this.savedSnapshot.set(JSON.stringify(saved));
        if (then) {
          then(sc.id);
        } else {
          this.notify.success('Scenario saved', sc.name);
        }
        if (!id) void this.router.navigate(['/scenarios', sc.id], { replaceUrl: true });
      },
      error: (e) => {
        this.saving.set(false);
        this.error.set(toApiError(e));
        window.scrollTo({ top: 0, behavior: 'smooth' });
      },
    });
  }

  protected saveAndRun(): void {
    const start = (scenarioId: string) => {
      const env = this.selectedEnvironment();
      this.launcher.start({
        scenarioId,
        scenarioName: this.s.name,
        environmentName: env?.name ?? '',
        environmentIsProduction: env?.isProduction ?? false,
      });
    };
    const id = this.id();
    if (id && !this.isDirty()) start(id);
    else this.save(start);
  }

  protected clone(): void {
    const id = this.id();
    if (!id) return;
    this.api.cloneScenario(id).subscribe({
      next: (c) => {
        this.notify.success('Scenario cloned', c.name);
        void this.router.navigate(['/scenarios', c.id]);
      },
      error: (e) => this.notify.error(e, 'Could not clone scenario'),
    });
  }

  protected scriptDownloadUrl(): string | null {
    const id = this.id();
    return id ? this.api.scenarioScriptUrl(id) : null;
  }

  protected deleteScenario(): void {
    const id = this.id();
    if (!id) return;
    this.confirm.confirm({
      header: 'Delete scenario',
      message: `Delete "${this.s.name}"? Its past runs are kept.`,
      icon: 'pi pi-exclamation-triangle',
      acceptButtonProps: { label: 'Delete', severity: 'danger' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', text: true },
      accept: () =>
        this.api.deleteScenario(id).subscribe({
          next: () => {
            this.notify.success('Scenario deleted');
            void this.router.navigate(['/scenarios']);
          },
          error: (e) => this.notify.error(e, 'Could not delete scenario'),
        }),
    });
  }
}
