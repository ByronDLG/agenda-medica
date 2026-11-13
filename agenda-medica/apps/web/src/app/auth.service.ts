import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface OrganizationAccess {
  id: string;
  name: string;
  role: 'ClinicAdmin' | 'Practitioner' | 'Secretary';
}

export interface CurrentUser {
  email: string | null;
  organizations: OrganizationAccess[];
}

export interface CreatedInvitation {
  id: string;
  expiresAt: string;
}

export interface OrganizationSettings {
  id: string;
  name: string;
  countryCode: string;
  timeZoneId: string;
  appointmentConfirmationMode: 'ManualApproval' | 'Automatic';
}

export interface WeeklyScheduleWindow {
  dayOfWeek: number;
  startTime: string;
  endTime: string;
}

export interface WeeklySchedule {
  timeZoneId: string;
  appointmentDurationMinutes: number;
  preparationBufferMinutes: number;
  windows: WeeklyScheduleWindow[];
}

export interface ScheduleExceptions {
  timeZoneId: string;
  dates: string[];
}

export interface SchedulePreviewDay {
  localDate: string;
  dayOfWeek: number;
  isException: boolean;
  windows: Array<Pick<WeeklyScheduleWindow, 'startTime' | 'endTime'>>;
  potentialSlots: Array<Pick<WeeklyScheduleWindow, 'startTime' | 'endTime'>>;
}

export interface SchedulePreview {
  timeZoneId: string;
  appointmentDurationMinutes: number;
  preparationBufferMinutes: number;
  days: SchedulePreviewDay[];
}

export type AppointmentStatus =
  | 'Requested'
  | 'Confirmed'
  | 'Rejected'
  | 'Cancelled'
  | 'Completed'
  | 'NoShow';

export interface Appointment {
  id: string;
  patientName: string;
  contactEmail: string | null;
  contactPhone: string | null;
  localDate: string;
  startTime: string;
  endTime: string;
  occupiedUntilLocalDate: string;
  occupiedUntilTime: string;
  durationMinutes: number;
  status: AppointmentStatus;
}

export interface AppointmentList {
  timeZoneId: string;
  appointments: Appointment[];
}

export interface AuthorizedPractitionerAgenda {
  userId: string;
  email: string | null;
}

export interface SecretaryAgendaAssignments {
  practitioners: AuthorizedPractitionerAgenda[];
  secretaries: Array<{
    userId: string;
    email: string | null;
    practitionerUserIds: string[];
  }>;
}

export interface PublicBookingLinkStatus {
  isEnabled: boolean;
  createdAtUtc: string | null;
}

export interface PublicBookingAvailability {
  organizationName: string;
  timeZoneId: string;
  appointmentDurationMinutes: number;
  days: Array<{
    localDate: string;
    slots: PublicBookingSlot[];
  }>;
}

export interface PublicBookingSlot {
  startTime: string;
  endTime: string;
}

export interface PublicAppointmentRequestResponse {
  localDate: string;
  startTime: string;
  status: 'Requested' | 'Confirmed';
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  prepareCsrf(): Observable<void> {
    return this.http.get<void>('/api/v1/auth/csrf');
  }

  signIn(email: string, password: string): Observable<void> {
    return this.http.post<void>('/api/v1/auth/login', { email, password });
  }

  getCurrentUser(): Observable<CurrentUser> {
    return this.http.get<CurrentUser>('/api/v1/auth/me');
  }

  signOut(): Observable<void> {
    return this.http.post<void>('/api/v1/auth/logout', {});
  }

