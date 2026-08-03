using System;
using System.Globalization;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AIChronicle
{
    /// <summary>
    /// Edge TTS（微软免费神经语音）实现。无 API Key、无额度限制，通过 WebSocket 调用微软 readaloud 端点。
    ///
    /// 为什么手写 WebSocket 而不用 ClientWebSocket：
    /// 1. .NET Framework 的 ClientWebSocket 禁止设置 User-Agent 等受限 header（抛 ArgumentException），
    ///    而 Edge 服务器校验浏览器 UA（缺 UA 返回 403）；
    /// 2. 协议只需连接一个固定端点，帧编解码简单，手写完全可控。
    /// 协议对齐 edge-tts 开源实现（Sec-MS-GEC 走 URL 查询参数、5 分钟取整 + SHA256、二进制帧前两字节为头长）。
    /// 若微软调整协议导致失效，模组静默降级（不播放），不影响其他功能。
    /// </summary>
    internal sealed class EdgeTtsProvider : ITtsProvider
    {
        public string Name => "Edge TTS（免费）";
        public bool IsConfigured => true;

        private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
        private const string SecMsGecVersion = "1-143.0.3650.75";
        private const long WinEpochSeconds = 11644473600; // 1601-01-01 到 1970-01-01 的秒数
        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0";

        public async Task<byte[]?> SynthesizeAsync(string text, string voiceName, int ratePercent, CancellationToken ct)
        {
            var connectionId = Guid.NewGuid().ToString("N");
            var path = "/consumer/speech/synthesize/readaloud/edge/v1" +
                       $"?TrustedClientToken={TrustedClientToken}" +
                       $"&ConnectionId={connectionId}" +
                       $"&Sec-MS-GEC={GenerateSecMsGec()}" +
                       $"&Sec-MS-GEC-Version={SecMsGecVersion}";

            var headers = new[]
            {
                "User-Agent: " + UserAgent,
                "Accept-Encoding: gzip, deflate, br, zstd",
                "Accept-Language: en-US,en;q=0.9",
                "Pragma: no-cache",
                "Cache-Control: no-cache",
                "Origin: chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold",
                "Cookie: muid=" + GenerateMuid() + ";"
            };

            try
            {
                using (var ws = await EdgeWsClient.ConnectAsync("speech.platform.bing.com", path, headers, 20000))
                {
                    // 1) speech.config（注意 JSON 花括号必须精确配平——多一个 } 会被服务器 Bad request 拒绝）
                    var config =
                        "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":" +
                        "{\"sentenceBoundaryEnabled\":\"true\",\"wordBoundaryEnabled\":\"false\"}," +
                        "\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}\r\n";
                    var ts = JsDateString();
                    await ws.SendTextAsync(
                        $"X-Timestamp:{ts}\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n{config}");

                    // 2) SSML
                    var rate = ratePercent >= 0 ? $"+{ratePercent}%" : $"{ratePercent}%";
                    var ssml = "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='zh-CN'>" +
                               $"<voice name='{voiceName}'><prosody pitch='+0Hz' rate='{rate}' volume='+0%'>" +
                               $"{EscapeXml(text)}</prosody></voice></speak>";
                    var ssmlFrame = $"X-RequestId:{Guid.NewGuid().ToString("N")}\r\n" +
                                    "Content-Type:application/ssml+xml\r\n" +
                                    $"X-Timestamp:{ts}Z\r\n" + // 微软 Edge 协议要求 Z 后缀
                                    "Path:ssml\r\n\r\n" +
                                    ssml;
                    await ws.SendTextAsync(ssmlFrame);

                    // 3) 接收音频（二进制帧前两字节为 header 长度；文本帧标记 turn.end）
                    var audio = new MemoryStream();
                    var scratch = new byte[8192];
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        cts.CancelAfter(TimeSpan.FromSeconds(25));
                        while (true)
                        {
                            var (opcode, payload) = await ws.ReceiveFrameAsync(scratch, cts.Token);
                            if (opcode == 0x8) break; // close
                            if (opcode == 0x2)
                            {
                                ExtractAudio(payload, audio);
                            }
                            else if (opcode == 0x1)
                            {
                                var headerEnd = IndexOf(payload, "\r\n\r\n");
                                if (headerEnd >= 0
                                    && Encoding.UTF8.GetString(payload, 0, headerEnd).IndexOf("Path:turn.end", StringComparison.Ordinal) >= 0)
                                    break;
                            }
                        }
                    }

                    var result = audio.ToArray();
                    return result.Length > 0 ? result : null;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Edge TTS 请求异常：{ex.Message}");
                return null;
            }
        }

        /// <summary>解析二进制音频帧：前两字节（大端）= header 长度，随后为 header，之后是音频数据。</summary>
        private static void ExtractAudio(byte[] data, MemoryStream audio)
        {
            if (data.Length < 2) return;
            var headerLength = (data[0] << 8) | data[1];
            if (headerLength < 2 || headerLength > data.Length) return;
            var header = Encoding.UTF8.GetString(data, 2, headerLength - 2);
            if (header.IndexOf("Path:audio", StringComparison.Ordinal) < 0) return;
            var bodyLength = data.Length - headerLength;
            if (bodyLength > 0) audio.Write(data, headerLength, bodyLength);
        }

        private static int IndexOf(byte[] data, string marker)
        {
            var needle = Encoding.UTF8.GetBytes(marker);
            if (needle.Length == 0 || data.Length < needle.Length) return -1;
            for (var i = 0; i <= data.Length - needle.Length; i++)
            {
                var match = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (data[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        private static string EscapeXml(string text)
        {
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                       .Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        private static string JsDateString()
        {
            return DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss",
                CultureInfo.InvariantCulture) + " GMT+0000 (Coordinated Universal Time)";
        }

        private static string GenerateMuid()
        {
            var bytes = new byte[16];
            new Random().NextBytes(bytes);
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }

        /// <summary>生成 Sec-MS-GEC（对齐 edge-tts）：unix 秒 + WIN_EPOCH → 向下取整到 5 分钟 → 100ns 单位，
        /// 与 TrustedClientToken 拼接后 SHA256 大写 hex。注意 ToString("0") 强制整数输出——
        /// double 默认 ToString 对 1.7e16 会输出科学计数法（1.753536E+16），会导致 token 与服务器校验不一致（403）。</summary>
        private static string GenerateSecMsGec()
        {
            double ticks = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            ticks += WinEpochSeconds;
            ticks -= ticks % 300;   // 5 分钟窗口
            ticks *= 1e7;
            var strToHash = Math.Round(ticks).ToString("0", CultureInfo.InvariantCulture) + TrustedClientToken;
            var hash = SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(strToHash));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }
    }

    /// <summary>
    /// 极简 WebSocket 客户端（TcpClient + SslStream 握手 + 手动帧编解码）。
    /// 仅用于与固定端点通信（Edge TTS 合成）：支持自定义任意 header（含 UA）、文本/二进制帧、
    /// ping/pong 应答。服务端帧不掩码；客户端帧掩码。不做分片重组（Edge 服务端消息均在单帧内）。
    /// </summary>
    internal sealed class EdgeWsClient : IDisposable
    {
        private readonly TcpClient _tcp;
        private readonly SslStream _ssl;
        private readonly byte[] _readBuf = new byte[1];

        private EdgeWsClient(TcpClient tcp, SslStream ssl)
        {
            _tcp = tcp;
            _ssl = ssl;
        }

        public static async Task<EdgeWsClient> ConnectAsync(string host, string path, string[] extraHeaders, int timeoutMs)
        {
            var tcp = new TcpClient();
            using (var ct = new CancellationTokenSource(timeoutMs))
                await tcp.ConnectAsync(host, 443).ConfigureAwait(false);

            var ssl = new SslStream(tcp.GetStream(), false);
            using (var ct = new CancellationTokenSource(timeoutMs))
                await ssl.AuthenticateAsClientAsync(host, null, SslProtocols.Tls12, false).ConfigureAwait(false);

            var keyBytes = new byte[16];
            new Random().NextBytes(keyBytes);
            var sb = new StringBuilder();
            sb.Append("GET ").Append(path).Append(" HTTP/1.1\r\n");
            sb.Append("Host: ").Append(host).Append("\r\n");
            foreach (var h in extraHeaders) sb.Append(h).Append("\r\n");
            sb.Append("Upgrade: websocket\r\n");
            sb.Append("Connection: Upgrade\r\n");
            sb.Append("Sec-WebSocket-Key: ").Append(Convert.ToBase64String(keyBytes)).Append("\r\n");
            sb.Append("Sec-WebSocket-Version: 13\r\n\r\n");

            var req = Encoding.UTF8.GetBytes(sb.ToString());
            await ssl.WriteAsync(req, 0, req.Length).ConfigureAwait(false);
            await ssl.FlushAsync().ConfigureAwait(false);

            var resp = await ReadHandshakeAsync(ssl, timeoutMs).ConfigureAwait(false);
            if (resp.IndexOf("101", StringComparison.Ordinal) < 0)
                throw new IOException("WebSocket handshake failed: " + resp.Trim());

            return new EdgeWsClient(tcp, ssl);
        }

        private static async Task<string> ReadHandshakeAsync(Stream s, int timeoutMs)
        {
            var buf = new byte[1024];
            var data = new MemoryStream();
            using (var ct = new CancellationTokenSource(timeoutMs))
            {
                while (true)
                {
                    int n = await s.ReadAsync(buf, 0, buf.Length, ct.Token).ConfigureAwait(false);
                    if (n <= 0) break;
                    data.Write(buf, 0, n);
                    if (Encoding.UTF8.GetString(data.ToArray()).IndexOf("\r\n\r\n", StringComparison.Ordinal) >= 0) break;
                }
            }
            return Encoding.UTF8.GetString(data.ToArray());
        }

        public Task SendTextAsync(string payload)
        {
            return SendFrameAsync(0x1, Encoding.UTF8.GetBytes(payload));
        }

        private async Task SendFrameAsync(byte opcode, byte[] payload)
        {
            using (var frame = new MemoryStream())
            {
                frame.WriteByte((byte)(0x80 | opcode));
                var len = payload.Length;
                if (len <= 125) frame.WriteByte((byte)(0x80 | len));
                else if (len <= 0xFFFF)
                {
                    frame.WriteByte(0x80 | 126);
                    frame.WriteByte((byte)(len >> 8));
                    frame.WriteByte((byte)(len & 0xFF));
                }
                else
                {
                    frame.WriteByte(0x80 | 127);
                    var l = (ulong)len;
                    for (int i = 7; i >= 0; i--) frame.WriteByte((byte)((l >> (8 * i)) & 0xFF));
                }
                var mask = new byte[4];
                new Random().NextBytes(mask);
                frame.Write(mask, 0, 4);
                for (int i = 0; i < len; i++) frame.WriteByte((byte)(payload[i] ^ mask[i % 4]));

                var bytes = frame.ToArray();
                await _ssl.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                await _ssl.FlushAsync().ConfigureAwait(false);
            }
        }

        /// <summary>读取一帧，返回 (opcode, payload)。自动应答 ping，处理 close。ct 用于取消。</summary>
        public async Task<(byte opcode, byte[] payload)> ReceiveFrameAsync(byte[] scratch, CancellationToken ct)
        {
            while (true)
            {
                var b0 = await ReadByteAsync(ct).ConfigureAwait(false);
                var b1 = await ReadByteAsync(ct).ConfigureAwait(false);
                var opcode = (byte)(b0 & 0x0F);
                ulong len = (ulong)(b1 & 0x7F);
                var masked = (b1 & 0x80) != 0;
                if (len == 126)
                {
                    var h = await ReadBytesAsync(2, ct).ConfigureAwait(false);
                    len = ((ulong)h[0] << 8) | h[1];
                }
                else if (len == 127)
                {
                    var h = await ReadBytesAsync(8, ct).ConfigureAwait(false);
                    len = 0;
                    for (int i = 0; i < 8; i++) len = (len << 8) | h[i];
                }
                byte[]? mask = null;
                if (masked) mask = await ReadBytesAsync(4, ct).ConfigureAwait(false);

                var payload = await ReadBytesAsync((int)len, ct).ConfigureAwait(false);
                if (masked && mask != null)
                    for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i % 4];

                if (opcode == 0x9) { await SendFrameAsync(0xA, payload).ConfigureAwait(false); continue; } // ping → pong
                if (opcode == 0x8) return (0x8, payload);                                              // close
                if (opcode == 0x1 || opcode == 0x2) return (opcode, payload);                           // text / binary
            }
        }

        private async Task<byte> ReadByteAsync(CancellationToken ct)
        {
            int n = await _ssl.ReadAsync(_readBuf, 0, 1, ct).ConfigureAwait(false);
            if (n <= 0) throw new IOException("WebSocket connection closed");
            return _readBuf[0];
        }

        private async Task<byte[]> ReadBytesAsync(int count, CancellationToken ct)
        {
            var result = new byte[count];
            var read = 0;
            while (read < count)
            {
                int n = await _ssl.ReadAsync(result, read, count - read, ct).ConfigureAwait(false);
                if (n <= 0) throw new IOException("WebSocket connection closed");
                read += n;
            }
            return result;
        }

        public void Dispose()
        {
            try { _ssl.Dispose(); } catch { }
            try { _tcp.Close(); } catch { }
        }
    }
}
