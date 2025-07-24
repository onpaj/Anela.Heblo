/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./src/**/*.{js,jsx,ts,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        // Primary colors
        'primary': {
          white: '#FFFFFF',
          blue: '#2563EB',
        },
        'neutral': {
          slate: '#0F172A',
          gray: '#64748B',
        },
        // Secondary colors
        'secondary': {
          'blue-light': '#3B82F6',
          'blue-pale': '#EFF6FF',
        },
        // Accent colors
        'accent': {
          'blue-bright': '#1D4ED8',
        },
        // State colors
        'success': '#10B981',
        'warning': '#F59E0B',
        'error': '#EF4444',
        'info': '#06B6D4',
        // Functional colors
        'border-light': '#E2E8F0',
        'background-gray': '#F8FAFC',
        'disabled-gray': '#94A3B8',
        // Background colors
        'surface-white': '#FFFFFF',
        'background-neutral': '#F1F5F9',
        'background-subtle': '#F8FAFC',
      },
      boxShadow: {
        'soft': '0 1px 3px rgba(0, 0, 0, 0.05)',
        'hover': '0 4px 20px rgba(0, 0, 0, 0.08)',
      },
      ringColor: {
        'primary': 'rgba(37, 99, 235, 0.1)',
      },
      fontFamily: {
        'sans': ['Inter', '-apple-system', 'BlinkMacSystemFont', 'Segoe UI', 'Roboto', 'sans-serif'],
        'mono': ['JetBrains Mono', 'Fira Code', 'Consolas', 'monospace'],
      },
      transitionDuration: {
        '200': '200ms',
        '250': '250ms',
        '350': '350ms',
      },
      transitionTimingFunction: {
        'in-out': 'cubic-bezier(0.4, 0, 0.2, 1)',
        'out': 'cubic-bezier(0, 0, 0.2, 1)',
        'in': 'cubic-bezier(0.4, 0, 1, 1)',
      },
    },
  },
  plugins: [],
}