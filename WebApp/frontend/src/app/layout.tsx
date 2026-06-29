import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "SmartParking MVP",
  description: "Unity snapshot based parking guidance and admin operations dashboard"
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="ja">
      <body>{children}</body>
    </html>
  );
}
