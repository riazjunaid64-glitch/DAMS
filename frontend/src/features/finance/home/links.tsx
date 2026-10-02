import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { BottomSheet, Button } from "../../../components/ui";
import {
  IconBadge,
  IconFile,
  IconPlus,
  IconSettings,
  IconUsers,
  IconWallet,
} from "../../../components/ui/icons.tsx";
import { TAX_TO_FBR_PATH } from "./paths.ts";

const FINANCE_LINKS: { label: string; to: string; icon: ReactNode }[] = [
  { label: "Financial reports", to: "/finance/reports", icon: <IconFile size={18} /> },
  { label: "Capital partners", to: "/finance/partners", icon: <IconUsers size={18} /> },
  { label: "Loans", to: "/finance/loans", icon: <IconWallet size={18} /> },
  { label: "Cash with staff", to: "/finance/staff-cash", icon: <IconWallet size={18} /> },
  { label: "Manage accounts", to: "/finance/accounts", icon: <IconSettings size={18} /> },
  { label: "Tax to FBR", to: TAX_TO_FBR_PATH, icon: <IconBadge size={18} /> },
  { label: "Settings", to: "/finance/settings", icon: <IconSettings size={18} /> },
  { label: "Commissions & rebates", to: "/finance/commissions-rebates", icon: <IconBadge size={18} /> },
];

const rowClass = "flex h-12 w-full cursor-pointer items-center gap-3 border-0 bg-transparent px-0 text-left text-body font-extrabold text-ink focus-visible:outline-2 focus-visible:outline-primary";

export function FinanceMoreSheet({
  open,
  onClose,
  onAddRevenue,
  onAddAsset,
}: {
  open: boolean;
  onClose: () => void;
  onAddRevenue: () => void;
  onAddAsset: () => void;
}) {
  const closeThen = (action: () => void) => {
    onClose();
    action();
  };
  return (
    <BottomSheet open={open} onClose={onClose} title="More actions" footer={null}>
      <ul className="m-0 flex list-none flex-col p-0">
        <li>
          <button type="button" className={rowClass} onClick={() => closeThen(onAddRevenue)}>
            <IconPlus size={18} className="text-ink-2" />
            Add revenue
          </button>
        </li>
        <li>
          <button type="button" className={rowClass} onClick={() => closeThen(onAddAsset)}>
            <IconPlus size={18} className="text-ink-2" />
            Add fixed asset
          </button>
        </li>
        <li className="my-1 h-px bg-line" aria-hidden="true" />
        {FINANCE_LINKS.map((link) => (
          <li key={link.label}>
            <Link to={link.to} className={`${rowClass} no-underline`} onClick={onClose}>
              <span className="text-ink-2">{link.icon}</span>
              {link.label}
            </Link>
          </li>
        ))}
      </ul>
    </BottomSheet>
  );
}

export function FinanceLinkRow() {
  return (
    <nav aria-label="Finance" className="flex flex-wrap gap-x-4 gap-y-2">
      {FINANCE_LINKS.map((link) => (
        <Link key={link.label} to={link.to} className="text-small font-bold text-primary no-underline hover:underline">
          {link.label}
        </Link>
      ))}
    </nav>
  );
}

export function FinanceAddButtons({
  onRevenue,
  onAsset,
  onExpense,
}: {
  onRevenue: () => void;
  onAsset: () => void;
  onExpense: () => void;
}) {
  return (
    <>
      <Button variant="outline" icon={<IconPlus size={16} />} onClick={onRevenue}>Add revenue</Button>
      <Button variant="outline" icon={<IconPlus size={16} />} onClick={onAsset}>Add fixed asset</Button>
      <Button icon={<IconPlus size={16} />} onClick={onExpense}>Add expense</Button>
    </>
  );
}
