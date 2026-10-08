import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import { ApiService } from '../../core/api.service';
import { formatDuration, statusSeverity } from '../../core/format';
import { Environment, ScenarioListItem } from '../../core/models';
import { NotifyService } from '../../core/notify.service';
import { RunLauncher } from '../../shared/run-launcher';

@Component({
  selector: 'app-scenario-list',
  imports: [FormsModule, RouterLink, DatePipe, ButtonModule, SelectModule, TableModule, TagModule, TooltipModule],
  template: `
    <div class="page-header">
      <div>
        <h1>Scenarios</h1>
        <p class="muted">Saved load tests: endpoints, traffic mix, load profile and SLA thresholds.</p>
      </div>
      <div class="header-actions">
        <p-select [options]="environments()" [ngModel]="environmentFilter()" (ngModelChange)="setFilter($event)" optionLabel="name" optionValue="id"
          placeholder="All environments" [showClear]="true" styleClass="filter-select" />
        <p-button label="New scenario" icon="pi pi-plus" routerLink="/scenarios/new" />
      </div>
    </div>

    <div class="panel">
      <p-table [value]="scenarios()" [loading]="loading()" dataKey="id" [tableStyle]="{ 'min-width': '64rem' }">
        <ng-template #header>
          <tr>
            <th>Name</th>
            <th>Environment</th>
            <th>Endpoints</th>
            <th>Load</th>
            <th>Last run</th>
            <th>Updated</th>
            <th style="width: 15rem"></th>
          </tr>
        </ng-template>
        <ng-template #body let-s>
          <tr>
            <td>
              <a [routerLink]="['/scenarios', s.id]" class="strong-link">{{ s.name }}</a>
              @if (s.description) { <div class="muted small ellipsis">{{ s.description }}</div> }
            </td>
            <td>
              {{ s.environmentName }}
              @if (s.environmentIsProduction) { <p-tag severity="danger" value="PROD" styleClass="ml-1" /> }
            </td>
            <td>{{ s.endpointCount }}</td>
            <td>
              <p-tag [value]="s.preset" severity="secondary" />
              <span class="small muted ml-1">{{ s.maxVus }} VUs · {{ duration(s.totalDurationSeconds) }}</span>
            </td>
            <td>
              @if (s.lastRun; as r) {
                <a [routerLink]="['/runs', r.id]" class="nowrap">
                  <p-tag [value]="r.status" [severity]="statusSeverity(r.status)" />
                  @if (r.thresholdsPassed === true) { <i class="pi pi-check-circle text-success ml-1" pTooltip="SLA passed"></i> }
                  @if (r.thresholdsPassed === false) { <i class="pi pi-times-circle text-danger ml-1" pTooltip="SLA breached"></i> }
                </a>
                <div class="small muted">{{ r.queuedAt | date: 'short' }}</div>
              } @else {
                <span class="muted">Never</span>
              }
            </td>
            <td class="small">{{ s.updatedAt | date: 'short' }}</td>
            <td class="actions">
              <p-button icon="pi pi-play" label="Run" size="small" (onClick)="run(s)" />
              <p-button icon="pi pi-copy" size="small" [text]="true" pTooltip="Clone" (onClick)="clone(s)" />
              <p-button icon="pi pi-history" size="small" [text]="true" pTooltip="Run history" [routerLink]="['/runs']" [queryParams]="{ scenarioId: s.id }" />
              <p-button icon="pi pi-trash" size="small" [text]="true" severity="danger" pTooltip="Delete" (onClick)="confirmDelete(s)" />
            </td>
          </tr>
        </ng-template>
        <ng-template #emptymessage>
          <tr>
            <td colspan="7" class="empty">No scenarios yet. <a routerLink="/scenarios/new">Build one</a> from an imported spec.</td>
          </tr>
        </ng-template>
      </p-table>
    </div>
  `,
})
export class ScenarioListComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly confirm = inject(ConfirmationService);
  private readonly notify = inject(NotifyService);
  private readonly launcher = inject(RunLauncher);

  protected readonly scenarios = signal<ScenarioListItem[]>([]);
  protected readonly environments = signal<Environment[]>([]);
  protected readonly environmentFilter = signal<string | null>(null);
  protected readonly loading = signal(true);
  protected readonly statusSeverity = statusSeverity;
  protected readonly duration = formatDuration;

  ngOnInit(): void {
    this.api.environments().subscribe({ next: (e) => this.environments.set(e) });
    this.load();
  }

  protected setFilter(id: string | null): void {
    this.environmentFilter.set(id);
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.api.scenarios(this.environmentFilter()).subscribe({
      next: (list) => {
        this.scenarios.set(list);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        this.notify.error(e, 'Could not load scenarios');
      },
    });
  }

  protected run(s: ScenarioListItem): void {
    this.launcher.start({
      scenarioId: s.id,
      scenarioName: s.name,
      environmentName: s.environmentName,
      environmentIsProduction: s.environmentIsProduction,
    });
  }

  protected clone(s: ScenarioListItem): void {
    this.api.cloneScenario(s.id).subscribe({
      next: (c) => {
        this.notify.success('Scenario cloned', c.name);
        void this.router.navigate(['/scenarios', c.id]);
      },
      error: (e) => this.notify.error(e, 'Could not clone scenario'),
    });
  }

  protected confirmDelete(s: ScenarioListItem): void {
    this.confirm.confirm({
      header: 'Delete scenario',
      message: `Delete "${s.name}"? Its past runs and results are kept.`,
      icon: 'pi pi-exclamation-triangle',
      acceptButtonProps: { label: 'Delete', severity: 'danger' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', text: true },
      accept: () =>
        this.api.deleteScenario(s.id).subscribe({
          next: () => {
            this.notify.success('Scenario deleted', s.name);
            this.load();
          },
          error: (e) => this.notify.error(e, 'Could not delete scenario'),
        }),
    });
  }
}
