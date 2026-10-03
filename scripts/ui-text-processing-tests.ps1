param([Parameter(Mandatory)][int]$AppPid)
$ErrorActionPreference = 'Stop'
$env:Path=[Environment]::GetEnvironmentVariable('Path','Machine')+';'+[Environment]::GetEnvironmentVariable('Path','User')
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskResults = [Collections.Generic.List[object]]::new()
$taskWindows = & winapp ui list-windows -a $AppPid --json | ConvertFrom-Json
$taskHwnd = ($taskWindows | Where-Object { $_.title -like 'AsciiStudio*' -and $_.ownerHwnd -eq 0 } | Select-Object -First 1).hwnd
if (!$taskHwnd) { throw 'Main window not found.' }
function UI {
    $taskResponse = & winapp ui @args -w $taskHwnd 2>&1
    if ($LASTEXITCODE) { throw ($taskResponse -join "`n") }
    return $taskResponse
}
function Choose([string]$Id, [string]$Value) {
    UI invoke $Id --action expand | Out-Null
    $taskChoice = (UI search $Value --type ListItem --json | ConvertFrom-Json).matches | Where-Object { $_.name -eq $Value } | Select-Object -First 1
    if (!$taskChoice) { throw "Missing choice: $Value" }
    UI invoke $taskChoice.selector --action select | Out-Null
}
function Check([string]$Name, [scriptblock]$Action) {
    try { & $Action; $taskResults.Add([pscustomobject]@{name=$Name;status='PASS'}) }
    catch { $taskResults.Add([pscustomobject]@{name=$Name;status='FAIL';detail=$_.Exception.Message}) }
}
function Encode([string]$Category, [string]$Name, [string]$Text, [string]$Expected) {
    Choose CryptoCategory $Category; Choose CryptoAlgorithm $Name
    UI set-value CryptoInput $Text | Out-Null; UI invoke CryptoEncrypt | Out-Null
    UI wait-for CryptoOutput --value $Expected -t 5000 | Out-Null
}
UI invoke NavCrypto | Out-Null
Check 'UTF-8 Hex and BOM' {
    Encode '字符编码' 'UTF-8' '中' 'E4B8AD'
    UI invoke CryptoBom --action toggle-on | Out-Null; UI invoke CryptoEncrypt | Out-Null
    UI wait-for CryptoOutput --value 'EFBBBFE4B8AD' -t 5000 | Out-Null
    Choose CryptoByteFormat 'Base64'; UI invoke CryptoEncrypt | Out-Null
    UI wait-for CryptoOutput --value '77u/5Lit' -t 5000 | Out-Null
    UI invoke CryptoBom --action toggle-off | Out-Null; Choose CryptoByteFormat 'Hex'
}
Check 'Strict ASCII failure preserves result' {
    Encode '字符编码' 'ASCII' 'ABC' '414243'
    UI set-value CryptoInput '中文' | Out-Null; UI invoke CryptoEncrypt | Out-Null
    UI wait-for CryptoEncrypt -p IsEnabled --value True -t 5000 | Out-Null
    UI wait-for CryptoOutput --value '414243' -t 1000 | Out-Null
    UI wait-for 'Unable to translate Unicode character' --type Text -t 5000 | Out-Null
}
foreach ($taskCase in @(
    @('字符编码','GBK','中','D6D0'), @('字符编码','Big5','中','A4A4'), @('字符编码','Shift_JIS','あ','82A0'),
    @('Unicode 与转义','Unicode \uXXXX','中','\u4E2D'), @('Unicode 与转义','Unicode \UXXXXXXXX','🙂','\U0001F642'),
    @('Unicode 与转义','HTML 十进制实体','M','&#77;'), @('Unicode 与转义','HTML 十六进制实体','M','&#x4D;'),
    @('Unicode 与转义','JSON 转义','abc','"abc"'), @('Unicode 与转义','Punycode','bücher','bcher-kva'),
    @('二进制转文本','Base32hex','foobar','CPNMUOJ1E8======'), @('二进制转文本','Base58','Hello World','JxF12TrwUP45BMd'),
    @('二进制转文本','Ascii85','Man','<~9jqo~>'), @('二进制转文本','Quoted-printable','é','=C3=A9'),
    @('校验','CRC32','123456789','cbf43926'), @('校验','Adler-32','Wikipedia','11e60398')
)) { Check $taskCase[1] { Encode @taskCase } }
foreach ($taskCase in @(@('NFC',"e$([char]0x301)",'é'), @('NFKC','Ａ①','A1'))) {
    Check $taskCase[0] {
        Encode 'Unicode 与转义' $taskCase[0] $taskCase[1] $taskCase[2]
        UI wait-for CryptoDecrypt -p IsEnabled --value False -t 1000 | Out-Null
    }
}
foreach ($taskMethod in @('GZIP','Zlib','Brotli')) {
    Check "$taskMethod round trip" {
        Choose CryptoCategory '压缩'; Choose CryptoAlgorithm $taskMethod
        UI set-value CryptoInput '测试 ASCII' | Out-Null; UI invoke CryptoEncrypt | Out-Null
        UI wait-for CryptoEncrypt -p IsEnabled --value True -t 5000 | Out-Null
        $taskEncoded = (UI get-value CryptoOutput) -join "`n"
        if (!$taskEncoded -or $taskEncoded -eq 'A1') { throw 'Compression produced no new output.' }
        UI set-value CryptoInput $taskEncoded | Out-Null; UI invoke CryptoDecrypt | Out-Null
        UI wait-for CryptoOutput --value '测试 ASCII' -t 5000 | Out-Null
    }
}
Check 'Checksum has no reverse operation' {
    Choose CryptoCategory '校验'; Choose CryptoAlgorithm 'CRC32'
    UI wait-for CryptoDecrypt -p IsEnabled --value False -t 1000 | Out-Null
}
$taskReport = Join-Path $taskRoot 'artifacts/ui-text-processing-results.json'
$taskResults | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $taskReport -Encoding utf8
$taskResults | Format-Table -AutoSize
if ($taskResults.status -contains 'FAIL') { throw "UI checks failed; see $taskReport" }
