import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { PasswordModule } from 'primeng/password';
import { SelectModule } from 'primeng/select';
import { SelectButtonModule } from 'primeng/selectbutton';
import { TextareaModule } from 'primeng/textarea';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { ApiService } from '../../core/api.service';
import { ApiError, toApiError } from '../../core/http';
import { AuthType, ConnectionTestResult, Environment, LoginScope, SaveEnvironmentRequest } from '../../core/models';
import { NotifyService } from '../../core/notify.service';
import { ErrorListComponent } from '../../shared/error-list';
import { jsonError } from '../../shared/json-editor';

interface EnvForm {
  name: string;
  baseUrl: string;
  openApiUrl: string;
  isProduction: boolean;
  ignoreTlsErrors: boolean;
  authType: AuthType;
  secret: string;
  clearSecret: boolean;
  apiKeyHeaderName: string;
  loginUrl: string;
  loginBody: string;
  loginTokenJsonPath: string;
  loginTokenHeaderName: string;
  loginTokenPrefix: string;
  loginScope: LoginScope;
}

const emptyForm = (): EnvForm => ({
  name: '',
  baseUrl: '',
  openApiUrl: '',
  isProduction: false,
  ignoreTlsErrors: false,
  authType: 'None',
  secret: '',
  clearSecret: false,
  apiKeyHeaderName: 'X-API-Key',
  loginUrl: '/auth/login',
  loginBody: '{\n  "username": "perf-user",\n  "password": "change-me"\n}',
  loginTokenJsonPath: '$.access_token',
  loginTokenHeaderName: 'Authorization',
  loginTokenPrefix: 'Bearer ',
  loginScope: 'PerVu',
});

