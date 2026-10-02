#!/usr/bin/env bash
# Fetches the raw material for a catalog import into WORK (default ./work):
#   awesome-tuis.md          rothgar/awesome-tuis README
#   trove-pages/<slug>.html  every tool page in Terminal Trove's TUI category
#   debian-sources.tsv       Debian 13 source packages: name, binaries, homepage, vcs
#
# Terminal Trove has no API and sits behind Cloudflare. Its robots.txt allows crawling, and
# plain curl gets through where some HTTP clients are refused, so pages are fetched one at a
# time with a pause between them. Pages already fetched are skipped, so an interrupted run
# resumes where it stopped. Ten failures in a row and it gives up rather than hammering.
set -euo pipefail
WORK=${1:-./work}
mkdir -p "$WORK/trove-pages"
UA='Mozilla/5.0 (Windows NT 10.0; Win64; x64)'

curl -fsSL -o "$WORK/awesome-tuis.md" https://raw.githubusercontent.com/rothgar/awesome-tuis/master/README.md

curl -fsSL -A "$UA" -o "$WORK/trove-tui.html" https://terminaltrove.com/categories/tui/
grep -oE 'href="/[a-z0-9._-]+/"' "$WORK/trove-tui.html" | sed 's|href="/||; s|/"||' | sort -u \
  | grep -vxE 'categories|compare|blog|language|terminals|ai-coding-agents|explore|about|submit|privacy|terms|newsletter|search|tags' \
  > "$WORK/trove-slugs.txt"

failures=0
while read -r slug; do
  [ -s "$WORK/trove-pages/$slug.html" ] && continue
  code=$(curl -sL -A "$UA" -o "$WORK/trove-pages/$slug.html" -w '%{http_code}' "https://terminaltrove.com/$slug/")
  if [ "$code" != 200 ]; then
    rm -f "$WORK/trove-pages/$slug.html"
    echo "FAIL $slug $code"
    failures=$((failures + 1))
    [ $failures -ge 10 ] && { echo "giving up after 10 failures"; exit 1; }
  else
    failures=0
  fi
  sleep 0.6
done < "$WORK/trove-slugs.txt"

curl -fsSL https://deb.debian.org/debian/dists/trixie/main/source/Sources.xz | xz -dc \
  | awk '/^Package: /{p=$2} /^Binary: /{b=substr($0,9)} /^Homepage: /{h=$2} /^Vcs-Browser: /{v=$2} /^Vcs-Git: /{g=$2}
         /^$/{ if (p) print p "\t" b "\t" h "\t" v "\t" g; p=b=h=v=g="" }' > "$WORK/debian-sources.tsv"

echo "pages: $(ls "$WORK/trove-pages" | wc -l)  debian sources: $(wc -l < "$WORK/debian-sources.tsv")"
