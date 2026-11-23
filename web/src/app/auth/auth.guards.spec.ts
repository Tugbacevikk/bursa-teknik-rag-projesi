import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, provideRouter, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { adminOnly, passwordChangedFirst, passwordPath, safeReturnUrl, signedInOnly } from './auth.guards';
import { Session } from './session';

// Where the login screen may send someone afterwards: only the app's own addresses.
describe('safeReturnUrl', () => {
  it.each([
    ['/panel/analitik', '/panel/analitik'],
    ['/panel?x=1', '/panel?x=1'],
    [null, '/panel'],
    ['', '/panel'],
    ['https://evil.example/phish', '/panel'],
    ['//evil.example', '/panel'],
    ['/\\evil.example', '/panel'],
    ['javascript:alert(1)', '/panel'],
  ])('%s → %s', (input, expected) => {
    expect(safeReturnUrl(input)).toBe(expected);
  });
});

describe('route guards', () => {
  const session = {
    isSignedIn: signal(true),
    isAdmin: signal(false),
    mustChangePassword: signal(false),
  };

  beforeEach(() => {
    session.isSignedIn.set(true);
    session.isAdmin.set(false);
    session.mustChangePassword.set(false);
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: Session, useValue: session }] });
  });

  function run(guard: typeof passwordChangedFirst, url: string): boolean | UrlTree {
    return TestBed.runInInjectionContext(() =>
      guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
    ) as boolean | UrlTree;
  }

  const serialize = (result: boolean | UrlTree) =>
    result instanceof UrlTree ? TestBed.inject(Router).serializeUrl(result) : result;

  // PLANNING D6: a temporary password is changed before anything else.
  it('sends someone with a temporary password to the password screen', () => {
    session.mustChangePassword.set(true);

    expect(serialize(run(passwordChangedFirst, '/panel/analitik'))).toBe(passwordPath);
    expect(run(passwordChangedFirst, passwordPath)).toBe(true);
  });

  it('lets everyone else through', () => {
    expect(run(passwordChangedFirst, '/panel/analitik')).toBe(true);
  });

  it('sends the signed out to the login screen, remembering where they were going', () => {
    session.isSignedIn.set(false);

    expect(serialize(run(signedInOnly, '/panel/hesaplar'))).toBe('/giris?returnUrl=%2Fpanel%2Fhesaplar');
    expect(serialize(run(signedInOnly, '/panel'))).toBe('/giris');
  });

  it('keeps editors out of admin screens', () => {
    expect(serialize(run(adminOnly, '/panel/hesaplar'))).toBe('/panel');

    session.isAdmin.set(true);
    expect(run(adminOnly, '/panel/hesaplar')).toBe(true);
  });
});
