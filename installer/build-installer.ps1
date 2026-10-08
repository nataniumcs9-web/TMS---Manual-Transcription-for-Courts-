$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot "TranscriberClient\TranscriberClient.csproj"
$publishDir = Join-Path $projectRoot "TranscriberClient\publish\installer-payload"
$installerScript = Join-Path $PSScriptRoot "GZHC-Transcriber.iss"
$dotnetCommand = Get-Command "dotnet.exe" -ErrorAction SilentlyContinue
if ($dotnetCommand) {
    $dotnetPath = $dotnetCommand.Source
} elseif (Test-Path -LiteralPath "C:\Program Files\dotnet\dotnet.exe") {
    $dotnetPath = "C:\Program Files\dotnet\dotnet.exe"
} else {
    throw ".NET 8 SDK was not found."
}

$compilerCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
)

$compiler = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if ($compiler) {
    $compilerPath = $compiler.Source
} else {
    $compilerPath = $compilerCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}

if (-not $compilerPath) {
    throw "Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup 6, then run this script again."
}

& $dotnetPath publish $projectFile `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDir `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "The application publish failed with exit code $LASTEXITCODE."
}

$payloadExe = Join-Path $publishDir "TranscriberClient.exe"
if (-not (Test-Path -LiteralPath $payloadExe -PathType Leaf)) {
    throw "Published application executable was not found: $payloadExe"
}

$outputDirectory = Join-Path $PSScriptRoot "output"
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

& $compilerPath $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

$setupExe = Join-Path $outputDirectory "GZHC-Transcriber-Setup-1.0.0.exe"
if (-not (Test-Path -LiteralPath $setupExe -PathType Leaf)) {
    throw "Installer executable was not created: $setupExe"
}

Write-Output "Installer created: $setupExe"
