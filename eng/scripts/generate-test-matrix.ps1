<#
.SYNOPSIS
  Generate split-tests matrix JSON supporting collection-based and class-based modes.

.DESCRIPTION
  Reads *.tests.list files:
    collection mode format:
      collection:Name
      ...
      uncollected:*    (catch-all)
    class mode format:
      class:Full.Namespace.ClassName

  Builds matrix entries with fields consumed by CI:
    type                (collection | uncollected | class)
    projectName
    shortname
    name
    fullClassName (class mode only)
    testProjectPath
    extraTestArgs
    requiresNugets
    requiresTestSdk
    enablePlaywrightInstall
    testSessionTimeout
    testHangTimeout

  Defaults (if metadata absent):
    testSessionTimeout=20m
    testHangTimeout=10m
    uncollectedTestsSessionTimeout=15m
    uncollectedTestsHangTimeout=10m

.NOTES
  PowerShell 7+, cross-platform.
#>

[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)]
  [string]$TestListsDirectory,
  [Parameter(Mandatory=$true)]
  [string]$OutputDirectory,
  [Parameter(Mandatory=$false)]
  [string]$RegularTestProjectsFile = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Define default values - only include properties in output when they differ from these
$script:Defaults = @{
  extraTestArgs = ''
  requiresNugets = $false
  requiresTestSdk = $false
  enablePlaywrightInstall = $false
  testSessionTimeout = '20m'
  testHangTimeout = '10m'
  supportedOSes = @('windows', 'linux', 'macos')
}

function Read-Metadata($file, $projectName) {
  $defaults = @{
    projectName = $projectName
    testClassNamesPrefix = $projectName
    testProjectPath = "tests/$projectName/$projectName.csproj"
    extraTestArgs = ''
    requiresNugets = 'false'
    requiresTestSdk = 'false'
    enablePlaywrightInstall = 'false'
    testSessionTimeout = '20m'
    testHangTimeout = '10m'
    uncollectedTestsSessionTimeout = '15m'
    uncollectedTestsHangTimeout = '10m'
    supportedOSes = @('windows', 'linux', 'macos')
  }
  if (-not (Test-Path $file)) { return $defaults }
  try {
    $json = Get-Content -Raw -Path $file | ConvertFrom-Json
    foreach ($k in $json.PSObject.Properties.Name) {
      $defaults[$k] = $json.$k
    }
  } catch {
    throw "Failed parsing metadata for ${projectName}: $_"
  }
  return $defaults
}

function Add-OptionalProperty($entry, $key, $value, $default) {
  # Only add property if it differs from the default
  if ($null -ne $default) {
    if ($value -is [Array] -and $default -is [Array]) {
      # Compare arrays
      if (($value.Count -ne $default.Count) -or (Compare-Object $value $default)) {
        $entry[$key] = $value
      }
    } elseif ($value -ne $default) {
      $entry[$key] = $value
    }
  } else {
    # No default, always include
    $entry[$key] = $value
  }
}

function New-EntryCollection($c,$meta) {
  $projectShortName = $meta.projectName -replace '^Aspire\.' -replace '\.Tests$'
  $extraTestArgsValue = "--filter-trait `"Partition=$c`""

  $entry = [ordered]@{
    type = 'collection'
    projectName = $meta.projectName
    name = $c
    shortname = "${projectShortName}_$c"
    testProjectPath = $meta.testProjectPath
  }

  # Add optional properties only if they differ from defaults
  Add-OptionalProperty $entry 'extraTestArgs' $extraTestArgsValue $script:Defaults.extraTestArgs
  Add-OptionalProperty $entry 'requiresNugets' ($meta.requiresNugets -eq 'true') $script:Defaults.requiresNugets
  Add-OptionalProperty $entry 'requiresTestSdk' ($meta.requiresTestSdk -eq 'true') $script:Defaults.requiresTestSdk
  Add-OptionalProperty $entry 'enablePlaywrightInstall' ($meta.enablePlaywrightInstall -eq 'true') $script:Defaults.enablePlaywrightInstall
  Add-OptionalProperty $entry 'testSessionTimeout' $meta.testSessionTimeout $script:Defaults.testSessionTimeout
  Add-OptionalProperty $entry 'testHangTimeout' $meta.testHangTimeout $script:Defaults.testHangTimeout
  Add-OptionalProperty $entry 'supportedOSes' $meta.supportedOSes $script:Defaults.supportedOSes

  return $entry
}

function New-EntryUncollected($collections,$meta) {
  $filters = @()
  foreach ($c in $collections) {
    $filters += "--filter-not-trait `"Partition=$c`""
  }
  $extraTestArgsValue = ($filters -join ' ')

  $entry = [ordered]@{
    type = 'uncollected'
    projectName = $meta.projectName
    name = 'UncollectedTests'
    shortname = 'Uncollected'
    testProjectPath = $meta.testProjectPath
  }

  # Add optional properties only if they differ from defaults
  # Note: uncollected tests may have different timeout defaults
  $uncollectedSessionTimeout = $meta.uncollectedTestsSessionTimeout ?? $meta.testSessionTimeout
  $uncollectedHangTimeout = $meta.uncollectedTestsHangTimeout ?? $meta.testHangTimeout

  Add-OptionalProperty $entry 'extraTestArgs' $extraTestArgsValue $script:Defaults.extraTestArgs
  Add-OptionalProperty $entry 'requiresNugets' ($meta.requiresNugets -eq 'true') $script:Defaults.requiresNugets
  Add-OptionalProperty $entry 'requiresTestSdk' ($meta.requiresTestSdk -eq 'true') $script:Defaults.requiresTestSdk
  Add-OptionalProperty $entry 'enablePlaywrightInstall' ($meta.enablePlaywrightInstall -eq 'true') $script:Defaults.enablePlaywrightInstall
  Add-OptionalProperty $entry 'testSessionTimeout' $uncollectedSessionTimeout $script:Defaults.testSessionTimeout
  Add-OptionalProperty $entry 'testHangTimeout' $uncollectedHangTimeout $script:Defaults.testHangTimeout
  Add-OptionalProperty $entry 'supportedOSes' $meta.supportedOSes $script:Defaults.supportedOSes

  return $entry
}

