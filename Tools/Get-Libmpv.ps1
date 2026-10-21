param(
    [Parameter(Mandatory = $true)][ValidateSet('x64', 'x86')][string]$Arch,
    [Parameter(Mandatory = $true)][string]$CodecDir,
    # 可用環境變數 LIBMPV_BASE_URL 改成鏡像站或本機測試伺服器
    [string]$BaseUrl = $(if ($env:LIBMPV_BASE_URL) { $env:LIBMPV_BASE_URL } else { 'https://github.com/JordanChiang/UltimateKtv/releases/download/deps-libmpv' })
)
# 下載 libmpv-2.dll 到 Codec / Codec_x86 (檔案超過 GitHub 單檔 100MB 限制，不放進 repo)。
# 來源：本專案 GitHub Release「deps-libmpv」(pre-release，不會被 Updater 當成更新包) 的 .dll.gz 附件。
# 已存在就不動；下載失敗只發出警告，不讓編譯失敗 (沒有 libmpv 時程式會退回 DirectShow)。
# 更換 libmpv 版本：把新的 .dll.gz 傳到同一個 release，再改下面的檔名與 SHA256 (解壓後 DLL 的雜湊)。
$ErrorActionPreference = 'Stop'

$pins = @{
    # shinchiro mpv-dev-x86_64-20261018 (mpv v0.41.0-1205-g01e2533f3)
    'x64' = @{ File = 'libmpv-2-x64-20261018.dll.gz'; Sha256 = '82BAE3B6BF7BB737AFC9B52D590B477983C715F3C63C1A361F9EE06523DC5DC2' }
    # shinchiro mpv-dev-i686-20260412 (mpv v0.41.0-458-g062f4bf04)；較新的 i686 版會在 OpenSSL 初始化時 abort
    'x86' = @{ File = 'libmpv-2-x86-20260412.dll.gz'; Sha256 = '9CCCC64A2EFC038CEB5BD154C33C10C970DEAD6DAF7B5FE27BC16C0EF759B489' }
}

$target = Join-Path $CodecDir 'libmpv-2.dll'
if (Test-Path -LiteralPath $target) { exit 0 }

$pin = $pins[$Arch]
$url = "$BaseUrl/$($pin.File)"
$gz = Join-Path ([IO.Path]::GetTempPath()) ("{0}.{1}" -f $pin.File, [Guid]::NewGuid().ToString('N'))
$dll = "$gz.dll"
try {
    Write-Host "Get-Libmpv: downloading $url"
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue' # PowerShell 5 的進度列會讓下載慢好幾倍
    Invoke-WebRequest -Uri $url -OutFile $gz -UseBasicParsing

    $in = [IO.File]::OpenRead($gz)
    try {
        $out = [IO.File]::Create($dll)
        try {
            $unzip = New-Object IO.Compression.GZipStream($in, [IO.Compression.CompressionMode]::Decompress)
            $unzip.CopyTo($out)
            $unzip.Dispose()
        }
        finally { $out.Dispose() }
    }
    finally { $in.Dispose() }

    $hash = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    if ($hash -ne $pin.Sha256) { throw "SHA256 mismatch: expected $($pin.Sha256), got $hash" }

    New-Item -ItemType Directory -Force -Path $CodecDir | Out-Null
    Move-Item -LiteralPath $dll -Destination $target
    Write-Host "Get-Libmpv: saved $target"
}
catch {
    Write-Warning "Get-Libmpv: could not get libmpv for $Arch ($($_.Exception.Message)). The libmpv engine will be unavailable; download $url manually, gunzip it and save it as $target."
}
finally {
    Remove-Item -LiteralPath $gz, $dll -Force -ErrorAction SilentlyContinue
}
exit 0
