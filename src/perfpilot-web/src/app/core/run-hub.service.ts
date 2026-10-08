import { Injectable, OnDestroy } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Observable, Subject, filter } from 'rxjs';
import { RunProgressMessage, RunStatusMessage } from './models';

/** SignalR client for live run progress (/hubs/runs on the same origin). */
@Injectable({ providedIn: 'root' })
export class RunHubService implements OnDestroy {
  private readonly connection: HubConnection;
  private readonly status$ = new Subject<RunStatusMessage>();
  private readonly progress$ = new Subject<RunProgressMessage>();
  private readonly runGroups = new Map<string, number>();
  private listSubscribers = 0;
  private starting: Promise<void> | null = null;

  constructor() {
    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/runs')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();
    this.connection.on('runStatus', (m: RunStatusMessage) => this.status$.next(m));
    this.connection.on('runProgress', (m: RunProgressMessage) => this.progress$.next(m));
    // Groups are per connection; re-join after an automatic reconnect.
    this.connection.onreconnected(() => {
      for (const runId of this.runGroups.keys()) void this.connection.invoke('JoinRun', runId);
      if (this.listSubscribers > 0) void this.connection.invoke('JoinRunList');
    });
  }

  /** Live progress and status for one run. */
  watchRun(runId: string): { progress: Observable<RunProgressMessage>; status: Observable<RunStatusMessage>; stop: () => void } {
    this.runGroups.set(runId, (this.runGroups.get(runId) ?? 0) + 1);
    void this.ensureStarted().then(() => this.connection.invoke('JoinRun', runId)).catch(() => undefined);
    return {
      progress: this.progress$.pipe(filter((m) => m.runId === runId)),
      status: this.status$.pipe(filter((m) => m.runId === runId)),
      stop: () => {
        const count = (this.runGroups.get(runId) ?? 1) - 1;
        if (count <= 0) {
          this.runGroups.delete(runId);
          if (this.connection.state === HubConnectionState.Connected) void this.connection.invoke('LeaveRun', runId);
        } else {
          this.runGroups.set(runId, count);
        }
      },
    };
  }

  /** Status changes for all runs (run history page). */
  watchRunList(): { status: Observable<RunStatusMessage>; stop: () => void } {
    this.listSubscribers++;
    void this.ensureStarted().then(() => this.connection.invoke('JoinRunList')).catch(() => undefined);
    return {
      status: this.status$.asObservable(),
      stop: () => {
        this.listSubscribers = Math.max(0, this.listSubscribers - 1);
        if (this.listSubscribers === 0 && this.connection.state === HubConnectionState.Connected) {
          void this.connection.invoke('LeaveRunList');
        }
      },
    };
  }

  private ensureStarted(): Promise<void> {
    if (this.connection.state === HubConnectionState.Connected) return Promise.resolve();
    if (!this.starting) {
      this.starting = this.connection.start().finally(() => (this.starting = null));
    }
    return this.starting;
  }

  ngOnDestroy(): void {
    void this.connection.stop();
  }
}
