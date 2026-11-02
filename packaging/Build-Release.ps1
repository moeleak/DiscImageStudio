[CmdletBinding()]
param(
    [string]$Tag,
    # win-x64: single-file EXE. linux-*: single-file executable in a tar.gz with a .desktop
    # entry and icon. osx-*: ad-hoc signed .app bundle in a zip and a dmg.
    [ValidateSet('win-x64', 'linux-x64', 'linux-arm64', 'osx-arm64', 'osx-x64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$BundleIdentifier = 'io.github.jiafeimiao-k-cat.discimagestudio'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$onWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
$onMac = [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::OSX)
$onLinux = [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Linux)
$targetWindows = $RuntimeIdentifier.StartsWith('win-')
$targetMac = $RuntimeIdentifier.StartsWith('osx-')
$targetLinux = $RuntimeIdentifier.StartsWith('linux-')

# Packages are smoke-tested by running them, so each is built on its own operating system.
if (($targetWindows -and -not $onWindows) -or ($targetMac -and -not $onMac) -or ($targetLinux -and -not $onLinux)) {
    throw "Build the $RuntimeIdentifier package on its own operating system so the published binary can be smoke-tested."
}

$hostArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$targetArchitecture = $RuntimeIdentifier.Split('-')[1]
$canRunTarget = $hostArchitecture -eq $targetArchitecture -or $env:DIS_FORCE_SMOKE -eq '1'

$metadata = & (Join-Path $PSScriptRoot 'Get-ReleaseMetadata.ps1') -Tag $Tag
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspacePrefix = $workspaceRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$pathComparison = if ($onLinux) { [StringComparison]::Ordinal } else { [StringComparison]::OrdinalIgnoreCase }
$releaseDirectory = [IO.Path]::GetFullPath((Join-Path $workspaceRoot (Join-Path 'artifacts/release' $metadata.Tag)))
$workDirectory = Join-Path $releaseDirectory $RuntimeIdentifier

function Assert-SafeReleasePath([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($workspacePrefix, $pathComparison)) {
        throw "Release output resolved outside the workspace: $fullPath"
    }

    # A junction or symbolic link could make a lexically safe path leave the workspace.
    $currentPath = $fullPath
    while ($currentPath.Length -ge $workspaceRoot.Length) {
        if (Test-Path -LiteralPath $currentPath) {
            $item = Get-Item -LiteralPath $currentPath -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Release output must not pass through a junction or symbolic link: $currentPath"
            }
        }
        if ($currentPath.Equals($workspaceRoot, $pathComparison)) {
            break
        }
        $currentPath = [IO.Path]::GetDirectoryName($currentPath)
    }
}

function Remove-ReleaseItem([string]$Path) {
    Assert-SafeReleasePath $Path
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $linkedItems = @(Get-ChildItem -LiteralPath $Path -Force -Recurse -ErrorAction SilentlyContinue |
        Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 })
    if ($linkedItems.Count -ne 0) {
        throw "Refusing to clear release output containing junctions or symbolic links: $Path"
    }
    Remove-Item -LiteralPath $Path -Recurse -Force
}

# Only this tag's output for this runtime is replaced; other runtimes and versions are kept.
$packageBaseName = "DiscImageStudio-$($metadata.Tag)-$RuntimeIdentifier"
Remove-ReleaseItem $workDirectory
foreach ($extension in '.exe', '.tar.gz', '.zip', '.dmg') {
    Remove-ReleaseItem (Join-Path $releaseDirectory "$packageBaseName$extension")
}
Remove-ReleaseItem (Join-Path $releaseDirectory "SHA256SUMS-$RuntimeIdentifier.txt")

$publishDirectory = Join-Path $workDirectory 'publish'
$smokeDirectory = Join-Path $workDirectory 'smoke'
$isolatedAppDirectory = Join-Path $smokeDirectory 'app'
$projectPath = Join-Path $workspaceRoot (Join-Path 'src/DiscImageStudio.App' 'DiscImageStudio.App.csproj')
$iconPng = Join-Path $workspaceRoot (Join-Path 'src/DiscImageStudio.App/Assets' 'DiscImageStudio-256.png')
New-Item -ItemType Directory -Path $publishDirectory, $isolatedAppDirectory -Force | Out-Null

# A macOS bundle keeps its libraries next to the executable so codesign can sign each one;
# Windows and Linux ship one self-extracting executable.
$singleFile = if ($targetMac) { 'false' } else { 'true' }

# The app and the referenced DVD executable can produce the same runtimeconfig output with
# different global properties. Serialize this publish graph.
& dotnet publish $projectPath `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $publishDirectory `
    -maxcpucount:1 `
    -p:BuildInParallel=false `
    "-p:Version=$($metadata.Version)" `
    "-p:AssemblyVersion=$($metadata.FileVersion)" `
    "-p:FileVersion=$($metadata.FileVersion)" `
    "-p:PublishSingleFile=$singleFile" `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$executableName = if ($targetWindows) { 'DiscImageStudio.exe' } else { 'DiscImageStudio' }
$publishedExecutable = Join-Path $publishDirectory $executableName
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Published executable is missing: $publishedExecutable"
}
if ($targetWindows) {
    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($publishedExecutable).FileVersion
    if ($fileVersion -ne $metadata.FileVersion) {
        throw "Published executable version '$fileVersion' does not match '$($metadata.FileVersion)'."
    }
}

