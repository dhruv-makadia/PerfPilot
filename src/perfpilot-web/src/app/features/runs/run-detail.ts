import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { ConfirmationService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ChartModule } from 'primeng/chart';
import { MessageModule } from 'primeng/message';
import { ProgressBarModule } from 'primeng/progressbar';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import { ApiService } from '../../core/api.service';
import {
  formatDuration,
  formatMs,
  formatNumber,
  formatPct,
  formatSla,
  methodClass,
  parseDuration,
  slaLabel,
  statusSeverity,
} from '../../core/format';
import { RunDetail, RunProgressMessage, SlaMetric, TimeSeriesPoint } from '../../core/models';
import { NotifyService } from '../../core/notify.service';
import { RunHubService } from '../../core/run-hub.service';
import { RunLauncher } from '../../shared/run-launcher';

const COLORS = { p50: '#3b82f6', p95: '#f59e0b', p99: '#ef4444', rps: '#10b981', vus: '#8b5cf6', err: '#ef4444' };

@Component({
  selector: 'app-run-detail',
  imports: [RouterLink, DatePipe, ButtonModule, ChartModule, MessageModule, ProgressBarModule, TableModule, TagModule, TooltipModule],
  templateUrl: './run-detail.html',
})
export class RunDetailComponent implements OnInit, OnDestroy {
  readonly id = input.required<string>();

  protected readonly api = inject(ApiService);
  private readonly notify = inject(NotifyService);
  private readonly hub = inject(RunHubService);
  private readonly confirm = inject(ConfirmationService);
  private readonly launcher = inject(RunLauncher);
  private watch: { stop: () => void } | null = null;
  /** Fallback refresh while active: live pushes come only from the instance executing the run. */
  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private readonly subs: Subscription[] = [];

  protected readonly run = signal<RunDetail | null>(null);
  protected readonly live = signal<RunProgressMessage | null>(null);
  protected readonly livePoints = signal<TimeSeriesPoint[]>([]);
  protected readonly cancelling = signal(false);

  protected readonly ms = formatMs;
  protected readonly pct = formatPct;
  protected readonly num = formatNumber;
  protected readonly duration = formatDuration;
  protected readonly slaLabel = slaLabel;
  protected readonly formatSla = formatSla;
  protected readonly methodClass = methodClass;
  protected readonly statusSeverity = statusSeverity;

  protected readonly isActive = computed(() => {
    const s = this.run()?.status;
    return s === 'Queued' || s === 'Running';
  });

  /** Stored series once finished; live points while running. */
  protected readonly series = computed(() => {
    const r = this.run();
    if (r && !this.isActive() && r.timeSeries.length > 0) return r.timeSeries;
    return this.livePoints();
  });

  protected readonly plannedSeconds = computed(() =>
    (this.run()?.scenarioSnapshot.loadProfile.stages ?? []).reduce((sum, s) => sum + (parseDuration(s.duration) ?? 0), 0),
  );

  protected readonly progressPct = computed(() => {
    const planned = this.plannedSeconds();
    const elapsed = this.live()?.elapsedSeconds ?? 0;
    return planned > 0 ? Math.min(100, Math.round((elapsed / planned) * 100)) : 0;
  });

  protected readonly breaches = computed(() => {
    const map = new Map<string, Set<SlaMetric>>();
    for (const t of this.run()?.thresholdResults ?? []) {
      if (!t.passed && t.endpointKey) {
        if (!map.has(t.endpointKey)) map.set(t.endpointKey, new Set());
        map.get(t.endpointKey)!.add(t.metric);
      }
    }
    return map;
  });

  protected readonly failedThresholds = computed(() => (this.run()?.thresholdResults ?? []).filter((t) => !t.passed).length);

  private readonly baseOptions = {
    animation: false as const,
    maintainAspectRatio: false,
    interaction: { mode: 'index' as const, intersect: false },
    plugins: { legend: { position: 'bottom' as const } },
  };

  private xAxis = {
    type: 'linear' as const,
    min: 0,
    title: { display: true, text: 'Elapsed' },
    ticks: { callback: (v: number | string) => formatDuration(Number(v)) },
  };

  protected readonly latencyChart = computed(() => {
    const pts = this.series();
    return {
      datasets: [
        this.dataset('p50', pts.map((p) => ({ x: p.t, y: p.requests ? p.p50Ms : null })), COLORS.p50),
        this.dataset('p95', pts.map((p) => ({ x: p.t, y: p.requests ? p.p95Ms : null })), COLORS.p95),
        this.dataset('p99', pts.map((p) => ({ x: p.t, y: p.requests ? p.p99Ms : null })), COLORS.p99),
      ],
    };
  });
  protected readonly latencyOptions = {
    ...this.baseOptions,
    scales: { x: this.xAxis, y: { beginAtZero: true, title: { display: true, text: 'ms' } } },
  };

