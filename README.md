# Lantronix network tool

<p>
  <a href="https://github.com/TechnikWeber/lantronix-network-tool/releases/latest"><img alt="Download" src="https://img.shields.io/github/v/release/TechnikWeber/lantronix-network-tool?label=download"></a>
  <img alt="Platform: Windows, Linux with Mono" src="https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20(Mono)-blue">
  <img alt="UI language: German" src="https://img.shields.io/badge/UI-Deutsch-informational">
  <a href="LICENSE"><img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-green"></a>
</p>

A small Windows tool (C#, WinForms) that finds Lantronix devices on the local network with
the Lantronix discovery protocol (UDP broadcast, port 30718) and shows their IP and MAC
addresses. Search for all devices or for one specific MAC address; "Verbinden" (connect)
opens the web interface of the selected device in your browser. The interface is in German.

![Lantronix network tool listing four devices with IP and MAC address](docs/screenshot.png)

## Download and run

Get `lantronix-network-tool.exe` from the
[latest release](https://github.com/TechnikWeber/lantronix-network-tool/releases/latest).

- **Windows 10/11:** just start it. The file is not signed; SmartScreen may warn
  (*More info › Run anyway*).
- **Linux:** run it with Mono: `mono lantronix-network-tool.exe`

Or build the solution in Visual Studio.

## Demo without hardware

`python3 demo/fake_devices.py` simulates four devices on your own machine — that is how the
screenshot above was made.

## License

[MIT](LICENSE).
