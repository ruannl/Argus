import type { Metadata } from "next";
import localFont from "next/font/local";
import type { ReactNode } from "react";

import ThemeInitScript from "@/components/theme-init-script";
import ThemeToggle from "@/components/theme-toggle";
import { PRODUCT_NAME } from "@/lib/brand";

import "./globals.css";

// Finance-only UI typeface. `preload: false` keeps the file out of public and
// auth pages, which never reference the variable.
const financeSans = localFont({
  src: "./fonts/InterVariable.woff2",
  display: "swap",
  weight: "100 900",
  variable: "--font-finance",
  preload: false,
});

export const metadata: Metadata = {
  title: PRODUCT_NAME,
  description:
    "See where your money goes. Make sense of your money, see the patterns, and understand your financial behaviour with ZAR bank statement insights.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: ReactNode;
}>) {
  return (
    <html lang="en" suppressHydrationWarning>
      <head>
        <ThemeInitScript />
      </head>
      <body className={financeSans.variable}>
        {children}
        <ThemeToggle />
      </body>
    </html>
  );
}
