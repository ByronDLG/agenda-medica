import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';

type LoginState = 'loading' | 'ready' | 'submitting';

@Component({
  selector: 'app-login-page',
  templateUrl: './login-page.html',
})
export class LoginPage implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly state = signal<LoginState>('loading');
  protected readonly errorMessage = signal('');
  protected email = '';
  protected password = '';

  ngOnInit(): void {
    this.auth.prepareCsrf().subscribe({
      next: () => this.state.set('ready'),
      error: () => {
        this.state.set('ready');
        this.errorMessage.set('No se pudo preparar el inicio de sesión. Intenta de nuevo.');
      },
    });
  }

  protected updateEmail(event: Event): void {
    if (event.target instanceof HTMLInputElement) {
      this.email = event.target.value;
    }
  }

  protected updatePassword(event: Event): void {
    if (event.target instanceof HTMLInputElement) {
      this.password = event.target.value;
    }
  }

  protected signIn(event: SubmitEvent): void {
    event.preventDefault();
    if (this.state() !== 'ready') {
      return;
    }

    this.state.set('submitting');
    this.errorMessage.set('');
    this.auth.signIn(this.email.trim(), this.password).subscribe({
      next: () => {
        this.password = '';
        void this.router.navigateByUrl('/');
      },
      error: (error: HttpErrorResponse) => {
        this.state.set('ready');
        this.errorMessage.set(
          error.status === 401
            ? 'Correo o contraseña no válidos.'
            : 'No se pudo iniciar sesión. Revisa la conexión e intenta de nuevo.',
        );
      },
    });
  }
}
