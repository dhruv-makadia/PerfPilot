import { Injectable, OnDestroy } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Observable, Subject, filter } from 'rxjs';
import { RunProgressMessage, RunStatusMessage } from './models';

/** Grace period before closing an unused connection, so moving between run pages doesn't reconnect. */
const IDLE_DISCONNECT_MS = 3000;

/**
 * SignalR client for live run progress (/hubs/runs on the same origin).
 * The connection is opened only while a run page is watching and closed shortly after the last watcher leaves:
 * each open connection holds a server request slot, and some hosts (IIS on Windows client editions) have very few.
 */
@Injectable({ providedIn: 'root' })
export class RunHubService implements OnDestroy {
  private readonly connection: HubConnection;
  private readonly status$ = new Subject<RunStatusMessage>();
  private readonly progress$ = new Subject<RunProgressMessage>();
  private readonly runGroups = new Map<string, number>();
  private listSubscribers = 0;
  private starting: Promise<void> | null = null;
  private stopping: Promise<void> | null = null;
  private idleTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/runs')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();
    this.connection.on('runStatus', (m: RunStatusMessage) => this.status$.next(m));
    this.connection.on('runProgress', (m: RunProgressMessage) => this.progress$.next(m));
    // Groups are per connection; re-join after an automatic reconnect.
    this.connection.onreconnected(() => this.rejoinGroups());
  }

  /** Live progress and status for one run. */
  watchRun(runId: string): { progress: Observable<RunProgressMessage>; status: Observable<RunStatusMessage>; stop: () => void } {
    this.runGroups.set(runId, (this.runGroups.get(runId) ?? 0) + 1);
    void this.ensureStarted()
      .then((started) => (started ? this.connection.invoke('JoinRun', runId) : undefined))
      .catch(() => undefined);
    return {
      progress: this.progress$.pipe(filter((m) => m.runId === runId)),
      status: this.status$.pipe(filter((m) => m.runId === runId)),
      stop: () => {
        const count = (this.runGroups.get(runId) ?? 1) - 1;
        if (count <= 0) {
          this.runGroups.delete(runId);
          if (this.connection.state === HubConnectionState.Connected) void this.connection.invoke('LeaveRun', runId).catch(() => undefined);
        } else {
          this.runGroups.set(runId, count);
        }
        this.disconnectWhenIdle();
      },
    };
  }

  /** Status changes for all runs (run history page). */
  watchRunList(): { status: Observable<RunStatusMessage>; stop: () => void } {
    this.listSubscribers++;
    void this.ensureStarted()
      .then((started) => (started ? this.connection.invoke('JoinRunList') : undefined))
      .catch(() => undefined);
    return {
      status: this.status$.asObservable(),
      stop: () => {
        this.listSubscribers = Math.max(0, this.listSubscribers - 1);
        if (this.listSubscribers === 0 && this.connection.state === HubConnectionState.Connected) {
          void this.connection.invoke('LeaveRunList').catch(() => undefined);
        }
        this.disconnectWhenIdle();
      },
    };
  }

  private get hasWatchers(): boolean {
    return this.runGroups.size > 0 || this.listSubscribers > 0;
  }

  /**
   * Resolves true once connected (groups can be joined now), false when a reconnect is in progress
   * (onreconnected re-joins every group).
   */
  private async ensureStarted(): Promise<boolean> {
    if (this.idleTimer) {
      clearTimeout(this.idleTimer);
      this.idleTimer = null;
    }
    if (this.stopping) await this.stopping;
    switch (this.connection.state) {
      case HubConnectionState.Connected:
        return true;
      case HubConnectionState.Disconnected:
        this.starting ??= this.connection.start().finally(() => (this.starting = null));
        await this.starting;
        return true;
      case HubConnectionState.Connecting:
        if (this.starting) {
          await this.starting;
          return true;
        }
        return false;
      default:
        return false;
    }
  }

  private disconnectWhenIdle(): void {
    if (this.hasWatchers || this.idleTimer) return;
    this.idleTimer = setTimeout(() => {
      this.idleTimer = null;
      if (this.hasWatchers || this.connection.state === HubConnectionState.Disconnected) return;
      this.stopping = (this.starting ?? Promise.resolve())
        .catch(() => undefined)
        .then(() => (this.hasWatchers ? undefined : this.connection.stop()))
        .finally(() => (this.stopping = null));
    }, IDLE_DISCONNECT_MS);
  }

  private rejoinGroups(): void {
    for (const runId of this.runGroups.keys()) void this.connection.invoke('JoinRun', runId).catch(() => undefined);
    if (this.listSubscribers > 0) void this.connection.invoke('JoinRunList').catch(() => undefined);
  }

  ngOnDestroy(): void {
    void this.connection.stop();
  }
}
