import { Component, Input } from '@angular/core';
import { CopyButtonComponent } from '../../../shared/copy-button/copy-button.component';
import { Form, embedSnippets } from '../forms.model';

/** Copy-paste HTML and fetch() snippets for posting to a form from a static site. */
@Component({
  selector: 'app-form-embed',
  standalone: true,
  imports: [CopyButtonComponent],
  template: `
    <section class="border border-border p-4 space-y-4">
      <div>
        <h3 class="text-sm font-semibold text-text-primary">Embed</h3>
        <p class="text-xs text-text-muted mt-0.5">
          Post a plain HTML form (redirects to your success URL when set) or send JSON with fetch().
          Add your site's origin under App settings → Allowed origins for fetch() calls.
        </p>
      </div>
      <div>
        <div class="flex items-center justify-between mb-1">
          <span class="text-xs font-medium text-text-muted uppercase tracking-wider">Endpoint</span>
          <app-copy-button [value]="snippets.action" />
        </div>
        <code class="block font-mono text-xs text-text-primary break-all" data-testid="embed-action">{{ snippets.action }}</code>
      </div>
      <div>
        <div class="flex items-center justify-between mb-1">
          <span class="text-xs font-medium text-text-muted uppercase tracking-wider">HTML</span>
          <app-copy-button [value]="snippets.html" />
        </div>
        <pre class="bg-background border border-border p-3 text-xs font-mono text-text-primary overflow-x-auto" data-testid="embed-html">{{ snippets.html }}</pre>
      </div>
      <div>
        <div class="flex items-center justify-between mb-1">
          <span class="text-xs font-medium text-text-muted uppercase tracking-wider">JavaScript</span>
          <app-copy-button [value]="snippets.js" />
        </div>
        <pre class="bg-background border border-border p-3 text-xs font-mono text-text-primary overflow-x-auto">{{ snippets.js }}</pre>
      </div>
    </section>
  `,
})
export class FormEmbedComponent {
  @Input({ required: true }) publicId!: string;
  @Input({ required: true }) form!: Pick<Form, 'slug' | 'honeypot_field'>;
  /** Defaults to this dashboard's origin, which proxies /api to the API. */
  @Input() apiBase = `${globalThis.location?.origin ?? ''}/api`;

  get snippets() {
    return embedSnippets(this.apiBase, this.publicId, this.form);
  }
}
