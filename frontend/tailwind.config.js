/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./src/**/*.{js,jsx,ts,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        // Custom colors based on UI design document
        'sidebar': 'white',
        'base': '#f9fafb', // gray-50
        'accent': '#4f46e5', // indigo-600
        'success': '#10b981', // emerald-500
        'error': '#f43f5e', // rose-500
        'cta': '#fb7185', // rose-400
        'hover': '#f3f4f6', // gray-100
        'icon-muted': '#9ca3af', // gray-400
      },
      fontFamily: {
        'sans': ['-apple-system', 'BlinkMacSystemFont', 'Segoe UI', 'Roboto', 'Oxygen', 'Ubuntu', 'Cantarell', 'Open Sans', 'Helvetica Neue', 'sans-serif'],
      },
    },
  },
  plugins: [],
}