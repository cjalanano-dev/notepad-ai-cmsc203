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
      borderRadius: {
        '2xl': '1rem',
        '3xl': '1.5rem',
        '4xl': '2rem',
      },
      colors: {
        canvas: 'var(--canvas)',
        surface: 'var(--surface)',
        'surface-raised': 'var(--surface-raised)',
        'surface-hover': 'var(--surface-hover)',
        'surface-active': 'var(--surface-active)',
        line: 'var(--line)',
        'line-strong': 'var(--line-strong)',
        'line-subtle': 'var(--line-subtle)',
        ink: 'var(--ink)',
        'ink-secondary': 'var(--ink-secondary)',
        'ink-muted': 'var(--ink-muted)',
        'ink-faint': 'var(--ink-faint)',
        accent: 'var(--accent)',
        'accent-hover': 'var(--accent-hover)',
        'accent-soft': 'var(--accent-soft)',
        'accent-border': 'var(--accent-border)',
        'accent-surface': 'var(--accent-surface)',
        danger: 'var(--danger)',
        'danger-soft': 'var(--danger-soft)',
        success: 'var(--success)',
        'success-soft': 'var(--success-soft)',
      },
      boxShadow: {
        'card': '0 4px 20px -2px rgba(0, 0, 0, 0.45)',
        'card-hover': '0 12px 36px -4px rgba(0, 0, 0, 0.65), 0 0 16px -3px rgba(59, 130, 246, 0.12)',
        'float': '0 16px 48px -8px rgba(0, 0, 0, 0.85), 0 0 20px -4px rgba(59, 130, 246, 0.15)',
        'glow': '0 0 24px -2px rgba(59, 130, 246, 0.25)',
        'palette': '0 24px 60px rgba(0, 0, 0, 0.6)',
      }
    },
  },
  plugins: [],
}
