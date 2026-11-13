import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiHealthService } from './api-health.service';
import {
  AuthService,
  Appointment,
  AppointmentStatus,
  AuthorizedPractitionerAgenda,
  CurrentUser,
  OrganizationAccess,
  OrganizationSettings,
  SchedulePreview,
  SchedulePreviewDay,
  SecretaryAgendaAssignments,
  WeeklySchedule,
  WeeklyScheduleWindow,
} from './auth.service';

type ApiStatus = 'checking' | 'online' | 'offline';
type SessionState = 'checking' | 'ready' | 'error';
type OrganizationSettingsState = 'idle' | 'loading' | 'ready' | 'saving' | 'error';
type WeeklyScheduleState = 'idle' | 'loading' | 'ready' | 'saving' | 'error';
type AppointmentListState = 'idle' | 'loading' | 'ready' | 'error';
type AppointmentSubmissionState = 'idle' | 'saving';
type PublicBookingLinkState = 'idle' | 'loading' | 'ready' | 'saving' | 'error';
type SecretaryAssignmentState = 'idle' | 'loading' | 'ready' | 'saving' | 'error';

interface ScheduleWindowForm {
  startTime: string;
  endTime: string;
}

@Component({
  selector: 'app-home-page',
  templateUrl: './home-page.html',
})
export class HomePage implements OnInit {
  private readonly apiHealth = inject(ApiHealthService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly apiStatus = signal<ApiStatus>('checking');
  protected readonly sessionState = signal<SessionState>('checking');
  protected readonly currentUser = signal<CurrentUser | null>(null);
  protected readonly selectedOrganizationId = signal('');
  protected readonly invitationState = signal<'idle' | 'submitting'>('idle');
  protected readonly invitationMessage = signal('');
  protected readonly invitationError = signal('');
  protected readonly failedInvitationId = signal('');
  protected readonly signOutError = signal('');
  protected readonly organizationSettings = signal<OrganizationSettings | null>(null);
  protected readonly settingsState = signal<OrganizationSettingsState>('idle');
  protected readonly settingsMode = signal<OrganizationSettings['appointmentConfirmationMode']>(
    'ManualApproval',
  );
  protected readonly settingsMessage = signal('');
  protected readonly settingsError = signal('');
  protected readonly practitionerOrganizations = signal<OrganizationAccess[]>([]);
  protected readonly appointmentOrganizations = signal<OrganizationAccess[]>([]);
  protected readonly appointmentOrganizationId = signal('');
  protected readonly authorizedPractitionerAgendas = signal<AuthorizedPractitionerAgenda[]>([]);
  protected readonly appointmentPractitionerId = signal('');
  protected readonly secretaryAgendaAccessState = signal<'idle' | 'loading' | 'ready' | 'error'>(
    'idle',
  );
  protected readonly secretaryAssignmentsData = signal<SecretaryAgendaAssignments | null>(null);
  protected readonly secretaryAssignmentState = signal<SecretaryAssignmentState>('idle');
  protected readonly selectedSecretaryId = signal('');
  protected readonly selectedSecretaryPractitionerIds = signal<string[]>([]);
  protected readonly secretaryAssignmentError = signal('');
  protected readonly secretaryAssignmentMessage = signal('');
  protected readonly scheduleOrganizationId = signal('');
  protected readonly weeklySchedule = signal<ScheduleWindowForm[][]>(
    Array.from({ length: 7 }, () => []),
  );
  protected readonly scheduleTimeZoneId = signal('');
  protected readonly scheduleState = signal<WeeklyScheduleState>('idle');
  protected readonly scheduleMessage = signal('');
  protected readonly scheduleError = signal('');
  protected readonly appointmentDurationMinutes = signal(30);
  protected readonly preparationBufferMinutes = signal(5);
  protected readonly appointmentDurationOptions = [15, 20, 30, 45, 60];
  protected readonly preparationBufferOptions = [0, 5, 10, 15];
  protected readonly scheduleExceptionDates = signal<string[]>([]);
  protected readonly exceptionState = signal<'idle' | 'loading' | 'saving' | 'error'>('idle');
  protected readonly exceptionMessage = signal('');
  protected readonly exceptionError = signal('');
  protected readonly schedulePreviewDays = signal<SchedulePreviewDay[]>([]);
  protected readonly previewState = signal<'idle' | 'loading' | 'error'>('idle');
  protected readonly appointments = signal<Appointment[]>([]);
  protected readonly appointmentListState = signal<AppointmentListState>('idle');
  protected readonly appointmentSubmissionState = signal<AppointmentSubmissionState>('idle');
  protected readonly appointmentError = signal('');
  protected readonly appointmentMessage = signal('');
  protected readonly selectedAppointmentDate = signal('');
  protected readonly selectedAppointmentStartTime = signal('');
  private appointmentIdempotencyKey = '';
  private readonly savedPreparationBufferMinutes = signal(5);
  protected readonly publicBookingLinkState = signal<PublicBookingLinkState>('idle');
  protected readonly publicBookingLinkEnabled = signal(false);
  protected readonly publicBookingLinkCreatedAt = signal<string | null>(null);
  protected readonly publicBookingUrl = signal('');
  protected readonly publicBookingLinkError = signal('');
  protected readonly publicBookingLinkMessage = signal('');
  protected readonly scheduleDays = [
    { value: 1, label: 'Lunes' },
    { value: 2, label: 'Martes' },
    { value: 3, label: 'Miércoles' },
    { value: 4, label: 'Jueves' },
    { value: 5, label: 'Viernes' },
    { value: 6, label: 'Sábado' },
    { value: 0, label: 'Domingo' },
  ];
  protected readonly todayLabel = new Intl.DateTimeFormat('es', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
  }).format(new Date());

