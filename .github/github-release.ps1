# Publishes a GitHub release for a tag, with that version's section of docs/CHANGELOG.md as its notes
# (docs/DESIGN.md, "Publishing: GitHub and NuGet"). Run by the workflows; needs the `gh` CLI and GH_TOKEN.
#
#     pwsh .github/github-release.ps1 v0.1.0-alpha.1              (creates the release; fails if there is one)
#     pwsh .github/github-release.ps1 v0.1.0-alpha.1 -NotesOnly   (prints the notes and stops: for trying it)
#
# A version with a hyphen (0.1.0-alpha.1) is marked as a pre-release. The tag must exist already.
param(
    [Parameter(Mandatory = $true)] [string] $Tag,
    [switch] $NotesOnly
)

$ErrorActionPreference = "Stop"
$version = $Tag.TrimStart("v")
if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "'$Tag' is not a version tag (v1.2.3 or v1.2.3-alpha.1)." }

# The version's section: from its heading ("## 0.1.0-alpha.1 (2026-11-20)") to the next "## ".
$changelog = Join-Path $PSScriptRoot "../docs/CHANGELOG.md"
$section = New-Object System.Collections.Generic.List[string]
$inside = $false
foreach ($line in Get-Content $changelog) {
    if ($line -match '^## ') {
        if ($inside) { break }
        $inside = $line -match ('^## ' + [regex]::Escape($version) + '(\s|$)')
        continue
    }
    if ($inside) { $section.Add($line) }
}
$notes = ($section -join "`n").Trim()
if (-not $notes) { throw "docs/CHANGELOG.md has no section for $version (a heading '## $version ...')." }

$packages = "The packages are on NuGet: ``dotnet add package Scribble.Desktop --version $version`` (or ``Scribble.Browser``)."
$notes = $notes + "`n`n" + $packages + "`n"

if ($NotesOnly) {
    Write-Output $notes
    exit 0
}

$notesFile = Join-Path ([System.IO.Path]::GetTempPath()) "scribble-release-notes.md"
Set-Content -Path $notesFile -Value $notes -Encoding utf8
$arguments = @("release", "create", $Tag, "--title", "Scribble $version", "--notes-file", $notesFile, "--verify-tag")
if ($version.Contains("-")) { $arguments += "--prerelease" }
& gh @arguments
if ($LASTEXITCODE -ne 0) { throw "gh release create ended with $LASTEXITCODE." }
