param([string]$ReposFile, [string]$OutFile)
# Fetches stars, archive state, last push, licence and latest-release asset names for each
# GitHub repo, 50 per GraphQL call.
$repos = Get-Content $ReposFile | ForEach-Object {
  $p = ($_ -replace '^https://github.com/', '' -replace '\.git$', '').Trim('/').Split('/')
  if ($p.Count -ge 2) { [pscustomobject]@{ owner = $p[0]; name = $p[1]; url = "https://github.com/$($p[0])/$($p[1])" } }
} | Sort-Object url -Unique

$results = @()
for ($i = 0; $i -lt $repos.Count; $i += 50) {
  $batch = $repos[$i..([Math]::Min($i + 49, $repos.Count - 1))]
  $fields = 'url stargazerCount isArchived pushedAt description licenseInfo { spdxId } primaryLanguage { name } latestRelease { tagName releaseAssets(first: 40) { nodes { name } } }'
  $parts = for ($j = 0; $j -lt $batch.Count; $j++) {
    $o = $batch[$j].owner -replace '"', ''; $n = $batch[$j].name -replace '"', ''
    "r${j}: repository(owner: `"$o`", name: `"$n`") { $fields }"
  }
  $query = "query { " + ($parts -join ' ') + " }"
  $json = gh api graphql -f query="$query" 2>$null
  if ($LASTEXITCODE -ne 0) { Write-Host "batch $i failed: $(($json | Out-String).Substring(0, [Math]::Min(300, ($json | Out-String).Length)))" }
  if (-not $json) { continue }
  $data = ($json | ConvertFrom-Json).data
  foreach ($prop in $data.PSObject.Properties) {
    $r = $prop.Value
    if ($null -eq $r) { continue }
    $results += [pscustomobject]@{
      url = $r.url.ToLowerInvariant()
      stars = $r.stargazerCount
      archived = $r.isArchived
      pushed = $r.pushedAt
      description = $r.description
      license = $r.licenseInfo.spdxId
      language = $r.primaryLanguage.name
      release = $r.latestRelease.tagName
      assets = @($r.latestRelease.releaseAssets.nodes.name)
    }
  }
}
$results | ConvertTo-Json -Depth 4 | Set-Content $OutFile -Encoding utf8
"repos: $($repos.Count) resolved: $($results.Count)"
