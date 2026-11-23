import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { passwordPath } from './auth.guards';
import { Session } from './session';
import { sessionExpiredInterceptor } from './session-expired.interceptor';

describe('sessionExpiredInterceptor', () => {
  const session = {
    isSignedIn: signal(true),
    mustChangePassword: signal(false),
    expire: vi.fn(),
    load: vi.fn(),
  };
  const router = { url: '/panel/analitik', navigate: vi.fn(), navigateByUrl: vi.fn() };
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    session.isSignedIn.set(true);
    session.mustChangePassword.set(false);
    vi.clearAllMocks();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([sessionExpiredInterceptor])),
        provideHttpClientTesting(),
        { provide: Session, useValue: session },
        { provide: Router, useValue: router },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  function fail(status: number): Promise<unknown> {
    const request = firstValueFrom(http.get('/api/documents')).catch((e: unknown) => e);
    backend.expectOne('/api/documents').flush(null, { status, statusText: 'x' });
    return request;
  }

  // The token expired or the account was closed: back to the login screen, then here.
  it('signs out on 401 and remembers the screen', async () => {
    await fail(401);

    expect(session.expire).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/giris'], { queryParams: { returnUrl: '/panel/analitik' } });
  });

  // A 401 while nobody is signed in is the login screen's own "wrong password".
  it('leaves a 401 alone when nobody is signed in', async () => {
    session.isSignedIn.set(false);

    await fail(401);

    expect(session.expire).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  // PLANNING D6: an Admin reset the password meanwhile; the API now allows only changing it.
  it('goes to the password screen when a 403 turns out to be a reset password', async () => {
    session.load.mockImplementation(async () => session.mustChangePassword.set(true));

    await fail(403);
    await Promise.resolve();

    expect(session.load).toHaveBeenCalled();
    expect(router.navigateByUrl).toHaveBeenCalledWith(passwordPath);
  });

  it('leaves other 403s to the screen that asked', async () => {
    session.load.mockResolvedValue(undefined);

    await fail(403);
    await Promise.resolve();

    expect(session.load).toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('passes the error on to the caller either way', async () => {
    const error = await fail(500);

    expect(error).toMatchObject({ status: 500 });
    expect(session.load).not.toHaveBeenCalled();
  });
});
