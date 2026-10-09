import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { ToastModule } from 'primeng/toast';
import { SessionService } from './core/session.service';
import { ProductionConfirmComponent } from './shared/run-launcher';

@Component({
  selector: 'app-root',
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive, FormsModule, ButtonModule, DialogModule, InputTextModule, MessageModule,
    ToastModule, ConfirmDialogModule, ProductionConfirmComponent,
  ],
  template: `
    <header class="topbar">
      <a class="brand" routerLink="/">
        <i class="pi pi-bolt"></i>
        <span>PerfPilot</span>
      </a>
      <nav>
        <a routerLink="/environments" routerLinkActive="active"><i class="pi pi-server"></i> Environments</a>
        <a routerLink="/scenarios" routerLinkActive="active"><i class="pi pi-sitemap"></i> Scenarios</a>
        <a routerLink="/runs" routerLinkActive="active"><i class="pi pi-chart-line"></i> Runs</a>
      </nav>
      <span class="spacer"></span>
      <a routerLink="/help" routerLinkActive="active" class="help-link" title="User guide">
        <i class="pi pi-question-circle"></i> Help
      </a>
      @if (session.usesHeaderIdentity()) {
        <button type="button" class="user-chip" (click)="openNameDialog()" title="Change your display name">
          <i class="pi pi-user"></i> {{ session.userLabel() }}
        </button>
      } @else {
        <span class="user-chip static"><i class="pi pi-user"></i> {{ session.userLabel() }}</span>
      }
    </header>

    @if (session.config(); as cfg) {
      @if (!cfg.k6.available) {
        <div class="banner banner-warn">
          <i class="pi pi-exclamation-triangle"></i>
          <span>
            k6 is not available on the server: {{ cfg.k6.error }} Runs will fail until k6 is installed or <code>K6:BinaryPath</code> is set.
          </span>
        </div>
      }
    }

    <main class="page">
      <router-outlet />
    </main>

    <p-dialog header="Who are you?" [modal]="true" [visible]="nameDialog() || session.needsName()" [closable]="!session.needsName()"
      (visibleChange)="!$event && nameDialog.set(false)" [style]="{ width: '28rem' }">
      <p class="muted">Your name is recorded on the runs you start.</p>
      <label class="field-label" for="display-name">Display name</label>
      <input pInputText id="display-name" class="w-full" [(ngModel)]="nameDraft" (keydown.enter)="saveName()" autocomplete="name" />
      <ng-template #footer>
        <p-button label="Save" icon="pi pi-check" [disabled]="!nameDraft.trim()" (onClick)="saveName()" />
      </ng-template>
    </p-dialog>

    <app-production-confirm />
    <p-toast position="top-right" />
    <p-confirmdialog />
  `,
})
export class App implements OnInit {
  protected readonly session = inject(SessionService);
  protected readonly nameDialog = signal(false);
  protected nameDraft = '';

  ngOnInit(): void {
    this.session.load();
    this.nameDraft = this.session.displayName();
  }

  protected openNameDialog(): void {
    this.nameDraft = this.session.displayName();
    this.nameDialog.set(true);
  }

  protected saveName(): void {
    if (!this.nameDraft.trim()) return;
    this.session.setDisplayName(this.nameDraft);
    this.nameDialog.set(false);
  }
}
