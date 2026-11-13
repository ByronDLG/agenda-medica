import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { HomePage } from './home-page';

describe('App', () => {
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HomePage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.verify();
    document.cookie = 'XSRF-TOKEN=; Max-Age=0; path=/';
  });

  it('shows the empty agenda and reports a successful API check', () => {
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    httpTesting.expectOne('/api/v1/health').flush({ status: 'ok' });
    httpTesting.expectOne('/api/v1/auth/me').flush({
      email: 'admin@example.test',
      organizations: [],
    });
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('h1')?.textContent).toContain(
      'Tu agenda, con claridad.',
    );
    expect(compiled.textContent).toContain('API disponible');
    expect(compiled.textContent).toContain('No ingreses información personal ni médica');
  });

  it('shows a recoverable connection error and retries on request', () => {
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    httpTesting
      .expectOne('/api/v1/health')
      .flush({}, { status: 503, statusText: 'Service Unavailable' });
    httpTesting
      .expectOne('/api/v1/auth/me')
      .flush({
        email: 'admin@example.test',
        organizations: [],
      });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'No se pudo conectar con la API',
    );
    const retryButton = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '[data-testid="api-retry"]',
    );
    retryButton?.click();
    httpTesting.expectOne('/api/v1/health').flush({ status: 'ok' });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'API disponible',
    );
  });

  it('lets a clinic administrator send a staff invitation with CSRF protection', () => {
    document.cookie = 'XSRF-TOKEN=csrf-test-token; path=/';
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    httpTesting.expectOne('/api/v1/health').flush({ status: 'ok' });
    httpTesting.expectOne('/api/v1/auth/me').flush({
      email: 'admin@example.test',
      organizations: [
        { id: 'organization-1', name: 'Consultorio de prueba', role: 'ClinicAdmin' },
      ],
    });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/settings',
    ).flush({
      id: 'organization-1',
      name: 'Consultorio de prueba',
      countryCode: 'GT',
      timeZoneId: 'America/Guatemala',
      appointmentConfirmationMode: 'ManualApproval',
    });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/secretary-agenda-assignments',
    ).flush({ practitioners: [], secretaries: [] });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Confirmación de citas',
    );
    const email = fixture.nativeElement.querySelector(
      '#inviteEmail',
    ) as HTMLInputElement;
    email.value = 'staff@example.test';
    email.closest('form')?.dispatchEvent(
      new Event('submit', { bubbles: true, cancelable: true }),
    );

    const request = httpTesting.expectOne(
      '/api/v1/organizations/organization-1/invitations',
    );
    expect(request.request.body).toEqual({
      email: 'staff@example.test',
      role: 'Practitioner',
    });
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-test-token');
    request.flush({ id: 'invitation-1', expiresAt: '2026-11-14T12:00:00Z' });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Invitación enviada',
    );
  });

  it('lets a clinic administrator save the organization confirmation policy', () => {
    document.cookie = 'XSRF-TOKEN=csrf-test-token; path=/';
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    httpTesting.expectOne('/api/v1/health').flush({ status: 'ok' });
    httpTesting.expectOne('/api/v1/auth/me').flush({
      email: 'admin@example.test',
      organizations: [
        { id: 'organization-1', name: 'Consultorio de prueba', role: 'ClinicAdmin' },
      ],
    });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/settings',
    ).flush({
      id: 'organization-1',
      name: 'Consultorio de prueba',
      countryCode: 'GT',
      timeZoneId: 'America/Guatemala',
      appointmentConfirmationMode: 'ManualApproval',
    });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/secretary-agenda-assignments',
    ).flush({ practitioners: [], secretaries: [] });
    fixture.detectChanges();

    const mode = fixture.nativeElement.querySelector(
      '#confirmationMode',
    ) as HTMLSelectElement;
    mode.value = 'Automatic';
    mode.dispatchEvent(new Event('change', { bubbles: true }));
    mode.closest('form')?.dispatchEvent(
      new Event('submit', { bubbles: true, cancelable: true }),
    );

    const request = httpTesting.expectOne(
      '/api/v1/organizations/organization-1/settings',
    );
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ appointmentConfirmationMode: 'Automatic' });
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-test-token');
    request.flush({
      id: 'organization-1',
      name: 'Consultorio de prueba',
      countryCode: 'GT',
      timeZoneId: 'America/Guatemala',
      appointmentConfirmationMode: 'Automatic',
    });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Preferencia de confirmación guardada.',
    );
  });

  it('lets a clinic administrator assign a professional agenda to a secretary', () => {
    document.cookie = 'XSRF-TOKEN=csrf-test-token; path=/';
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    httpTesting.expectOne('/api/v1/health').flush({ status: 'ok' });
    httpTesting.expectOne('/api/v1/auth/me').flush({
      email: 'admin@example.test',
      organizations: [
        { id: 'organization-1', name: 'Consultorio de prueba', role: 'ClinicAdmin' },
      ],
    });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/settings',
    ).flush({
      id: 'organization-1',
      name: 'Consultorio de prueba',
      countryCode: 'GT',
      timeZoneId: 'America/Guatemala',
      appointmentConfirmationMode: 'ManualApproval',
    });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/secretary-agenda-assignments',
    ).flush({
      practitioners: [{ userId: 'practitioner-1', email: 'profesional@example.test' }],
      secretaries: [{
        userId: 'secretary-1',
        email: 'secretaria@example.test',
        practitionerUserIds: [],
      }],
    });
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const agendaCheckbox = root.querySelector<HTMLInputElement>(
      'fieldset input[type="checkbox"]',
    );
    expect(agendaCheckbox).not.toBeNull();
    agendaCheckbox!.checked = true;
    agendaCheckbox!.dispatchEvent(new Event('change', { bubbles: true }));
    root.querySelector<HTMLButtonElement>(
      '[aria-labelledby="secretary-assignment-title"] button',
    )?.click();

    const request = httpTesting.expectOne(
      '/api/v1/organizations/organization-1/secretary-agenda-assignments/secretaries/secretary-1',
    );
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ practitionerUserIds: ['practitioner-1'] });
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-test-token');
    request.flush({
      secretaryUserId: 'secretary-1',
      practitionerUserIds: ['practitioner-1'],
    });
    fixture.detectChanges();

    expect(root.textContent).toContain('Agendas autorizadas actualizadas.');
  });

  it('lets a practitioner save weekly availability in the organization timezone', () => {
    document.cookie = 'XSRF-TOKEN=csrf-test-token; path=/';
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    httpTesting.expectOne('/api/v1/health').flush({ status: 'ok' });
    httpTesting.expectOne('/api/v1/auth/me').flush({
      email: 'practitioner@example.test',
      organizations: [
        { id: 'organization-1', name: 'Consultorio de prueba', role: 'Practitioner' },
      ],
    });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/schedule',
    ).flush({
      timeZoneId: 'America/Guatemala',
      appointmentDurationMinutes: 30,
      preparationBufferMinutes: 5,
      windows: [],
    });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/schedule/exceptions',
    ).flush({ timeZoneId: 'America/Guatemala', dates: [] });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/schedule/preview',
    ).flush({
      timeZoneId: 'America/Guatemala',
      appointmentDurationMinutes: 30,
      preparationBufferMinutes: 5,
      days: [],
    });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/appointments',
    ).flush({
      timeZoneId: 'America/Guatemala',
      appointments: [],
    });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/public-booking-link',
    ).flush({ isEnabled: false, createdAtUtc: null });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'America/Guatemala',
    );
    const duration = fixture.nativeElement.querySelector(
      '#appointmentDuration',
    ) as HTMLSelectElement;
    const buffer = fixture.nativeElement.querySelector(
      '#preparationBuffer',
    ) as HTMLSelectElement;
    expect(duration.value).toBe('30');
    expect(buffer.value).toBe('5');
    duration.value = '45';
    duration.dispatchEvent(new Event('change', { bubbles: true }));
    buffer.value = '10';
    buffer.dispatchEvent(new Event('change', { bubbles: true }));
    const addWindow = fixture.nativeElement.querySelector(
      '[data-testid="schedule-add-window"]',
    ) as HTMLButtonElement;
    addWindow.click();
    fixture.detectChanges();
    const start = fixture.nativeElement.querySelector(
      '#schedule-start-1-0',
    ) as HTMLInputElement;
    const end = fixture.nativeElement.querySelector(
      '#schedule-end-1-0',
    ) as HTMLInputElement;
    start.value = '09:00';
    start.dispatchEvent(new Event('change', { bubbles: true }));
    end.value = '12:00';
    end.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));

    const request = httpTesting.expectOne(
      '/api/v1/organizations/organization-1/schedule',
    );
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      windows: [{ dayOfWeek: 1, startTime: '09:00', endTime: '12:00' }],
      appointmentDurationMinutes: 45,
      preparationBufferMinutes: 10,
    });
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-test-token');
    request.flush({
      timeZoneId: 'America/Guatemala',
      appointmentDurationMinutes: 45,
      preparationBufferMinutes: 10,
      windows: [{ dayOfWeek: 1, startTime: '09:00', endTime: '12:00' }],
    });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/schedule/preview',
    ).flush({
      timeZoneId: 'America/Guatemala',
      appointmentDurationMinutes: 30,
      preparationBufferMinutes: 5,
      days: [],
    });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Horario semanal guardado.',
    );

    const exceptionDate = fixture.nativeElement.querySelector(
      '#exceptionDate',
    ) as HTMLInputElement;
    exceptionDate.value = '2026-12-25';
    exceptionDate.closest('form')?.dispatchEvent(
      new Event('submit', { bubbles: true, cancelable: true }),
    );
    const exceptionRequest = httpTesting.expectOne(
      '/api/v1/organizations/organization-1/schedule/exceptions',
    );
    expect(exceptionRequest.request.method).toBe('POST');
    expect(exceptionRequest.request.body).toEqual({ localDate: '2026-12-25' });
    expect(exceptionRequest.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-test-token');
    exceptionRequest.flush(null, { status: 204, statusText: 'No Content' });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/schedule/exceptions',
    ).flush({ timeZoneId: 'America/Guatemala', dates: ['2026-12-25'] });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/schedule/preview',
    ).flush({
      timeZoneId: 'America/Guatemala',
      appointmentDurationMinutes: 30,
      preparationBufferMinutes: 5,
      days: [{
        localDate: '2026-11-12',
        dayOfWeek: 4,
        isException: false,
        windows: [],
        potentialSlots: [],
      }],
    });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Día marcado como no laborable.',
    );
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('2026-12-25');
  });

  it('creates a clinic appointment with CSRF protection and an idempotency key', () => {
    document.cookie = 'XSRF-TOKEN=csrf-test-token; path=/';
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    httpTesting.expectOne('/api/v1/health').flush({ status: 'ok' });
    httpTesting.expectOne('/api/v1/auth/me').flush({
      email: 'practitioner@example.test',
      organizations: [
        { id: 'organization-1', name: 'Consultorio de prueba', role: 'Practitioner' },
      ],
    });

    const dateTime = new Date();
    dateTime.setUTCDate(dateTime.getUTCDate() + 2);
    const dateParts = new Intl.DateTimeFormat('en-CA', {
      timeZone: 'America/Guatemala',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }).formatToParts(dateTime);
    const dateValues = Object.fromEntries(dateParts.map((part) => [part.type, part.value]));
    const localDate = `${dateValues['year']}-${dateValues['month']}-${dateValues['day']}`;
    const dayOfWeek = new Date(`${localDate}T12:00:00Z`).getUTCDay();

    httpTesting.expectOne('/api/v1/organizations/organization-1/schedule').flush({
      timeZoneId: 'America/Guatemala',
      appointmentDurationMinutes: 30,
      preparationBufferMinutes: 5,
      windows: [],
    });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/schedule/exceptions',
    ).flush({ timeZoneId: 'America/Guatemala', dates: [] });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/schedule/preview',
    ).flush({
      timeZoneId: 'America/Guatemala',
      appointmentDurationMinutes: 30,
      preparationBufferMinutes: 5,
      days: [{
        localDate,
        dayOfWeek,
        isException: false,
        windows: [{ startTime: '09:00', endTime: '12:00' }],
        potentialSlots: [{ startTime: '09:00', endTime: '09:30' }],
      }],
    });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/appointments',
    ).flush({ timeZoneId: 'America/Guatemala', appointments: [] });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/public-booking-link',
    ).flush({ isEnabled: false, createdAtUtc: null });
    fixture.detectChanges();

    const form = fixture.nativeElement.querySelector(
      '[aria-labelledby="create-appointment-title"] form',
    ) as HTMLFormElement;
    (fixture.nativeElement.querySelector('#patientName') as HTMLInputElement).value =
      'Persona de prueba';
    (fixture.nativeElement.querySelector('#contactEmail') as HTMLInputElement).value =
      'persona@example.test';
    (fixture.nativeElement.querySelector('[name="contactConsent"]') as HTMLInputElement).checked =
      true;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));

    const request = httpTesting.expectOne(
      '/api/v1/organizations/organization-1/appointments',
    );
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      localDate,
      startTime: '09:00',
      patientName: 'Persona de prueba',
      contactEmail: 'persona@example.test',
      contactPhone: null,
      contactConsent: true,
    });
    expect(request.request.headers.get('Idempotency-Key')).toMatch(
      /^[0-9a-f-]{36}$/i,
    );
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-test-token');
    request.flush({
      id: 'appointment-1',
      patientName: 'Persona de prueba',
      contactEmail: 'persona@example.test',
      contactPhone: null,
      localDate,
      startTime: '09:00',
      endTime: '09:30',
      occupiedUntilLocalDate: localDate,
      occupiedUntilTime: '09:35',
      durationMinutes: 30,
      status: 'Requested',
    });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/appointments',
    ).flush({
      timeZoneId: 'America/Guatemala',
      appointments: [{
        id: 'appointment-1',
        patientName: 'Persona de prueba',
        contactEmail: 'persona@example.test',
        contactPhone: null,
        localDate,
        startTime: '09:00',
        endTime: '09:30',
        occupiedUntilLocalDate: localDate,
        occupiedUntilTime: '09:35',
        durationMinutes: 30,
        status: 'Requested',
      }],
    });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'La cita se guardó correctamente.',
    );
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Persona de prueba');
  });

  it('lets a secretary create a booking only for an assigned agenda', () => {
    document.cookie = 'XSRF-TOKEN=csrf-test-token; path=/';
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    httpTesting.expectOne('/api/v1/health').flush({ status: 'ok' });
    httpTesting.expectOne('/api/v1/auth/me').flush({
      email: 'secretaria@example.test',
      organizations: [
        { id: 'organization-1', name: 'Consultorio de prueba', role: 'Secretary' },
      ],
    });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/appointments/practitioners',
    ).flush([{ userId: 'practitioner-1', email: 'profesional@example.test' }]);

    const dateTime = new Date();
    dateTime.setUTCDate(dateTime.getUTCDate() + 2);
    const dateParts = new Intl.DateTimeFormat('en-CA', {
      timeZone: 'America/Guatemala',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }).formatToParts(dateTime);
    const dateValues = Object.fromEntries(dateParts.map((part) => [part.type, part.value]));
    const localDate = `${dateValues['year']}-${dateValues['month']}-${dateValues['day']}`;
    const dayOfWeek = new Date(`${localDate}T12:00:00Z`).getUTCDay();
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/appointments?practitionerUserId=practitioner-1',
    ).flush({ timeZoneId: 'America/Guatemala', appointments: [] });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/schedule/preview?practitionerUserId=practitioner-1',
    ).flush({
      timeZoneId: 'America/Guatemala',
      appointmentDurationMinutes: 30,
      preparationBufferMinutes: 5,
      days: [{
        localDate,
        dayOfWeek,
        isException: false,
        windows: [{ startTime: '09:00', endTime: '12:00' }],
        potentialSlots: [{ startTime: '09:00', endTime: '09:30' }],
      }],
    });
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Registrar cita para una agenda asignada');
    expect(root.textContent).toContain('profesional@example.test');
    expect(root.textContent).not.toContain('Marcar atendida');
    expect(root.textContent).not.toContain('Marcar inasistencia');
    const form = root.querySelector(
      '[aria-labelledby="create-appointment-title"] form',
    ) as HTMLFormElement;
    (root.querySelector('#patientName') as HTMLInputElement).value = 'Persona de prueba';
    (root.querySelector('#contactEmail') as HTMLInputElement).value = 'persona@example.test';
    (root.querySelector('[name="contactConsent"]') as HTMLInputElement).checked = true;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));

    const request = httpTesting.expectOne(
      '/api/v1/organizations/organization-1/appointments',
    );
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      localDate,
      startTime: '09:00',
      patientName: 'Persona de prueba',
      contactEmail: 'persona@example.test',
      contactPhone: null,
      contactConsent: true,
      practitionerUserId: 'practitioner-1',
    });
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-test-token');
    request.flush({ id: 'appointment-1' });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/appointments?practitionerUserId=practitioner-1',
    ).flush({ timeZoneId: 'America/Guatemala', appointments: [] });
    fixture.detectChanges();

    expect(root.textContent).toContain('La cita se guardó correctamente.');
  });

  it('lets a practitioner generate and copy a direct booking link only once', () => {
    document.cookie = 'XSRF-TOKEN=csrf-test-token; path=/';
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    httpTesting.expectOne('/api/v1/health').flush({ status: 'ok' });
    httpTesting.expectOne('/api/v1/auth/me').flush({
      email: 'practitioner@example.test',
      organizations: [
        { id: 'organization-1', name: 'Consultorio de prueba', role: 'Practitioner' },
      ],
    });
    httpTesting.expectOne('/api/v1/organizations/organization-1/schedule').flush({
      timeZoneId: 'America/Guatemala',
      appointmentDurationMinutes: 30,
      preparationBufferMinutes: 5,
      windows: [],
    });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/schedule/exceptions',
    ).flush({ timeZoneId: 'America/Guatemala', dates: [] });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/schedule/preview',
    ).flush({
      timeZoneId: 'America/Guatemala',
      appointmentDurationMinutes: 30,
      preparationBufferMinutes: 5,
      days: [],
    });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/appointments',
    ).flush({ timeZoneId: 'America/Guatemala', appointments: [] });
    httpTesting.expectOne(
      '/api/v1/organizations/organization-1/public-booking-link',
    ).flush({ isEnabled: false, createdAtUtc: null });
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const generateButton = [...root.querySelectorAll('button')].find(
      (button) => button.textContent?.includes('Generar enlace'),
    );
    expect(generateButton).toBeDefined();
    generateButton?.click();

    const request = httpTesting.expectOne(
      '/api/v1/organizations/organization-1/public-booking-link',
    );
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({});
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-test-token');
    request.flush({ token: 'public-booking-token' });
    fixture.detectChanges();

    const linkInput = root.querySelector<HTMLInputElement>('#publicBookingUrl');
    expect(linkInput?.value).toContain('/book#public-booking-token');
    expect(root.textContent).toContain(
      'Guárdalo ahora: no se puede volver a consultar después.',
    );
  });
});
