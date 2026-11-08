export interface DashboardStats {
  todayAppointments: number;
  upcomingAppointments: number;
  totalDoctors: number;
  totalPatients: number;
  totalServices: number;
}

export interface RevenuePoint {
  /** "yyyy-MM-dd" for daily points, "yyyy-MM" for monthly points. */
  period: string;
  revenue: number;
}

export interface SchedulingDashboard {
  todayAppointments: number;
  upcomingAppointments: number;
  completedToday: number;
  cancelledToday: number;
  noShowsToday: number;
  /** Payments received over the last 30 days; days with none are omitted. Null for receptionists (admins only). */
  revenueByDay: RevenuePoint[] | null;
  /** Payments received over the last 12 months. Null for receptionists. */
  revenueByMonth: RevenuePoint[] | null;
}
