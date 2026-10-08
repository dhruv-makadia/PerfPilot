import { Component, input } from '@angular/core';
import { MessageModule } from 'primeng/message';
import { ApiError } from '../core/http';

/** Shows a server validation/problem response as a list. */
@Component({
  selector: 'app-error-list',
  imports: [MessageModule],
  template: `
    @if (error(); as e) {
      <p-message severity="error" styleClass="mb-3 w-full">
        <div>
          <strong>{{ e.title }}</strong>
          @if (e.messages.length === 1) {
            <div>{{ e.messages[0] }}</div>
          } @else {
            <ul class="error-items">
              @for (m of e.messages; track $index) {
                <li>{{ m }}</li>
              }
            </ul>
          }
        </div>
      </p-message>
    }
  `,
})
export class ErrorListComponent {
  readonly error = input<ApiError | null>(null);
}
