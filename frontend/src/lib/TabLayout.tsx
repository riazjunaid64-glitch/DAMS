import { type ReactNode } from "react";
import { Tabs } from "../components/ui";

export interface Tab {
  id: string;
  label: string;
  icon?: ReactNode;
  badge?: string | number;
  disabled?: boolean;
}

interface TabLayoutProps {
  tabs: Tab[];
  activeTab: string;
  onTabChange: (tabId: string) => void;
  ariaLabel?: string;
  children: ReactNode;
}

/** Tabs over their content, for existing screens; drawn by the shared Tabs. */
export default function TabLayout({ tabs, activeTab, onTabChange, ariaLabel = "Sections", children }: TabLayoutProps) {
  return (
    <div>
      <div className="sticky top-14 z-20 border-b border-line bg-page/90 px-4 py-3 backdrop-blur md:top-16 md:px-6 lg:px-8">
        <Tabs
          aria-label={ariaLabel}
          value={activeTab}
          onChange={onTabChange}
          items={tabs.map(({ badge, ...tab }) => ({ ...tab, count: badge }))}
        />
      </div>

      <div className="animate-fade-in min-h-[50vh]" role="tabpanel" aria-labelledby={`tab-${activeTab}`} key={activeTab}>
        {children}
      </div>
    </div>
  );
}
