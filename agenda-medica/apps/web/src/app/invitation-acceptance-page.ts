import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';

type AcceptanceState = 'loading' | 'ready' | 'submitting' | 'success' | 'error';

@Component({
  selector: 'app-invitation-acceptance-page',
  templateUrl: './invitation-acceptance-page.html',
})
export class InvitationAcceptancePage implements OnInit {
  private readonly http = inject(HttpClient);

  protected readonly state = signal<AcceptanceState>('loading');
  protected readonly errorMessage = signal('');
  protected password = '';
  protected passwordConfirmation = '';
  private invitationToken = '';

  ngOnInit(): void {
    this.invitationToken = new URLSearchParams(
      window.location.hash.slice(1),
    ).get('token') ?? '';
    window.history.replaceState(
      window.history.state,
      '',
      window.location.pathname + window.location.search,
    );

    if (this.invitationToken.length < 40) {
      this.state.set('error');
      this.errorMessage.set('El enlace de invitación no es válido o está incompleto.');
      return;
    }

    this.http.get<void>('/api/v1/auth/csrf').subscribe({
      next: () => this.state.set('ready'),
      error: () => {
        this.state.set('error');
        this.errorMessage.set('No se pudo preparar la aceptación. Intenta abrir el enlace nuevamente.');
      },
    });
  }

  protected updatePassword(event: Event): void {
    if (event.target instanceof HTMLInputElement) {
      this.password = event.target.value;
    }
  }

  protected updatePasswordConfirmation(event: Event): void {
    if (event.target instanceof HTMLInputElement) {
      this.passwordConfirmation = event.target.value;
    }
  }

  protected acceptInvitation(event: SubmitEvent): void {
    event.preventDefault();
    if (this.state() !== 'ready') {
      return;
    }

    if (this.password !== this.passwordConfirmation) {
      this.errorMessage.set('Las contraseñas no coinciden.');
      return;
    }

    this.state.set('submitting');
    this.errorMessage.set('');
    this.http.post<void>('/api/v1/invitations/accept', {
      token: this.invitationToken,
      password: this.password,
    }).subscribe({
      next: () => {
        this.password = '';
        this.passwordConfirmation = '';
        this.invitationToken = '';
        this.state.set('success');
      },
      error: (error: HttpErrorResponse) => {
        this.state.set('ready');
        this.errorMessage.set(
          error.status === 429
            ? 'Se realizaron demasiados intentos. Espera un minuto y vuelve a intentar.'
            : 'No se pudo aceptar la invitación. El enlace pudo vencer o la contraseña no cumple los requisitos.',
        );
      },
    });
  }
}
