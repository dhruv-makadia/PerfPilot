import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AccordionModule } from 'primeng/accordion';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { FileUploadModule, FileSelectEvent } from 'primeng/fileupload';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TabsModule } from 'primeng/tabs';
import { TagModule } from 'primeng/tag';
import { ApiService } from '../../core/api.service';
import { methodClass } from '../../core/format';
import { ApiError, toApiError } from '../../core/http';
import { ApiEndpoint, Environment, ImportSpecFromUrlRequest, SpecDetail, SpecSummary } from '../../core/models';
import { NotifyService } from '../../core/notify.service';
import { ErrorListComponent } from '../../shared/error-list';

@Component({
  selector: 'app-spec-browser',
  imports: [
    FormsModule, RouterLink, DatePipe, AccordionModule, ButtonModule, CheckboxModule, FileUploadModule, IconFieldModule, InputIconModule,
    InputNumberModule, InputTextModule, MessageModule, SelectModule, TabsModule, TagModule, ErrorListComponent,
  ],
  template: `
    <div class="page-header">
      <div>
        <a routerLink="/environments" class="back-link"><i class="pi pi-arrow-left"></i> Environments</a>
        <h1>{{ environment()?.name ?? 'Environment' }} <span class="muted">· API spec</span></h1>
        @if (environment(); as env) {
          <p class="muted mono small">{{ env.baseUrl }}</p>
        }
      </div>
      <div class="header-actions">
        <p-button label="Edit environment" icon="pi pi-pencil" [text]="true" [routerLink]="['/environments', id()]" />
        @if (detail()) {
          <p-button label="New scenario from this spec" icon="pi pi-plus" [routerLink]="['/scenarios/new']" [queryParams]="{ environmentId: id() }" />
        }
      </div>
    </div>

    <section class="panel">
      <h2>Import OpenAPI spec</h2>
      <p class="muted">OpenAPI 3.x or Swagger 2.0, JSON or YAML. The document is downloaded or uploaded to the PerfPilot server and parsed there.</p>
      <app-error-list [error]="importError()" />
      <p-tabs value="url">
        <p-tablist>
          <p-tab value="url"><i class="pi pi-link"></i> Fetch from URL</p-tab>
          <p-tab value="upload"><i class="pi pi-upload"></i> Upload file</p-tab>
        </p-tablist>
        <p-tabpanels>
          <p-tabpanel value="url">
            <label class="field-label" for="specUrl">Spec URL</label>
            <input pInputText id="specUrl" class="w-full mono" [(ngModel)]="importReq.url" placeholder="https://api.staging.example.com/openapi.json" />
            <div class="grid-3">
              <div>
                <label class="field-label" for="hdrName">Extra header name <span class="muted">(optional)</span></label>
                <input pInputText id="hdrName" class="w-full mono" [(ngModel)]="importReq.authHeaderName" placeholder="Authorization" />
              </div>
              <div>
                <label class="field-label" for="hdrValue">Header value</label>
                <input pInputText id="hdrValue" type="password" class="w-full mono" [(ngModel)]="importReq.authHeaderValue" autocomplete="off" />
              </div>
              <div>
                <label class="field-label" for="timeout">Timeout (seconds)</label>
                <p-inputnumber inputId="timeout" [(ngModel)]="importReq.timeoutSeconds" [min]="1" [max]="300" [showButtons]="true" styleClass="w-full" />
              </div>
            </div>
            <div class="switch-row">
              <p-checkbox inputId="useEnvAuth" [(ngModel)]="importReq.useEnvironmentAuth" [binary]="true" />
              <label for="useEnvAuth">Authenticate with the environment's auth settings</label>
            </div>
            <div class="switch-row">
              <p-checkbox inputId="ignoreTls" [(ngModel)]="importReq.ignoreTlsErrors" [binary]="true" />
              <label for="ignoreTls">Ignore TLS certificate errors (internal staging certificates)</label>
            </div>
            <p-button label="Fetch and import" icon="pi pi-download" [loading]="importing()" (onClick)="importFromUrl()" styleClass="mt-3" />
          </p-tabpanel>
          <p-tabpanel value="upload">
            <p-fileupload mode="basic" accept=".json,.yaml,.yml" [maxFileSize]="26214400" chooseLabel="Choose .json / .yaml file" chooseIcon="pi pi-upload"
              [auto]="false" [customUpload]="true" (onSelect)="onFileSelected($event)" [disabled]="importing()" />
            @if (importing()) {
              <p class="muted"><i class="pi pi-spin pi-spinner"></i> Uploading and parsing…</p>
            }
          </p-tabpanel>
        </p-tabpanels>
      </p-tabs>
    </section>

    @if (versions().length > 0) {
      <section class="panel">
        <div class="panel-header">
          <h2>Endpoints</h2>
          <div class="header-actions">
            <p-select [options]="versions()" [ngModel]="selectedSpecId()" (ngModelChange)="selectVersion($event)" optionValue="id" [optionLabel]="'label'"
              styleClass="version-select" />
            @if (detail(); as d) {
              <a class="p-button p-button-text p-button-sm" [href]="api.specRawUrl(d.spec.id)"><i class="pi pi-file"></i>&nbsp;Raw document</a>
            }
          </div>
        </div>
        @if (detail(); as d) {
          <div class="spec-meta">
            <strong>{{ d.spec.title }}</strong>
            @if (d.spec.apiVersion) { <span class="muted">v{{ d.spec.apiVersion }}</span> }
            <p-tag [value]="d.spec.specFormat" severity="secondary" />
            <span class="muted small">{{ d.spec.endpointCount }} endpoints · imported {{ d.spec.importedAt | date: 'medium' }}
              @if (d.spec.importedBy) { by {{ d.spec.importedBy }} } from {{ d.spec.source === 'Url' ? d.spec.sourceLocation : 'upload: ' + d.spec.sourceLocation }}</span>
          </div>
          @if (d.spec.warnings.length > 0) {
            <p-message severity="warn" styleClass="mb-3 w-full">
              <div>
                <strong>{{ d.spec.warnings.length }} parser warning(s)</strong>
                <ul class="error-items">
                  @for (w of d.spec.warnings; track $index) { <li>{{ w }}</li> }
                </ul>
              </div>
            </p-message>
          }
          <p-iconfield styleClass="mb-3">
            <p-inputicon styleClass="pi pi-search" />
            <input pInputText class="w-full" placeholder="Filter by path, method, summary or tag" [ngModel]="filter()" (ngModelChange)="filter.set($event)" />
          </p-iconfield>

          <p-accordion [multiple]="true" [value]="openTags()">
            @for (group of filteredGroups(); track group.tag) {
              <p-accordion-panel [value]="group.tag">
                <p-accordion-header>
                  <span class="tag-title">{{ group.tag }}</span>
                  <span class="muted small ml-2">{{ group.endpoints.length }}</span>
                </p-accordion-header>
                <p-accordion-content>
                  @for (ep of group.endpoints; track ep.id) {
                    <div class="endpoint-row" [class.expanded]="expanded() === ep.id">
                      <button type="button" class="endpoint-line" (click)="toggle(ep.id)">
                        <span [class]="methodClass(ep.method)">{{ ep.method }}</span>
                        <span class="mono" [class.deprecated]="ep.deprecated">{{ ep.path }}</span>
                        <span class="muted ellipsis">{{ ep.summary }}</span>
                        <i class="pi" [class.pi-chevron-down]="expanded() !== ep.id" [class.pi-chevron-up]="expanded() === ep.id"></i>
                      </button>
                      @if (expanded() === ep.id) {
                        <div class="endpoint-details">
                          @if (ep.description) { <p>{{ ep.description }}</p> }
                          @if (ep.deprecated) { <p-tag severity="warn" value="Deprecated" /> }
                          <div class="small muted">Responses: {{ ep.responseCodes.join(', ') || '–' }}
                            @if (ep.operationId) { · operationId <code>{{ ep.operationId }}</code> }</div>
                          @if (ep.parameters.length > 0) {
                            <h4>Parameters</h4>
                            <table class="simple-table">
                              <thead><tr><th>Name</th><th>In</th><th>Type</th><th>Required</th><th>Example / enum</th><th>Description</th></tr></thead>
                              <tbody>
                                @for (p of ep.parameters; track p.name + p.in) {
                                  <tr>
                                    <td class="mono">{{ p.name }}</td>
                                    <td>{{ p.in }}</td>
                                    <td class="mono small">{{ p.type }}{{ p.format ? ' (' + p.format + ')' : '' }}</td>
                                    <td>{{ p.required ? 'yes' : '' }}</td>
                                    <td class="mono small">{{ p.enum?.join(' | ') ?? p.example }}</td>
                                    <td class="small">{{ p.description }}</td>
                                  </tr>
                                }
                              </tbody>
                            </table>
                          }
                          @if (ep.requestBodyContentType) {
                            <h4>Request body <span class="muted small">{{ ep.requestBodyContentType }}</span></h4>
                            <div class="grid-2">
                              <div>
                                <div class="small muted">Schema</div>
                                <pre class="code-block">{{ ep.requestBodySchema ?? '–' }}</pre>
                              </div>
                              <div>
                                <div class="small muted">Generated sample</div>
                                <pre class="code-block">{{ ep.sampleRequestBody ?? '–' }}</pre>
                              </div>
                            </div>
                          }
                        </div>
                      }
                    </div>
                  }
                </p-accordion-content>
              </p-accordion-panel>
            }
          </p-accordion>
        }
      </section>
    } @else if (!loading()) {
      <section class="panel empty">No spec imported yet for this environment.</section>
    }
  `,
})
export class SpecBrowserComponent implements OnInit {
  readonly id = input.required<string>();

