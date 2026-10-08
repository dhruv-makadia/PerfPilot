import { RunStatus, SlaMetric } from './models';

export function formatMs(ms: number | null | undefined): string {
  if (ms === null || ms === undefined || Number.isNaN(ms)) return '–';
  return ms >= 1000 ? `${(ms / 1000).toFixed(2)} s` : `${ms.toFixed(ms < 10 ? 1 : 0)} ms`;
}

export function formatPct(ratio: number | null | undefined): string {
  if (ratio === null || ratio === undefined) return '–';
  const pct = ratio * 100;
  return `${pct.toFixed(pct > 0 && pct < 1 ? 2 : 1)}%`;
}

export function formatNumber(n: number | null | undefined, digits = 0): string {
  if (n === null || n === undefined) return '–';
  return n.toLocaleString(undefined, { maximumFractionDigits: digits, minimumFractionDigits: 0 });
}

export function formatDuration(totalSeconds: number | null | undefined): string {
  if (totalSeconds === null || totalSeconds === undefined) return '–';
  const s = Math.round(totalSeconds);
  if (s <= 0) return '0s';
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = s % 60;
  return `${h ? h + 'h' : ''}${m ? m + 'm' : ''}${sec ? sec + 's' : ''}`;
}

/** Parses k6 duration strings ("30s", "5m", "1h30m", "500ms"); returns seconds or null when invalid. */
export function parseDuration(value: string | null | undefined): number | null {
  if (!value) return null;
  const v = value.trim();
  if (!/^(\d+(\.\d+)?(ms|s|m|h))+$/.test(v)) return null;
  let total = 0;
  for (const m of v.matchAll(/(\d+(?:\.\d+)?)(ms|s|m|h)/g)) {
    const n = Number(m[1]);
    total += m[2] === 'ms' ? n / 1000 : m[2] === 's' ? n : m[2] === 'm' ? n * 60 : n * 3600;
  }
  return total;
}

export function statusSeverity(status: RunStatus): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
  switch (status) {
    case 'Completed':
      return 'success';
    case 'Running':
      return 'info';
    case 'Queued':
      return 'secondary';
    case 'Cancelled':
      return 'warn';
    default:
      return 'danger';
  }
}

export function methodClass(method: string): string {
  return `method method-${method.toLowerCase()}`;
}

export function slaLabel(metric: SlaMetric): string {
  return { P95: 'p95 latency', P99: 'p99 latency', ErrorRate: 'Error rate', MinRps: 'Throughput' }[metric];
}

export function formatSla(metric: SlaMetric, value: number | null, isLimit: boolean): string {
  if (value === null) return 'no data';
  const prefix = isLimit ? (metric === 'MinRps' ? '≥ ' : '≤ ') : '';
  switch (metric) {
    case 'P95':
    case 'P99':
      return prefix + formatMs(value);
    case 'ErrorRate':
      return `${prefix}${value.toFixed(2)}%`;
    case 'MinRps':
      return `${prefix}${value.toFixed(2)} req/s`;
  }
}

export function downloadText(content: string, fileName: string, type = 'text/plain'): void {
  const url = URL.createObjectURL(new Blob([content], { type }));
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  a.click();
  URL.revokeObjectURL(url);
}
