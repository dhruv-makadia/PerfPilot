import { AfterViewInit, Component, ElementRef, inject, viewChild } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ActivatedRoute } from '@angular/router';
import { Marked, Tokens } from 'marked';
// The user guide is bundled at build time from docs/USER-GUIDE.md, so the help page always matches the repo.
import userGuide from '../../../../../../docs/USER-GUIDE.md';

interface TocEntry {
  id: string;
  text: string;
  depth: number;
}

/** GitHub-style heading slug, so the guide's own "#section" links work here too. */
function slugify(text: string): string {
  return text
    .toLowerCase()
    .trim()
    .replace(/[^\p{L}\p{N}\s-]/gu, '')
    .replace(/\s/g, '-');
}

function renderGuide(markdown: string): { html: string; toc: TocEntry[] } {
  const toc: TocEntry[] = [];
  const used = new Map<string, number>();
  const marked = new Marked({ gfm: true });
  marked.use({
    renderer: {
      heading({ tokens, depth, text }: Tokens.Heading): string {
        let id = slugify(text);
        const seen = used.get(id) ?? 0;
        used.set(id, seen + 1);
        if (seen > 0) id = `${id}-${seen}`;
        if (depth >= 2 && depth <= 3 && id !== 'contents') {
          toc.push({ id, text: text.replace(/[*`]/g, ''), depth });
        }
        return `<h${depth} id="${id}">${this.parser.parseInline(tokens)}</h${depth}>\n`;
      },
      link({ href, title, tokens }: Tokens.Link): string {
        const label = this.parser.parseInline(tokens);
        const titleAttr = title ? ` title="${title.replace(/"/g, '&quot;')}"` : '';
        // In-page anchors are handled by the component; other links open in a new tab.
        return href.startsWith('#')
          ? `<a href="${href}"${titleAttr}>${label}</a>`
          : `<a href="${href}"${titleAttr} target="_blank" rel="noopener noreferrer">${label}</a>`;
      },
    },
  });
  return { html: marked.parse(markdown, { async: false }), toc };
}

@Component({
  selector: 'app-help-page',
  template: `
    <div class="page-header">
      <div>
        <h1>Help</h1>
        <p class="muted">How to use PerfPilot, prepare test data, and read performance results.</p>
      </div>
    </div>

    <div class="help-layout">
      <nav class="help-toc panel" aria-label="User guide contents">
        <div class="help-toc-title">Contents</div>
        @for (entry of toc; track entry.id) {
          <a [href]="'#' + entry.id" [class]="'toc-depth-' + entry.depth" (click)="goTo($event, entry.id)">{{ entry.text }}</a>
        }
      </nav>
      <article #content class="panel markdown-body" [innerHTML]="html" (click)="onContentClick($event)"></article>
    </div>
  `,
})
export class HelpPageComponent implements AfterViewInit {
  private readonly route = inject(ActivatedRoute);
  private readonly content = viewChild.required<ElementRef<HTMLElement>>('content');

  private readonly rendered = renderGuide(userGuide);
  protected readonly toc = this.rendered.toc;
  // Trusted: the HTML is rendered from a markdown file compiled into the app bundle, not from user or server data.
  protected readonly html: SafeHtml = inject(DomSanitizer).bypassSecurityTrustHtml(this.rendered.html);

  ngAfterViewInit(): void {
    const fragment = this.route.snapshot.fragment;
    if (fragment) setTimeout(() => this.scrollTo(fragment));
  }

  protected goTo(event: Event, id: string): void {
    event.preventDefault();
    this.scrollTo(id);
  }

  /** Keeps "#section" links inside the guide on this page instead of navigating the SPA to "/#section". */
  protected onContentClick(event: MouseEvent): void {
    const anchor = (event.target as HTMLElement).closest('a');
    const href = anchor?.getAttribute('href');
    if (href?.startsWith('#')) {
      event.preventDefault();
      this.scrollTo(decodeURIComponent(href.slice(1)));
    }
  }

  private scrollTo(id: string): void {
    const target = this.content().nativeElement.querySelector<HTMLElement>(`[id="${CSS.escape(id)}"]`);
    if (!target) return;
    target.scrollIntoView({ behavior: 'smooth', block: 'start' });
    history.replaceState(history.state, '', `${location.pathname}#${id}`);
  }
}
