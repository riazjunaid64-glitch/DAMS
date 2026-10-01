/*
 * DAMS shared component library (light theme). Every screen is built from these; colours, type
 * and radii come from the tokens in src/index.css. Preview: /dev/components.
 */

// 01 Theme helpers
export { cx } from "./cx.ts";
export { useIsPhone, useMediaQuery, PHONE_QUERY } from "./useMediaQuery.ts";
export * from "./icons.tsx";
export type { Option } from "./types.ts";

// 02 Button
export { Button, Spinner, type ButtonProps, type ButtonSize, type ButtonVariant } from "./Button.tsx";

// 03 Dropdown
export { Dropdown, type DropdownOption, type DropdownProps } from "./Dropdown.tsx";

// 04 Search bar
export { SearchBar, type SearchBarProps } from "./SearchBar.tsx";
export { FilterBar, type FilterBarProps, type FilterDef, type FilterValues } from "./FilterBar.tsx";

// 05 Pagination
export { Pagination, LoadMore, type PaginationProps, type LoadMoreProps } from "./Pagination.tsx";
export { DEFAULT_PAGE_SIZE, pageItems, pageRange } from "./pageItems.ts";

// 06 Form fields
export { FieldShell, type FieldBaseProps } from "./FieldShell.tsx";
export { TextField, NumberField, type TextFieldProps, type NumberFieldProps } from "./TextField.tsx";
export { DatePicker, type DatePickerProps } from "./DatePicker.tsx";
export { TimePicker, type TimePickerProps } from "./TimePicker.tsx";
export { AttachProof, type AttachProofFile, type AttachProofProps } from "./AttachProof.tsx";
export { PROOF_MAX_BYTES, PROOF_TOO_LARGE, PROOF_WRONG_TYPE, formatFileSize, proofFileError, fileRuleError, type FileRule } from "./proofFile.ts";
export { TextArea, type TextAreaProps } from "./TextArea.tsx";
export { ChoiceChips, RadioGroup, Checkbox, Toggle } from "./Choice.tsx";

// 07 Status badge
export { StatusBadge, type StatusBadgeProps } from "./StatusBadge.tsx";
export { statusTone, statusLabel, type StatusTone } from "./statusTone.ts";

// 08 Stat / info card
export { StatCard, InfoCard, StatSummary, type StatCardProps, type InfoCardProps, type StatSummaryProps } from "./StatCard.tsx";

// 09 Card, list card, table
export { Card, KeyValueGrid, ListCard, type CardProps, type KeyValueItem, type ListCardProps } from "./Card.tsx";
export { DataTable, type DataTableColumn, type DataTableProps } from "./DataTable.tsx";

// 10 Tabs
export { Tabs, type TabItem, type TabsProps } from "./Tabs.tsx";

// 11 Actions menu
export { ActionsMenu, type ActionItem, type ActionsMenuProps } from "./ActionsMenu.tsx";

// 12 Popup
export { Modal, ConfirmDialog, type ModalProps, type ConfirmDialogProps } from "./Modal.tsx";
export type { DialogAction } from "./DialogPanel.tsx";
export { Overlay, type OverlayPlacement } from "./Overlay.tsx";
export { Portal } from "./Portal.tsx";

// 13 Empty state, notice, toast, photos
export { EmptyState, type EmptyStateProps } from "./EmptyState.tsx";
export { Notice, type NoticeProps, type NoticeTone } from "./Notice.tsx";
export { ToastProvider } from "./Toast.tsx";
export { useToast, type ToastApi, type ToastKind } from "./toastContext.ts";
export { PhotoSlider, type Photo, type PhotoSliderProps } from "./PhotoSlider.tsx";
export { PhotoGallery, type PhotoGalleryProps } from "./PhotoGallery.tsx";

// 14 App layout + phone pieces
export { Sidebar, TopBar, PhoneTopBar, BottomNav, type Crumb, type ShellUser, type SidebarProps, type PhoneTopBarProps } from "./AppShell.tsx";
export { PageHeader, type PageHeaderProps } from "./PageHeader.tsx";
export { BottomSheet, type BottomSheetProps } from "./BottomSheet.tsx";
export { Avatar } from "./Avatar.tsx";
export { isNavActive, activeNavItem, type NavGroup, type NavItem } from "./navigation.ts";
