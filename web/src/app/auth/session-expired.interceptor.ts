import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { passwordPath } from './auth.guards';
import { Session } from './session';

// Adapted from DocAssistant.
//
// When the API answers 401 to somebody the app believes is signed in, the session has
// ended: the token expired (60 minutes) or the account was deactivated (backend:
// SignOutInactiveAccountsResultHandler). The app forgets the user and shows the login
// screen, which brings them back to where they were afterwards.
//
// A 401 while nobody is signed in is left alone: that is the login screen being told
// "wrong password", or the startup question "who am I?" being answered "nobody".
export const sessionExpiredInterceptor: HttpInterceptorFn = (request, next) => {
  const session = inject(Session);
  const router = inject(Router);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && session.isSignedIn()) {
        session.expire();

        void router.navigate(['/giris'], {
          queryParams: router.url === '/panel' ? {} : { returnUrl: router.url },
        });
      }

      // A 403 may mean an Admin has just reset this account's password: the API now only
      // allows changing it. Ask again who is signed in, and go to the password screen if
      // so; other 403s (a missing role) are left to the screen that made the request.
      if (error instanceof HttpErrorResponse && error.status === 403 && session.isSignedIn() && !session.mustChangePassword()) {
        void session.load().then(() => {
          if (session.mustChangePassword()) {
            void router.navigateByUrl(passwordPath);
          }
        });
      }

      // The caller still gets the error: its request did fail.
      return throwError(() => error);
    }),
  );
};