  protected readonly api = inject(ApiService);
  private readonly notify = inject(NotifyService);

  protected readonly environment = signal<Environment | null>(null);
  protected readonly versions = signal<(SpecSummary & { label: string })[]>([]);
  protected readonly selectedSpecId = signal<string | null>(null);
  protected readonly detail = signal<SpecDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly importing = signal(false);
  protected readonly importError = signal<ApiError | null>(null);
  protected readonly filter = signal('');
  protected readonly expanded = signal<string | null>(null);
  protected readonly methodClass = methodClass;

  private readonly importState = signal<ImportSpecFromUrlRequest>({
    url: null,
    authHeaderName: null,
    authHeaderValue: null,
    useEnvironmentAuth: false,
    timeoutSeconds: 30,
    ignoreTlsErrors: false,
  });
  protected get importReq(): ImportSpecFromUrlRequest {
    return this.importState();
  }

  protected readonly filteredGroups = computed(() => {
    const d = this.detail();
    if (!d) return [];
    const q = this.filter().trim().toLowerCase();
    if (!q) return d.groups;
    const matches = (e: ApiEndpoint) =>
      `${e.method} ${e.path} ${e.summary ?? ''} ${e.operationId ?? ''} ${e.tag}`.toLowerCase().includes(q);
    return d.groups.map((g) => ({ ...g, endpoints: g.endpoints.filter(matches) })).filter((g) => g.endpoints.length > 0);
  });

