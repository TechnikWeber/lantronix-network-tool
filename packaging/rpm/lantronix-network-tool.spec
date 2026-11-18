# Framework-dependent: uses Fedora's own .NET runtime (dotnet-runtime-10.0).
%global debug_package %{nil}
%global __requires_exclude_from ^%{_libdir}/%{name}/.*$
%global __provides_exclude_from ^%{_libdir}/%{name}/.*$

Name:           lantronix-network-tool
Version:        2.0.0
Release:        1%{?dist}
Summary:        Find Lantronix device servers on the network and assign an IP address
License:        MIT
URL:            https://github.com/TechnikWeber/lantronix-network-tool
Source0:        %{url}/archive/v%{version}/%{name}-%{version}.tar.gz
ExclusiveArch:  x86_64 aarch64

BuildRequires:  dotnet-sdk-10.0
BuildRequires:  desktop-file-utils
Requires:       dotnet-runtime-10.0
# fonts and ICU for the window
Requires:       libicu
Requires:       fontconfig

%description
Asks every Lantronix device server on the local network (XPort, UDS, EDS, MSS,
WiPort ...) for its IP and MAC address with the Lantronix discovery protocol and
opens its web interface. Devices without a usable address get a temporary one
the ARP way. Includes the command line tool lantronix-scan.

%prep
%autosetup

%build
case %{_arch} in x86_64) rid=linux-x64 ;; aarch64) rid=linux-arm64 ;; esac
for proj in src/LantronixScan src/LantronixTool; do
    dotnet publish $proj -c Release -r $rid --self-contained false \
        -p:PublishTrimmed=false -p:DebugType=none -p:Version=%{version} -o out
done
rm -f out/*.pdb
# SkiaSharp/HarfBuzz ship natives for every platform; keep ours
find out/runtimes -mindepth 1 -maxdepth 1 ! -name "$rid" -exec rm -rf {} + 2>/dev/null || true

%install
install -d %{buildroot}%{_libdir}/%{name}
cp -a out/. %{buildroot}%{_libdir}/%{name}/
install -d %{buildroot}%{_bindir}
ln -s ../%{_lib}/%{name}/lantronix-network-tool %{buildroot}%{_bindir}/lantronix-network-tool
ln -s ../%{_lib}/%{name}/lantronix-scan %{buildroot}%{_bindir}/lantronix-scan
desktop-file-install --dir=%{buildroot}%{_datadir}/applications share/%{name}.desktop
install -Dm644 share/%{name}.svg %{buildroot}%{_datadir}/icons/hicolor/scalable/apps/%{name}.svg
install -Dm644 share/%{name}.png %{buildroot}%{_datadir}/icons/hicolor/256x256/apps/%{name}.png
install -Dm644 share/io.github.technikweber.%{name}.metainfo.xml \
    %{buildroot}%{_metainfodir}/io.github.technikweber.%{name}.metainfo.xml

%files
%license LICENSE
%doc README.md CHANGELOG.md
%{_bindir}/lantronix-network-tool
%{_bindir}/lantronix-scan
%{_libdir}/%{name}/
%{_datadir}/applications/%{name}.desktop
%{_datadir}/icons/hicolor/scalable/apps/%{name}.svg
%{_datadir}/icons/hicolor/256x256/apps/%{name}.png
%{_metainfodir}/io.github.technikweber.%{name}.metainfo.xml

%changelog
* Wed Nov 18 2026 Philipp Weber - 2.0.0-1
- New: Windows, Linux and macOS from one code base (.NET 10, Avalonia), lantronix-scan
