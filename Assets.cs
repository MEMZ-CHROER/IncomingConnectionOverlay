using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace IncomingConnectionOverlay;

/// <summary>
/// 加载资源（贴图、Kremlin 字体、音效）。
/// 资源以 EmbeddedResource 内嵌进 exe，运行时从程序集内存流加载；若嵌入资源缺失，
/// 回退到 exe 旁的 assets/ 目录（便于开发调试）。
///
/// 音效方案（纯内存，不写临时文件，降低杀软启发式误报）：
///   beep（窗口显示提示音，0.11s）+ doomshock⊕brightflash（双音叠加）
///   运行时从嵌入资源读出 PCM，拼接/混音成单个 wav，用 SoundPlayer 内存播放。
///   PlaySound 是单通道的，所以必须预先混成一条，否则多个 Play 会互相打断。
/// </summary>
public sealed class Assets : IDisposable
{
    public Bitmap CautionIcon { get; private set; }
    public Bitmap CautionIconBG { get; private set; }
    public Bitmap StripePattern { get; private set; }
    public Font TitleFont { get; private set; }
    public Font DetailFont { get; private set; }

    private SoundPlayer _activateSound;   // beep + 双音混合，一次内存播放
    private MemoryStream _activateStream;
    private PrivateFontCollection _fontCollection;

    /// <summary>加载全部资源。资源缺失时对应字段为 null，调用处需容忍。</summary>
    public static Assets Load()
    {
        var a = new Assets();
        a.CautionIcon = LoadBitmap("CautionIcon.png");
        a.CautionIconBG = LoadBitmap("CautionIconBG.png");
        a.StripePattern = LoadBitmap("StripePattern.png");

        a._fontCollection = new PrivateFontCollection();
        if (TryReadResource("kremlin-1.ttf", out byte[] ttf))
        {
            IntPtr mem = Marshal.AllocCoTaskMem(ttf.Length);
            try
            {
                Marshal.Copy(ttf, 0, mem, ttf.Length);
                a._fontCollection.AddMemoryFont(mem, ttf.Length);
                a.TitleFont = new Font(a._fontCollection.Families[0], 24f, FontStyle.Regular, GraphicsUnit.Pixel);
            }
            finally
            {
                Marshal.FreeCoTaskMem(mem);
            }
        }

        if (a.TitleFont == null)
        {
            a.TitleFont = new Font("Segoe UI", 24f, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        // Font7 位图字体未知，用等宽字体近似原版"终端小字"观感
        a.DetailFont = new Font("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Pixel);

        a.BuildActivateSound();
        return a;
    }

    /// <summary>窗口显示时播放：beep 提示音 + DoomShock/BrightFlash 双音叠加（对应原版 Activate()）。</summary>
    public void PlayActivateSounds()
    {
        try
        {
            _activateSound?.Play(); // 异步内存播放，不阻塞 UI
        }
        catch
        {
            // 播放失败不影响覆盖层
        }
    }

    // ================= 音效合成（纯内存） =================

    private void BuildActivateSound()
    {
        if (!TryReadResource("beep.wav", out byte[] beep)) return;
        if (!TryReadResource("doomshock.wav", out byte[] doom)) return;
        if (!TryReadResource("brightflash.wav", out byte[] bf)) return;

        // 解析 PCM（PCM 16bit，采样率 44100；beep 单声道，另两个立体声）
        (int channels, byte[] pcm) = ParsePcm16(beep);
        if (pcm == null)
        {
            return;
        }
        int beepCh = channels;

        byte[] doomPcm = ParsePcm16(doom).pcm;
        byte[] bfPcm = ParsePcm16(bf).pcm;
        if (doomPcm == null || bfPcm == null)
        {
            return;
        }

        // beep（单声道 → 立体声，与主音效声道一致）
        byte[] beepStereo = beepCh == 1 ? MonoToStereo16(pcm) : pcm;
        // doomshock ⊕ brightflash 叠加（立体声 16bit 饱和相加）
        byte[] mixed = MixStereo16(doomPcm, bfPcm);
        // 拼接：beep 先（窗口显示提示音），随后双音叠加段
        byte[] all = new byte[beepStereo.Length + mixed.Length];
        Buffer.BlockCopy(beepStereo, 0, all, 0, beepStereo.Length);
        Buffer.BlockCopy(mixed, 0, all, beepStereo.Length, mixed.Length);

        byte[] wav = BuildWavContainer(all);
        _activateStream = new MemoryStream(wav, writable: false);
        _activateSound = new SoundPlayer(_activateStream);
        try
        {
            _activateSound.Load(); // 预加载，避免首播卡顿
        }
        catch
        {
            _activateSound = null;
        }
    }

    /// <summary>调试用：把合成的激活音效 wav 写到文件，便于检查拼接/混音是否正确。</summary>
    public void DumpActivateSound(string path)
    {
        if (_activateStream == null)
        {
            return;
        }
        File.WriteAllBytes(path, _activateStream.ToArray());
    }

    private static (int channels, byte[] pcm) ParsePcm16(byte[] wav)
    {
        int pos = 12;
        ushort fmtTag = 0, channels = 0, bits = 0;
        byte[] data = null;
        while (pos + 8 <= wav.Length)
        {
            string id = Encoding.ASCII.GetString(wav, pos, 4);
            int sz = BitConverter.ToInt32(wav, pos + 4);
            if (id == "fmt ")
            {
                fmtTag = BitConverter.ToUInt16(wav, pos + 8);
                channels = BitConverter.ToUInt16(wav, pos + 10);
                bits = BitConverter.ToUInt16(wav, pos + 22);
            }
            else if (id == "data")
            {
                data = new byte[sz];
                Array.Copy(wav, pos + 8, data, 0, sz);
                break;
            }
            pos += 8 + sz;
        }
        if (fmtTag != 1 || bits != 16 || data == null)
        {
            return (0, null);
        }
        return (channels, data);
    }

    private static byte[] MonoToStereo16(byte[] mono)
    {
        int n = mono.Length / 2;
        byte[] stereo = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            short s = BitConverter.ToInt16(mono, i * 2);
            byte[] b = BitConverter.GetBytes(s);
            stereo[i * 4] = b[0];
            stereo[i * 4 + 1] = b[1];
            stereo[i * 4 + 2] = b[0];
            stereo[i * 4 + 3] = b[1];
        }
        return stereo;
    }

    private static byte[] MixStereo16(byte[] a, byte[] b)
    {
        int len = Math.Max(a.Length, b.Length);
        byte[] outBuf = new byte[len];
        for (int i = 0; i < len; i += 2)
        {
            short sa = i < a.Length ? BitConverter.ToInt16(a, i) : (short)0;
            short sb = i < b.Length ? BitConverter.ToInt16(b, i) : (short)0;
            int sum = sa + sb;
            if (sum > short.MaxValue)
            {
                sum = short.MaxValue;
            }
            else if (sum < short.MinValue)
            {
                sum = short.MinValue;
            }
            outBuf[i] = (byte)(sum & 0xFF);
            outBuf[i + 1] = (byte)((sum >> 8) & 0xFF);
        }
        return outBuf;
    }

    /// <summary>按目标格式（PCM 44100Hz 立体声 16bit）封装 wav 容器。</summary>
    private static byte[] BuildWavContainer(byte[] pcm)
    {
        const ushort channels = 2, bits = 16;
        const uint sampleRate = 44100;
        ushort blockAlign = (ushort)(channels * bits / 8);
        uint avg = sampleRate * blockAlign;

        using var ms = new MemoryStream(pcm.Length + 44);
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + pcm.Length);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);
        w.Write((ushort)1);     // PCM
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(avg);
        w.Write(blockAlign);
        w.Write(bits);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(pcm.Length);
        w.Write(pcm);
        w.Flush();
        return ms.ToArray();
    }

