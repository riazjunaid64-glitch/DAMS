import { type ReactNode } from "react";

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

export default function TabLayout({ tabs, activeTab, onTabChange, ariaLabel = "Sections", children }: TabLayoutProps) {
  return (
    <div>
      <div className="tab-bar-wrapper">
        <div className="tab-bar" role="tablist" aria-label={ariaLabel}>
          {tabs.map((tab) => {
            const isActive = activeTab === tab.id;
            return (
              <button
                key={tab.id}
                onClick={() => !tab.disabled && onTabChange(tab.id)}
                disabled={tab.disabled}
                className={`tab-item ${isActive ? "tab-item--active" : ""} ${tab.disabled ? "tab-item--disabled" : ""}`}
                role="tab"
                aria-selected={isActive}
                id={`tab-${tab.id}`}
              >
                {tab.icon && <span className="tab-item__icon">{tab.icon}</span>}
                <span>{tab.label}</span>
                {tab.badge !== undefined && (
                  <span className={`tab-item__badge ${isActive ? "tab-item__badge--active" : ""}`}>
                    {tab.badge}
                  </span>
                )}
              </button>
            );
          })}
        </div>
      </div>

      <div className="tab-content animate-fade-in" key={activeTab}>
        {children}
      </div>
    </div>
  );
}
