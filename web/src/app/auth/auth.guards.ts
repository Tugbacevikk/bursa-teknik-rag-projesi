import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Session } from './session';

// Adapted from DocAssistant.
//
// Route guards: the router asks them before showing a screen. Returning true lets the
// screen through; returning an address sends the user there instead.
//
// By the time they run the session is known: the app waits for it at startup
// (app.config.ts). They only decide what to show. What a user may actually read or change
// is decided by the API on every request.

// For screens that need a signed-in staff member. Others go to the login screen, which
// remembers where they were heading.
export const signedInOnly: CanActivateFn = (_route, state) => {
  if (inject(Session).isSignedIn()) {
    return true;
  }

  return inject(Router).createUrlTree(['/giris'], {
    queryParams: state.url === '/panel' ? {} : { returnUrl: state.url },
  });
};

// For the login screen: somebody already signed in has no use for it.
export const signedOutOnly: CanActivateFn = () =>
  inject(Session).isSignedIn() ? inject(Router).createUrlTree(['/panel']) : true;

// For screens only admins use. Others go to the panel: the screen would only show them
// what the API refuses.
export const adminOnly: CanActivateFn = () =>
  inject(Session).isAdmin() ? true : inject(Router).createUrlTree(['/panel']);

// For every panel screen but the password one: someone signed in with a temporary
// password changes it first (PLANNING D6; the API refuses everything else anyway).
export const passwordChangedFirst: CanActivateFn = (_route, state) =>
  inject(Session).mustChangePassword() && state.url !== passwordPath
    ? inject(Router).createUrlTree([passwordPath])
    : true;

export const passwordPath = '/panel/parola';

// Where to go after signing in: the address the login screen was given, if it is one of
// the app's own. Anything else (another site, a protocol-relative "//host") is ignored, so
// a crafted link cannot use the login screen to send people elsewhere.
export function safeReturnUrl(returnUrl: string | null | undefined): string {
  return returnUrl &&
    returnUrl.startsWith('/') &&
    !returnUrl.startsWith('//') &&
    !returnUrl.includes('\\')
    ? returnUrl
    : '/panel';
}
