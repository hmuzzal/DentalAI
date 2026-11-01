/** GET /platform/overview: counts only. Platform admins never see patient or appointment details. */
export interface PlatformOverview {
  clinics: number;
  activeUsers: number;
  doctors: number;
  patients: number;
  appointmentsLast30Days: number;
  upcomingAppointments: number;
  clinicUsage: ClinicUsage[];
}

export interface ClinicUsage {
  clinicId: string;
  name: string;
  country: string;
  timeZone: string;
  createdDate: string;
  activeUsers: number;
  doctors: number;
  patients: number;
  appointmentsLast30Days: number;
  upcomingAppointments: number;
}
