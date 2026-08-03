using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using NAudio.Wave;

namespace AIChronicle
{
    /// <summary>
    /// 语音合成 Provider 抽象。新增付费引擎（Azure/OpenAI 等）时实现此接口并在 TtsService 中切换即可，
    /// 上层（聊天窗口）无需改动。
    /// </summary>
    public interface ITtsProvider
    {
        /// <summary>提供方显示名，如 "Edge TTS（免费）"。</summary>
        string Name { get; }

        /// <summary>是否已具备合成条件（免费引擎恒 true；付费引擎需用户配置好密钥）。</summary>
        bool IsConfigured { get; }

        /// <summary>合成文本为 mp3 音频字节。ratePercent 为语速偏移（-50 ~ +50，0 为正常）。失败返回 null。</summary>
        Task<byte[]?> SynthesizeAsync(string text, string voiceName, int ratePercent, CancellationToken ct);
    }

    /// <summary>
    /// 语音合成门面：触发合成 → 磁盘缓存 → 播放。负责打断/停止、性别音色映射，以及调用 Provider。
    /// 全部调用从主线程发起（聊天窗口回调），合成在后台线程完成，播放走 NAudio 独立线程。
    /// </summary>
    public static class TtsService
    {
        private static readonly object _playLock = new();
        private static IWavePlayer? _player;
        private static MemoryStream? _audioStream;
        private static Mp3FileReader? _reader;
        private static volatile int _playToken; // 打断令牌：每次播放/停止递增，旧任务完成后发现不匹配即放弃
        private static ITtsProvider? _provider;

        /// <summary>当前生效的 Provider。未来支持付费引擎时在此处按配置切换。</summary>
        public static ITtsProvider Provider => _provider ??= new EdgeTtsProvider();

        public static bool IsEnabled => MySettings.Instance?.TtsEnabled == true && Provider.IsConfigured;

        /// <summary>朗读一段文本（仅主线程调用）。自动打断上一次播放。speaker 可为 null（主菜单无主角时用默认男声）。</summary>
        public static void Speak(Hero speaker, string text)
        {
            try
            {
                if (!IsEnabled) return;

                var clean = CleanSpeechText(text);
                if (clean.Length == 0) return;

                var token = Interlocked.Increment(ref _playToken); // 新请求打断旧播放
                StopPlayback();

                var voice = MapVoice(speaker);
                var rate = MySettings.Instance?.TtsSpeed ?? 0;
                var campaignDir = PromptManager.CampaignDir;
                var cacheFile = string.IsNullOrEmpty(campaignDir)
                    ? null
                    : Path.Combine(campaignDir, "tts_cache", ComputeHash($"{voice}|{rate}|{clean}") + ".mp3");

                Task.Run(() =>
                {
                    byte[]? audio;
                    try
                    {
                        audio = TryLoadCache(cacheFile);
                        if (audio == null)
                        {
                            audio = Provider.SynthesizeAsync(clean, voice, rate, CancellationToken.None).GetAwaiter().GetResult();
                            if (audio != null && audio.Length > 0 && cacheFile != null)
                                TrySaveCache(cacheFile, audio);
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"TTS 合成失败（{voice}）：{ex.Message}");
                        return;
                    }

                    if (audio == null || audio.Length == 0) return;
                    if (token != _playToken) return; // 合成期间已被打断/窗口已关
                    Play(audio, token);
                });
            }
            catch (Exception ex)
            {
                // 防御：任何同步段异常都不允许逃逸（曾因 Hero.MainHero 主菜单 NRE 导致崩溃）
                DebugLogger.Log($"TTS Speak 异常：{ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>停止当前播放并使其后的合成结果失效（发消息打断、关窗口、切档时调用）。</summary>
        public static void Stop()
        {
            Interlocked.Increment(ref _playToken);
            StopPlayback();
        }

        private static void Play(byte[] audio, int token)
        {
            lock (_playLock)
            {
                if (token != _playToken) return;
                StopPlayback();
                try
                {
                    var ms = new MemoryStream(audio);
                    var reader = new Mp3FileReader(ms);
                    var player = new WaveOutEvent();
                    player.Volume = (MySettings.Instance?.TtsVolume ?? 80) / 100f;
                    player.Init(reader);
                    _audioStream = ms;
                    _reader = reader;
                    _player = player;
                    player.PlaybackStopped += (_, _) =>
                    {
                        lock (_playLock)
                        {
                            if (ReferenceEquals(_player, player)) _player = null;
                            if (ReferenceEquals(_reader, reader)) { _reader?.Dispose(); _reader = null; }
                            if (ReferenceEquals(_audioStream, ms)) { _audioStream?.Dispose(); _audioStream = null; }
                        }
                    };
                    player.Play();
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"TTS 播放失败：{ex.Message}");
                    StopPlayback();
                }
            }
        }

        private static void StopPlayback()
        {
            lock (_playLock)
            {
                try { _player?.Stop(); } catch { }
                try { _reader?.Dispose(); } catch { }
                try { _audioStream?.Dispose(); } catch { }
                _player = null;
                _reader = null;
                _audioStream = null;
            }
        }

        /// <summary>性别 → 音色映射。集中在此便于日后按年龄/性格扩展（可维护性）。</summary>
        private static string MapVoice(Hero hero)
        {
            // 女声：晓晓；男声：云希。均为 Edge TTS 中文神经音色。
            return hero != null && hero.IsFemale ? "zh-CN-XiaoxiaoNeural" : "zh-CN-YunxiNeural";
        }

        /// <summary>清洗 LLM 输出为适合朗读的纯文本：去 markdown 符号、控制标记、超长截断。</summary>
        private static string CleanSpeechText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var t = text.Trim();
            t = t.Replace("**", "").Replace("##", "").Replace("#", "")
                 .Replace("*", "").Replace("`", "").Replace(">", "").Replace("~", "")
                 .Replace("【", "").Replace("】", "").Replace("（系统）", "");
            if (t.StartsWith("[AI编年史]", StringComparison.Ordinal))
                t = t.Substring(9).Trim();
            if (t.Length > 300) // Edge 对超长文本不稳定，限制单次合成长度
                t = t.Substring(0, 300);
            return t.Trim();
        }

        private static byte[]? TryLoadCache(string? cacheFile)
        {
            try
            {
                if (cacheFile != null && File.Exists(cacheFile))
                    return File.ReadAllBytes(cacheFile);
            }
            catch { }
            return null;
        }

        private static void TrySaveCache(string cacheFile, byte[] audio)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
                File.WriteAllBytes(cacheFile, audio);
            }
            catch { }
        }

        private static string ComputeHash(string input)
        {
            using var md5 = MD5.Create();
            var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
