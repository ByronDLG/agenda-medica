import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PublicBookingPage } from './public-booking-page';

describe('PublicBookingPage', () => {
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    window.history.replaceState({}, '', '/book');
    await TestBed.configureTestingModule({
      imports: [PublicBookingPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.verify();
    document.cookie = 'XSRF-TOKEN=; Max-Age=0; path=/';
    window.history.replaceState({}, '', '/');
  });

  it('does not contact the API when the link has no token', () => {
    const fixture = TestBed.createComponent(PublicBookingPage);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'Este enlace no es válido o ya fue revocado.',
    );
    httpTesting.expectNone('/api/v1/auth/csrf');
  });

  it('keeps the token out of the address bar and sends a consented, idempotent request', () => {
    document.cookie = 'XSRF-TOKEN=csrf-test-token; path=/';
    window.history.replaceState({}, '', '/book#public-booking-token');
    const fixture = TestBed.createComponent(PublicBookingPage);
    fixture.detectChanges();

    expect(window.location.hash).toBe('');
    httpTesting.expectOne('/api/v1/auth/csrf').flush(null, {
      status: 204,
      statusText: 'No Content',
    });

    const availability = httpTesting.expectOne(
      '/api/v1/public/booking/availability?days=14',
    );
    expect(availability.request.headers.get('Authorization')).toBe(
      'Bearer public-booking-token',
    );
    availability.flush({
      organizationName: 'Consultorio de prueba',
      timeZoneId: 'America/Guatemala',
      appointmentDurationMinutes: 30,
      days: [{
        localDate: '2026-12-01',
        slots: [{ startTime: '09:00', endTime: '09:30' }],
      }],
    });
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    (root.querySelector('#publicPatientName') as HTMLInputElement).value =
      'Persona de prueba';
    (root.querySelector('#publicContactEmail') as HTMLInputElement).value =
      'persona@example.test';
    (root.querySelector('[name="contactConsent"]') as HTMLInputElement).checked = true;
    root.querySelector('form')?.dispatchEvent(
      new Event('submit', { bubbles: true, cancelable: true }),
    );

    const request = httpTesting.expectOne('/api/v1/public/booking/requests');
    expect(request.request.body).toEqual({
      localDate: '2026-12-01',
      startTime: '09:00',
      patientName: 'Persona de prueba',
      contactEmail: 'persona@example.test',
      contactPhone: null,
      contactConsent: true,
    });
    expect(request.request.headers.get('Authorization')).toBe(
      'Bearer public-booking-token',
    );
    expect(request.request.headers.get('Idempotency-Key')).toMatch(
      /^[0-9a-f-]{36}$/i,
    );
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-test-token');
    request.flush({
      localDate: '2026-12-01',
      startTime: '09:00',
      status: 'Requested',
    });
    fixture.detectChanges();

    expect(root.textContent).toContain('Tu solicitud fue enviada');
    expect(root.textContent).toContain('El consultorio debe revisar la solicitud');
  });
});
