using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GbImgTool
{
    public class MainForm : Form
    {
        string loadedImgPath = null;
        PictureBox preview;
        Label info;
        TextBox log;

        public MainForm()
        {
            Text = "GB Img Tool  -  .img <-> PNG (para Pyxel Edit)";
            Width = 700; Height = 620; StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9);

            // ---- Paso 1 ----
            var b1 = new Button { Text = "1)  Abrir un .img", Left = 12, Top = 12, Width = 220, Height = 38 };
            b1.Click += (s, e) => OpenImg();
            Controls.Add(b1);

            var bGal = new Button { Text = "📂  Previsualizar CARPETA (galeria)", Left = 242, Top = 12, Width = 300, Height = 38, BackColor = Color.FromArgb(220, 235, 255) };
            bGal.Click += (s, e) => new GalleryForm().Show();
            Controls.Add(bGal);

            preview = new PictureBox { Left = 12, Top = 58, Width = 210, Height = 210, BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(45, 45, 45) };
            Controls.Add(preview);

            info = new Label { Left = 232, Top = 58, Width = 440, Height = 210, AutoSize = false, Text = "Abre un .img para ver su info y un preview del primer frame." };
            Controls.Add(info);

            // ---- Paso 2 ----
            var lbl2 = new Label { Left = 12, Top = 278, Width = 660, Height = 20, Text = "Paso 2  —  EXTRAER para editar en Pyxel Edit:", Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            Controls.Add(lbl2);
            var b2a = new Button { Text = "(*)  Exportar a HOJA (1 solo PNG)", Left = 12, Top = 300, Width = 320, Height = 36 };
            b2a.Click += (s, e) => ExportSheet();
            Controls.Add(b2a);
            var b2b = new Button { Text = "Exportar a PNGs sueltos (carpeta)", Left = 342, Top = 300, Width = 330, Height = 36 };
            b2b.Click += (s, e) => ExportPngs();
            Controls.Add(b2b);

            // ---- Paso 3 ----
            var lbl3 = new Label { Left = 12, Top = 346, Width = 660, Height = 20, Text = "Paso 3  —  VOLVER a .img (despues de editar y GUARDAR en Pyxel Edit):", Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            Controls.Add(lbl3);
            var b3a = new Button { Text = "(*)  HOJA editada   ->   .img", Left = 12, Top = 368, Width = 320, Height = 36 };
            b3a.Click += (s, e) => SheetToImg();
            Controls.Add(b3a);
            var b3b = new Button { Text = "Carpeta de PNGs   ->   .img", Left = 342, Top = 368, Width = 330, Height = 36 };
            b3b.Click += (s, e) => DirToImg();
            Controls.Add(b3b);

            var note = new Label { Left = 12, Top = 410, Width = 660, Height = 36, AutoSize = false, ForeColor = Color.Firebrick,
                Text = "Paso 4 (a mano, ya lo sabes): mete el .img a graphics.xfs con XFS2 -> Delete viejo -> Import File -> Save. El nombre debe ser EXACTO (ej. mf00001.img)." };
            Controls.Add(note);

            log = new TextBox { Left = 12, Top = 450, Width = 660, Height = 122, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.Black, ForeColor = Color.LightGreen, Font = new Font("Consolas", 9) };
            Controls.Add(log);
            Log("Listo. Empieza por el boton 1) Abrir un .img");
            Log("Recuerda: MAGENTA (rosa) = zona transparente. Deja magenta lo que quieras ver-a-traves.");
        }

        void Log(string msg) { log.AppendText(msg + Environment.NewLine); }

        void OpenImg()
        {
            using (var d = new OpenFileDialog { Filter = "GunBound image (*.img)|*.img" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var frames = ImgConverter.ParseImg(d.FileName);
                    loadedImgPath = d.FileName;
                    if (preview.Image != null) { preview.Image.Dispose(); preview.Image = null; }
                    if (frames.Count > 0) preview.Image = ImgConverter.DecodeFrame(frames[0]);
                    int maxW = 0, maxH = 0;
                    foreach (var f in frames) { if (f.Width > maxW) maxW = f.Width; if (f.Height > maxH) maxH = f.Height; }
                    int t0 = frames.Count > 0 ? (frames[0].ImgTransparencyType & 0xFF) : 0;
                    info.Text = "Archivo:  " + Path.GetFileName(d.FileName) +
                        "\r\nFrames:   " + frames.Count +
                        "\r\nTamano max:  " + maxW + " x " + maxH + " px" +
                        "\r\nTipo transparencia:  " + t0 +
                        "\r\n\r\nPreview = primer frame (magenta = transparente).";
                    Log("Abierto: " + Path.GetFileName(d.FileName) + "  (" + frames.Count + " frames)");
                }
                catch (Exception ex) { Log("ERROR al abrir: " + ex.Message); }
            }
        }

        void ExportSheet()
        {
            if (loadedImgPath == null) { Log("Primero abre un .img (boton 1)."); return; }
            using (var d = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = Path.GetFileNameWithoutExtension(loadedImgPath) + "_hoja.png" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                try
                {
                    int tw, th;
                    string r = ImgConverter.BuildSheet(loadedImgPath, d.FileName, 6, out tw, out th);
                    Log("HOJA creada: " + d.FileName);
                    Log("   " + r);
                    Log("   >> En Pyxel Edit usa tile width=" + tw + ", height=" + th + ".");
                    Log("   >> Edita, deja MAGENTA = transparente, y Guarda SOBRE el mismo PNG (no lo renombres).");
                    Log("   >> No borres el .meta.txt que quedo al lado.");
                }
                catch (Exception ex) { Log("ERROR hoja: " + ex.Message); }
            }
        }

        void ExportPngs()
        {
            if (loadedImgPath == null) { Log("Primero abre un .img (boton 1)."); return; }
            using (var d = new FolderBrowserDialog { Description = "Carpeta donde dejar los PNGs" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                try { int n = ImgConverter.ExportFrames(loadedImgPath, d.SelectedPath); Log("Exportados " + n + " PNG en: " + d.SelectedPath); }
                catch (Exception ex) { Log("ERROR export: " + ex.Message); }
            }
        }

        void SheetToImg()
        {
            using (var op = new OpenFileDialog { Filter = "Hoja PNG editada (*.png)|*.png" })
            {
                if (op.ShowDialog() != DialogResult.OK) return;
                string sugerido = Path.GetFileName(op.FileName).Replace("_hoja", "").Replace(".png", ".img");
                using (var sv = new SaveFileDialog { Filter = "GunBound image (*.img)|*.img", FileName = sugerido })
                {
                    if (sv.ShowDialog() != DialogResult.OK) return;
                    try
                    {
                        int n = ImgConverter.Unsheet(op.FileName, sv.FileName);
                        Log(".img creado desde la hoja: " + sv.FileName + "  (" + n + " frames)");
                        Log("   >> Paso 4: metelo a graphics.xfs con XFS2 (nombre EXACTO).");
                    }
                    catch (Exception ex) { Log("ERROR hoja->img: " + ex.Message); }
                }
            }
        }

        void DirToImg()
        {
            using (var fb = new FolderBrowserDialog { Description = "Carpeta con los PNGs editados" })
            {
                if (fb.ShowDialog() != DialogResult.OK) return;
                using (var sv = new SaveFileDialog { Filter = "GunBound image (*.img)|*.img" })
                {
                    if (sv.ShowDialog() != DialogResult.OK) return;
                    try { int n = ImgConverter.ImportDir(fb.SelectedPath, sv.FileName); Log(".img creado desde carpeta: " + sv.FileName + "  (" + n + " frames)"); }
                    catch (Exception ex) { Log("ERROR carpeta->img: " + ex.Message); }
                }
            }
        }
    }
}
