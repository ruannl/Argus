import type { Metadata } from "next";
import type { ReactNode } from "react";

import ThemeToggle from "@/components/theme-toggle";
import { PRODUCT_NAME } from "@/lib/brand";

import "./globals.css";

export const metadata: Metadata = {
  title: PRODUCT_NAME,
  description:
    "See where your money goes. Make sense of your money, see the patterns, and understand your financial behaviour with ZAR bank statement insights.",
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
