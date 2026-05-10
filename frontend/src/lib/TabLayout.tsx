import { type ReactNode, useState, useRef, useEffect } from "react";

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
  children: ReactNode;
}

export default function TabLayout({ tabs, activeTab, onTabChange, children }: TabLayoutProps) {
  const [indicatorStyle, setIndicatorStyle] = useState<{ left: number; width: number }>({ left: 0, width: 0 });
  const tabRefs = useRef<Map<string, HTMLButtonElement>>(new Map());
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const activeEl = tabRefs.current.get(activeTab);
    const container = containerRef.current;
    if (activeEl && container) {
      const containerRect = container.getBoundingClientRect();
      const elRect = activeEl.getBoundingClientRect();
      setIndicatorStyle({
        left: elRect.left - containerRect.left,
        width: elRect.width,
      });
    }
  }, [activeTab]);

  return (
    <div>
      {/* Tab Bar */}
      <div className="tab-bar-wrapper">
        <div className="tab-bar" ref={containerRef}>
          {tabs.map((tab) => {
            const isActive = activeTab === tab.id;
            return (
              <button
                key={tab.id}
                ref={(el) => {
                  if (el) tabRefs.current.set(tab.id, el);
                }}
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
          {/* Sliding indicator */}
          <div
            className="tab-indicator"
            style={{
              transform: `translateX(${indicatorStyle.left}px)`,
              width: `${indicatorStyle.width}px`,
            }}
          />
        </div>
      </div>

      {/* Tab Content */}
      <div className="tab-content animate-fade-in" key={activeTab}>
        {children}
      </div>
    </div>
  );
}
