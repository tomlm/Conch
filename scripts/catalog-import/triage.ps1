# Merges Terminal Trove and awesome-tuis into one candidate list and sorts it by install route.
param([string]$Work = "./work", [int]$MinStars = 1000, [int]$BatchSize = 20)
$d = $Work
$catalogDir = Join-Path $PSScriptRoot "../../src/Conch/Tools"
$norm = { param($u) if (-not $u) { return $null }; $u = "$u"; $m = [regex]::Match($u.ToLowerInvariant(), '(github\.com|gitlab\.com|codeberg\.org)/([^/#?\s]+)/([^/#?\s]+)'); if ($m.Success) { "https://$($m.Groups[1].Value)/$($m.Groups[2].Value)/$($m.Groups[3].Value -replace '\.git$','')" } }

# --- awesome-tuis: entries with their section ---------------------------------------------
$section = ''
$awesome = @{}
foreach ($line in Get-Content (Join-Path $d 'awesome-tuis.md')) {
  $h = [regex]::Match($line, '<h2>([^<]+)</h2>'); if ($h.Success) { $section = $h.Groups[1].Value; continue }
  $m = [regex]::Match($line, '^\s*- \[([^\]]+)\]\(([^)]+)\)\s*(.*)$')
  if (-not $m.Success) { continue }
  $repo = & $norm $m.Groups[2].Value
  if (-not $repo) { continue }
  $awesome[$repo] = [pscustomobject]@{ name = $m.Groups[1].Value; description = $m.Groups[3].Value.Trim(); section = $section }
}

# --- Terminal Trove ------------------------------------------------------------------------
$trove = @{}
foreach ($t in (Get-Content (Join-Path $d 'trove.json') -Raw | ConvertFrom-Json)) {
  $repo = & $norm $t.repo
  if ($repo) { $trove[$repo] = $t }
}

# --- GitHub metadata -----------------------------------------------------------------------
$meta = @{}
foreach ($f in 'meta.json') {
  foreach ($r in (Get-Content (Join-Path $d $f) -Raw | ConvertFrom-Json)) { $meta[(& $norm $r.url)] = $r }
}

# --- Debian 13 source packages by upstream repo --------------------------------------------
$debian = @{}
foreach ($row in Get-Content (Join-Path $d 'debian-sources.tsv')) {
  $c = $row.Split("`t")
  foreach ($u in $c[2..4]) { $r = & $norm $u; if ($r -and -not $debian.ContainsKey($r)) { $debian[$r] = [pscustomobject]@{ source = $c[0]; binaries = $c[1] } } }
}

# --- What the catalog already has ----------------------------------------------------------
# Debian binary package names, for a name match when the homepage is not the repo.
$debianNames = @{}
foreach ($row in Get-Content (Join-Path $d "debian-sources.tsv")) { $c = $row.Split("`t"); foreach ($b in ($c[1] -split ',\s*')) { if ($b) { $debianNames[$b.Trim().ToLowerInvariant()] = $c[0] } } }

$catalog = @{}
foreach ($f in Get-ChildItem (Join-Path $catalogDir '*.yml')) {
  foreach ($l in Get-Content $f) { if ($l -match '^(source|website|documentation):\s*(\S+)') { $r = & $norm $Matches[2]; if ($r) { $catalog[$r] = $f.Name } } }
}

