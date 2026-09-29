using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Dafeiyv
{
    /// <summary>
    /// 通用 ICO 加载器：手动解析 ICO 文件（兼容裸 BGRA 帧 / BITMAPINFOHEADER 帧）。
    /// 帧的宽高直接取自 ICO 目录条目，不用猜测。
    /// </summary>
    public static class IconLoader
    {
        /// <summary>从 ico 文件加载图标（优先目标尺寸帧，否则取最近尺寸缩放）。失败回退系统图标。</summary>
        public static Icon LoadFromIco(string icoPath, int size = 32)
        {
            try
            {
                using var bmp = LoadBestFrame(icoPath, size);
                if (bmp != null)
                    return Icon.FromHandle(bmp.GetHicon());
            }
            catch { }
            return System.Drawing.SystemIcons.Application;
        }

        private static Bitmap? LoadBestFrame(string icoPath, int targetSize)
        {
            var frames = ParseIco(icoPath);
            if (frames.Count == 0) return null;

            Bitmap? best = null;
            int bestDiff = int.MaxValue;
            foreach (var f in frames)
            {
                int diff = Math.Abs(f.Width - targetSize);
                if (diff < bestDiff) { best = f; bestDiff = diff; }
            }
            best ??= frames[frames.Count - 1];

            if (best.Width == targetSize && best.Height == targetSize)
                return best;

            using (best)
            {
                var scaled = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.Transparent);
                    g.DrawImage(best, 0, 0, targetSize, targetSize);
                }
                return scaled;
            }
        }

        private static System.Collections.Generic.List<Bitmap> ParseIco(string path)
        {
            var result = new System.Collections.Generic.List<Bitmap>();
            try
            {
                byte[] data = File.ReadAllBytes(path);
                if (data.Length < 6) return result;
                int count = data[4] + data[5] * 256;
                for (int i = 0; i < count; i++)
                {
                    int e = 6 + i * 16;
                    if (e + 16 > data.Length) break;
                    int w = data[e] == 0 ? 256 : data[e];
                    int h = data[e + 1] == 0 ? 256 : data[e + 1];
                    int len = data[e + 8] + data[e + 9] * 256 + data[e + 10] * 65536 + data[e + 11] * 16777216;
                    int off = data[e + 12] + data[e + 13] * 256 + data[e + 14] * 65536 + data[e + 15] * 16777216;
                    if (off < 0 || off + len > data.Length || len <= 0) continue;

                    byte[] frame = new byte[len];
                    Array.Copy(data, off, frame, 0, len);

                    try
                    {
                        var bmp = DecodeFrame(frame, w, h);
                        if (bmp != null) result.Add(bmp);
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }

        private static Bitmap? DecodeFrame(byte[] frame, int width, int height)
        {
            if (width <= 0 || height <= 0 || width > 1024 || height > 1024) return null;
            try
            {
                // 标准 BITMAPINFOHEADER 帧（biSize = 40）
                if (frame.Length >= 40 && BitConverter.ToInt32(frame, 0) == 40)
                {
                    return DecodeBihFrame(frame, width, height);
                }

                // 裸 32bpp BGRA（自底向上）+ 尾部可带 AND 掩码
                int pxBytes = width * height * 4;
                if (frame.Length < pxBytes) return null;

                var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                var bd = bmp.LockBits(new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    unsafe
                    {
                        byte* dst = (byte*)bd.Scan0;
                        for (int y = 0; y < height; y++)
                        {
                            int srcRow = height - 1 - y;   // DIB 自底向上，翻转
                            int src = srcRow * width * 4;
                            int dstOff = y * bd.Stride;
                            for (int x = 0; x < width; x++)
                            {
                                int p = src + x * 4;
                                dst[dstOff + x * 4 + 0] = frame[p + 0];
                                dst[dstOff + x * 4 + 1] = frame[p + 1];
                                dst[dstOff + x * 4 + 2] = frame[p + 2];
                                dst[dstOff + x * 4 + 3] = frame[p + 3];
                            }
                        }
                    }
                }
                finally { bmp.UnlockBits(bd); }
                return (Bitmap)bmp.Clone();
            }
            catch { return null; }
        }

        /// <summary>解码带 BITMAPINFOHEADER 的帧（支持 32/24 bpp）</summary>
        private static Bitmap? DecodeBihFrame(byte[] f, int width, int height)
        {
            try
            {
                int bpp = BitConverter.ToUInt16(f, 14);
                int headerSize = BitConverter.ToInt32(f, 0);
                int pxOffset = headerSize;
                if (bpp <= 8)
                {
                    int pal = BitConverter.ToInt32(f, 32);
                    if (pal == 0) pal = 1 << bpp;
                    pxOffset += pal * 4;
                }
                int pxSize = bpp / 8;
                if (pxSize == 0 || pxSize > 4) return null;
                int rowStride = ((width * bpp + 31) / 32) * 4;
                if (pxOffset + (height - 1) * rowStride + width * pxSize > f.Length) return null;

                var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                var bd = bmp.LockBits(new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    unsafe
                    {
                        byte* dst = (byte*)bd.Scan0;
                        for (int y = 0; y < height; y++)
                        {
                            int srcRow = height - 1 - y;
                            int src = pxOffset + srcRow * rowStride;
                            int dstOff = y * bd.Stride;
                            for (int x = 0; x < width; x++)
                            {
                                int p = src + x * pxSize;
                                dst[dstOff + x * 4 + 0] = f[p + 0];
                                dst[dstOff + x * 4 + 1] = f[p + 1];
                                dst[dstOff + x * 4 + 2] = f[p + 2];
                                dst[dstOff + x * 4 + 3] = pxSize == 4 ? f[p + 3] : (byte)255;
                            }
                        }
                    }
                }
                finally { bmp.UnlockBits(bd); }
                return (Bitmap)bmp.Clone();
            }
            catch { return null; }
        }
    }
}
