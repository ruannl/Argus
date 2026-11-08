import type { Metadata } from "next";
import type { ReactNode } from "react";

import ThemeInitScript from "@/components/theme-init-script";
import ThemeToggle from "@/components/theme-toggle";
import { PRODUCT_NAME } from "@/lib/brand";

import "./globals.css";

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
      <body>
        {children}
        <ThemeToggle />
      </body>
    </html>
  );
}
