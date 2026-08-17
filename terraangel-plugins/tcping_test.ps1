$ErrorActionPreference = 'Continue'
$hostname = 'terraria.lightofd.cn'
$port = 7777

# 1) DNS 解析耗时
$sw = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $ips = [System.Net.Dns]::GetHostAddresses($hostname)
    $sw.Stop()
    Write-Output ("DNS OK: {0} -> {1}  ({2} ms)" -f $hostname, ($ips -join ', '), $sw.ElapsedMilliseconds)
} catch {
    $sw.Stop()
    Write-Output ("DNS FAIL: {0} ms - {1}" -f $sw.ElapsedMilliseconds, $_.Exception.Message)
    exit
}

# 2) TCP 连接耗时（3 次）
foreach ($try in 1..3) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $c = New-Object System.Net.Sockets.TcpClient
    try {
        $task = $c.ConnectAsync($hostname, $port)
        if ($task.Wait(10000)) {
            $sw.Stop()
            Write-Output ("TCP CONNECT OK (try {0}): {1} ms, connected={2}" -f $try, $sw.ElapsedMilliseconds, $c.Connected)
            # 3) 发送 ClientHello 后等待服务器第一个字节
            $stream = $c.GetStream()
            # 帧: [ushort len][1][string version]
            $ver = "Terraria319"
            $body = New-Object System.IO.MemoryStream
            $bw = New-Object System.IO.BinaryWriter($body, [System.Text.Encoding]::UTF8)
            $bw.Write([byte]1)
            $bw.Write($ver)
            $bw.Flush()
            $bodyBytes = $body.ToArray()
            $total = $bodyBytes.Length + 2
            $frame = New-Object byte[] $total
            $frame[0] = $total -band 0xFF
            $frame[1] = ($total -shr 8) -band 0xFF
            [System.Buffer]::BlockCopy($bodyBytes, 0, $frame, 2, $bodyBytes.Length)
            $sw2 = [System.Diagnostics.Stopwatch]::StartNew()
            $stream.Write($frame, 0, $frame.Length)
            $stream.Flush()
            $c.ReceiveTimeout = 8000
            $buf = New-Object byte[] 4
            $n = 0
            try {
                $n = $stream.Read($buf, 0, 4)
                $sw2.Stop()
                if ($n > 0) {
                    Write-Output ("SERVER RESPONSE OK: {0} bytes in {1} ms, first bytes: {2}" -f $n, $sw2.ElapsedMilliseconds, (($buf[0..($n-1)] | ForEach-Object { $_.ToString('X2') }) -join ' '))
                } else {
                    Write-Output ("SERVER EOF in {0} ms" -f $sw2.ElapsedMilliseconds)
                }
            } catch {
                $sw2.Stop()
                Write-Output ("SERVER READ FAIL: {0} ms - {1}" -f $sw2.ElapsedMilliseconds, $_.Exception.Message)
            }
            $bw.Dispose(); $body.Dispose()
        } else {
            $sw.Stop()
            Write-Output ("TCP CONNECT TIMEOUT (try {0}): >10000 ms" -f $try)
        }
    } catch {
        $sw.Stop()
        Write-Output ("TCP CONNECT FAIL (try {0}): {1} ms - {2}" -f $try, $sw.ElapsedMilliseconds, $_.Exception.Message)
    }
    $c.Close()
}
