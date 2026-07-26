import { api } from "../../api/api.ts";
import type {
  AudiencePreview,
  AuditRow,
  ComposeRequest,
  DeliveryPage,
  DeliveryRow,
  JobRow,
  NotificationCategory,
  NotificationPage,
  NotificationSettings,
  NotificationSummary,
  OpenResult,
  PreferenceRow,
  PushConfig,
  PushDevice,
  RuleRow,
  SuppressionRow,
  TemplatePreview,
  TemplateRow,
} from "./types.ts";

/** Shared error shaping so every notification screen reports failures the same way. */
export async function notificationRequest<T>(endpoint: string, options?: RequestInit): Promise<T> {
  const response = await api(endpoint, options);

  if (!response.ok) {
    const body = (await response.json().catch(() => ({}))) as { message?: string; title?: string };
    if (response.status === 401) throw new Error("Your session has expired. Sign in again.");
    if (response.status === 403) throw new Error(body.message ?? "You do not have permission to do that.");
    if (response.status === 404) throw new Error(body.message ?? "That notification could not be found.");
    if (response.status === 429) throw new Error("Too many attempts. Wait a moment and try again.");
    throw new Error(body.message ?? body.title ?? `Request failed (HTTP ${response.status}).`);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

const json = (method: string, body: unknown): RequestInit => ({ method, body: JSON.stringify(body) });

// ── Inbox ──────────────────────────────────────────────────────────────────────

export function fetchSummary(take = 10) {
  return notificationRequest<NotificationSummary>(`/api/notifications/summary?take=${take}`);
}

export function fetchNotifications(params: {
  category?: NotificationCategory | null;
  unreadOnly?: boolean;
  search?: string;
  page?: number;
  pageSize?: number;
}) {
  const query = new URLSearchParams();
  if (params.category) query.set("category", params.category);
  if (params.unreadOnly) query.set("unreadOnly", "true");
  if (params.search) query.set("search", params.search);
  query.set("page", String(params.page ?? 1));
  query.set("pageSize", String(params.pageSize ?? 20));

  return notificationRequest<NotificationPage>(`/api/notifications?${query.toString()}`);
}

export function markRead(id: number) {
  return notificationRequest<void>(`/api/notifications/${id}/read`, { method: "POST" });
}

export function markAllRead(category?: NotificationCategory | null) {
  const suffix = category ? `?category=${category}` : "";
  return notificationRequest<{ updated: number }>(`/api/notifications/read-all${suffix}`, { method: "POST" });
}

export function archiveNotification(id: number) {
  return notificationRequest<void>(`/api/notifications/${id}/archive`, { method: "POST" });
}

/** Marks read and asks the server where this should open, re-checking access now. */
export function openNotification(id: number) {
  return notificationRequest<OpenResult>(`/api/notifications/${id}/open`, { method: "POST" });
}

// ── Preferences and push ───────────────────────────────────────────────────────

export function fetchPreferences() {
  return notificationRequest<PreferenceRow[]>("/api/notifications/preferences");
}

export function savePreferences(items: { category: string; emailEnabled: boolean; pushEnabled: boolean }[]) {
  return notificationRequest<PreferenceRow[]>("/api/notifications/preferences", json("PUT", { items }));
}

export function fetchPushConfig() {
  return notificationRequest<PushConfig>("/api/notifications/push/config");
}

export function fetchPushDevices() {
  return notificationRequest<PushDevice[]>("/api/notifications/push/devices");
}

export function registerPushSubscription(payload: {
  endpoint: string;
  p256dh: string;
  auth: string;
  deviceLabel?: string;
}) {
  return notificationRequest<void>("/api/notifications/push/subscribe", json("POST", payload));
}

export function unregisterPushSubscription(endpoint: string) {
  return notificationRequest<void>("/api/notifications/push/unsubscribe", json("POST", { endpoint }));
}

export function unregisterAllPushSubscriptions() {
  return notificationRequest<{ removed: number }>("/api/notifications/push/unsubscribe-all", { method: "POST" });
}

export function sendTestPush() {
  return notificationRequest<{ delivered: number }>("/api/notifications/push/test", { method: "POST" });
}

// ── Admin ──────────────────────────────────────────────────────────────────────

export function fetchSettings() {
  return notificationRequest<NotificationSettings>("/api/notification-admin/settings");
}

export function saveSettings(values: Record<string, string | null>) {
  return notificationRequest<NotificationSettings>("/api/notification-admin/settings", json("PUT", { values }));
}

export function sendTestEmail(recipient: string | null) {
  return notificationRequest<{ message: string }>(
    "/api/notification-admin/settings/test-email",
    json("POST", { recipient })
  );
}

export function generatePushKeys() {
  return notificationRequest<{ publicKey: string }>("/api/notification-admin/settings/push-keys", { method: "POST" });
}

export function fetchTemplates() {
  return notificationRequest<TemplateRow[]>("/api/notification-admin/templates");
}

export function saveTemplate(type: string, channel: string, payload: Partial<TemplateRow>) {
  return notificationRequest<TemplateRow>(
    `/api/notification-admin/templates/${type}/${channel}`,
    json("PUT", payload)
  );
}

export function previewTemplate(type: string, draft: Partial<TemplateRow> | null) {
  return notificationRequest<TemplatePreview>(
    `/api/notification-admin/templates/${type}/preview`,
    json("POST", draft)
  );
}

export function fetchRules() {
  return notificationRequest<RuleRow[]>("/api/notification-admin/rules");
}

export function saveRule(type: string, payload: Partial<RuleRow> & { confirmEssentialChange?: boolean }) {
  return notificationRequest<RuleRow>(`/api/notification-admin/rules/${type}`, json("PUT", payload));
}

export function previewAudience(request: ComposeRequest) {
  return notificationRequest<AudiencePreview>("/api/notification-admin/compose/preview", json("POST", request));
}

export function compose(request: ComposeRequest) {
  return notificationRequest<JobRow>("/api/notification-admin/compose", json("POST", request));
}

export function fetchJobs(take = 50) {
  return notificationRequest<JobRow[]>(`/api/notification-admin/jobs?take=${take}`);
}

export function cancelJob(id: number) {
  return notificationRequest<JobRow>(`/api/notification-admin/jobs/${id}/cancel`, { method: "POST" });
}

export function fetchDeliveries(params: {
  channel?: string | null;
  status?: string | null;
  failuresOnly?: boolean;
  search?: string;
  page?: number;
  pageSize?: number;
}) {
  const query = new URLSearchParams();
  if (params.channel) query.set("channel", params.channel);
  if (params.status) query.set("status", params.status);
  if (params.failuresOnly) query.set("failuresOnly", "true");
  if (params.search) query.set("search", params.search);
  query.set("page", String(params.page ?? 1));
  query.set("pageSize", String(params.pageSize ?? 25));

  return notificationRequest<DeliveryPage>(`/api/notification-admin/deliveries?${query.toString()}`);
}

export function retryDelivery(id: number) {
  return notificationRequest<DeliveryRow>(`/api/notification-admin/deliveries/${id}/retry`, { method: "POST" });
}

export function fetchSuppressions() {
  return notificationRequest<SuppressionRow[]>("/api/notification-admin/suppressions");
}

export function removeSuppression(id: number) {
  return notificationRequest<void>(`/api/notification-admin/suppressions/${id}`, { method: "DELETE" });
}

export function fetchAudit(take = 100) {
  return notificationRequest<AuditRow[]>(`/api/notification-admin/audit?take=${take}`);
}