function New-EntryClass($full,$meta) {
  $prefix = $meta.testClassNamesPrefix
  $short = $full
  if ($prefix -and $full.StartsWith("$prefix.")) {
    $short = $full.Substring($prefix.Length + 1)
  }
  $extraTestArgsValue = "--filter-class `"$full`""

  $entry = [ordered]@{
    type = 'class'
    projectName = $meta.projectName
    name = $short
    shortname = $short
    fullClassName = $full
    testProjectPath = $meta.testProjectPath
  }

  # Add optional properties only if they differ from defaults
  Add-OptionalProperty $entry 'extraTestArgs' $extraTestArgsValue $script:Defaults.extraTestArgs
  Add-OptionalProperty $entry 'requiresNugets' ($meta.requiresNugets -eq 'true') $script:Defaults.requiresNugets
  Add-OptionalProperty $entry 'requiresTestSdk' ($meta.requiresTestSdk -eq 'true') $script:Defaults.requiresTestSdk
  Add-OptionalProperty $entry 'enablePlaywrightInstall' ($meta.enablePlaywrightInstall -eq 'true') $script:Defaults.enablePlaywrightInstall
  Add-OptionalProperty $entry 'testSessionTimeout' $meta.testSessionTimeout $script:Defaults.testSessionTimeout
  Add-OptionalProperty $entry 'testHangTimeout' $meta.testHangTimeout $script:Defaults.testHangTimeout
  Add-OptionalProperty $entry 'supportedOSes' $meta.supportedOSes $script:Defaults.supportedOSes

  return $entry
}

function New-EntryRegular($shortName) {
  $entry = [ordered]@{
    type = 'regular'
    projectName = "Aspire.$shortName.Tests"
    name = $shortName
    shortname = $shortName
    testProjectPath = "tests/Aspire.$shortName.Tests/Aspire.$shortName.Tests.csproj"
  }

  # All defaults match, so no need to add any optional properties
  # (extraTestArgs is empty, which matches the default)

  return $entry
}if (-not (Test-Path $TestListsDirectory)) {
  throw "Test lists directory not found: $TestListsDirectory"
}

$listFiles = @(Get-ChildItem -Path $TestListsDirectory -Filter '*.tests.list' -Recurse -ErrorAction SilentlyContinue)
if ($listFiles.Count -eq 0) {
  $empty = @{ include = @() }
  New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
  $empty | ConvertTo-Json -Depth 5 -Compress | Set-Content -Path (Join-Path $OutputDirectory 'combined-tests-matrix.json') -Encoding UTF8
  Write-Host "Empty matrix written (no .tests.list files)."
  exit 0
}

$entries = [System.Collections.Generic.List[object]]::new()

foreach ($lf in $listFiles) {
  $fileName = $lf.Name -replace '\.tests\.list$',''
  $projectName = $fileName
  $lines = @(Get-Content $lf.FullName | Where-Object { $_ -and -not [string]::IsNullOrWhiteSpace($_) })
  $metadataPath = ($lf.FullName -replace '\.tests\.list$', '.tests.metadata.json')
  $meta = Read-Metadata $metadataPath $projectName
  if ($lines.Count -eq 0) { continue }

  if ($lines[0].StartsWith('collection:') -or $lines[0].StartsWith('uncollected:')) {
    # collection mode
    $collections = @()
    $hasUncollected = $false
    foreach ($l in $lines) {
      if ($l -match '^collection:(.+)$') { $collections += $Matches[1].Trim() }
      elseif ($l -match '^uncollected:') { $hasUncollected = $true }
    }
    foreach ($c in ($collections | Sort-Object)) {
      $entries.Add( (New-EntryCollection $c $meta) ) | Out-Null
    }
    if ($hasUncollected) {
      $entries.Add( (New-EntryUncollected $collections $meta) ) | Out-Null
    }
  } elseif ($lines[0].StartsWith('class:')) {
    # class mode
    foreach ($l in $lines) {
      if ($l -match '^class:(.+)$') {
        $entries.Add( (New-EntryClass $Matches[1].Trim() $meta) ) | Out-Null
      }
    }
  }
}

# Add regular (non-split) test projects if provided
if ($RegularTestProjectsFile -and (Test-Path $RegularTestProjectsFile)) {
  # Check if JSON file exists with full metadata
  $jsonFile = "$RegularTestProjectsFile.json"
  if (Test-Path $jsonFile) {
    $regularProjectsData = Get-Content -Raw $jsonFile | ConvertFrom-Json
    if ($regularProjectsData -isnot [Array]) {
      $regularProjectsData = @($regularProjectsData)
    }
    Write-Host "Adding $($regularProjectsData.Count) regular test project(s) from JSON"
    foreach ($proj in $regularProjectsData) {
      # Try to read metadata file for this project if it exists
      $metadataFile = $null
      if ($proj.metadataFile) {
        # metadataFile path is relative to repo root, so make it absolute
        $metadataFile = Join-Path $TestListsDirectory ".." ($proj.metadataFile -replace '^artifacts/', '')
      }

      $meta = $null
      if ($metadataFile -and (Test-Path $metadataFile)) {
        $meta = Read-Metadata $metadataFile $proj.project
        #Write-Host "  Loaded metadata for $($proj.project) from $metadataFile (requiresNugets=$($meta.requiresNugets))"
      } else {
        # Use defaults if no metadata file exists
        # Note: supportedOSes comes from the project JSON, not defaults
        $projectSupportedOSes = if ($proj.PSObject.Properties['supportedOSes']) { $proj.supportedOSes } else { @('windows', 'linux', 'macos') }
        $meta = @{
          projectName = $proj.project
          testProjectPath = $proj.fullPath
          extraTestArgs = ''
          requiresNugets = 'false'
          requiresTestSdk = 'false'
          enablePlaywrightInstall = 'false'
          testSessionTimeout = '20m'
          testHangTimeout = '10m'
          supportedOSes = $projectSupportedOSes
        }
        Write-Host "  Using default metadata for $($proj.project) (no metadata file found at $metadataFile)"
      }

      $entry = [ordered]@{
        type = 'regular'
        projectName = $proj.project
        name = $proj.shortName
        shortname = $proj.shortName
        testProjectPath = $proj.fullPath
      }

      # Add optional properties only if they differ from defaults
      # Note: supportedOSes from the project JSON takes precedence
      $finalSupportedOSes = if ($proj.PSObject.Properties['supportedOSes']) { $proj.supportedOSes } else { $meta.supportedOSes }

      Add-OptionalProperty $entry 'extraTestArgs' $meta.extraTestArgs $script:Defaults.extraTestArgs
      Add-OptionalProperty $entry 'requiresNugets' ($meta.requiresNugets -eq 'true') $script:Defaults.requiresNugets
      Add-OptionalProperty $entry 'requiresTestSdk' ($meta.requiresTestSdk -eq 'true') $script:Defaults.requiresTestSdk
      Add-OptionalProperty $entry 'enablePlaywrightInstall' ($meta.enablePlaywrightInstall -eq 'true') $script:Defaults.enablePlaywrightInstall
      Add-OptionalProperty $entry 'testSessionTimeout' $meta.testSessionTimeout $script:Defaults.testSessionTimeout
      Add-OptionalProperty $entry 'testHangTimeout' $meta.testHangTimeout $script:Defaults.testHangTimeout
      Add-OptionalProperty $entry 'supportedOSes' $finalSupportedOSes $script:Defaults.supportedOSes

      $entries.Add($entry) | Out-Null
    }
  } else {
    # Fallback to old behavior for backward compatibility
    $regularProjects = @(Get-Content $RegularTestProjectsFile | Where-Object { $_ -and -not [string]::IsNullOrWhiteSpace($_) })
    Write-Host "Adding $($regularProjects.Count) regular test project(s) (legacy mode)"
    foreach ($shortName in $regularProjects) {
      $entries.Add( (New-EntryRegular $shortName) ) | Out-Null
    }
  }
}

$matrix = @{ include = $entries }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$matrix | ConvertTo-Json -Depth 10 -Compress | Set-Content -Path (Join-Path $OutputDirectory 'combined-tests-matrix.json') -Encoding UTF8
Write-Host "Matrix entries: $($entries.Count)"
