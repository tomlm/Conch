param([string]$Work = './work')
# Turns fetch-sources.sh's output into:
#   trove.json          one record per Terminal Trove TUI page
#   repos.txt           every GitHub repo either source names, one per line, for ghmeta.ps1
#
# Terminal Trove puts what a tool page shows into the HTML itself: the tagline in the
# description meta tag, the screenshot in og:image, and the install commands as JSON in the
# data-install attribute. Star counts are filled in by script, so they come from GitHub instead.
$ErrorActionPreference = 'Stop'
$dec = { param($s) [System.Net.WebUtility]::HtmlDecode($s) }

$trove = foreach ($file in Get-ChildItem (Join-Path $Work 'trove-pages') -Filter *.html) {
  $h = Get-Content $file.FullName -Raw
  $install = [regex]::Match($h, 'data-install="([^"]*)"').Groups[1].Value
  $repo = [regex]::Matches($h, 'href="(https://(github|gitlab|codeberg)\.(com|org)/[^"?]+)(\?ref=terminaltrove)?"') |
    ForEach-Object { $_.Groups[1].Value } |
    Where-Object { $_ -notmatch 'github.com/terminaltrove' } | Select-Object -First 1
  [pscustomobject]@{
    slug = $file.BaseName
    name = (& $dec ([regex]::Match($h, '<h1[^>]*>([^<]*)</h1>').Groups[1].Value)).Trim()
    description = & $dec ([regex]::Match($h, '<meta name="description" content="([^"]*)"').Groups[1].Value)
    image = [regex]::Match($h, '<meta property="og:image" content="([^"]*)"').Groups[1].Value
    install = if ($install) { (& $dec $install) | ConvertFrom-Json } else { $null }
    repo = $repo
    categories = @([regex]::Matches($h, 'href="/categories/([a-z0-9-]+)/"') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
  }
}
$trove | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Work 'trove.json') -Encoding utf8

$awesomeRepos = Select-String -Path (Join-Path $Work 'awesome-tuis.md') -Pattern '^\s*- \[[^\]]+\]\((https://github\.com/[^)]+)\)' |
  ForEach-Object { $_.Matches[0].Groups[1].Value }
$troveRepos = $trove | Where-Object { $_.repo -match '^https://github\.com/' } | ForEach-Object { $_.repo }
@($awesomeRepos) + @($troveRepos) | ForEach-Object { $_.TrimEnd('/').ToLowerInvariant() } | Sort-Object -Unique |
  Set-Content (Join-Path $Work 'repos.txt') -Encoding utf8

"trove pages: $($trove.Count)  repos: $((Get-Content (Join-Path $Work 'repos.txt')).Count)"
