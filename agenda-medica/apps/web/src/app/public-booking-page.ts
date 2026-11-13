import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import {
  AuthService,
  PublicAppointmentRequestResponse,
  PublicBookingAvailability,
  PublicBookingSlot,
} from './auth.service';

type PublicBookingState = 'loading' | 'ready' | 'submitting' | 'success' | 'error';

@Component({
  selector: 'app-public-booking-page',
  templateUrl: './public-booking-page.html',
})
export class PublicBookingPage implements OnInit {
  private readonly auth = inject(AuthService);
  private token = '';
  private idempotencyKey = '';

  protected readonly state = signal<PublicBookingState>('loading');
  protected readonly availability = signal<PublicBookingAvailability | null>(null);
  protected readonly selectedDate = signal('');
  protected readonly selectedStartTime = signal('');
  protected readonly errorMessage = signal('');
  protected readonly canRetry = signal(false);
  protected readonly confirmation = signal<PublicAppointmentRequestResponse | null>(null);

  ngOnInit(): void {
    this.token = window.location.hash.slice(1);
    if (this.token) {
      window.history.replaceState(
        {},
        '',
        `${window.location.pathname}${window.location.search}`,
      );
    }

    if (!this.token) {
      this.state.set('error');
      this.errorMessage.set('Este enlace no es válido o ya fue revocado.');
      return;
    }

    this.prepareAndLoadAvailability();
  }

  protected retry(): void {
    if (!this.token || !this.canRetry()) {
      return;
    }

    this.state.set('loading');
    this.errorMessage.set('');
    this.canRetry.set(false);
    this.prepareAndLoadAvailability();
  }

  private prepareAndLoadAvailability(): void {
    this.auth.prepareCsrf().subscribe({
      next: () => this.loadAvailability(),
      error: () => {
        this.state.set('error');
        this.canRetry.set(true);
        this.errorMessage.set('No se pudo preparar la solicitud segura. Intenta de nuevo.');
      },
    });
  }

  protected availableDates(): string[] {
    return this.availability()?.days
      .filter((day) => day.slots.length > 0)
      .map((day) => day.localDate) ?? [];
  }

  protected slotsForSelectedDate(): PublicBookingSlot[] {
    return this.availability()?.days.find(
      (day) => day.localDate === this.selectedDate(),
    )?.slots ?? [];
  }

  protected dayLabel(localDate: string): string {
    const weekday = new Intl.DateTimeFormat('es', {
      weekday: 'long',
      timeZone: 'UTC',
    }).format(new Date(`${localDate}T12:00:00Z`));
    return `${weekday} · ${localDate}`;
  }

  protected updateDate(event: Event): void {
    if (!(event.target instanceof HTMLSelectElement)) {
      return;
    }

    this.selectedDate.set(event.target.value);
    this.selectedStartTime.set(this.slotsForSelectedDate()[0]?.startTime ?? '');
    this.idempotencyKey = '';
  }

  protected updateStartTime(event: Event): void {
    if (event.target instanceof HTMLSelectElement) {
      this.selectedStartTime.set(event.target.value);
      this.idempotencyKey = '';
    }
  }

  protected invalidateIdempotencyKey(): void {
    this.idempotencyKey = '';
  }

  protected submit(event: SubmitEvent): void {
    event.preventDefault();
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || this.state() !== 'ready') {
      return;
    }

    const patientName = form.elements.namedItem('patientName');
    const contactEmail = form.elements.namedItem('contactEmail');
    const contactPhone = form.elements.namedItem('contactPhone');
    const contactConsent = form.elements.namedItem('contactConsent');
    if (!(patientName instanceof HTMLInputElement) ||
        !(contactEmail instanceof HTMLInputElement) ||
        !(contactPhone instanceof HTMLInputElement) ||
        !(contactConsent instanceof HTMLInputElement)) {
      this.errorMessage.set('No se pudo leer el formulario. Recarga e intenta de nuevo.');
      return;
    }

    if (!contactEmail.value.trim() && !contactPhone.value.trim()) {
      this.errorMessage.set('Indica al menos un medio de contacto.');
      return;
    }

    if (!this.slotsForSelectedDate().some(
      (slot) => slot.startTime === this.selectedStartTime(),
    )) {
      this.errorMessage.set('Ese inicio ya no está disponible. Actualiza la página.');
      return;
    }

    this.idempotencyKey ||= crypto.randomUUID();
    this.state.set('submitting');
    this.errorMessage.set('');
    this.auth.createPublicAppointmentRequest(
      this.token,
      this.idempotencyKey,
      {
        localDate: this.selectedDate(),
        startTime: this.selectedStartTime(),
        patientName: patientName.value.trim(),
        contactEmail: contactEmail.value.trim() || null,
        contactPhone: contactPhone.value.trim() || null,
        contactConsent: contactConsent.checked,
      },
    ).subscribe({
      next: (result) => {
        this.confirmation.set(result);
        this.state.set('success');
        this.idempotencyKey = '';
      },
      error: (error: HttpErrorResponse) => {
        this.state.set(error.status === 404 ? 'error' : 'ready');
        this.canRetry.set(false);
        this.errorMessage.set(this.errorFor(error));
        if (error.status === 409) {
          this.loadAvailability();
        }
      },
    });
  }

  private loadAvailability(): void {
    this.auth.getPublicBookingAvailability(this.token).subscribe({
      next: (result) => {
        this.availability.set(result);
        this.canRetry.set(false);
        const selectedDate = this.availableDates()[0] ?? '';
        this.selectedDate.set(selectedDate);
        this.selectedStartTime.set(
          result.days.find((day) => day.localDate === selectedDate)?.slots[0]?.startTime ?? '',
        );
        this.state.set('ready');
      },
      error: (error: HttpErrorResponse) => {
        this.availability.set(null);
        this.state.set('error');
        this.canRetry.set(error.status !== 404);
        this.errorMessage.set(this.errorFor(error));
      },
    });
  }

  private errorFor(error: HttpErrorResponse): string {
    if (error.status === 404) {
      return 'Este enlace no es válido o ya fue revocado.';
    }

    if (error.status === 409) {
      return 'Ese horario acaba de ocuparse. Elige otro inicio disponible.';
    }

    if (error.status === 429) {
      return 'Se recibieron demasiadas solicitudes. Espera un momento antes de continuar.';
    }

    if (error.status === 400 && typeof error.error?.message === 'string') {
      return error.error.message;
    }

    return 'No se pudo procesar la solicitud. Intenta de nuevo.';
  }
}