Get-ChildItem -LiteralPath $publishDirectory -Filter '*.pdb' -File -Recurse -Force | Remove-Item -Force
$referencedRuntimeConfig = Join-Path $publishDirectory 'DvdImageSolver.runtimeconfig.json'
if (Test-Path -LiteralPath $referencedRuntimeConfig) {
    Remove-Item -LiteralPath $referencedRuntimeConfig -Force
}

function Invoke-Native([string]$FilePath, [string[]]$Arguments) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$FilePath failed with exit code $LASTEXITCODE." }
}

$packages = [Collections.Generic.List[string]]::new()
if ($targetWindows) {
    $packagePath = Join-Path $releaseDirectory "$packageBaseName.exe"
    Copy-Item -LiteralPath $publishedExecutable -Destination $packagePath
    $packages.Add($packagePath)
    $checkExecutable = Join-Path $isolatedAppDirectory "$packageBaseName.exe"
    Copy-Item -LiteralPath $packagePath -Destination $checkExecutable
}
elseif ($targetLinux) {
    $stage = Join-Path $workDirectory $packageBaseName
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Copy-Item -LiteralPath $publishedExecutable -Destination (Join-Path $stage 'DiscImageStudio')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'linux/disc-image-studio.desktop') -Destination $stage
    Copy-Item -LiteralPath $iconPng -Destination (Join-Path $stage 'disc-image-studio.png')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'linux/install.sh') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'LICENSE') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'THIRD-PARTY-NOTICES.txt') -Destination $stage
    Invoke-Native 'chmod' @('755', (Join-Path $stage 'DiscImageStudio'), (Join-Path $stage 'install.sh'))
    $packagePath = Join-Path $releaseDirectory "$packageBaseName.tar.gz"
    Invoke-Native 'tar' @('-C', $workDirectory, '-czf', $packagePath, $packageBaseName)
    $packages.Add($packagePath)
    # Smoke-test the archive as a user receives it.
    Invoke-Native 'tar' @('-C', $isolatedAppDirectory, '-xzf', $packagePath)
    $checkExecutable = Join-Path (Join-Path $isolatedAppDirectory $packageBaseName) 'DiscImageStudio'
}
else {
    $bundle = Join-Path $workDirectory 'Disc Image Studio.app'
    $contents = Join-Path $bundle 'Contents'
    $macOsDirectory = Join-Path $contents 'MacOS'
    $resources = Join-Path $contents 'Resources'
    New-Item -ItemType Directory -Path $macOsDirectory, $resources -Force | Out-Null
    Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $macOsDirectory -Recurse
    $plist = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'macos/Info.plist.template') -Raw).
        Replace('$(BundleIdentifier)', [Security.SecurityElement]::Escape($BundleIdentifier)).
        Replace('$(ShortVersion)', [Security.SecurityElement]::Escape($metadata.BaseVersion)).
        Replace('$(FileVersion)', [Security.SecurityElement]::Escape($metadata.FileVersion))
    [IO.File]::WriteAllText((Join-Path $contents 'Info.plist'), $plist, [Text.UTF8Encoding]::new($false))

    # iconutil needs every size of an .iconset; sips scales the 256 px artwork.
    $iconSet = Join-Path $workDirectory 'DiscImageStudio.iconset'
    New-Item -ItemType Directory -Path $iconSet -Force | Out-Null
    foreach ($size in 16, 32, 128, 256, 512) {
        Invoke-Native 'sips' @('-z', "$size", "$size", $iconPng, '--out', (Join-Path $iconSet "icon_${size}x${size}.png")) | Out-Null
        $double = $size * 2
        Invoke-Native 'sips' @('-z', "$double", "$double", $iconPng, '--out', (Join-Path $iconSet "icon_${size}x${size}@2x.png")) | Out-Null
    }
    Invoke-Native 'iconutil' @('-c', 'icns', $iconSet, '-o', (Join-Path $resources 'DiscImageStudio.icns'))
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'LICENSE') -Destination $resources
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'THIRD-PARTY-NOTICES.txt') -Destination $resources

    # Apple silicon refuses unsigned code. An ad-hoc signature (no Developer ID) satisfies
    # that; Gatekeeper still asks the user to confirm the first launch.
    Invoke-Native 'codesign' @('--force', '--deep', '--sign', '-', '--timestamp=none', $bundle)
    Invoke-Native 'codesign' @('--verify', '--deep', '--strict', $bundle)

    $zipPath = Join-Path $releaseDirectory "$packageBaseName.zip"
    Invoke-Native 'ditto' @('-c', '-k', '--sequesterRsrc', '--keepParent', $bundle, $zipPath)
    $packages.Add($zipPath)
    $dmgStage = Join-Path $workDirectory 'dmg'
    New-Item -ItemType Directory -Path $dmgStage -Force | Out-Null
    Invoke-Native 'ditto' @($bundle, (Join-Path $dmgStage 'Disc Image Studio.app'))
    Invoke-Native 'ln' @('-s', '/Applications', (Join-Path $dmgStage 'Applications'))
    $dmgPath = Join-Path $releaseDirectory "$packageBaseName.dmg"
    Invoke-Native 'hdiutil' @('create', '-volname', 'Disc Image Studio', '-srcfolder', $dmgStage, '-ov', '-format', 'UDZO', $dmgPath)
    $packages.Add($dmgPath)
    # Smoke-test the zip as a user receives it.
    Invoke-Native 'ditto' @('-x', '-k', $zipPath, $isolatedAppDirectory)
    $checkExecutable = Join-Path $isolatedAppDirectory 'Disc Image Studio.app/Contents/MacOS/DiscImageStudio'
}

