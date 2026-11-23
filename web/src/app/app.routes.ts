import { Routes } from '@angular/router';
import { adminOnly, passwordChangedFirst, signedInOnly, signedOutOnly } from './auth/auth.guards';

// Screens are loaded when first visited (loadComponent): a visitor who only signs in does
// not download the staff panel, and the other way round.
export const routes: Routes = [
  {
    path: 'giris',
    loadComponent: () => import('./auth/login/login').then((m) => m.Login),
    canActivate: [signedOutOnly],
    title: 'Personel girişi · BTÜ Asistan',
  },

  // Everything staff see is shown inside the shell (top bar, navigation).
  {
    path: 'panel',
    loadComponent: () => import('./shell/shell').then((m) => m.Shell),
    canActivate: [signedInOnly],
    canActivateChild: [passwordChangedFirst],
    children: [
      // Managing documents is what staff come here for, so it is the panel's first screen.
      {
        path: '',
        loadComponent: () => import('./documents/documents').then((m) => m.Documents),
        title: 'Belgeler · BTÜ Asistan',
      },
      {
        path: 'arama',
        loadComponent: () => import('./search/search-page').then((m) => m.SearchPage),
        title: 'Arama dene · BTÜ Asistan',
      },
      {
        path: 'analitik',
        loadComponent: () => import('./analytics/analytics-page').then((m) => m.AnalyticsPage),
        title: 'Analitik · BTÜ Asistan',
      },
      {
        path: 'durum',
        loadComponent: () => import('./health/health-status').then((m) => m.HealthStatus),
        title: 'Sistem durumu · BTÜ Asistan',
      },
      {
        path: 'hesaplar',
        loadComponent: () => import('./accounts/accounts-page').then((m) => m.AccountsPage),
        canActivate: [adminOnly],
        title: 'Hesaplar · BTÜ Asistan',
      },
      {
        path: 'parola',
        loadComponent: () =>
          import('./auth/change-password/change-password').then((m) => m.ChangePassword),
        title: 'Parolayı değiştir · BTÜ Asistan',
      },
    ],
  },

  // The students' chat (PLANNING C13): anonymous, at the root address, outside the shell.
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./chat/chat-page').then((m) => m.ChatPage),
    title: 'BTÜ Asistan',
  },

  // The same chat for the school's site to show in an iframe (PLANNING D4). Which sites
  // may frame it is set by the server's Content-Security-Policy frame-ancestors header.
  {
    path: 'widget',
    loadComponent: () => import('./chat/chat-page').then((m) => m.ChatPage),
    data: { widget: true },
    title: 'BTÜ Asistan',
  },
  { path: '**', redirectTo: '' },
];
