import type { Metadata } from "next";
import type { ReactNode } from "react";

import ThemeToggle from "@/components/theme-toggle";
import { PRODUCT_NAME } from "@/lib/brand";

import "./globals.css";

export const metadata: Metadata = {
  title: PRODUCT_NAME,
  description:
    "Explore researched SaaS opportunities for people who want to build but do not know what to build.",
};

// Runs before first paint so the stored theme is applied without a flash.
// Dark is the default; only an explicit "light" choice disables it.
const THEME_SCRIPT = `(function(){try{var stored=localStorage.getItem("argus-theme");var dark=stored!=="light";document.documentElement.classList.toggle("dark",dark);document.documentElement.style.colorScheme=dark?"dark":"light";}catch(e){document.documentElement.classList.add("dark");document.documentElement.style.colorScheme="dark";}})();`;

export default function RootLayout({
  children,
}: Readonly<{
  children: ReactNode;
}>) {
  return (
    <html lang="en" suppressHydrationWarning>
      <head>
        <script dangerouslySetInnerHTML={{ __html: THEME_SCRIPT }} />
      </head>
      <body>
        {children}
        <ThemeToggle />
      </body>
    </html>
  );
}
