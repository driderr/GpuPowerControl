# Clean ANSI escape codes from the most recent counters log
$logs = Get-ChildItem "Profiling\results\counters-*.log" -ErrorAction SilentlyContinue
if ($logs.Count -eq 0) {
    Write-Output "No log files found."
    exit
}

$latest = $logs | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$clean = (Get-Content $latest.FullName -Raw) -replace '[^\x20-\x7E\r\n]', ''
$cleanFile = "$($latest.FullName).clean.txt"
$clean | Out-File $cleanFile -Encoding UTF8
Write-Output "Cleaned: $cleanFile"