  ngOnInit(): void {
    this.checkApi();
    this.auth.getCurrentUser().subscribe({
      next: (user) => {
        this.currentUser.set(user);
        const firstAdminOrganization = user.organizations.find(
          (organization) => organization.role === 'ClinicAdmin',
        );
        if (firstAdminOrganization) {
          this.selectedOrganizationId.set(firstAdminOrganization.id);
          this.loadOrganizationSettings(firstAdminOrganization.id);
          this.loadSecretaryAssignments(firstAdminOrganization.id);
        }
        const practitionerOrganizations = user.organizations.filter(
          (organization) => organization.role === 'Practitioner',
        );
        this.practitionerOrganizations.set(practitionerOrganizations);
        const appointmentOrganizations = user.organizations.filter(
          (organization) =>
            organization.role === 'Practitioner' || organization.role === 'Secretary',
        );
        this.appointmentOrganizations.set(appointmentOrganizations);
        const firstAppointmentOrganization =
          appointmentOrganizations.find(
            (organization) => organization.role === 'Practitioner',
          ) ?? appointmentOrganizations[0];
        if (firstAppointmentOrganization) {
          this.appointmentOrganizationId.set(firstAppointmentOrganization.id);
          if (firstAppointmentOrganization.role === 'Secretary') {
            this.loadAuthorizedPractitionerAgendas(firstAppointmentOrganization.id);
          }
        }
        const firstPractitionerOrganization = practitionerOrganizations[0];
        if (firstPractitionerOrganization) {
          this.scheduleOrganizationId.set(firstPractitionerOrganization.id);
          this.loadWeeklySchedule(firstPractitionerOrganization.id);
          this.loadScheduleExceptions(firstPractitionerOrganization.id);
          this.loadSchedulePreview(firstPractitionerOrganization.id);
          this.loadAppointments(firstPractitionerOrganization.id);
          this.loadPublicBookingLink(firstPractitionerOrganization.id);
        }
        this.sessionState.set('ready');
      },
      error: (error: HttpErrorResponse) => {
        if (error.status === 401) {
          void this.router.navigateByUrl('/login');
          return;
        }

        this.sessionState.set('error');
      },
    });
  }

  protected checkApi(): void {
    this.apiStatus.set('checking');
    this.apiHealth.check().subscribe({
      next: (health) => this.apiStatus.set(health.status === 'ok' ? 'online' : 'offline'),
      error: () => this.apiStatus.set('offline'),
    });
  }

  protected adminOrganizations(): OrganizationAccess[] {
    return this.currentUser()?.organizations.filter(
      (organization) => organization.role === 'ClinicAdmin',
    ) ?? [];
  }

  protected updateOrganization(event: Event): void {
    if (event.target instanceof HTMLSelectElement) {
      this.selectedOrganizationId.set(event.target.value);
      this.loadOrganizationSettings(event.target.value);
      this.loadSecretaryAssignments(event.target.value);
    }
  }

  protected updateSelectedSecretary(event: Event): void {
    if (!(event.target instanceof HTMLSelectElement)) {
      return;
    }

    const secretaryUserId = event.target.value;
    this.selectedSecretaryId.set(secretaryUserId);
    const secretary = this.secretaryAssignmentsData()?.secretaries.find(
      (item) => item.userId === secretaryUserId,
    );
    this.selectedSecretaryPractitionerIds.set(secretary?.practitionerUserIds ?? []);
    this.secretaryAssignmentError.set('');
    this.secretaryAssignmentMessage.set('');
  }

  protected toggleSecretaryPractitioner(
    practitionerUserId: string,
    event: Event,
  ): void {
    if (!(event.target instanceof HTMLInputElement)) {
      return;
    }

    const selected = new Set(this.selectedSecretaryPractitionerIds());
    if (event.target.checked) {
      selected.add(practitionerUserId);
    } else {
      selected.delete(practitionerUserId);
    }
    this.selectedSecretaryPractitionerIds.set([...selected].sort());
    this.secretaryAssignmentError.set('');
    this.secretaryAssignmentMessage.set('');
  }

  protected saveSecretaryAssignments(): void {
    const organizationId = this.selectedOrganizationId();
    const secretaryUserId = this.selectedSecretaryId();
    if (!organizationId ||
        !secretaryUserId ||
        this.secretaryAssignmentState() !== 'ready') {
      return;
    }

    this.secretaryAssignmentState.set('saving');
    this.secretaryAssignmentError.set('');
    this.secretaryAssignmentMessage.set('');
    this.auth.replaceSecretaryAgendaAssignments(
      organizationId,
      secretaryUserId,
      this.selectedSecretaryPractitionerIds(),
    ).subscribe({
      next: (result) => {
        if (this.selectedOrganizationId() !== organizationId) {
          return;
        }

        const assignments = this.secretaryAssignmentsData();
        if (assignments) {
          this.secretaryAssignmentsData.set({
            ...assignments,
            secretaries: assignments.secretaries.map((secretary) =>
              secretary.userId === result.secretaryUserId
                ? { ...secretary, practitionerUserIds: result.practitionerUserIds }
                : secretary),
          });
        }
        this.secretaryAssignmentState.set('ready');
        this.secretaryAssignmentMessage.set('Agendas autorizadas actualizadas.');
      },
      error: () => {
        if (this.selectedOrganizationId() !== organizationId) {
          return;
        }

        this.secretaryAssignmentState.set('ready');
        this.secretaryAssignmentError.set(
          'No se pudieron guardar las agendas autorizadas. Actualiza la lista e intenta de nuevo.',
        );
      },
    });
  }

  protected updateConfirmationMode(event: Event): void {
    if (event.target instanceof HTMLSelectElement &&
        (event.target.value === 'ManualApproval' || event.target.value === 'Automatic')) {
      this.settingsMode.set(event.target.value);
    }
  }

  protected saveOrganizationSettings(event: SubmitEvent): void {
    event.preventDefault();
    const organizationId = this.selectedOrganizationId();
    if (!organizationId || this.settingsState() !== 'ready') {
      return;
    }

    this.settingsState.set('saving');
    this.settingsError.set('');
    this.settingsMessage.set('');
    this.auth.updateOrganizationSettings(organizationId, this.settingsMode()).subscribe({
      next: (settings) => {
        this.organizationSettings.set(settings);
        this.settingsMode.set(settings.appointmentConfirmationMode);
        this.settingsState.set('ready');
        this.settingsMessage.set('Preferencia de confirmación guardada.');
      },
      error: () => {
        this.settingsState.set('ready');
        this.settingsError.set('No se pudo guardar la preferencia. Intenta de nuevo.');
      },
    });
  }

