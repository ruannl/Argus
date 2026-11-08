"use client";

// Runs before first paint so the stored theme is applied without a flash.
// Dark is the default; only an explicit "light" choice disables it.
const THEME_SCRIPT = `(function(){try{var stored=localStorage.getItem("argus-theme");var dark=stored!=="light";document.documentElement.classList.toggle("dark",dark);document.documentElement.style.colorScheme=dark?"dark":"light";}catch(e){document.documentElement.classList.add("dark");document.documentElement.style.colorScheme="dark";}})();`;

// React 19 warns about script tags rendered on the client because it cannot
// execute them. The server render keeps type="text/javascript" so the browser
// runs the script during HTML parsing. The client render uses text/plain,
// which React treats as a data block instead of a script; suppressHydrationWarning
// keeps the server-rendered executable script in place.
export default function ThemeInitScript() {
  return (
    <script
      dangerouslySetInnerHTML={{ __html: THEME_SCRIPT }}
      suppressHydrationWarning
      type={typeof window === "undefined" ? "text/javascript" : "text/plain"}
    />
  );
}
