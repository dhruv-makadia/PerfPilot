import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import { ApiService } from '../../core/api.service';
import { formatDuration, formatMs, formatNumber, formatPct, statusSeverity } from '../../core/format';
import { Environment, RunListItem, RunStatus, ScenarioListItem } from '../../core/models';
import { NotifyService } from '../../core/notify.service';
import { RunHubService } from '../../core/run-hub.service';

@Component({
  selector: 'app-run-list',
  imports: [FormsModule, RouterLink, DatePipe, ButtonModule, SelectModule, TableModule, TagModule, TooltipModule],
  template: `
    <div class="page-header">
      <div>
        <h1>Run history</h1>
        <p class="muted">Every k6 run with its verdict and headline metrics. Updates live while runs execute.</p>
      </div>
    </div>

    <div class="panel">
      <div class="filters">
        <p-select [options]="scenarios()" [ngModel]="scenarioId()" (ngModelChange)="setFilter('scenarioId', $event)" optionLabel="name" optionValue="id"
          placeholder="All scenarios" [showClear]="true" [filter]="true" styleClass="filter-select" />
        <p-select [options]="environments()" [ngModel]="environmentId()" (ngModelChange)="setFilter('environmentId', $event)" optionLabel="name"
          optionValue="id" placeholder="All environments" [showClear]="true" styleClass="filter-select" />
        <p-select [options]="statuses" [ngModel]="status()" (ngModelChange)="setFilter('status', $event)" placeholder="Any status" [showClear]="true"
          styleClass="filter-select" />
        <span class="spacer"></span>
        <p-button icon="pi pi-refresh" [text]="true" pTooltip="Refresh" (onClick)="load()" />
      </div>

      <p-table [value]="runs()" [lazy]="true" (onLazyLoad)="onLazyLoad($event)" [paginator]="true" [rows]="pageSize()" [first]="first()"
        [totalRecords]="total()" [loading]="loading()" [rowsPerPageOptions]="[10, 25, 50, 100]" dataKey="id" [tableStyle]="{ 'min-width': '72rem' }">
        <ng-template #header>
          <tr>
            <th>Status</th>
            <th>Verdict</th>
            <th>Scenario</th>
            <th>Environment</th>
            <th>Started</th>
            <th>Duration</th>
            <th>By</th>
            <th class="num">Requests</th>
            <th class="num">RPS</th>
            <th class="num">p95</th>
            <th class="num">Errors</th>
          </tr>
        </ng-template>
        <ng-template #body let-r>
          <tr class="clickable" (click)="open(r)">
            <td><p-tag [value]="r.status" [severity]="statusSeverity(r.status)" /></td>
            <td>
              @if (r.thresholdsPassed === true) { <span class="verdict-pill pass">PASS</span> }
              @else if (r.thresholdsPassed === false) { <span class="verdict-pill fail">FAIL</span> }
              @else { <span class="muted">–</span> }
            </td>
            <td><a [routerLink]="['/runs', r.id]" class="strong-link" (click)="$event.stopPropagation()">{{ r.scenarioName }}</a>
              <span class="small muted ml-1">{{ r.preset }}</span></td>
            <td>{{ r.environmentName }} @if (r.environmentIsProduction) { <p-tag severity="danger" value="PROD" /> }</td>
            <td class="small">{{ (r.startedAt ?? r.queuedAt) | date: 'short' }}</td>
            <td class="small">{{ duration(r.durationSeconds) }}</td>
            <td class="small">{{ r.startedBy }}</td>
            <td class="num">{{ num(r.overall.count) }}</td>
            <td class="num">{{ num(r.overall.rps, 1) }}</td>
            <td class="num">{{ r.overall.count ? ms(r.overall.p95Ms) : '–' }}</td>
            <td class="num">{{ r.overall.count ? pct(r.overall.errorRate) : '–' }}</td>
          </tr>
        </ng-template>
        <ng-template #emptymessage>
          <tr><td colspan="11" class="empty">No runs match these filters.</td></tr>
        </ng-template>
      </p-table>
    </div>
  `,
})
export class RunListComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly notify = inject(NotifyService);
  private readonly hub = inject(RunHubService);
  private watch: { stop: () => void } | null = null;
  private sub: Subscription | null = null;

  protected readonly runs = signal<RunListItem[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly pageSize = signal(25);
  protected readonly first = signal(0);
  protected readonly scenarioId = signal<string | null>(null);
  protected readonly environmentId = signal<string | null>(null);
  protected readonly status = signal<RunStatus | null>(null);
  protected readonly scenarios = signal<ScenarioListItem[]>([]);
  protected readonly environments = signal<Environment[]>([]);
  protected readonly statuses: RunStatus[] = ['Queued', 'Running', 'Completed', 'Failed', 'Cancelled'];

  protected readonly statusSeverity = statusSeverity;
  protected readonly duration = formatDuration;
  protected readonly ms = formatMs;
  protected readonly pct = formatPct;
  protected readonly num = formatNumber;

  ngOnInit(): void {
    const q = this.route.snapshot.queryParamMap;
    this.scenarioId.set(q.get('scenarioId'));
    this.environmentId.set(q.get('environmentId'));
    this.status.set((q.get('status') as RunStatus | null) ?? null);
    this.api.scenarios().subscribe({ next: (s) => this.scenarios.set(s) });
    this.api.environments().subscribe({ next: (e) => this.environments.set(e) });

    const watch = this.hub.watchRunList();
    this.watch = watch;
    this.sub = watch.status.subscribe(() => this.load());
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    this.watch?.stop();
  }

  protected onLazyLoad(event: TableLazyLoadEvent): void {
    this.first.set(event.first ?? 0);
    this.pageSize.set(event.rows ?? 25);
    this.load();
  }

  protected setFilter(name: 'scenarioId' | 'environmentId' | 'status', value: string | null): void {
    if (name === 'scenarioId') this.scenarioId.set(value);
    if (name === 'environmentId') this.environmentId.set(value);
    if (name === 'status') this.status.set(value as RunStatus | null);
    this.first.set(0);
    void this.router.navigate([], {
      queryParams: { scenarioId: this.scenarioId(), environmentId: this.environmentId(), status: this.status() },
      replaceUrl: true,
    });
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api
      .runs({
        scenarioId: this.scenarioId(),
        environmentId: this.environmentId(),
        status: this.status(),
        page: Math.floor(this.first() / this.pageSize()) + 1,
        pageSize: this.pageSize(),
      })
      .subscribe({
        next: (page) => {
          this.runs.set(page.items);
          this.total.set(page.total);
          this.loading.set(false);
        },
        error: (e) => {
          this.loading.set(false);
          this.notify.error(e, 'Could not load runs');
        },
      });
  }

  protected open(r: RunListItem): void {
    void this.router.navigate(['/runs', r.id]);
  }
}
