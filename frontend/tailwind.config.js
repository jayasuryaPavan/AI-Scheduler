/** @type {import('tailwindcss').Config} */
export default {
  content: [
    './index.html',
    './src/**/*.{vue,js,ts,jsx,tsx}'
  ],
  theme: {
    extend: {
      fontFamily: {
        sans: ['Inter', 'system-ui', 'sans-serif'],
        mono: ['"JetBrains Mono"', '"Fira Code"', 'monospace']
      },
      animation: {
        'pulse-slow':  'pulse 3s ease-in-out infinite',
        'bounce-slow': 'bounce 1.5s infinite'
      },
      backdropBlur: {
        xs: '2px'
      }
    }
  },
  plugins: []
}