  protected readonly throughputChart = computed(() => {
    const pts = this.series();
    return {
      datasets: [
        { ...this.dataset('Requests/s', pts.map((p) => ({ x: p.t, y: p.rps })), COLORS.rps), yAxisID: 'y' },
        { ...this.dataset('Virtual users', pts.map((p) => ({ x: p.t, y: p.vus })), COLORS.vus), yAxisID: 'y1', borderDash: [6, 4] },
      ],
    };
  });
  protected readonly throughputOptions = {
    ...this.baseOptions,
    scales: {
      x: this.xAxis,
      y: { beginAtZero: true, position: 'left' as const, title: { display: true, text: 'req/s' } },
      y1: { beginAtZero: true, position: 'right' as const, grid: { drawOnChartArea: false }, title: { display: true, text: 'VUs' } },
    },
  };

  protected readonly errorChart = computed(() => {
    const pts = this.series();
    return {
      datasets: [{ ...this.dataset('Error rate %', pts.map((p) => ({ x: p.t, y: p.requests ? p.errorRate * 100 : null })), COLORS.err), fill: true, backgroundColor: 'rgba(239,68,68,0.12)' }],
    };
  });
  protected readonly errorOptions = {
    ...this.baseOptions,
    scales: { x: this.xAxis, y: { beginAtZero: true, suggestedMax: 5, title: { display: true, text: '%' } } },
  };

  ngOnInit(): void {
    this.load();
    this.pollTimer = setInterval(() => {
      if (this.isActive()) this.load();
    }, 5000);
    const watch = this.hub.watchRun(this.id());
    this.watch = watch;
    this.subs.push(
      watch.progress.subscribe((m) => {
        this.live.set(m);
        this.livePoints.update((pts) => [...pts, m.point]);
        const r = this.run();
        if (r && r.status === 'Queued') this.run.set({ ...r, status: 'Running' });
      }),
      watch.status.subscribe((m) => {
        const r = this.run();
        if (r) this.run.set({ ...r, status: m.status, thresholdsPassed: m.thresholdsPassed, errorMessage: m.errorMessage });
        if (m.status !== 'Queued' && m.status !== 'Running') this.load();
      }),
    );
  }

  ngOnDestroy(): void {
    if (this.pollTimer) clearInterval(this.pollTimer);
    this.subs.forEach((s) => s.unsubscribe());
    this.watch?.stop();
  }

  private load(): void {
    this.api.run(this.id()).subscribe({
      next: (r) => this.run.set(r),
      error: (e) => this.notify.error(e, 'Could not load run'),
    });
  }

  /** Buckets without requests have no latency or error rate; null leaves a gap instead of a misleading 0. */
  private dataset(label: string, data: { x: number; y: number | null }[], color: string) {
    return { label, data, borderColor: color, backgroundColor: color, borderWidth: 2, pointRadius: data.length > 60 ? 0 : 2, tension: 0.25 };
  }

  protected breached(key: string, metric: SlaMetric): boolean {
    return this.breaches().get(key)?.has(metric) ?? false;
  }

  protected checksRate(passed: number, failed: number): string {
    const total = passed + failed;
    return total === 0 ? '–' : formatPct(passed / total);
  }

  protected cancel(): void {
    this.confirm.confirm({
      header: 'Cancel run',
      message: 'Stop this run? k6 is asked to stop gracefully and partial results are kept.',
      icon: 'pi pi-stop-circle',
      acceptButtonProps: { label: 'Stop run', severity: 'danger' },
      rejectButtonProps: { label: 'Keep running', severity: 'secondary', text: true },
      accept: () => {
        this.cancelling.set(true);
        this.api.cancelRun(this.id()).subscribe({
          next: (r) => {
            this.cancelling.set(false);
            this.run.set(r);
            this.notify.info('Cancellation requested');
          },
          error: (e) => {
            this.cancelling.set(false);
            this.notify.error(e, 'Could not cancel run');
          },
        });
      },
    });
  }

  protected rerun(): void {
    const r = this.run();
    if (!r?.scenarioId) return;
    this.launcher.start({
      scenarioId: r.scenarioId,
      scenarioName: r.scenarioName,
      environmentName: r.environmentName,
      environmentIsProduction: r.environmentIsProduction,
    });
  }
}
