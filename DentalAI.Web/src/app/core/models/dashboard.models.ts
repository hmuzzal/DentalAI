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
  /** Completed-appointment revenue for the last 30 days. Days with no revenue are omitted. */
  revenueByDay: RevenuePoint[];
  /** Completed-appointment revenue for the last 12 months. */
  revenueByMonth: RevenuePoint[];
}
