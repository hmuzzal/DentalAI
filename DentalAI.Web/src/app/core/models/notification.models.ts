export type MessageChannel = 'Email' | 'Sms';
export type MessageKind = 'BookingConfirmation' | 'Reminder' | 'Rescheduled' | 'Cancelled' | 'StaffInvite' | 'Test' | 'Reply';
export type MessageStatus = 'Pending' | 'Sending' | 'Sent' | 'Failed' | 'Skipped' | 'Cancelled';

/** Times are "HH:mm:ss" in the clinic's time zone. */
export interface NotificationSettings {
  emailEnabled: boolean;
  smsEnabled: boolean;
  confirmationsEnabled: boolean;
  remindersEnabled: boolean;
  firstReminderHours: number;
  /** 0 = no second reminder. */
  secondReminderHours: number;
  quietHoursStart: string;
  quietHoursEnd: string;
  /** False when no email provider is set up on the server: messages are only logged. */
  emailLive: boolean;
  smsLive: boolean;
}

export type NotificationSettingsRequest = Omit<NotificationSettings, 'emailLive' | 'smsLive'>;

export interface OutboundMessage {
  id: string;
  channel: MessageChannel;
  kind: MessageKind;
  to: string;
  subject: string | null;
  body: string;
  status: MessageStatus;
  patientId: string | null;
  patientName: string | null;
  appointmentId: string | null;
  reminderHours: number | null;
  createdAtUtc: string;
  notBeforeUtc: string;
  sentAtUtc: string | null;
  attempts: number;
  lastError: string | null;
}

export interface MessageListQuery {
  status?: MessageStatus | null;
  channel?: MessageChannel | null;
  patientId?: string | null;
  appointmentId?: string | null;
  page: number;
  pageSize: number;
}

export const MESSAGE_STATUS: Record<MessageStatus, { label: string; color: string }> = {
  Pending: { label: 'Scheduled', color: 'blue' },
  Sending: { label: 'Sending', color: 'processing' },
  Sent: { label: 'Sent', color: 'green' },
  Failed: { label: 'Failed', color: 'red' },
  Skipped: { label: 'Skipped', color: 'default' },
  Cancelled: { label: 'Withdrawn', color: 'default' }
};

export const MESSAGE_KIND_LABELS: Record<MessageKind, string> = {
  BookingConfirmation: 'Booking confirmation',
  Reminder: 'Reminder',
  Rescheduled: 'Rescheduled notice',
  Cancelled: 'Cancellation notice',
  StaffInvite: 'Staff invite',
  Test: 'Test message',
  Reply: 'Reply'
};
