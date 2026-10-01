import type { ReactNode } from "react";

/* Inline SVG icons on one 24px stroked grid: nothing to install, and each glyph ships with the
   component that draws it. Decorative by default — the control around an icon carries the label. */

export type IconProps = { size?: number; className?: string; strokeWidth?: number };

function icon(paths: ReactNode, defaultStroke = 1.9) {
  return function Icon({ size = 18, className, strokeWidth = defaultStroke }: IconProps) {
    return (
      <svg
        width={size}
        height={size}
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth={strokeWidth}
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
        focusable="false"
        className={className}
      >
        {paths}
      </svg>
    );
  };
}

export const IconSearch = icon(<><circle cx="11" cy="11" r="7" /><path d="m20 20-3.2-3.2" /></>, 2);
export const IconClose = icon(<path d="M18 6 6 18M6 6l12 12" />, 2);
export const IconChevronDown = icon(<path d="m6 9 6 6 6-6" />, 2.2);
export const IconChevronLeft = icon(<path d="m15 18-6-6 6-6" />, 2.2);
export const IconChevronRight = icon(<path d="m9 18 6-6-6-6" />, 2.2);
export const IconArrowRight = icon(<path d="M5 12h14M13 6l6 6-6 6" />, 2.2);
export const IconArrowUpRight = icon(<><path d="M7 7h10v10" /><path d="M7 17 17 7" /></>, 2.2);
export const IconCheck = icon(<path d="m5 12 4.5 4.5L19 7" />, 2.2);
export const IconPlus = icon(<path d="M12 5v14M5 12h14" />, 2.2);
export const IconFilter = icon(<path d="M4 7h16M7 12h10M10 17h4" />, 2);
export const IconCalendar = icon(<><rect x="3.5" y="5" width="17" height="15.5" rx="2" /><path d="M16 3v4M8 3v4M3.5 10h17" /></>);
export const IconClock = icon(<><circle cx="12" cy="12" r="8.5" /><path d="M12 7.5V12l3 2" /></>);
export const IconMore = icon(<><circle cx="5" cy="12" r="1.3" fill="currentColor" /><circle cx="12" cy="12" r="1.3" fill="currentColor" /><circle cx="19" cy="12" r="1.3" fill="currentColor" /></>, 1.2);
export const IconImage = icon(<><rect x="3" y="3" width="18" height="18" rx="2" /><circle cx="8.5" cy="8.5" r="1.5" /><path d="m21 15-5-5L5 21" /></>, 1.8);
export const IconUpload = icon(<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M17 8l-5-5-5 5M12 3v12" />, 2.1);
export const IconBell = icon(<path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9M10.3 21a1.94 1.94 0 0 0 3.4 0" />);
export const IconLogout = icon(<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9" />);
export const IconMenu = icon(<path d="M3 6h18M3 12h18M3 18h18" />, 2);
export const IconCircle = icon(<circle cx="12" cy="12" r="5" />, 1.8);
export const IconStar = icon(<path d="m12 3 2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1-4.4-4.3 6.1-.9L12 3Z" />, 1.8);
export const IconTrash = icon(<path d="M4 7h16M10 11v6M14 11v6M5 7l1 13a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1l1-13M9 7V4h6v3" />, 1.8);
export const IconAlert = icon(<><circle cx="12" cy="12" r="9" /><path d="M12 8v4M12 16h.01" /></>, 2);

/* Navigation */
export const IconHome = icon(<><path d="m3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" /><path d="M9 22V12h6v10" /></>, 1.8);
export const IconBuilding = icon(<><rect x="4" y="2" width="16" height="20" rx="1.5" /><path d="M9 7h.01M15 7h.01M9 12h.01M15 12h.01M9 17h.01M15 17h.01" /></>, 1.8);
export const IconInfo = icon(<><circle cx="12" cy="12" r="10" /><path d="M12 16v-4M12 8h.01" /></>, 1.8);
export const IconMail = icon(<><rect x="2" y="4" width="20" height="16" rx="2" /><path d="m22 6-10 7L2 6" /></>, 1.8);
export const IconTarget = icon(<><circle cx="12" cy="12" r="9" /><circle cx="12" cy="12" r="5" /><circle cx="12" cy="12" r="1" /></>, 1.8);
export const IconInbox = icon(<><path d="M22 12h-6l-2 3h-4l-2-3H2" /><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11Z" /></>, 1.8);
export const IconCalendarCheck = icon(<><rect x="3" y="4" width="18" height="18" rx="2" /><path d="M16 2v4M8 2v4M3 10h18M9 16l2 2 4-4" /></>, 1.8);
export const IconUsers = icon(<><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75" /></>, 1.8);
export const IconBadge = icon(<><rect x="3" y="4" width="18" height="16" rx="2" /><path d="M9 4v2a2 2 0 0 0 2 2h2a2 2 0 0 0 2-2V4M8 14h8M8 18h5" /></>, 1.8);
export const IconWallet = icon(<><path d="M21 12V7H5a2 2 0 0 1 0-4h14v4" /><path d="M3 5v14a2 2 0 0 0 2 2h16v-5" /><path d="M18 12a2 2 0 0 0 0 4h4v-4Z" /></>, 1.8);
export const IconFolder = icon(<path d="M4 20h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13c0 1.1.9 2 2 2Z" />, 1.8);
export const IconFile = icon(<><path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z" /><path d="M14 3v6h6M8 13h8M8 17h5" /></>, 1.8);
export const IconSettings = icon(<><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1Z" /></>, 1.7);
export const IconLayers = icon(<path d="m12 3 9 5-9 5-9-5 9-5ZM3 13l9 5 9-5" />, 1.8);
export const IconGrid = icon(<><rect x="3" y="3" width="7" height="7" rx="1.5" /><rect x="14" y="3" width="7" height="7" rx="1.5" /><rect x="3" y="14" width="7" height="7" rx="1.5" /><rect x="14" y="14" width="7" height="7" rx="1.5" /></>, 1.8);

/* Lead work */
export const IconPhone = icon(<path d="M22 16.9v3a2 2 0 0 1-2.18 2 19.8 19.8 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.18 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.1 9.9a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.9.34 1.85.57 2.81.7A2 2 0 0 1 22 16.9Z" />, 1.8);
export const IconWhatsapp = icon(<path d="M7.9 20A9 9 0 1 0 4 16.1L2 22Z" />, 1.8);
export const IconPrinter = icon(<><path d="M6 9V2h12v7M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2" /><rect x="6" y="14" width="12" height="8" /></>, 1.8);
export const IconMapPin = icon(<><path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0Z" /><circle cx="12" cy="10" r="3" /></>, 1.8);
export const IconBlock = icon(<><circle cx="12" cy="12" r="9" /><path d="m5.6 5.6 12.8 12.8" /></>, 1.8);
export const IconIdCard = icon(<><rect x="2.5" y="5" width="19" height="14" rx="2" /><circle cx="8.5" cy="11" r="2" /><path d="M5.5 16c.6-1.5 1.7-2.2 3-2.2s2.4.7 3 2.2M14.5 10h4M14.5 13.5h3" /></>, 1.7);
export const IconDownload = icon(<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M7 10l5 5 5-5M12 15V3" />, 2.1);
export const IconArrowUp = icon(<path d="M12 19V5M5 12l7-7 7 7" />, 2.2);
export const IconArrowDown = icon(<path d="M12 5v14M19 12l-7 7-7-7" />, 2.2);
export const IconPaperclip = icon(<path d="M21.4 11.1 12.3 20.2a5 5 0 0 1-7.1-7.1l8.5-8.5a3.5 3.5 0 0 1 5 5l-8.5 8.5a2 2 0 0 1-2.9-2.9l7.8-7.8" />, 1.8);
export const IconPencil = icon(<path d="M17 3a2.85 2.85 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5Z" />, 1.8);
export const IconUserPlus = icon(<><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M19 8v6M22 11h-6" /></>, 1.8);
export const IconLink = icon(<path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71" />, 1.8);