function ConvertTo-WindowsArgument([string]$Value) {
    # Start-Process joins ArgumentList into a command line. Quote each argument using
    # Windows' backslash/quote rules so paths containing spaces arrive intact.
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function Invoke-PublishedCheck([string]$Name, [string[]]$Arguments) {
    Write-Host "Checking published package: $Name"
    $stdout = Join-Path $smokeDirectory "$Name.stdout.log"
    $stderr = Join-Path $smokeDirectory "$Name.stderr.log"
    if ($targetWindows) {
        # A WinExe does not block the console; wait for the process explicitly.
        $commandLine = ($Arguments | ForEach-Object { ConvertTo-WindowsArgument $_ }) -join ' '
        $process = Start-Process -FilePath $checkExecutable `
            -ArgumentList $commandLine `
            -WorkingDirectory $isolatedAppDirectory `
            -WindowStyle Hidden `
            -RedirectStandardOutput $stdout `
            -RedirectStandardError $stderr `
            -Wait -PassThru
        $exitCode = $process.ExitCode
        $process.Dispose()
    }
    else {
        Push-Location $isolatedAppDirectory
        try {
            & $checkExecutable @Arguments > $stdout 2> $stderr
            $exitCode = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
    }
    if ($exitCode -ne 0) {
        throw "Published package check '$Name' failed with exit code $exitCode. See $smokeDirectory for logs."
    }
}

function Assert-FileLength([string]$Path, [long]$ExpectedLength) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Smoke check did not produce its expected output: $Path"
    }
    $actualLength = (Get-Item -LiteralPath $Path).Length
    if ($actualLength -ne $ExpectedLength) {
        throw "Smoke output '$Path' has $actualLength bytes; expected $ExpectedLength."
    }
}

