# Mundus Vivens Proto & C# Stub Sync Script
# This script copies the .proto file and generated C# gRPC stubs from the C# server project to the Unity client project.

$ErrorActionPreference = "Stop"

# 1. Paths (Relative to Unity Project Root)
$UnityProjectRoot = Get-Location
$CSharpServerRoot = Resolve-Path "../MundusVivens" -ErrorAction SilentlyContinue

if ($null -eq $CSharpServerRoot) {
    Write-Error "C# Server project directory (../MundusVivens) not found. Please check directory structure."
}

$ProtoSrc = "$CSharpServerRoot\MundusVivens.Prototype\Protos\mundus_vivens.proto"
$ProtoDestDir = "$UnityProjectRoot\Assets\Protos"
$ProtoDest = "$ProtoDestDir\mundus_vivens.proto"

$GeneratedSrcDir = "$CSharpServerRoot\MundusVivens.Prototype\obj\Debug\net10.0\Protos"
$GeneratedDestDir = "$UnityProjectRoot\Assets\Scripts\Generated\Protos"

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host "[Sync] Starting gRPC Proto and C# stub synchronization..." -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan

# 2. Create destination directories if they don't exist
if (-not (Test-Path $ProtoDestDir)) {
    Write-Host "[Dir] Creating directory: Assets/Protos" -ForegroundColor Gray
    New-Item -ItemType Directory -Force -Path $ProtoDestDir | Out-Null
}

if (-not (Test-Path $GeneratedDestDir)) {
    Write-Host "[Dir] Creating directory: Assets/Scripts/Generated/Protos" -ForegroundColor Gray
    New-Item -ItemType Directory -Force -Path $GeneratedDestDir | Out-Null
}

# 3. Copy .proto file
if (Test-Path $ProtoSrc) {
    Write-Host "[Copy] Copying mundus_vivens.proto" -ForegroundColor Green
    Copy-Item -Path $ProtoSrc -Destination $ProtoDest -Force
} else {
    Write-Error "Source proto file ($ProtoSrc) not found. Please check Protos directory in C# Server."
}

# 4. Copy generated C# stubs (MundusVivens.cs, MundusVivensGrpc.cs)
$StubFiles = @("MundusVivens.cs", "MundusVivensGrpc.cs")
foreach ($file in $StubFiles) {
    $srcPath = "$GeneratedSrcDir\$file"
    $destPath = "$GeneratedDestDir\$file"
    
    if (Test-Path $srcPath) {
        Write-Host "[Copy] Copying C# stub: $file" -ForegroundColor Green
        Copy-Item -Path $srcPath -Destination $destPath -Force
    } else {
        Write-Warning "Generated C# stub ($srcPath) not found. Please build the C# server first to generate stub files."
    }
}

Write-Host "=======================================================" -ForegroundColor Green
Write-Host "[Sync] gRPC Proto and stub synchronization completed!" -ForegroundColor Green
Write-Host "=======================================================" -ForegroundColor Green
