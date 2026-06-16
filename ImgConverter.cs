using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;
using GunboundTools.Archive;
using GunboundTools.Decoding;
using GunboundTools.Encoding;
using GunboundTools.Imaging;

namespace GbImgTool
{
    // Toda la logica .img <-> PNG (la misma del convertidor de consola), expuesta para la GUI.
    public static class ImgConverter
    {
        public const int MAGENTA = unchecked((int)0xFFFF00FF);   // magenta opaco = transparente
        public static readonly Regex NameRx = new Regex(@"__(\d+)__\d+x\d+__c(-?\d+)_(-?\d+)__t(\d+)");

        // Lee el .img (replica LoadImages SIN el Dispatcher de WPF).
        public static List<GunboundImg> ParseImg(string path)
        {
            var list = new List<GunboundImg>();
            byte[] data = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                ms.Position = 4;
                int count = r.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var img = new GunboundImg
                    {
                        ImgTransparencyType = r.ReadInt32(),
                        Width = r.ReadInt32(),
                        Height = r.ReadInt32(),
                        XCenter = r.ReadInt32(),
                        YCenter = r.ReadInt32(),
                        FlippedX = r.ReadInt32(),
                        FlippedY = r.ReadInt32(),
                        Unknown1 = r.ReadInt32(),
                        Unknown2 = r.ReadInt32(),
                        Lenght = r.ReadInt32(),
                        Name = "img" + i
                    };
                    img.Data = r.ReadBytes(img.Lenght);
                    if (img.FlippedX == 1 && img.FlippedY == 0)
                    {
                        r.ReadInt32();
                        using (var output = new MemoryStream())
                        using (var w = new BinaryWriter(output))
                        {
                            w.Write(img.Data);
                            w.Write(img.Lenght);
                            w.Write(r.ReadBytes(img.Lenght));
                            img.Data = output.ToArray();
                        }
                    }
                    list.Add(img);
                }
            }
            return list;
        }

        public static Bitmap DecodeFrame(GunboundImg im)
        {
            return new GunboundImageDecoder(im) { AlphaColor = MAGENTA }.GetImage();
        }

        // .img -> carpeta de PNGs (uno por frame). Devuelve cuantos exporto.
        public static int ExportFrames(string imgPath, string outDir)
        {
            Directory.CreateDirectory(outDir);
            var images = ParseImg(imgPath);
            string baseName = Path.GetFileNameWithoutExtension(imgPath);
            int ok = 0;
            for (int i = 0; i < images.Count; i++)
            {
                var im = images[i];
                if (im.Width <= 0 || im.Height <= 0 || im.Width > 8192 || im.Height > 8192) continue;
                using (var bmp = DecodeFrame(im))
                {
                    string outPath = Path.Combine(outDir, baseName + "__" + i + "__" + im.Width + "x" + im.Height +
                        "__c" + im.XCenter + "_" + im.YCenter + "__t" + (im.ImgTransparencyType & 0xFF) + ".png");
                    bmp.Save(outPath, ImageFormat.Png);
                    ok++;
                }
            }
            return ok;
        }

        // .img -> UNA hoja PNG (cuadricula) + .meta.txt. Devuelve un texto descriptivo.
        public static string BuildSheet(string imgPath, string outPng, int cols, out int tileW, out int tileH)
        {
            if (cols < 1) cols = 1;
            var images = ParseImg(imgPath);
            int n = images.Count;
            int maxW = 1, maxH = 1;
            foreach (var im in images) { if (im.Width > maxW) maxW = im.Width; if (im.Height > maxH) maxH = im.Height; }
            int rows = (n + cols - 1) / cols;
            var meta = new List<string> { "cols=" + cols, "cell=" + maxW + "x" + maxH, "count=" + n };
            using (var sheet = new Bitmap(cols * maxW, rows * maxH, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(sheet))
                {
                    g.Clear(Color.Transparent);
                    for (int i = 0; i < n; i++)
                    {
                        var im = images[i];
                        if (im.Width <= 0 || im.Height <= 0) { meta.Add(i + " 0 0 0 0 0"); continue; }
                        int gx = (i % cols) * maxW, gy = (i / cols) * maxH;
                        using (var bmp = DecodeFrame(im)) g.DrawImageUnscaled(bmp, gx, gy);
                        meta.Add(i + " " + im.Width + " " + im.Height + " " + im.XCenter + " " + im.YCenter + " " + (im.ImgTransparencyType & 0xFF));
                    }
                }
                sheet.Save(outPng, ImageFormat.Png);
            }
            File.WriteAllLines(outPng + ".meta.txt", meta);
            tileW = maxW; tileH = maxH;
            return cols + " columnas x " + rows + " filas, celda " + maxW + "x" + maxH + ", " + n + " frames";
        }

        // Carpeta de PNGs -> .img multi-frame.
        public static int ImportDir(string dir, string outImg)
        {
            var pngs = new List<string>(Directory.GetFiles(dir, "*.png"));
            if (pngs.Count == 0) throw new Exception("No hay PNG en esa carpeta.");
            pngs.Sort((a, b) => FrameIndex(a).CompareTo(FrameIndex(b)));
            var file = new GunboundImageFile(outImg);
            file.Create();
            foreach (var p in pngs)
            {
                var m = NameRx.Match(Path.GetFileName(p));
                int xc = m.Success ? int.Parse(m.Groups[2].Value) : 0;
                int yc = m.Success ? int.Parse(m.Groups[3].Value) : 0;
                int t = m.Success ? int.Parse(m.Groups[4].Value) : 1;
                using (var bmp = LoadPng(p)) file.AddImage(EncodeBitmap(bmp, t, xc, yc));
            }
            file.Save();
            return pngs.Count;
        }

        // Hoja PNG editada -> .img multi-frame (usa el .meta.txt de al lado).
        public static int Unsheet(string sheetPng, string outImg)
        {
            string metaPath = sheetPng + ".meta.txt";
            if (!File.Exists(metaPath)) throw new Exception("Falta el archivo .meta.txt junto a la hoja:\n" + metaPath);
            int cols = 0, cellW = 0, cellH = 0;
            var fW = new List<int>(); var fH = new List<int>();
            var fcx = new List<int>(); var fcy = new List<int>(); var ft = new List<int>();
            foreach (var raw in File.ReadAllLines(metaPath))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("cols=")) cols = int.Parse(line.Substring(5));
                else if (line.StartsWith("cell=")) { var p = line.Substring(5).Split('x'); cellW = int.Parse(p[0]); cellH = int.Parse(p[1]); }
                else if (line.StartsWith("count=")) { }
                else { var p = line.Split(' '); if (p.Length >= 6) { fW.Add(int.Parse(p[1])); fH.Add(int.Parse(p[2])); fcx.Add(int.Parse(p[3])); fcy.Add(int.Parse(p[4])); ft.Add(int.Parse(p[5])); } }
            }
            if (cols < 1 || cellW < 1 || cellH < 1) throw new Exception("El .meta.txt es invalido.");
            var file = new GunboundImageFile(outImg);
            file.Create();
            int ok = 0;
            using (var sheet = new Bitmap(sheetPng))
            {
                for (int i = 0; i < fW.Count; i++)
                {
                    int ow = fW[i], oh = fH[i];
                    if (ow <= 0 || oh <= 0) continue;
                    int gx = (i % cols) * cellW, gy = (i / cols) * cellH;
                    using (var frame = new Bitmap(ow, oh, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(frame))
                            g.DrawImage(sheet, new Rectangle(0, 0, ow, oh), new Rectangle(gx, gy, ow, oh), GraphicsUnit.Pixel);
                        file.AddImage(EncodeBitmap(frame, ft[i], fcx[i], fcy[i]));
                        ok++;
                    }
                }
            }
            file.Save();
            return ok;
        }

        static Bitmap LoadPng(string png)
        {
            using (var loaded = new Bitmap(png))
            {
                var bmp = new Bitmap(loaded.Width, loaded.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp)) g.DrawImageUnscaled(loaded, 0, 0);
                return bmp;
            }
        }

        static GunboundImg EncodeBitmap(Bitmap bmp, int t, int xc, int yc)
        {
            TransparencyType tt = t == 0 ? TransparencyType.None : t == 2 ? TransparencyType.Alpha : TransparencyType.Simple;
            return new GunboundImageEncoder(bmp, tt, xc, yc) { AlphaColor = 0x00FF00FF }.Encode();
        }

        static int FrameIndex(string path)
        {
            var m = NameRx.Match(Path.GetFileName(path));
            return m.Success ? int.Parse(m.Groups[1].Value) : int.MaxValue;
        }
    }
}
