import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { LoginPage } from './login-page';

describe('LoginPage', () => {
  let httpTesting: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }).compileComponents();
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    httpTesting.verify();
    document.cookie = 'XSRF-TOKEN=; Max-Age=0; path=/';
  });

  it('prepares CSRF and submits credentials', async () => {
    document.cookie = 'XSRF-TOKEN=csrf-test-token; path=/';
    const fixture = TestBed.createComponent(LoginPage);
    fixture.detectChanges();
    httpTesting.expectOne('/api/v1/auth/csrf').flush(null);
    fixture.detectChanges();

    const email = fixture.nativeElement.querySelector(
      '#email',
    ) as HTMLInputElement;
    const password = fixture.nativeElement.querySelector(
      '#password',
    ) as HTMLInputElement;
    email.value = 'admin@example.test';
    email.dispatchEvent(new Event('input', { bubbles: true }));
    password.value = 'Secure-Example-123!';
    password.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));

    const request = httpTesting.expectOne('/api/v1/auth/login');
    expect(request.request.body).toEqual({
      email: 'admin@example.test',
      password: 'Secure-Example-123!',
    });
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-test-token');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(router.url).toBe('/');
    expect(password.value).toBe('');
  });
});
