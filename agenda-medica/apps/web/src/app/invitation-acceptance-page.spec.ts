import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { InvitationAcceptancePage } from './invitation-acceptance-page';

describe('InvitationAcceptancePage', () => {
  let httpTesting: HttpTestingController;
  const token = 'an-invitation-token-that-is-only-kept-in-memory-123';

  beforeEach(async () => {
    window.history.replaceState({}, '', `/accept-invitation#token=${token}`);
    await TestBed.configureTestingModule({
      imports: [InvitationAcceptancePage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.verify();
    document.cookie = 'XSRF-TOKEN=; Max-Age=0; path=/';
  });

  it('removes the token from the URL and sends it only after CSRF setup', async () => {
    document.cookie = 'XSRF-TOKEN=csrf-test-token; path=/';
    const fixture = TestBed.createComponent(InvitationAcceptancePage);
    fixture.detectChanges();

    expect(window.location.hash).toBe('');
    httpTesting.expectOne('/api/v1/auth/csrf').flush(null);
    fixture.detectChanges();

    const password = fixture.nativeElement.querySelector(
      '#password',
    ) as HTMLInputElement;
    const confirmation = fixture.nativeElement.querySelector(
      '#password-confirmation',
    ) as HTMLInputElement;
    password.value = 'Secure-Example-123!';
    password.dispatchEvent(new Event('input', { bubbles: true }));
    confirmation.value = 'Secure-Example-123!';
    confirmation.dispatchEvent(new Event('input', { bubbles: true }));
    await fixture.whenStable();
    fixture.detectChanges();
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));

    const request = httpTesting.expectOne('/api/v1/invitations/accept');
    expect(request.request.body).toEqual({
      token,
      password: 'Secure-Example-123!',
    });
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-test-token');
    request.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Invitación aceptada');
  });

  it('does not contact the API when the link has no token', () => {
    window.history.replaceState({}, '', '/accept-invitation');
    const fixture = TestBed.createComponent(InvitationAcceptancePage);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'El enlace de invitación no es válido o está incompleto.',
    );
    httpTesting.expectNone('/api/v1/auth/csrf');
  });
});