@Component({
  selector: 'app-environment-form',
  imports: [
    FormsModule, RouterLink, ButtonModule, CheckboxModule, InputTextModule, MessageModule, PasswordModule, SelectModule,
    SelectButtonModule, TextareaModule, ToggleSwitchModule, ErrorListComponent,
  ],
  template: `
    <div class="page-header">
      <div>
        <a routerLink="/environments" class="back-link"><i class="pi pi-arrow-left"></i> Environments</a>
        <h1>{{ id() ? 'Edit environment' : 'New environment' }}</h1>
      </div>
      @if (id()) {
        <p-button label="Endpoints & spec import" icon="pi pi-list" [outlined]="true" [routerLink]="['/environments', id(), 'spec']" />
      }
    </div>

    <app-error-list [error]="error()" />

    <div class="grid-2">
      <section class="panel">
        <h2>Target</h2>
        <label class="field-label" for="name">Name</label>
        <input pInputText id="name" class="w-full" [(ngModel)]="form.name" placeholder="e.g. Orders API – Staging" />

        <label class="field-label" for="baseUrl">Base URL</label>
        <input pInputText id="baseUrl" class="w-full mono" [(ngModel)]="form.baseUrl" placeholder="https://orders.staging.example.com/api" />
        <small class="hint">Endpoint paths from the spec are appended to this URL.</small>

        <label class="field-label" for="openApiUrl">OpenAPI URL <span class="muted">(optional)</span></label>
        <input pInputText id="openApiUrl" class="w-full mono" [(ngModel)]="form.openApiUrl" placeholder="https://orders.staging.example.com/swagger/v1/swagger.json" />

        <div class="switch-row">
          <p-toggleswitch inputId="isProd" [(ngModel)]="form.isProduction" />
          <label for="isProd"><strong>Production environment</strong> — runs require typing the environment name to confirm.</label>
        </div>
        <div class="switch-row">
          <p-toggleswitch inputId="ignoreTls" [(ngModel)]="form.ignoreTlsErrors" />
          <label for="ignoreTls"><strong>Ignore TLS certificate errors</strong> — for internal staging certificates only (connection test and k6).</label>
        </div>
      </section>

      <section class="panel">
        <h2>Authentication</h2>
        <p-selectbutton [options]="authOptions" [(ngModel)]="form.authType" optionLabel="label" optionValue="value" [allowEmpty]="false" />

        @switch (form.authType) {
          @case ('None') {
            <p class="muted mt-3">Requests are sent without credentials.</p>
          }
          @case ('BearerToken') {
            <label class="field-label" for="secret">Bearer token</label>
            <p-password inputId="secret" [(ngModel)]="form.secret" [feedback]="false" [toggleMask]="true" styleClass="w-full" inputStyleClass="w-full mono"
              [placeholder]="secretPlaceholder()" autocomplete="off" />
            <small class="hint">Sent as <code>Authorization: Bearer &lt;token&gt;</code>.</small>
          }
          @case ('ApiKey') {
            <label class="field-label" for="apiKeyHeader">Header name</label>
            <input pInputText id="apiKeyHeader" class="w-full mono" [(ngModel)]="form.apiKeyHeaderName" />
            <label class="field-label" for="secret">API key value</label>
            <p-password inputId="secret" [(ngModel)]="form.secret" [feedback]="false" [toggleMask]="true" styleClass="w-full" inputStyleClass="w-full mono"
              [placeholder]="secretPlaceholder()" autocomplete="off" />
          }
          @case ('LoginFlow') {
            <label class="field-label" for="loginUrl">Token endpoint (POST)</label>
            <input pInputText id="loginUrl" class="w-full mono" [(ngModel)]="form.loginUrl" placeholder="/auth/login or https://idp.example.com/token" />
            <label class="field-label" for="loginBody">JSON body</label>
            <textarea pTextarea id="loginBody" class="w-full code-input" rows="6" [(ngModel)]="form.loginBody"
              [placeholder]="hasStoredLoginBody() ? 'Saved — leave unchanged to keep the stored body' : ''"></textarea>
            @if (loginBodyError()) {
              <small class="text-danger">{{ loginBodyError() }}</small>
            }
            <div class="grid-3">
              <div>
                <label class="field-label" for="tokenPath">Token JSONPath</label>
                <input pInputText id="tokenPath" class="w-full mono" [(ngModel)]="form.loginTokenJsonPath" />
              </div>
              <div>
                <label class="field-label" for="tokenHeader">Send token in header</label>
                <input pInputText id="tokenHeader" class="w-full mono" [(ngModel)]="form.loginTokenHeaderName" />
              </div>
              <div>
                <label class="field-label" for="tokenPrefix">Token prefix</label>
                <input pInputText id="tokenPrefix" class="w-full mono" [(ngModel)]="form.loginTokenPrefix" />
              </div>
            </div>
            <label class="field-label" for="loginScope">When k6 logs in</label>
            <p-select inputId="loginScope" [options]="scopeOptions" [(ngModel)]="form.loginScope" optionLabel="label" optionValue="value" styleClass="w-full" />
          }
        }

        @if ((form.authType === 'BearerToken' || form.authType === 'ApiKey') && hasStoredSecret()) {
          <div class="switch-row">
            <p-checkbox inputId="clearSecret" [(ngModel)]="form.clearSecret" [binary]="true" />
            <label for="clearSecret">Remove the stored secret</label>
          </div>
        }
        <p-message severity="secondary" styleClass="mt-3" size="small">
          Secrets are encrypted at rest and never shown again after saving. k6 receives them as environment variables, not in the script.
        </p-message>
      </section>
    </div>

    <section class="panel">
      <div class="panel-header">
        <h2>Test connection</h2>
        <p-button label="Test connection" icon="pi pi-wifi" [outlined]="true" [loading]="testing()" (onClick)="test()" />
      </div>
      <p class="muted">The PerfPilot server calls the base URL{{ form.openApiUrl ? ', the OpenAPI URL' : '' }}{{ form.authType === 'LoginFlow' ? ' and the login endpoint' : '' }} and reports what it sees.</p>
      @if (testResult(); as result) {
        <ul class="checks">
          @for (check of result.checks; track check.name) {
            <li [class]="'check check-' + check.severity.toLowerCase()">
              <i class="pi" [class.pi-check-circle]="check.severity === 'Ok'" [class.pi-exclamation-triangle]="check.severity === 'Warning'"
                [class.pi-times-circle]="check.severity === 'Error'"></i>
              <div>
                <div><strong>{{ check.name }}</strong> <span class="mono small muted">{{ check.url }}</span></div>
                <div>{{ check.message }}</div>
                <div class="small muted">
                  @if (check.statusCode) { HTTP {{ check.statusCode }} · }
                  {{ check.latencyMs }} ms
                  @if (check.errorKind) { · {{ check.errorKind }} }
                </div>
              </div>
            </li>
          }
        </ul>
      }
    </section>

    <div class="form-actions">
      <p-button label="Cancel" severity="secondary" [text]="true" routerLink="/environments" />
      <p-button [label]="id() ? 'Save changes' : 'Create environment'" icon="pi pi-check" [loading]="saving()" (onClick)="save()" />
    </div>
  `,
})
export class EnvironmentFormComponent implements OnInit {
  readonly id = input<string>();

  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);

  private readonly formState = signal<EnvForm>(emptyForm());
  protected get form(): EnvForm {
    return this.formState();
  }
  protected readonly existing = signal<Environment | null>(null);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly saving = signal(false);
  protected readonly testing = signal(false);
  protected readonly testResult = signal<ConnectionTestResult | null>(null);

  protected readonly authOptions = [
    { label: 'None', value: 'None' },
    { label: 'Bearer token', value: 'BearerToken' },
    { label: 'API key', value: 'ApiKey' },
    { label: 'Login flow', value: 'LoginFlow' },
  ];
  protected readonly scopeOptions = [
    { label: 'Once per virtual user (on its first iteration)', value: 'PerVu' },
    { label: 'Once in setup(), token shared by all VUs', value: 'Shared' },
  ];

  ngOnInit(): void {
    const id = this.id();
    if (!id) return;
    this.api.environment(id).subscribe({
      next: (env) => {
        this.existing.set(env);
        this.formState.set({
          ...emptyForm(),
          name: env.name,
          baseUrl: env.baseUrl,
          openApiUrl: env.openApiUrl ?? '',
          isProduction: env.isProduction,
          ignoreTlsErrors: env.ignoreTlsErrors,
          authType: env.auth.type,
          apiKeyHeaderName: env.auth.apiKeyHeaderName ?? 'X-API-Key',
          loginUrl: env.auth.loginUrl ?? '/auth/login',
          loginBody: '',
          loginTokenJsonPath: env.auth.loginTokenJsonPath ?? '$.access_token',
          loginTokenHeaderName: env.auth.loginTokenHeaderName,
          loginTokenPrefix: env.auth.loginTokenPrefix,
          loginScope: env.auth.loginScope,
        });
      },
      error: (e) => this.notify.error(e, 'Could not load environment'),
    });
  }

  protected hasStoredSecret(): boolean {
    const env = this.existing();
    return !!env && env.auth.hasSecret && env.auth.type === this.form.authType;
  }

  protected hasStoredLoginBody(): boolean {
    const env = this.existing();
    return !!env && env.auth.hasLoginBody && env.auth.type === 'LoginFlow';
  }

  protected secretPlaceholder(): string {
    return this.hasStoredSecret() ? `${this.existing()?.auth.secretMask} (saved — leave blank to keep)` : '';
  }

  protected loginBodyError(): string | null {
    return this.form.loginBody.trim() ? jsonError(this.form.loginBody) : null;
  }

  private toRequest(): SaveEnvironmentRequest {
    const f = this.form;
    return {
      name: f.name,
      baseUrl: f.baseUrl,
      openApiUrl: f.openApiUrl || null,
      isProduction: f.isProduction,
      ignoreTlsErrors: f.ignoreTlsErrors,
      auth: {
        type: f.authType,
        secret: f.secret || null,
        clearSecret: f.clearSecret,
        apiKeyHeaderName: f.apiKeyHeaderName || null,
        loginUrl: f.loginUrl || null,
        loginBody: f.loginBody.trim() ? f.loginBody : null,
        loginTokenJsonPath: f.loginTokenJsonPath || null,
        loginTokenHeaderName: f.loginTokenHeaderName || null,
        loginTokenPrefix: f.loginTokenPrefix,
        loginScope: f.loginScope,
      },
    };
  }

  protected test(): void {
    this.testing.set(true);
    this.testResult.set(null);
    this.error.set(null);
    this.api.testConnection(this.id() ?? null, this.toRequest()).subscribe({
      next: (r) => {
        this.testing.set(false);
        this.testResult.set(r);
      },
      error: (e) => {
        this.testing.set(false);
        this.error.set(toApiError(e));
      },
    });
  }

  protected save(): void {
    this.saving.set(true);
    this.error.set(null);
    const id = this.id();
    const request = this.toRequest();
    const call = id ? this.api.updateEnvironment(id, request) : this.api.createEnvironment(request);
    call.subscribe({
      next: (env) => {
        this.saving.set(false);
        this.notify.success(id ? 'Environment saved' : 'Environment created', env.name);
        void this.router.navigate(id ? ['/environments'] : ['/environments', env.id, 'spec']);
      },
      error: (e) => {
        this.saving.set(false);
        this.error.set(toApiError(e));
      },
    });
  }
}