  createInvitation(
    organizationId: string,
    email: string,
    role: 'Practitioner' | 'Secretary',
  ): Observable<CreatedInvitation> {
    return this.http.post<CreatedInvitation>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/invitations`,
      { email, role },
    );
  }

  revokeInvitation(organizationId: string, invitationId: string): Observable<void> {
    return this.http.delete<void>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/invitations/${encodeURIComponent(invitationId)}`,
    );
  }

  getOrganizationSettings(organizationId: string): Observable<OrganizationSettings> {
    return this.http.get<OrganizationSettings>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/settings`,
    );
  }

  updateOrganizationSettings(
    organizationId: string,
    appointmentConfirmationMode: OrganizationSettings['appointmentConfirmationMode'],
  ): Observable<OrganizationSettings> {
    return this.http.put<OrganizationSettings>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/settings`,
      { appointmentConfirmationMode },
    );
  }

  getWeeklySchedule(organizationId: string): Observable<WeeklySchedule> {
    return this.http.get<WeeklySchedule>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/schedule`,
    );
  }

  updateWeeklySchedule(
    organizationId: string,
    windows: WeeklyScheduleWindow[],
    appointmentDurationMinutes: number,
    preparationBufferMinutes: number,
  ): Observable<WeeklySchedule> {
    return this.http.put<WeeklySchedule>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/schedule`,
      { windows, appointmentDurationMinutes, preparationBufferMinutes },
    );
  }

  getScheduleExceptions(organizationId: string): Observable<ScheduleExceptions> {
    return this.http.get<ScheduleExceptions>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/schedule/exceptions`,
    );
  }

  addScheduleException(organizationId: string, localDate: string): Observable<void> {
    return this.http.post<void>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/schedule/exceptions`,
      { localDate },
    );
  }

  removeScheduleException(organizationId: string, localDate: string): Observable<void> {
    return this.http.delete<void>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/schedule/exceptions/${encodeURIComponent(localDate)}`,
    );
  }

  getSchedulePreview(
    organizationId: string,
    practitionerUserId?: string,
  ): Observable<SchedulePreview> {
    const query = practitionerUserId
      ? `?practitionerUserId=${encodeURIComponent(practitionerUserId)}`
      : '';
    return this.http.get<SchedulePreview>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/schedule/preview${query}`,
    );
  }

  getAppointments(
    organizationId: string,
    practitionerUserId?: string,
  ): Observable<AppointmentList> {
    const query = practitionerUserId
      ? `?practitionerUserId=${encodeURIComponent(practitionerUserId)}`
      : '';
    return this.http.get<AppointmentList>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/appointments${query}`,
    );
  }

  getAuthorizedPractitionerAgendas(
    organizationId: string,
  ): Observable<AuthorizedPractitionerAgenda[]> {
    return this.http.get<AuthorizedPractitionerAgenda[]>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/appointments/practitioners`,
    );
  }

  createAppointment(
    organizationId: string,
    idempotencyKey: string,
    appointment: {
      localDate: string;
      startTime: string;
      patientName: string;
      contactEmail: string | null;
      contactPhone: string | null;
      contactConsent: boolean;
      practitionerUserId?: string;
    },
  ): Observable<Appointment> {
    return this.http.post<Appointment>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/appointments`,
      appointment,
      { headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }) },
    );
  }

  updateAppointmentStatus(
    organizationId: string,
    appointmentId: string,
    status: AppointmentStatus,
  ): Observable<{ id: string; status: AppointmentStatus }> {
    return this.http.patch<{ id: string; status: AppointmentStatus }>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/appointments/${encodeURIComponent(appointmentId)}/status`,
      { status },
    );
  }

  getSecretaryAgendaAssignments(
    organizationId: string,
  ): Observable<SecretaryAgendaAssignments> {
    return this.http.get<SecretaryAgendaAssignments>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/secretary-agenda-assignments`,
    );
  }

  replaceSecretaryAgendaAssignments(
    organizationId: string,
    secretaryUserId: string,
    practitionerUserIds: string[],
  ): Observable<{ secretaryUserId: string; practitionerUserIds: string[] }> {
    return this.http.put<{ secretaryUserId: string; practitionerUserIds: string[] }>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/secretary-agenda-assignments/secretaries/${encodeURIComponent(secretaryUserId)}`,
      { practitionerUserIds },
    );
  }

  getPublicBookingLink(organizationId: string): Observable<PublicBookingLinkStatus> {
    return this.http.get<PublicBookingLinkStatus>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/public-booking-link`,
    );
  }

  rotatePublicBookingLink(organizationId: string): Observable<{ token: string }> {
    return this.http.put<{ token: string }>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/public-booking-link`,
      {},
    );
  }

  revokePublicBookingLink(organizationId: string): Observable<void> {
    return this.http.delete<void>(
      `/api/v1/organizations/${encodeURIComponent(organizationId)}/public-booking-link`,
    );
  }

  getPublicBookingAvailability(
    token: string,
    days = 14,
  ): Observable<PublicBookingAvailability> {
    return this.http.get<PublicBookingAvailability>(
      `/api/v1/public/booking/availability?days=${days}`,
      { headers: new HttpHeaders({ Authorization: `Bearer ${token}` }) },
    );
  }

  createPublicAppointmentRequest(
    token: string,
    idempotencyKey: string,
    request: {
      localDate: string;
      startTime: string;
      patientName: string;
      contactEmail: string | null;
      contactPhone: string | null;
      contactConsent: boolean;
    },
  ): Observable<PublicAppointmentRequestResponse> {
    return this.http.post<PublicAppointmentRequestResponse>(
      '/api/v1/public/booking/requests',
      request,
      {
        headers: new HttpHeaders({
          Authorization: `Bearer ${token}`,
          'Idempotency-Key': idempotencyKey,
        }),
      },
    );
  }
}
