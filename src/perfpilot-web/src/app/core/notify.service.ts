import { Injectable, inject } from '@angular/core';
import { MessageService } from 'primeng/api';
import { toApiError } from './http';

@Injectable({ providedIn: 'root' })
export class NotifyService {
  private readonly messages = inject(MessageService);

  success(summary: string, detail?: string): void {
    this.messages.add({ severity: 'success', summary, detail, life: 3500 });
  }

  info(summary: string, detail?: string): void {
    this.messages.add({ severity: 'info', summary, detail, life: 3500 });
  }

  error(error: unknown, summary?: string): void {
    const e = toApiError(error);
    this.messages.add({
      severity: 'error',
      summary: summary ?? e.title,
      detail: e.messages.join('\n'),
      life: 8000,
    });
  }
}
