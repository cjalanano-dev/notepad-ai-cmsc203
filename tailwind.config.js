/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    './Pages/**/*.{razor,html}',
    './Layout/**/*.{razor,html}',
    './Components/**/*.{razor,html}',
    './App.razor',
    './wwwroot/index.html'
  ],
  darkMode: ['class', '[data-theme="dark"]'],
  theme: {
    extend: {
      fontFamily: {
        sans: ['Plus Jakarta Sans', '-apple-system', 'BlinkMacSystemFont', 'Segoe UI', 'Roboto', 'sans-serif'],
        mono: ['JetBrains Mono', 'monospace'],
      },
      colors: {
        canvas: 'var(--canvas)',
        surface: 'var(--surface)',
        'surface-raised': 'var(--surface-raised)',
        'surface-hover': 'var(--surface-hover)',
        line: 'var(--line)',
        'line-strong': 'var(--line-strong)',
        ink: 'var(--ink)',
        'ink-secondary': 'var(--ink-secondary)',
        'ink-muted': 'var(--ink-muted)',
        accent: 'var(--accent)',
        'accent-hover': 'var(--accent-hover)',
        'accent-soft': 'var(--accent-soft)',
        'accent-border': 'var(--accent-border)',
        danger: 'var(--danger)',
        'danger-soft': 'var(--danger-soft)',
        success: 'var(--success)',
        'success-soft': 'var(--success-soft)',
      },
      boxShadow: {
        'card': '0 4px 20px -2px rgba(0, 0, 0, 0.45)',
        'card-hover': '0 12px 36px -4px rgba(0, 0, 0, 0.65), 0 0 20px -3px rgba(56, 189, 248, 0.15)',
        'palette': '0 24px 60px rgba(0, 0, 0, 0.6)',
      }
    },
  },
  plugins: [],
}
