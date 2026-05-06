import React, { ReactNode } from "react";

interface ToggleOption {
  value: string;
  icon: ReactNode;
  label: string;
}

interface PhotoViewToggleProps {
  options: ToggleOption[];
  value: string;
  onChange: (value: string) => void;
}

function buttonClass(isActive: boolean, isFirst: boolean, isLast: boolean): string {
  const rounded = isFirst && isLast ? "rounded" : isFirst ? "rounded-l" : isLast ? "rounded-r" : "";
  const active = isActive
    ? "bg-primary-blue text-white"
    : "bg-white text-gray-600 border border-gray-300 hover:bg-gray-50";
  return `w-8 h-8 flex items-center justify-center ${rounded} ${active}`;
}

export default function PhotoViewToggle({ options, value, onChange }: PhotoViewToggleProps) {
  return (
    <div className="flex">
      {options.map((option, index) => {
        const isActive = option.value === value;
        const isFirst = index === 0;
        const isLast = index === options.length - 1;
        return (
          <button
            key={option.value}
            type="button"
            title={option.label}
            aria-pressed={isActive}
            className={buttonClass(isActive, isFirst, isLast)}
            onClick={() => onChange(option.value)}
          >
            {option.icon}
          </button>
        );
      })}
    </div>
  );
}
