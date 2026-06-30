import React from "react";
import { Sun, Moon } from "lucide-react";
import { useTheme } from "../../contexts/ThemeContext";

interface ThemeToggleProps {
  className?: string;
}

export const ThemeToggle: React.FC<ThemeToggleProps> = ({ className }) => {
  const { theme, toggle } = useTheme();
  const isDark = theme === "dark";

  return (
    <button
      type="button"
      onClick={toggle}
      title={isDark ? "Světlý režim" : "Tmavý režim"}
      aria-label={isDark ? "Přepnout na světlý režim" : "Přepnout na tmavý režim"}
      className={
        "p-2 rounded-md text-neutral-gray hover:text-primary-blue hover:bg-secondary-blue-pale " +
        "dark:text-graphite-muted dark:hover:text-graphite-accent dark:hover:bg-white/5 " +
        "focus:outline-none focus:ring-2 focus:ring-primary transition-colors" +
        (className ? ` ${className}` : "")
      }
    >
      {isDark ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
    </button>
  );
};

export default ThemeToggle;