  protected readonly openTags = computed(() => this.filteredGroups().map((g) => g.tag));

  ngOnInit(): void {
    this.api.environment(this.id()).subscribe({
      next: (env) => {
        this.environment.set(env);
        this.importState.set({ ...this.importReq, url: env.openApiUrl, ignoreTlsErrors: env.ignoreTlsErrors });
      },
      error: (e) => this.notify.error(e, 'Could not load environment'),
    });
    this.loadVersions(true);
  }

  private loadVersions(selectLatest: boolean): void {
    this.api.specVersions(this.id()).subscribe({
      next: (list) => {
        this.loading.set(false);
        this.versions.set(list.map((s) => ({ ...s, label: `v${s.version} · ${new Date(s.importedAt).toLocaleString()}` })));
        if (selectLatest && list.length > 0) this.selectVersion(list[0].id);
      },
      error: (e) => {
        this.loading.set(false);
        this.notify.error(e, 'Could not load spec versions');
      },
    });
  }

  protected selectVersion(specId: string): void {
    this.selectedSpecId.set(specId);
    this.api.spec(specId).subscribe({
      next: (d) => this.detail.set(d),
      error: (e) => this.notify.error(e, 'Could not load spec'),
    });
  }

  protected toggle(id: string): void {
    this.expanded.set(this.expanded() === id ? null : id);
  }

  protected importFromUrl(): void {
    this.runImport(this.api.importSpecFromUrl(this.id(), this.importReq));
  }

  protected onFileSelected(event: FileSelectEvent): void {
    const file = event.files[0];
    if (file) this.runImport(this.api.uploadSpec(this.id(), file));
  }

  private runImport(call: ReturnType<ApiService['uploadSpec']>): void {
    this.importing.set(true);
    this.importError.set(null);
    call.subscribe({
      next: (d) => {
        this.importing.set(false);
        this.notify.success(`Imported spec v${d.spec.version}`, `${d.spec.endpointCount} endpoints from ${d.spec.title}`);
        this.detail.set(d);
        this.selectedSpecId.set(d.spec.id);
        this.loadVersions(false);
      },
      error: (e) => {
        this.importing.set(false);
        this.importError.set(toApiError(e));
      },
    });
  }
}
