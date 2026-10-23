# Export the files listed in <Out>\names.txt (written by casc_sample.py) from DiIiS's bundled 2.7.4 MPQs to
# <Out>\mpq\<Group>\<file>, for comparing with the installed game's copies (cmp_formats.py, diff_offsets.py).
param(
    [Parameter(Mandatory)][string]$Out,
    [string]$DiIiS = (Join-Path $PSScriptRoot "..\..\src\DiIiS-NA")
)

Add-Type -Path (Join-Path $DiIiS "libs\CrystalMpq.dll")
$fs = New-Object CrystalMpq.MpqFileSystem
# newest archive first, so a file patched in a later archive wins
foreach ($m in 'Core4.mpq', 'Core3.mpq', 'Core2.mpq', 'Core1.mpq', 'Core.mpq') {
    $path = Join-Path $DiIiS "DataBase\MPQ\$m"
    if (Test-Path $path) {
        $fs.Archives.Add((New-Object CrystalMpq.MpqArchive((New-Object IO.FileStream($path, 'Open', 'Read', 'ReadWrite')), $true)))
    }
}

$found = 0; $missing = 0
foreach ($name in Get-Content (Join-Path $Out "names.txt")) {
    $f = $fs.FindFile($name)
    if (-not $f) { $missing++; continue }
    $st = $f.Open(); $b = New-Object byte[] $st.Length; [void]$st.Read($b, 0, $b.Length); $st.Close()
    $dest = Join-Path (Join-Path $Out "mpq") $name
    [void](New-Item -ItemType Directory -Force (Split-Path $dest))
    [IO.File]::WriteAllBytes($dest, $b)
    $found++
}
"found $found, missing $missing (missing ones are new in the installed game)"
