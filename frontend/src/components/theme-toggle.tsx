"use client";

function toggleTheme() {
  const root = document.documentElement;
  const isDark = root.classList.toggle("dark");
  root.style.colorScheme = isDark ? "dark" : "light";

  try {
    localStorage.setItem("argus-theme", isDark ? "dark" : "light");
  } catch {
    // Storage can be unavailable in private or restricted contexts. The class
    // change still applies for this page load.
  }
}

export default function ThemeToggle() {
  return (
    <button
      aria-label="Switch between light and dark theme"
      className="fixed right-4 bottom-4 z-50 inline-flex h-11 w-11 items-center justify-center rounded-full border border-line bg-surface text-ink shadow-lg transition hover:bg-elevated focus-visible:ring-2 focus-visible:ring-accent-text focus-visible:ring-offset-2 focus-visible:ring-offset-page"
      onClick={toggleTheme}
      title="Switch between light and dark theme"
      type="button"
    >
      <svg
        aria-hidden="true"
        className="hidden h-5 w-5 dark:block"
        fill="none"
        stroke="currentColor"
        strokeLinecap="round"
        strokeWidth="2"
        viewBox="0 0 24 24"
      >
        <circle cx="12" cy="12" r="4" />
        <path d="M12 2v2M12 20v2M4.93 4.93l1.42 1.42M17.66 17.66l1.41 1.41M2 12h2M20 12h2M4.93 19.07l1.42-1.42M17.66 6.34l1.41-1.41" />
      </svg>
      <svg
        aria-hidden="true"
        className="block h-5 w-5 dark:hidden"
        fill="none"
        stroke="currentColor"
        strokeLinecap="round"
        strokeWidth="2"
        viewBox="0 0 24 24"
      >
        <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79Z" />
      </svg>
    </button>
  );
}
