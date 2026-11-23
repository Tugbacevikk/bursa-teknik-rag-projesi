import { HttpInterceptorFn } from '@angular/common/http';

// Adapted from DocAssistant.
//
// The API rejects cookie-authenticated requests that change something unless they carry
// X-Requested-With (backend: SessionCookieCsrfProtection); a page on another site cannot
// add it. Only requests to the API get it.
//
// No withCredentials: the app and the API share one origin (the development proxy, a
// reverse proxy in production), so the browser sends the session cookie by itself.
export const apiRequestInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/')) {
    return next(request);
  }

  return next(request.clone({ setHeaders: { 'X-Requested-With': 'XMLHttpRequest' } }));
};