    // ================= 资源读取：嵌入优先，外部 assets/ 目录回退 =================

    /// <summary>按文件名查找嵌入资源流；找不到时回退 exe 旁 assets/ 目录文件。</summary>
    private static Stream OpenResource(string fileName)
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        string resName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));
        if (resName != null)
        {
            return asm.GetManifestResourceStream(resName);
        }

        string p = Path.Combine(AppContext.BaseDirectory, "assets", fileName);
        return File.Exists(p) ? File.OpenRead(p) : null;
    }

    private static bool TryReadResource(string fileName, out byte[] bytes)
    {
        using Stream s = OpenResource(fileName);
        if (s == null)
        {
            bytes = null;
            return false;
        }
        bytes = new byte[s.Length];
        int off = 0;
        while (off < bytes.Length)
        {
            int n = s.Read(bytes, off, bytes.Length - off);
            if (n <= 0)
            {
                break;
            }
            off += n;
        }
        return off == bytes.Length;
    }

    private static Bitmap LoadBitmap(string fileName)
    {
        using Stream s = OpenResource(fileName);
        return s != null ? new Bitmap(s) : null;
    }

    public void Dispose()
    {
        CautionIcon?.Dispose();
        CautionIconBG?.Dispose();
        StripePattern?.Dispose();
        TitleFont?.Dispose();
        DetailFont?.Dispose();
        _fontCollection?.Dispose();

        try
        {
            _activateSound?.Dispose();
            _activateStream?.Dispose();
        }
        catch
        {
            // 释放失败无碍
        }
    }
}