  private loadOrganizationSettings(organizationId: string): void {
    if (!organizationId) {
      this.organizationSettings.set(null);
      this.settingsState.set('idle');
      return;
    }

    this.settingsState.set('loading');
    this.settingsError.set('');
    this.settingsMessage.set('');
    this.auth.getOrganizationSettings(organizationId).subscribe({
      next: (settings) => {
        if (this.selectedOrganizationId() !== organizationId) {
          return;
        }

        this.organizationSettings.set(settings);
        this.settingsMode.set(settings.appointmentConfirmationMode);
        this.settingsState.set('ready');
      },
      error: () => {
        if (this.selectedOrganizationId() !== organizationId) {
          return;
        }

        this.organizationSettings.set(null);
        this.settingsState.set('error');
        this.settingsError.set('No se pudo cargar la configuración del consultorio.');
      },
    });
  }

  protected inviteStaff(event: SubmitEvent): void {
    event.preventDefault();
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || this.invitationState() !== 'idle') {
      return;
    }

    const emailInput = form.elements.namedItem('inviteEmail');
    const roleInput = form.elements.namedItem('inviteRole');
    if (!(emailInput instanceof HTMLInputElement) || !(roleInput instanceof HTMLSelectElement)) {
      return;
    }

    const organizationId = this.selectedOrganizationId();
    const email = emailInput.value.trim();
    const role = roleInput.value;
    if (!organizationId || (role !== 'Practitioner' && role !== 'Secretary')) {
      this.invitationError.set('Selecciona un consultorio y un rol válido.');
      return;
    }

