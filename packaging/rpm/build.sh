#!/usr/bin/env bash
# Builds dist/lantronix-network-tool-<version>-1.<dist>.<arch>.rpm on Fedora.
# Installs its build dependencies when run as root (CI container).
set -euo pipefail

root="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")/../.." && pwd)"
cd "$root"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"
spec_version="$(sed -n 's/^Version: *//p' packaging/rpm/lantronix-network-tool.spec)"
[ "$version" = "$spec_version" ] || { echo "version mismatch: $version vs spec $spec_version" >&2; exit 1; }

if [ "$(id -u)" = 0 ]; then
    dnf install -y -q rpm-build dotnet-sdk-10.0 desktop-file-utils tar
fi

top="$root/build/rpm"
rm -rf "$top"
mkdir -p "$top/SOURCES" "$root/dist"
tar --exclude=./build --exclude=./dist --exclude=./.git --exclude='*/bin' --exclude='*/obj' \
    --transform "s|^\./|lantronix-network-tool-$version/|" \
    -czf "$top/SOURCES/lantronix-network-tool-$version.tar.gz" .
rpmbuild -bb --define "_topdir $top" packaging/rpm/lantronix-network-tool.spec
cp "$top"/RPMS/*/*.rpm "$root/dist/"
ls -l "$root/dist"
