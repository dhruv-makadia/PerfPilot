import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import { ApiService } from '../../core/api.service';
import { AuthType, Environment } from '../../core/models';
import { NotifyService } from '../../core/notify.service';

@Component({
  selector: 'app-environment-list',
  imports: [RouterLink, DatePipe, ButtonModule, TableModule, TagModule, TooltipModule],
  template: `
    <div class="page-header">
      <div>
        <h1>Environments</h1>
        <p class="muted">Target systems you can load test. All calls to them are made by the PerfPilot server, never the browser.</p>
      </div>
      <p-button label="New environment" icon="pi pi-plus" routerLink="/environments/new" />
    </div>

    <div class="panel">
      <p-table [value]="environments()" [loading]="loading()" dataKey="id" [tableStyle]="{ 'min-width': '60rem' }">
        <ng-template #header>
          <tr>
            <th>Name</th>
            <th>Base URL</th>
            <th>Auth</th>
            <th>API spec</th>
            <th style="width: 16rem"></th>
          </tr>
        </ng-template>
        <ng-template #body let-env>
          <tr>
            <td>
              <a [routerLink]="['/environments', env.id]" class="strong-link">{{ env.name }}</a>
              @if (env.isProduction) {
                <p-tag severity="danger" value="PRODUCTION" styleClass="ml-2" />
              }
              @if (env.ignoreTlsErrors) {
                <i class="pi pi-lock-open ml-2 text-warn" pTooltip="TLS certificate errors are ignored"></i>
              }
            </td>
            <td class="mono small">{{ env.baseUrl }}</td>
            <td>{{ authLabel(env.auth.type) }}</td>
            <td>
              @if (env.latestSpec; as spec) {
                <div>v{{ spec.version }} · {{ spec.endpointCount }} endpoints</div>
                <div class="muted small">{{ spec.importedAt | date: 'medium' }}</div>
              } @else {
                <span class="muted">Not imported</span>
              }
            </td>
            <td class="actions">
              <p-button icon="pi pi-list" label="Endpoints" size="small" [outlined]="true" [routerLink]="['/environments', env.id, 'spec']" />
              <p-button icon="pi pi-pencil" size="small" [text]="true" pTooltip="Edit" [routerLink]="['/environments', env.id]" />
              <p-button icon="pi pi-trash" size="small" [text]="true" severity="danger" pTooltip="Delete" (onClick)="confirmDelete(env)" />
            </td>
          </tr>
        </ng-template>
        <ng-template #emptymessage>
          <tr>
            <td colspan="5" class="empty">
              No environments yet. <a routerLink="/environments/new">Create one</a> to get started.
            </td>
          </tr>
        </ng-template>
      </p-table>
    </div>
  `,
})
export class EnvironmentListComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly confirm = inject(ConfirmationService);
  private readonly notify = inject(NotifyService);

  protected readonly environments = signal<Environment[]>([]);
  protected readonly loading = signal(true);

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.api.environments().subscribe({
      next: (list) => {
        this.environments.set(list);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        this.notify.error(e, 'Could not load environments');
      },
    });
  }

  protected authLabel(type: AuthType): string {
    return { None: 'None', BearerToken: 'Bearer token', ApiKey: 'API key', LoginFlow: 'Login flow' }[type];
  }

  protected confirmDelete(env: Environment): void {
    this.confirm.confirm({
      header: 'Delete environment',
      message: `Delete "${env.name}" and all its imported spec versions? Past run results are kept.`,
      icon: 'pi pi-exclamation-triangle',
      acceptButtonProps: { label: 'Delete', severity: 'danger' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', text: true },
      accept: () =>
        this.api.deleteEnvironment(env.id).subscribe({
          next: () => {
            this.notify.success('Environment deleted', env.name);
            this.load();
          },
          error: (e) => this.notify.error(e, 'Could not delete environment'),
        }),
    });
  }
}