# --- Merge and classify --------------------------------------------------------------------
$repos = @($awesome.Keys) + @($trove.Keys) | Sort-Object -Unique
$cutoff = (Get-Date).AddYears(-3)
$rows = foreach ($repo in $repos) {
  $a = $awesome[$repo]; $t = $trove[$repo]; $g = $meta[$repo]
  $assets = @($g.assets)
  $linuxAsset = @($assets | Where-Object { $_ -match '(?i)linux' -and $_ -match '(?i)(x86_64|amd64|x64)' -and $_ -notmatch '(?i)\.(sha256|sig|asc|sbom|json|txt|pem)$' }).Count -gt 0
  $installKeys = if ($t.install) { @($t.install.PSObject.Properties.Name) } else { @() }
  $route =
    if ($debian.ContainsKey($repo)) { 'debian' }
    elseif ($linuxAsset) { 'release' }
    elseif ($installKeys -contains 'pip' -or $installKeys -contains 'pipx' -or $installKeys -contains 'uv' -or $g.language -eq 'Python') { 'python' }
    elseif ($installKeys -contains 'npm' -or $installKeys -contains 'bun') { 'npm' }
    elseif ($installKeys -contains 'cargo' -or $g.language -eq 'Rust') { 'cargo' }
    elseif ($installKeys -contains 'go' -or $g.language -eq 'Go') { 'go' }
    else { 'other' }
  [pscustomobject]@{
    repo = $repo
    name = if ($t.name) { $t.name } elseif ($a) { $a.name } else { ($repo -split '/')[-1] }
    description = if ($t.description) { $t.description } elseif ($a.description) { $a.description } else { $g.description }
    image = $t.image
    section = $a.section
    categories = @($t.categories)
    sources = (@($(if ($a) { 'awesome' }), $(if ($t) { 'trove' })) | Where-Object { $_ }) -join '+'
    stars = [int]$g.stars
    archived = [bool]$g.archived
    stale = ($g.pushed -and ([datetime]$g.pushed) -lt $cutoff)
    pushed = $g.pushed
    language = $g.language
    license = $g.license
    release = $g.release
    linuxAsset = $linuxAsset
    troveInstall = $t.install
    debian = $debian[$repo]
    debianByName = $debianNames[(($repo -split "/")[-1]).ToLowerInvariant()]
    route = $route
    inCatalog = $catalog[$repo]
    library = ($a.section -eq 'Libraries') -or (@($t.categories) -contains 'tui-frameworks')
  }
}
$rows | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $d 'candidates.json') -Encoding utf8

$all = $rows.Count
$inCat = @($rows | Where-Object inCatalog).Count
$lib = @($rows | Where-Object { -not $_.inCatalog -and $_.library }).Count
$arch = @($rows | Where-Object { -not $_.inCatalog -and -not $_.library -and $_.archived }).Count
$staleN = @($rows | Where-Object { -not $_.inCatalog -and -not $_.library -and -not $_.archived -and $_.stale }).Count
$live = @($rows | Where-Object { -not $_.inCatalog -and -not $_.library -and -not $_.archived -and -not $_.stale })
"unique repos: $all"
"already in catalog: $inCat"
"libraries/frameworks: $lib"
"archived: $arch"
"no push in 3 years: $staleN"
"live candidates: $($live.Count)"
$live | Group-Object route | Sort-Object Count -Descending | ForEach-Object { "  {0,-8} {1,4}   ({2} with 500+ stars)" -f $_.Name, $_.Count, @($_.Group | Where-Object { $_.stars -ge 500 }).Count }
"live with 500+ stars: $(@($live | Where-Object { $_.stars -ge 500 }).Count)   1000+: $(@($live | Where-Object { $_.stars -ge 1000 }).Count)   with screenshot: $(@($live | Where-Object image).Count)"

# --- This round's batches --------------------------------------------------------------------
# Toolchain-only routes (cargo, go) wait for the Rust/Go prerequisites; see README.
$round = @($live | Where-Object { $_.stars -ge $MinStars -and $_.route -notin 'cargo', 'go' } | Sort-Object stars -Descending)
$batches = [Math]::Ceiling($round.Count / $BatchSize)
for ($i = 0; $i -lt $batches; $i++) {
  $b = $round[($i * $BatchSize)..([Math]::Min(($i + 1) * $BatchSize - 1, $round.Count - 1))]
  $b | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $d ('batch{0:00}.json' -f ($i + 1))) -Encoding utf8
}
@($live | Where-Object { $_.stars -ge $MinStars -and $_.route -in 'cargo', 'go' }) |
  Select-Object name, repo, route, stars | ConvertTo-Json | Set-Content (Join-Path $d 'deferred.json') -Encoding utf8
"round (>= $MinStars stars): $($round.Count) in $batches batches"
