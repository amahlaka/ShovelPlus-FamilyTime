# Builds ShovelPlus.dll with the C# compiler that ships with Windows and copies it to BepInEx\plugins.
$ErrorActionPreference = 'Stop'
$game = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$un  = Join-Path $game 'BepInEx\unstripped'
$man = Join-Path $game 'FamilyTime_Data\Managed'
$core = Join-Path $game 'BepInEx\core'
$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $out | Out-Null

$refs = @(
  "$un\mscorlib.dll", "$un\netstandard.dll", "$un\System.dll", "$un\System.Core.dll",
  "$un\UnityEngine.dll", "$un\UnityEngine.CoreModule.dll", "$un\UnityEngine.IMGUIModule.dll",
  "$un\UnityEngine.TextRenderingModule.dll", "$un\Unity.InputSystem.dll", "$un\Unity.Mathematics.dll", "$un\Unity.TextMeshPro.dll", "$un\UnityEngine.UIModule.dll", "$un\UnityEngine.UI.dll",
  "$man\Assembly-CSharp.dll", "$core\BepInEx.dll", "$core\0Harmony.dll"
) | ForEach-Object { "-r:$_" }

& $csc -nologo -noconfig -nostdlib+ -target:library -optimize+ -nowarn:1701,1702 `
  "-out:$out\ShovelPlus.dll" @refs (Join-Path $PSScriptRoot 'src\*.cs')
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

Copy-Item "$out\ShovelPlus.dll" (Join-Path $game 'BepInEx\plugins\ShovelPlus.dll') -Force
Write-Host "Built and installed ShovelPlus.dll"
