import { Component, computed, input, model } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';

const PLACEHOLDER = /\{\{\s*[A-Za-z]+\.[^{}]+?\s*\}\}/g;

/**
 * Protects {{...}} placeholders so the text can be parsed as JSON:
 * unquoted placeholders become string tokens, quoted ones become in-string tokens.
 */
function protect(text: string): { json: string; restore: (s: string) => string } {
  const raw: string[] = [];
  const inStr: string[] = [];
  let out = '';
  let inQuotes = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (c === '"' && text[i - 1] !== '\\') inQuotes = !inQuotes;
    if (c === '{' && text[i + 1] === '{') {
      PLACEHOLDER.lastIndex = i;
      const m = PLACEHOLDER.exec(text);
      if (m && m.index === i) {
        if (inQuotes) {
          inStr.push(m[0]);
          out += `__PPSTR${inStr.length - 1}__`;
        } else {
          raw.push(m[0]);
          out += `"__PPRAW${raw.length - 1}__"`;
        }
        i += m[0].length - 1;
        continue;
      }
    }
    out += c;
  }
  return {
    json: out,
    restore: (s) =>
      s.replace(/"__PPRAW(\d+)__"/g, (_, n) => raw[Number(n)]).replace(/__PPSTR(\d+)__/g, (_, n) => inStr[Number(n)]),
  };
}

export function jsonError(text: string | null | undefined): string | null {
  if (!text || !text.trim()) return null;
  try {
    JSON.parse(protect(text).json);
    return null;
  } catch (e) {
    return e instanceof Error ? e.message : 'Invalid JSON';
  }
}

/** Monospace JSON editor that validates and formats while tolerating {{placeholders}}. */
@Component({
  selector: 'app-json-editor',
  imports: [FormsModule, ButtonModule],
  template: `
    <div class="json-editor" [class.invalid]="!!error()">
      <div class="json-editor-toolbar">
        <span class="json-status">
          @if (!value()?.trim()) {
            <span class="muted">Empty</span>
          } @else if (error()) {
            <i class="pi pi-times-circle text-danger"></i> <span class="text-danger">{{ error() }}</span>
          } @else {
            <i class="pi pi-check-circle text-success"></i> Valid JSON
          }
        </span>
        <span class="spacer"></span>
        @if (sample()) {
          <p-button label="Reset to sample" icon="pi pi-replay" size="small" [text]="true" (onClick)="value.set(sample())" />
        }
        <p-button label="Format" icon="pi pi-align-left" size="small" [text]="true" [disabled]="!!error() || !value()" (onClick)="format()" />
      </div>
      <textarea class="code-input" [rows]="rows()" spellcheck="false" [ngModel]="value()" (ngModelChange)="value.set($event)"
        [attr.aria-label]="label()"></textarea>
    </div>
  `,
})
export class JsonEditorComponent {
  readonly value = model<string | null>(null);
  readonly sample = input<string | null>(null);
  readonly rows = input(10);
  readonly label = input('JSON');
  protected readonly error = computed(() => jsonError(this.value()));

  protected format(): void {
    const text = this.value();
    if (!text) return;
    const p = protect(text);
    this.value.set(p.restore(JSON.stringify(JSON.parse(p.json), null, 2)));
  }
}
