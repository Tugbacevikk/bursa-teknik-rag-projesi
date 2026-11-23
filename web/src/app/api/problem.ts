import { HttpErrorResponse } from '@angular/common/http';

// Adapted from DocAssistant. The API reports errors as RFC 9457 problem details. `errors`
// is present on 400 responses caused by invalid input: one entry per field, keyed by the
// field's name as the server spells it (e.g. "title").
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}

// The problem details of a failed request, or null when the response carried none: the
// API could not be reached, or something in between (a proxy) answered instead.
export function problemOf(error: unknown): ProblemDetails | null {
  if (!(error instanceof HttpErrorResponse)) {
    return null;
  }

  const body: unknown = error.error;

  return typeof body === 'object' && body !== null && !(body instanceof ProgressEvent)
    ? (body as ProblemDetails)
    : null;
}
