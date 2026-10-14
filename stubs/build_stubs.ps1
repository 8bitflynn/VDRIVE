# Rebuilds the C64 patch stubs and puts their bytes into VDRIVE\Protocol\PatchStubs.cs
#
#   powershell -ExecutionPolicy Bypass -File stubs\build_stubs.ps1          assemble, update the arrays, say what changed
#   powershell -ExecutionPolicy Bypass -File stubs\build_stubs.ps1 -Check   only compare: exit code 1 when one is out of date
#
# Needs only ACME (C:\Programming\ACME\acme.exe, or -Acme / the ACME environment variable) and the PowerShell every
# Windows has. Each stub is assembled to plain bytes (-f plain: the stub's own "* =" address is where it runs; the
# server adds the 2-byte load address itself). This ACME's "-f cbm" writes Intel HEX text after the load address
# instead of the program, so it can't be used for this.
param(
    [switch]$Check,
    [string]$Acme = $(if ($env:ACME) { $env:ACME } else { 'C:\Programming\ACME\acme.exe' })
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$target = Join-Path $here '..\VDRIVE\Protocol\PatchStubs.cs'
$stubs = @(@('patch_run.asm', 'RunStub'), @('patch_write.asm', 'WriteStub'))   # source -> array in PatchStubs.cs

if (-not (Test-Path $Acme)) { Write-Error "ACME not found at $Acme (use -Acme or set the ACME environment variable)" }

function Build-Stub([string]$source) {
    $out = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName())
    Push-Location $here
    try {
        & $Acme -f plain -o $out $source
        if ($LASTEXITCODE -ne 0) { Write-Error "ACME failed on $source" }
    } finally { Pop-Location }
    $bytes = [IO.File]::ReadAllBytes($out)
    Remove-Item $out
    return ,$bytes
}

function Format-Array([byte[]]$data, [string]$newline) {
    # same layout as the arrays already in the file: 16 bytes a line, a comma after every byte but the last
    $lines = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $data.Length; $i += 16) {
        $count = [Math]::Min(16, $data.Length - $i)
        $chunk = ($data[$i..($i + $count - 1)] | ForEach-Object { '0x{0:X2}' -f $_ }) -join ', '
        if ($i + 16 -lt $data.Length) { $chunk += ',' }
        $lines.Add('            ' + $chunk)
    }
    return ($lines -join $newline)
}

$raw = [IO.File]::ReadAllBytes($target)
$bom = $raw.Length -ge 3 -and $raw[0] -eq 0xEF -and $raw[1] -eq 0xBB -and $raw[2] -eq 0xBF
$skip = $(if ($bom) { 3 } else { 0 })
$text = [Text.Encoding]::UTF8.GetString($raw, $skip, $raw.Length - $skip)
$newline = $(if ($text.Contains("`r`n")) { "`r`n" } else { "`n" })
$stale = $false

foreach ($stub in $stubs) {
    $source = $stub[0]; $name = $stub[1]
    $data = Build-Stub $source
    $pattern = '(private static readonly byte\[\] ' + $name + ' =\s*\{\s*\r?\n)([\s\S]*?)(\r?\n\s*\};)'
    $m = [regex]::Match($text, $pattern)
    if (-not $m.Success) { Write-Error "PatchStubs.cs: array $name not found" }
    $old = [byte[]]([regex]::Matches($m.Groups[2].Value, '0x([0-9A-Fa-f]{2})') | ForEach-Object { [Convert]::ToByte($_.Groups[1].Value, 16) })
    if ([Linq.Enumerable]::SequenceEqual($old, $data)) {
        '{0,-16} {1}: up to date ({2} bytes)' -f $source, $name, $data.Length
        continue
    }
    $stale = $true
    '{0,-16} {1}: {2} -> {3} bytes{4}' -f $source, $name, $old.Length, $data.Length, $(if ($Check) { '' } else { ', updated' })
    if (-not $Check) {
        $body = Format-Array $data $newline
        $text = $text.Substring(0, $m.Groups[2].Index) + $body + $text.Substring($m.Groups[2].Index + $m.Groups[2].Length)
    }
}

if ($Check) { exit $(if ($stale) { 1 } else { 0 }) }
if ($stale) {
    $encoding = New-Object System.Text.UTF8Encoding($bom)
    [IO.File]::WriteAllText($target, $text, $encoding)
}