    this.invitationState.set('submitting');
    this.invitationError.set('');
    this.invitationMessage.set('');
    this.failedInvitationId.set('');
    this.auth.createInvitation(organizationId, email, role).subscribe({
      next: () => {
        this.invitationState.set('idle');
        this.invitationMessage.set('Invitación enviada. El enlace vence en 48 horas.');
        emailInput.value = '';
        roleInput.value = 'Practitioner';
      },
      error: (error: HttpErrorResponse) => {
        this.invitationState.set('idle');
        this.invitationError.set(this.invitationErrorFor(error));
        const invitationId = error.error?.invitationId;
        if (typeof invitationId === 'string') {
          this.failedInvitationId.set(invitationId);
        }
      },
    });
  }

  protected updateScheduleOrganization(event: Event): void {
    if (event.target instanceof HTMLSelectElement) {
      this.scheduleOrganizationId.set(event.target.value);
      this.appointmentOrganizationId.set(event.target.value);
      this.appointmentPractitionerId.set('');
      this.appointmentError.set('');
      this.appointmentMessage.set('');
      this.appointmentIdempotencyKey = '';
      this.publicBookingUrl.set('');
      this.publicBookingLinkError.set('');
      this.publicBookingLinkMessage.set('');
      this.loadWeeklySchedule(event.target.value);
      this.loadScheduleExceptions(event.target.value);
      this.loadSchedulePreview(event.target.value);
      this.loadAppointments(event.target.value);
      this.loadPublicBookingLink(event.target.value);
    }
  }

  protected updateAppointmentOrganization(event: Event): void {
    if (!(event.target instanceof HTMLSelectElement)) {
      return;
    }

    const organizationId = event.target.value;
    const organization = this.appointmentOrganizations().find(
      (item) => item.id === organizationId,
    );
    if (!organization) {
      return;
    }

    this.appointmentOrganizationId.set(organization.id);
    this.appointmentPractitionerId.set('');
    this.authorizedPractitionerAgendas.set([]);
    this.secretaryAgendaAccessState.set('idle');
    this.appointments.set([]);
    this.schedulePreviewDays.set([]);
    this.appointmentError.set('');
    this.appointmentMessage.set('');
    this.appointmentIdempotencyKey = '';
    this.appointmentListState.set('idle');
    this.previewState.set('idle');
    if (organization.role === 'Secretary') {
      this.loadAuthorizedPractitionerAgendas(organization.id);
      return;
    }

    this.secretaryAgendaAccessState.set('ready');
    this.loadAppointments(organization.id);
    this.loadSchedulePreview(organization.id);
  }

  protected updateAppointmentPractitioner(event: Event): void {
    if (!(event.target instanceof HTMLSelectElement)) {
      return;
    }

    const practitionerUserId = event.target.value;
    const practitioner = this.authorizedPractitionerAgendas().find(
      (item) => item.userId === practitionerUserId,
    );
    if (!practitioner) {
      return;
    }

    const organizationId = this.appointmentOrganizationId();
    this.appointmentPractitionerId.set(practitioner.userId);
    this.appointments.set([]);
    this.schedulePreviewDays.set([]);
    this.appointmentError.set('');
    this.appointmentMessage.set('');
    this.appointmentIdempotencyKey = '';
    this.appointmentListState.set('loading');
    this.previewState.set('loading');
    this.loadAppointments(organizationId, practitioner.userId);
    this.loadSchedulePreview(organizationId, practitioner.userId);
  }

  protected rotatePublicBookingLink(): void {
    const organizationId = this.scheduleOrganizationId();
    if (!organizationId ||
        this.publicBookingLinkState() !== 'ready' ||
        (this.publicBookingLinkEnabled() &&
          !window.confirm('El enlace actual dejará de funcionar. ¿Quieres generar otro?'))) {
      return;
    }

    this.publicBookingLinkState.set('saving');
    this.publicBookingLinkError.set('');
    this.publicBookingLinkMessage.set('');
    this.auth.rotatePublicBookingLink(organizationId).subscribe({
      next: (result) => {
        if (this.scheduleOrganizationId() !== organizationId) {
          return;
        }

        this.publicBookingLinkEnabled.set(true);
        this.publicBookingLinkCreatedAt.set(new Date().toISOString());
        this.publicBookingUrl.set(`${window.location.origin}/book#${result.token}`);
        this.publicBookingLinkState.set('ready');
        this.publicBookingLinkMessage.set(
          'Enlace generado. Guárdalo ahora: no se puede volver a consultar después.',
        );
      },
      error: () => {
        if (this.scheduleOrganizationId() !== organizationId) {
          return;
        }

        this.publicBookingLinkState.set('ready');
        this.publicBookingLinkError.set(
          'No se pudo generar el enlace. Si la solicitud llegó al servidor, vuelve a generar uno para invalidarlo y obtener el nuevo.',
        );
      },
    });
  }

  protected revokePublicBookingLink(): void {
    const organizationId = this.scheduleOrganizationId();
    if (!organizationId ||
        this.publicBookingLinkState() !== 'ready' ||
        !this.publicBookingLinkEnabled() ||
        !window.confirm('El enlace dejará de aceptar nuevas solicitudes. ¿Revocarlo?')) {
      return;
    }

    this.publicBookingLinkState.set('saving');
    this.publicBookingLinkError.set('');
    this.publicBookingLinkMessage.set('');
    this.auth.revokePublicBookingLink(organizationId).subscribe({
      next: () => {
        if (this.scheduleOrganizationId() !== organizationId) {
          return;
        }

        this.publicBookingLinkEnabled.set(false);
        this.publicBookingLinkCreatedAt.set(null);
        this.publicBookingUrl.set('');
        this.publicBookingLinkState.set('ready');
        this.publicBookingLinkMessage.set('Enlace revocado.');
      },
      error: () => {
        if (this.scheduleOrganizationId() !== organizationId) {
          return;
        }

        this.publicBookingLinkState.set('ready');
        this.publicBookingLinkError.set('No se pudo revocar el enlace. Intenta de nuevo.');
      },
    });
  }

  protected async copyPublicBookingLink(): Promise<void> {
    if (!this.publicBookingUrl()) {
      return;
    }

    try {
      await navigator.clipboard.writeText(this.publicBookingUrl());
      this.publicBookingLinkMessage.set('Enlace copiado.');
      this.publicBookingLinkError.set('');
    } catch (error) {
      if (!(error instanceof DOMException)) {
        throw error;
      }

      this.publicBookingLinkError.set(
        'El navegador no permitió copiarlo. Selecciona y copia el enlace manualmente.',
      );
    }
  }

  protected availableAppointmentDays(): SchedulePreviewDay[] {
    if (this.appointmentListState() !== 'ready' || this.previewState() !== 'idle') {
      return [];
    }

    return this.schedulePreviewDays().filter(
      (day) => this.availableAppointmentTimes(day).length > 0,
    );
  }

  protected availableAppointmentTimes(day: SchedulePreviewDay): string[] {
    const now = this.currentClinicLocalTimestamp();
    if (!now) {
      return [];
    }

    return day.potentialSlots
      .filter((slot) => {
        const start = `${day.localDate}T${slot.startTime}:00`;
        const end = this.addMinutesToLocalDateTime(
          day.localDate,
          slot.endTime,
          this.savedPreparationBufferMinutes(),
        );
        if (start <= now) {
          return false;
        }

        return !this.appointments().some((appointment) => {
          const existingStart = `${appointment.localDate}T${appointment.startTime}:00`;
          const existingEnd =
            `${appointment.occupiedUntilLocalDate}T${appointment.occupiedUntilTime}:00`;
          return start < existingEnd && existingStart < end;
        });
      })
      .map((slot) => slot.startTime);
  }

  protected updateAppointmentDate(event: Event): void {
    if (!(event.target instanceof HTMLSelectElement)) {
      return;
    }

    this.selectedAppointmentDate.set(event.target.value);
    this.selectedAppointmentStartTime.set('');
    this.invalidateAppointmentKey();
    const selectedDate = event.target.value;
    const day = this.schedulePreviewDays().find(
      (item) => item.localDate === selectedDate,
    );
    const firstTime = day ? this.availableAppointmentTimes(day)[0] : undefined;
    if (firstTime) {
      this.selectedAppointmentStartTime.set(firstTime);
    }
  }

  protected updateAppointmentStartTime(event: Event): void {
    if (event.target instanceof HTMLSelectElement) {
      this.selectedAppointmentStartTime.set(event.target.value);
      this.invalidateAppointmentKey();
    }
  }

  protected invalidateAppointmentKey(): void {
    this.appointmentIdempotencyKey = '';
  }

  protected createAppointment(event: SubmitEvent): void {
    event.preventDefault();
    const form = event.target;
    const organizationId = this.appointmentOrganizationId();
    const practitionerUserId = this.appointmentPractitionerId();
    const secretaryMode = this.isSecretaryAppointmentMode();
    if (!(form instanceof HTMLFormElement) ||
        !organizationId ||
        (secretaryMode && !practitionerUserId) ||
        this.appointmentSubmissionState() === 'saving') {
      return;
    }

    const patientName = form.elements.namedItem('patientName');
    const contactEmail = form.elements.namedItem('contactEmail');
    const contactPhone = form.elements.namedItem('contactPhone');
    const contactConsent = form.elements.namedItem('contactConsent');
    const localDate = form.elements.namedItem('appointmentDate');
    const startTime = form.elements.namedItem('appointmentStartTime');
    if (!(patientName instanceof HTMLInputElement) ||
        !(contactEmail instanceof HTMLInputElement) ||
        !(contactPhone instanceof HTMLInputElement) ||
        !(contactConsent instanceof HTMLInputElement) ||
        !(localDate instanceof HTMLSelectElement) ||
        !(startTime instanceof HTMLSelectElement)) {
      this.appointmentError.set('No se pudo leer el formulario de cita.');
      return;
    }

    if (!contactEmail.value.trim() && !contactPhone.value.trim()) {
      this.appointmentError.set('Indica al menos un medio de contacto.');
      return;
    }

    if (!this.availableAppointmentTimesForSelection().includes(startTime.value)) {
      this.appointmentError.set('Ese inicio ya no está disponible. Actualiza la agenda.');
      return;
    }

    this.appointmentIdempotencyKey ||= crypto.randomUUID();
    this.appointmentSubmissionState.set('saving');
    this.appointmentError.set('');
    this.appointmentMessage.set('');
    this.auth.createAppointment(
      organizationId,
      this.appointmentIdempotencyKey,
      {
        localDate: localDate.value,
        startTime: startTime.value,
        patientName: patientName.value.trim(),
        contactEmail: contactEmail.value.trim() || null,
        contactPhone: contactPhone.value.trim() || null,
        contactConsent: contactConsent.checked,
        ...(secretaryMode ? { practitionerUserId } : {}),
      },
    ).subscribe({
      next: () => {
        if (this.appointmentOrganizationId() !== organizationId ||
            (secretaryMode &&
              this.appointmentPractitionerId() !== practitionerUserId)) {
          return;
        }

        this.appointmentSubmissionState.set('idle');
        this.appointmentIdempotencyKey = '';
        form.reset();
        this.appointmentMessage.set('La cita se guardó correctamente.');
        this.loadAppointments(
          organizationId,
          secretaryMode ? practitionerUserId : undefined,
        );
      },
      error: (error: HttpErrorResponse) => {
        if (this.appointmentOrganizationId() !== organizationId ||
            (secretaryMode &&
              this.appointmentPractitionerId() !== practitionerUserId)) {
          return;
        }

        this.appointmentSubmissionState.set('idle');
        this.appointmentError.set(
          error.status === 409
            ? 'El horario ya no está disponible o la solicitud cambió. Actualiza la agenda e intenta de nuevo.'
            : error.status === 400 && typeof error.error?.message === 'string'
              ? error.error.message
              : 'No se pudo guardar la cita. Intenta de nuevo.',
        );
        this.loadAppointments(
          organizationId,
          secretaryMode ? practitionerUserId : undefined,
        );
      },
    });
  }

  protected updateAppointmentStatus(
    appointment: Appointment,
    status: AppointmentStatus,
  ): void {
    const organizationId = this.appointmentOrganizationId();
    const practitionerUserId = this.appointmentPractitionerId();
    const secretaryMode = this.isSecretaryAppointmentMode();
    if (!organizationId ||
        (secretaryMode &&
          (!practitionerUserId ||
            status === 'Completed' ||
            status === 'NoShow')) ||
        this.appointmentSubmissionState() === 'saving') {
      return;
    }

    const confirmationMessages: Partial<Record<AppointmentStatus, string>> = {
      Rejected: '¿Rechazar esta solicitud de cita?',
      Cancelled: '¿Cancelar esta cita? El horario volverá a estar disponible.',
      Completed: '¿Marcar esta cita como atendida?',
      NoShow: '¿Marcar esta cita como inasistencia?',
    };
    const confirmationMessage = confirmationMessages[status];
    if (confirmationMessage && !window.confirm(confirmationMessage)) {
      return;
    }

    this.appointmentSubmissionState.set('saving');
    this.appointmentError.set('');
    this.appointmentMessage.set('');
    this.auth.updateAppointmentStatus(organizationId, appointment.id, status).subscribe({
      next: () => {
        if (this.appointmentOrganizationId() !== organizationId ||
            (secretaryMode &&
              this.appointmentPractitionerId() !== practitionerUserId)) {
          return;
        }

        this.appointmentSubmissionState.set('idle');
        this.appointmentMessage.set('Estado de la cita actualizado.');
        this.loadAppointments(
          organizationId,
          secretaryMode ? practitionerUserId : undefined,
        );
      },
      error: (error: HttpErrorResponse) => {
        if (this.appointmentOrganizationId() !== organizationId ||
            (secretaryMode &&
              this.appointmentPractitionerId() !== practitionerUserId)) {
          return;
        }

        this.appointmentSubmissionState.set('idle');
        this.appointmentError.set(
          error.status === 409
            ? 'La cita cambió o el horario ya pasó. Actualiza la agenda.'
            : 'No se pudo actualizar la cita. Intenta de nuevo.',
        );
        this.loadAppointments(
          organizationId,
          secretaryMode ? practitionerUserId : undefined,
        );
      },
    });
  }

  protected appointmentStatusLabel(status: AppointmentStatus): string {
    return {
      Requested: 'Pendiente',
      Confirmed: 'Confirmada',
      Rejected: 'Rechazada',
      Cancelled: 'Cancelada',
      Completed: 'Atendida',
      NoShow: 'No asistió',
    }[status];
  }

  protected isAppointmentInPast(appointment: Appointment): boolean {
    return `${appointment.localDate}T${appointment.startTime}:00` <
      this.currentClinicLocalTimestamp();
  }

  protected availableAppointmentTimesForSelection(): string[] {
    const day = this.schedulePreviewDays().find(
      (item) => item.localDate === this.selectedAppointmentDate(),
    );
    return day ? this.availableAppointmentTimes(day) : [];
  }

  protected isSecretaryAppointmentMode(): boolean {
    return this.appointmentOrganizations().some(
      (organization) =>
        organization.id === this.appointmentOrganizationId() &&
        organization.role === 'Secretary',
    );
  }

  protected selectedAppointmentAgendaEmail(): string {
    return this.authorizedPractitionerAgendas().find(
      (agenda) => agenda.userId === this.appointmentPractitionerId(),
    )?.email ?? 'Agenda profesional';
  }

  protected canCreateAppointments(): boolean {
    return this.appointmentOrganizations().some(
      (organization) => organization.id === this.appointmentOrganizationId(),
    ) && (!this.isSecretaryAppointmentMode() ||
      this.authorizedPractitionerAgendas().some(
        (agenda) => agenda.userId === this.appointmentPractitionerId(),
      ));
  }

  protected dayName(dayOfWeek: number): string {
    return this.scheduleDays.find((day) => day.value === dayOfWeek)?.label ?? '';
  }

  protected updateAppointmentDuration(event: Event): void {
    if (!(event.target instanceof HTMLSelectElement)) {
      return;
    }

    const value = Number(event.target.value);
    if (this.appointmentDurationOptions.includes(value)) {
      this.appointmentDurationMinutes.set(value);
    }
  }

  protected updatePreparationBuffer(event: Event): void {
    if (!(event.target instanceof HTMLSelectElement)) {
      return;
    }

    const value = Number(event.target.value);
    if (this.preparationBufferOptions.includes(value)) {
      this.preparationBufferMinutes.set(value);
    }
  }

  protected addScheduleException(event: SubmitEvent): void {
    event.preventDefault();
    const form = event.target;
    if (!(form instanceof HTMLFormElement) ||
        this.exceptionState() === 'loading' ||
        this.exceptionState() === 'saving') {
      return;
    }

    const dateInput = form.elements.namedItem('exceptionDate');
    const organizationId = this.scheduleOrganizationId();
    if (!(dateInput instanceof HTMLInputElement) || !organizationId || !dateInput.value) {
      this.exceptionError.set('Selecciona una fecha válida.');
      return;
    }

    this.exceptionState.set('saving');
    this.exceptionError.set('');
    this.exceptionMessage.set('');
    this.auth.addScheduleException(organizationId, dateInput.value).subscribe({
      next: () => {
        if (this.scheduleOrganizationId() !== organizationId) {
          return;
        }

        dateInput.value = '';
        this.exceptionState.set('idle');
        this.loadScheduleExceptions(organizationId);
        this.loadSchedulePreview(organizationId);
        this.exceptionMessage.set('Día marcado como no laborable.');
      },
      error: (error: HttpErrorResponse) => {
        if (this.scheduleOrganizationId() !== organizationId) {
          return;
        }

        this.exceptionState.set('idle');
        this.exceptionError.set(
          error.status === 409
            ? 'Ese día ya está marcado como no laborable.'
            : error.status === 400 && typeof error.error?.message === 'string'
              ? error.error.message
              : 'No se pudo guardar el día no laborable. Intenta de nuevo.',
        );
      },
    });
  }

  protected removeScheduleException(localDate: string): void {
    const organizationId = this.scheduleOrganizationId();
    if (!organizationId ||
        this.exceptionState() === 'loading' ||
        this.exceptionState() === 'saving') {
      return;
    }

    this.exceptionState.set('saving');
    this.exceptionError.set('');
    this.exceptionMessage.set('');
    this.auth.removeScheduleException(organizationId, localDate).subscribe({
      next: () => {
        if (this.scheduleOrganizationId() !== organizationId) {
          return;
        }

        this.exceptionState.set('idle');
        this.loadScheduleExceptions(organizationId);
        this.loadSchedulePreview(organizationId);
        this.exceptionMessage.set('Día no laborable eliminado.');
      },
      error: () => {
        if (this.scheduleOrganizationId() !== organizationId) {
          return;
        }

        this.exceptionState.set('idle');
        this.exceptionError.set('No se pudo eliminar el día no laborable. Intenta de nuevo.');
      },
    });
  }

  protected addScheduleWindow(dayOfWeek: number): void {
    if (this.scheduleState() !== 'ready' || this.totalScheduleWindows() >= 56) {
      return;
    }

    const updated = this.weeklySchedule().map((windows) => [...windows]);
    updated[dayOfWeek].push({ startTime: '09:00', endTime: '17:00' });
    this.weeklySchedule.set(updated);
    this.scheduleMessage.set('');
    this.scheduleError.set('');
  }

  protected totalScheduleWindows(): number {
    return this.weeklySchedule().reduce((total, windows) => total + windows.length, 0);
  }

  protected updateScheduleTime(
    dayOfWeek: number,
    index: number,
    field: keyof ScheduleWindowForm,
    event: Event,
  ): void {
    if (this.scheduleState() !== 'ready' || !(event.target instanceof HTMLInputElement)) {
      return;
    }

    const updated = this.weeklySchedule().map((windows) => [...windows]);
    const window = updated[dayOfWeek]?.[index];
    if (!window) {
      return;
    }

    window[field] = event.target.value;
    this.weeklySchedule.set(updated);
    this.scheduleMessage.set('');
    this.scheduleError.set('');
  }

  protected removeScheduleWindow(dayOfWeek: number, index: number): void {
    if (this.scheduleState() !== 'ready') {
      return;
    }

    const updated = this.weeklySchedule().map((windows) => [...windows]);
    updated[dayOfWeek].splice(index, 1);
    this.weeklySchedule.set(updated);
    this.scheduleMessage.set('');
    this.scheduleError.set('');
  }

  protected saveWeeklySchedule(event: SubmitEvent): void {
    event.preventDefault();
    const organizationId = this.scheduleOrganizationId();
    if (!organizationId || this.scheduleState() !== 'ready') {
      return;
    }

    const windows: WeeklyScheduleWindow[] = this.weeklySchedule().flatMap(
      (dayWindows, dayOfWeek) =>
        dayWindows.map((window) => ({
          dayOfWeek,
          startTime: window.startTime,
          endTime: window.endTime,
        })),
    );
    this.scheduleState.set('saving');
    this.scheduleMessage.set('');
    this.scheduleError.set('');
    this.auth.updateWeeklySchedule(
      organizationId,
      windows,
      this.appointmentDurationMinutes(),
      this.preparationBufferMinutes(),
    ).subscribe({
      next: (schedule) => {
        if (this.scheduleOrganizationId() !== organizationId) {
          return;
        }

        this.applyWeeklySchedule(schedule);
        this.scheduleState.set('ready');
        this.scheduleMessage.set('Horario semanal guardado.');
        this.loadSchedulePreview(organizationId);
      },
      error: (error: HttpErrorResponse) => {
        if (this.scheduleOrganizationId() !== organizationId) {
          return;
        }

        this.scheduleState.set('ready');
        const apiMessage = error.error?.message;
        this.scheduleError.set(
          error.status === 400 && typeof apiMessage === 'string'
            ? apiMessage
            : 'No se pudo guardar el horario. Verifica los intervalos e intenta de nuevo.',
        );
      },
    });
  }

  private loadWeeklySchedule(organizationId: string): void {
    if (!organizationId) {
      this.weeklySchedule.set(Array.from({ length: 7 }, () => []));
      this.scheduleTimeZoneId.set('');
      this.appointmentDurationMinutes.set(30);
      this.preparationBufferMinutes.set(5);
      this.savedPreparationBufferMinutes.set(5);
      this.scheduleState.set('idle');
      return;
    }

    this.scheduleState.set('loading');
    this.scheduleError.set('');
    this.scheduleMessage.set('');
    this.auth.getWeeklySchedule(organizationId).subscribe({
      next: (schedule) => {
        if (this.scheduleOrganizationId() !== organizationId) {
          return;
        }

        this.applyWeeklySchedule(schedule);
        this.scheduleState.set('ready');
      },
      error: () => {
        if (this.scheduleOrganizationId() !== organizationId) {
          return;
        }

        this.weeklySchedule.set(Array.from({ length: 7 }, () => []));
        this.scheduleTimeZoneId.set('');
        this.appointmentDurationMinutes.set(30);
        this.preparationBufferMinutes.set(5);
        this.savedPreparationBufferMinutes.set(5);
        this.scheduleState.set('error');
        this.scheduleError.set('No se pudo cargar el horario semanal.');
      },
    });
  }

  private loadScheduleExceptions(organizationId: string): void {
    if (!organizationId) {
      this.scheduleExceptionDates.set([]);
      this.exceptionState.set('idle');
      return;
    }

    this.exceptionState.set('loading');
    this.exceptionError.set('');
    this.exceptionMessage.set('');
    this.auth.getScheduleExceptions(organizationId).subscribe({
      next: (result) => {
        if (this.scheduleOrganizationId() !== organizationId) {
          return;
        }

        this.scheduleExceptionDates.set(result.dates);
        this.exceptionState.set('idle');
      },
      error: () => {
        if (this.scheduleOrganizationId() !== organizationId) {
          return;
        }

        this.scheduleExceptionDates.set([]);
        this.exceptionState.set('error');
        this.exceptionError.set('No se pudieron cargar los días no laborables.');
      },
    });
  }

  private loadSecretaryAssignments(organizationId: string): void {
    if (!organizationId) {
      this.secretaryAssignmentsData.set(null);
      this.secretaryAssignmentState.set('idle');
      return;
    }

    this.secretaryAssignmentState.set('loading');
    this.secretaryAssignmentError.set('');
    this.secretaryAssignmentMessage.set('');
    this.auth.getSecretaryAgendaAssignments(organizationId).subscribe({
      next: (assignments) => {
        if (this.selectedOrganizationId() !== organizationId) {
          return;
        }

        this.secretaryAssignmentsData.set(assignments);
        const selectedSecretary = assignments.secretaries.find(
          (secretary) => secretary.userId === this.selectedSecretaryId(),
        ) ?? assignments.secretaries[0];
        this.selectedSecretaryId.set(selectedSecretary?.userId ?? '');
        this.selectedSecretaryPractitionerIds.set(
          selectedSecretary?.practitionerUserIds ?? [],
        );
        this.secretaryAssignmentState.set('ready');
      },
      error: () => {
        if (this.selectedOrganizationId() !== organizationId) {
          return;
        }

        this.secretaryAssignmentsData.set(null);
        this.secretaryAssignmentState.set('error');
        this.secretaryAssignmentError.set(
          'No se pudieron cargar las asignaciones de agenda.',
        );
      },
    });
  }

  private loadAuthorizedPractitionerAgendas(organizationId: string): void {
    this.secretaryAgendaAccessState.set('loading');
    this.authorizedPractitionerAgendas.set([]);
    this.appointmentPractitionerId.set('');
    this.appointmentListState.set('loading');
    this.previewState.set('loading');
    this.auth.getAuthorizedPractitionerAgendas(organizationId).subscribe({
      next: (agendas) => {
        if (this.appointmentOrganizationId() !== organizationId) {
          return;
        }

        this.authorizedPractitionerAgendas.set(agendas);
        this.secretaryAgendaAccessState.set('ready');
        const firstAgenda = agendas[0];
        if (!firstAgenda) {
          this.appointments.set([]);
          this.schedulePreviewDays.set([]);
          this.appointmentListState.set('ready');
          this.previewState.set('idle');
          this.selectedAppointmentDate.set('');
          this.selectedAppointmentStartTime.set('');
          return;
        }

        this.appointmentPractitionerId.set(firstAgenda.userId);
        this.loadAppointments(organizationId, firstAgenda.userId);
        this.loadSchedulePreview(organizationId, firstAgenda.userId);
      },
      error: () => {
        if (this.appointmentOrganizationId() !== organizationId) {
          return;
        }

        this.secretaryAgendaAccessState.set('error');
        this.appointmentListState.set('error');
        this.previewState.set('error');
        this.appointmentError.set('No se pudieron cargar las agendas autorizadas.');
      },
    });
  }

  private loadSchedulePreview(
    organizationId: string,
    practitionerUserId?: string,
  ): void {
    if (!organizationId) {
      this.schedulePreviewDays.set([]);
      this.previewState.set('idle');
      return;
    }

    this.previewState.set('loading');
    this.auth.getSchedulePreview(organizationId, practitionerUserId).subscribe({
      next: (preview: SchedulePreview) => {
        if (this.appointmentOrganizationId() !== organizationId ||
            (this.isSecretaryAppointmentMode() &&
              this.appointmentPractitionerId() !== practitionerUserId)) {
          return;
        }

        this.schedulePreviewDays.set(preview.days);
        this.scheduleTimeZoneId.set(preview.timeZoneId);
        this.savedPreparationBufferMinutes.set(preview.preparationBufferMinutes);
        this.previewState.set('idle');
        this.selectFirstAvailableAppointmentSlot();
      },
      error: () => {
        if (this.appointmentOrganizationId() !== organizationId ||
            (this.isSecretaryAppointmentMode() &&
              this.appointmentPractitionerId() !== practitionerUserId)) {
          return;
        }

        this.schedulePreviewDays.set([]);
        this.previewState.set('error');
        this.selectedAppointmentDate.set('');
        this.selectedAppointmentStartTime.set('');
      },
    });
  }

  private loadAppointments(
    organizationId: string,
    practitionerUserId?: string,
  ): void {
    if (!organizationId) {
      this.appointments.set([]);
      this.appointmentListState.set('idle');
      this.selectedAppointmentDate.set('');
      this.selectedAppointmentStartTime.set('');
      return;
    }

    this.appointmentListState.set('loading');
    this.auth.getAppointments(organizationId, practitionerUserId).subscribe({
      next: (result) => {
        if (this.appointmentOrganizationId() !== organizationId ||
            (this.isSecretaryAppointmentMode() &&
              this.appointmentPractitionerId() !== practitionerUserId)) {
          return;
        }

        this.appointments.set(result.appointments);
        this.appointmentListState.set('ready');
        this.selectFirstAvailableAppointmentSlot();
      },
      error: () => {
        if (this.appointmentOrganizationId() !== organizationId ||
            (this.isSecretaryAppointmentMode() &&
              this.appointmentPractitionerId() !== practitionerUserId)) {
          return;
        }

        this.appointments.set([]);
        this.appointmentListState.set('error');
        this.appointmentError.set('No se pudieron cargar las citas.');
      },
    });
  }

  private loadPublicBookingLink(organizationId: string): void {
    this.publicBookingUrl.set('');
    this.publicBookingLinkError.set('');
    this.publicBookingLinkMessage.set('');
    if (!organizationId) {
      this.publicBookingLinkEnabled.set(false);
      this.publicBookingLinkCreatedAt.set(null);
      this.publicBookingLinkState.set('idle');
      return;
    }

    this.publicBookingLinkState.set('loading');
    this.auth.getPublicBookingLink(organizationId).subscribe({
      next: (result) => {
        if (this.scheduleOrganizationId() !== organizationId) {
          return;
        }

        this.publicBookingLinkEnabled.set(result.isEnabled);
        this.publicBookingLinkCreatedAt.set(result.createdAtUtc);
        this.publicBookingLinkState.set('ready');
      },
      error: () => {
        if (this.scheduleOrganizationId() !== organizationId) {
          return;
        }

        this.publicBookingLinkEnabled.set(false);
        this.publicBookingLinkCreatedAt.set(null);
        this.publicBookingLinkState.set('error');
        this.publicBookingLinkError.set('No se pudo cargar el estado del enlace público.');
      },
    });
  }

  private selectFirstAvailableAppointmentSlot(): void {
    const availableDays = this.schedulePreviewDays().filter(
      (day) => this.availableAppointmentTimes(day).length > 0,
    );
    const selectedDay = availableDays.find(
      (day) => day.localDate === this.selectedAppointmentDate(),
    ) ?? availableDays[0];
    if (!selectedDay) {
      this.selectedAppointmentDate.set('');
      this.selectedAppointmentStartTime.set('');
      return;
    }

    this.selectedAppointmentDate.set(selectedDay.localDate);
    const times = this.availableAppointmentTimes(selectedDay);
    this.selectedAppointmentStartTime.set(
      times.includes(this.selectedAppointmentStartTime())
        ? this.selectedAppointmentStartTime()
        : times[0],
    );
  }

  private currentClinicLocalTimestamp(): string {
    const timeZoneId = this.scheduleTimeZoneId();
    if (!timeZoneId) {
      return '';
    }

    const parts = new Intl.DateTimeFormat('en-CA', {
      timeZone: timeZoneId,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hourCycle: 'h23',
    }).formatToParts(new Date());
    const values = Object.fromEntries(parts.map((part) => [part.type, part.value]));
    return `${values['year']}-${values['month']}-${values['day']}T${values['hour']}:${values['minute']}:${values['second']}`;
  }

  private addMinutesToLocalDateTime(localDate: string, time: string, minutes: number): string {
    const dateTime = new Date(`${localDate}T${time}:00Z`);
    dateTime.setUTCMinutes(dateTime.getUTCMinutes() + minutes);
    return dateTime.toISOString().slice(0, 19);
  }

  private applyWeeklySchedule(schedule: WeeklySchedule): void {
    const week = Array.from({ length: 7 }, () => [] as ScheduleWindowForm[]);
    for (const window of schedule.windows) {
      if (window.dayOfWeek >= 0 && window.dayOfWeek <= 6) {
        week[window.dayOfWeek].push({
          startTime: window.startTime,
          endTime: window.endTime,
        });
      }
    }

    this.weeklySchedule.set(week);
    this.scheduleTimeZoneId.set(schedule.timeZoneId);
    this.appointmentDurationMinutes.set(schedule.appointmentDurationMinutes);
    this.preparationBufferMinutes.set(schedule.preparationBufferMinutes);
    this.savedPreparationBufferMinutes.set(schedule.preparationBufferMinutes);
  }

  protected revokeFailedInvitation(): void {
    const organizationId = this.selectedOrganizationId();
    const invitationId = this.failedInvitationId();
    if (!organizationId || !invitationId) {
      return;
    }

    this.auth.revokeInvitation(organizationId, invitationId).subscribe({
      next: () => {
        this.failedInvitationId.set('');
        this.invitationError.set('');
        this.invitationMessage.set('Invitación pendiente revocada.');
      },
      error: () => {
        this.invitationError.set('No se pudo revocar la invitación. Intenta de nuevo.');
      },
    });
  }

  protected signOut(): void {
    this.signOutError.set('');
    this.auth.signOut().subscribe({
      next: () => void this.router.navigateByUrl('/login'),
      error: () => this.signOutError.set('No se pudo cerrar sesión. Intenta de nuevo.'),
    });
  }

  private invitationErrorFor(error: HttpErrorResponse): string {
    if (error.status === 409) {
      return 'Ya existe una cuenta o una invitación pendiente para ese correo.';
    }

    if (error.status === 502) {
      return 'No se pudo entregar el correo. Revoca la invitación pendiente antes de volver a intentarlo.';
    }

    if (error.status === 503) {
      return 'El envío de invitaciones no está configurado todavía.';
    }

    return 'No se pudo crear la invitación. Revisa los datos e intenta de nuevo.';
  }
}
