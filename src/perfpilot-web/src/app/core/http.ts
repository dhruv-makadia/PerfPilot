import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { SessionService } from './session.service';

export const USER_HEADER = 'X-PerfPilot-User';

/** Normalized error built from RFC 7807 ProblemDetails responses. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly title: string,
    readonly detail: string | null,
    readonly fieldErrors: Record<string, string[]>,
  ) {
    super(detail ?? title);
  }

  /** All messages, field errors first, for display in a list. */
  get messages(): string[] {
    const fields = Object.values(this.fieldErrors).flat();
    if (fields.length > 0) return fields;
    return [this.detail ?? this.title];
  }
}

export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) return error;
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return new ApiError(0, 'Network error', 'The PerfPilot server could not be reached.', {});
    }
    const body = error.error;
    if (body && typeof body === 'object') {
      return new ApiError(
        error.status,
        String(body['title'] ?? error.statusText),
        body['detail'] ? String(body['detail']) : null,
        (body['errors'] as Record<string, string[]>) ?? {},
      );
    }
    return new ApiError(error.status, error.statusText || 'Request failed', typeof body === 'string' && body ? body : null, {});
  }
  return new ApiError(0, 'Error', error instanceof Error ? error.message : String(error), {});
}

/** Sends the user's display name (header identity mode) and converts errors to ApiError. */
export const apiInterceptor: HttpInterceptorFn = (req, next) => {
  const session = inject(SessionService);
  const name = session.displayName();
  const request =
    session.usesHeaderIdentity() && name && req.url.startsWith('/api')
      ? req.clone({ setHeaders: { [USER_HEADER]: encodeURIComponent(name) } })
      : req;
  return next(request).pipe(catchError((e: unknown) => throwError(() => toApiError(e))));
};
