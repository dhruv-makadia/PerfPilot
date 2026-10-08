import { Component, Injectable, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { ApiService } from '../core/api.service';
import { NotifyService } from '../core/notify.service';
import { SessionService } from '../core/session.service';

export interface RunTarget {
  scenarioId: string;
  scenarioName: string;
  environmentName: string;
  environmentIsProduction: boolean;
}

/** Starts runs; production environments require typing the environment name first. */
@Injectable({ providedIn: 'root' })
export class RunLauncher {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);
  private readonly session = inject(SessionService);

  readonly pending = signal<RunTarget | null>(null);
  readonly starting = signal(false);

  start(target: RunTarget): void {
    if (this.session.needsName()) {
      this.notify.info('Set your name first', 'Runs record who started them. Click "Set your name" in the top bar.');
      return;
    }
    if (target.environmentIsProduction) {
      this.pending.set(target);
      return;
    }
    this.submit(target, null);
  }

  submit(target: RunTarget, confirmation: string | null): void {
    this.starting.set(true);
    this.api.startRun({ scenarioId: target.scenarioId, confirmEnvironmentName: confirmation }).subscribe({
      next: (run) => {
        this.starting.set(false);
        this.pending.set(null);
        this.notify.success('Run queued', `${target.scenarioName} on ${target.environmentName}`);
        void this.router.navigate(['/runs', run.id]);
      },
      error: (e) => {
        this.starting.set(false);
        this.notify.error(e, 'Could not start run');
      },
    });
  }
}

@Component({
  selector: 'app-production-confirm',
  imports: [FormsModule, DialogModule, ButtonModule, InputTextModule, MessageModule],
  template: `
    <p-dialog header="Run against production?" [modal]="true" [visible]="!!launcher.pending()" (visibleChange)="!$event && close()"
      [style]="{ width: '34rem' }" [closable]="true">
      @if (launcher.pending(); as target) {
        <p-message severity="error" styleClass="mb-3">
          <strong>{{ target.environmentName }}</strong> is marked as a production environment. This will send real load to it.
        </p-message>
        <p>Scenario: <strong>{{ target.scenarioName }}</strong></p>
        <label class="field-label" for="confirm-env">Type <code>{{ target.environmentName }}</code> to confirm</label>
        <input pInputText id="confirm-env" class="w-full" [(ngModel)]="typed" autocomplete="off" (keydown.enter)="confirm()" />
      }
      <ng-template #footer>
        <p-button label="Cancel" [text]="true" severity="secondary" (onClick)="close()" />
        <p-button label="Start run" icon="pi pi-play" severity="danger" [disabled]="!matches()" [loading]="launcher.starting()" (onClick)="confirm()" />
      </ng-template>
    </p-dialog>
  `,
})
export class ProductionConfirmComponent {
  protected readonly launcher = inject(RunLauncher);
  protected readonly typedSignal = signal('');
  protected readonly matches = computed(() => this.typedSignal().trim() === this.launcher.pending()?.environmentName);

  protected get typed(): string {
    return this.typedSignal();
  }
  protected set typed(value: string) {
    this.typedSignal.set(value);
  }

  protected confirm(): void {
    const target = this.launcher.pending();
    if (target && this.matches()) this.launcher.submit(target, this.typedSignal().trim());
  }

  protected close(): void {
    this.launcher.pending.set(null);
    this.typedSignal.set('');
  }
}