if ($canRunTarget) {
    Invoke-PublishedCheck 'burn-build-info' @('burn-build-info')
    Invoke-PublishedCheck 'selftest' @('selftest')

    # Spaces in these paths also exercise argument quoting in the packaged executable.
    $inputImage = Join-Path $smokeDirectory 'input image.png'
    Copy-Item -LiteralPath $iconPng -Destination $inputImage
    $rawPath = Join-Path $smokeDirectory 'two sectors.raw'
    Invoke-PublishedCheck 'cd-raw' @('cd-generate', '--input', $inputImage, '--output', $rawPath, '--sectors', '2')
    Assert-FileLength $rawPath 4704

    $wavePath = Join-Path $smokeDirectory 'two sectors.wav'
    Invoke-PublishedCheck 'cd-wav' @('cd-generate', '--input', $inputImage, '--output', $wavePath, '--sectors', '2')
    Assert-FileLength $wavePath 4748
    $cuePath = [IO.Path]::ChangeExtension($wavePath, '.cue')
    if (-not (Test-Path -LiteralPath $cuePath -PathType Leaf) -or (Get-Item -LiteralPath $cuePath).Length -eq 0) {
        throw "WAV generation did not produce a CUE sheet: $cuePath"
    }

    # The CUE sheet is opt-out: --cue false must still write the WAV and leave no .cue behind.
    $noCuePath = Join-Path $smokeDirectory 'no cue.wav'
    Invoke-PublishedCheck 'cd-wav-no-cue' @(
        'cd-generate', '--input', $inputImage, '--output', $noCuePath, '--sectors', '2', '--cue', 'false')
    Assert-FileLength $noCuePath 4748
    if (Test-Path -LiteralPath ([IO.Path]::ChangeExtension($noCuePath, '.cue'))) {
        throw "cd-generate --cue false still produced a CUE sheet for $noCuePath"
    }

    if (-not $targetWindows) {
        Invoke-PublishedCheck 'burn-devices' @('burn-devices')
    }

    $snapshotPath = Join-Path $smokeDirectory 'ui snapshot.png'
    Invoke-PublishedCheck 'ui-snapshot' @('ui-snapshot', '--output', $snapshotPath)
    if (-not (Test-Path -LiteralPath $snapshotPath -PathType Leaf) -or (Get-Item -LiteralPath $snapshotPath).Length -eq 0) {
        throw "Smoke check did not produce a UI snapshot: $snapshotPath"
    }
}
else {
    Write-Warning "Skipping run-time smoke checks: a $hostArchitecture host cannot run $RuntimeIdentifier (set DIS_FORCE_SMOKE=1 under emulation)."
}

if ($targetWindows) {
    $hash = (Get-FileHash -LiteralPath $packages[0] -Algorithm SHA256).Hash
    if ((Get-FileHash -LiteralPath $checkExecutable -Algorithm SHA256).Hash -ne $hash) {
        throw 'The isolated executable does not match the release executable.'
    }
}

$checksumLines = foreach ($package in $packages) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($package)
}
$checksumPath = Join-Path $releaseDirectory "SHA256SUMS-$RuntimeIdentifier.txt"
[IO.File]::WriteAllText($checksumPath, (($checksumLines -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
foreach ($package in $packages) { Write-Host "Release package ready: $package" }
Write-Host "Checksums: $checksumPath"
