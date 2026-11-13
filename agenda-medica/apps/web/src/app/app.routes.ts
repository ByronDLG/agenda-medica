import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./home-page').then((module) => module.HomePage),
  },
  {
    path: 'accept-invitation',
    loadComponent: () =>
      import('./invitation-acceptance-page').then(
        (module) => module.InvitationAcceptancePage,
      ),
  },
  {
    path: 'login',
    loadComponent: () => import('./login-page').then((module) => module.LoginPage),
  },
  {
    path: 'book',
    loadComponent: () =>
      import('./public-booking-page').then((module) => module.PublicBookingPage),
  },
  { path: '**', redirectTo: '' },
];
