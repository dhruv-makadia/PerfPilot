import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { AppConfig } from './models';

const USER_KEY = 'perfpilot.user';

function readStoredName(): string {
  try {
    return localStorage.getItem(USER_KEY) ?? '';
  } catch {
    return '';
  }
}

/** App configuration and the current user's display name. */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly api = inject(ApiService);

  readonly config = signal<AppConfig | null>(null);
  readonly displayName = signal<string>(readStoredName());

  readonly usesHeaderIdentity = computed(() => this.config()?.identityMode !== 'Windows');
  readonly userLabel = computed(() =>
    this.usesHeaderIdentity() ? this.displayName() || 'Set your name' : (this.config()?.currentUser ?? ''),
  );
  readonly needsName = computed(() => this.config() !== null && this.usesHeaderIdentity() && !this.displayName());

  load(): void {
    this.api.config().subscribe({ next: (c) => this.config.set(c) });
  }

  setDisplayName(name: string): void {
    const trimmed = name.trim().slice(0, 100);
    this.displayName.set(trimmed);
    try {
      localStorage.setItem(USER_KEY, trimmed);
    } catch {
      // Storage unavailable (private mode); keep the name for this session only.
    }
  }
}
