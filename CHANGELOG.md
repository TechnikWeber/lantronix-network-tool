# Changelog

## 2.0.0 — 2026-11-18

Rewritten from scratch: one code base for Windows, Linux and macOS (.NET 10, Avalonia)
instead of WinForms and Mono.

- Window for Windows, Linux (AppImage, RPM) and macOS, light and dark, German and English.
- New command line tool `lantronix-scan` (text, JSON, CSV) and the library `Lantronix.Discovery`.
- Search: every network adapter at once, three broadcasts instead of up to 100,000,
  results as they come in, the window stays responsive, cancel button.
- Fixed: devices were missing when one IP address was the beginning of another
  (192.168.1.1 hid 192.168.1.10–19).
- Replies are checked (0xF7, 30 bytes) before the MAC address is read.
- MAC filter in any notation and on part of the address.
- Ask addresses or a whole subnet directly (unicast).
- Assign a temporary IP address (ARP + port 1), with a check that the device answers.
- Hints for devices in another subnet or without an address, and for Linux firewalls.
- Copy, CSV export, telnet command.

## 1.0.1 — 2026-11-11

Last WinForms version (Windows, Linux with Mono).
