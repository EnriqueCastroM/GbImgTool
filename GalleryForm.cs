using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GunboundTools.Imaging;

namespace GbImgTool
{
    // Previsualizador de PACK: abre una carpeta de .img y muestra todas las imagenes como
    // miniaturas. Al seleccionar una, la ve en grande (zoom pixel-perfect), recorre frames y
    // reproduce la animacion (alineada por pivote). No hace falta abrir de una en una.
    public class GalleryForm : Form
    {
        const int LABEL_H = 34;

        class Entry { public string Path; public string Name; public int Frames; public int W; public int H; }

        // estado de la galeria (PAGINADO: solo se decodifica la pagina visible -> memoria acotada)
        string[] allFiles = new string[0];                  // todas las rutas .img (barato, sin decodificar)
        readonly List<string> filtered = new List<string>(); // rutas tras aplicar el filtro de nombre
        int page = 0;
        int pageSize = 100;
        int thumbSize = 96;
        ImageList thumbs;
        ListView list;
        CancellationTokenSource cts;

        // controles
        TextBox filterBox;
        Label statusLbl;
        ComboBox sizeCombo;
        CheckBox recurseChk;
        Button firstBtn, prevBtn, nextBtn, lastBtn;
        Label pageLbl;
        ComboBox pageSizeCombo;

        // panel de detalle / animacion
        CheckerView viewer;
        Label detailInfo;
        Label frameLbl;
        TrackBar frameBar;
        Button playBtn;
        System.Windows.Forms.Timer anim;
        NumericUpDown fpsSpin;
        List<Bitmap> curBitmaps;      // frames del .img seleccionado, alineados a un lienzo comun
        int curIndex;
        string curPath;

        public GalleryForm()
        {
            Text = "GB Img Tool — Galeria (previsualizar carpeta de .img)";
            Width = 1180; Height = 760; StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9);
            BackColor = Color.FromArgb(37, 37, 38);
            ForeColor = Color.Gainsboro;

            BuildToolbar();
            BuildDetailPanel();
            BuildPager();
            BuildList();

            anim = new System.Windows.Forms.Timer { Interval = 100 };
            anim.Tick += (s, e) => StepAnim();

            FormClosing += (s, e) => { CancelLoad(); DisposeCurrent(); };
        }

        // Permite abrir la galeria ya apuntando a una carpeta (modo --gallery).
        string pendingFolder;
        public void AutoLoad(string path) { pendingFolder = path; }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (pendingFolder != null) { string p = pendingFolder; pendingFolder = null; LoadFolder(p); }
        }

        // ---------------- UI ----------------
        void BuildToolbar()
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.FromArgb(45, 45, 48) };

            var open = new Button { Text = "Abrir carpeta…", Left = 8, Top = 6, Width = 130, Height = 28 };
            open.Click += (s, e) => ChooseFolder();
            bar.Controls.Add(open);

            recurseChk = new CheckBox { Text = "Subcarpetas", Left = 146, Top = 10, Width = 100, ForeColor = Color.Gainsboro };
            recurseChk.CheckedChanged += (s, e) => { if (!string.IsNullOrEmpty(LastFolder)) LoadFolder(LastFolder); };
            bar.Controls.Add(recurseChk);

            var szLbl = new Label { Text = "Tamano:", Left = 252, Top = 11, Width = 54, ForeColor = Color.Gainsboro };
            bar.Controls.Add(szLbl);
            sizeCombo = new ComboBox { Left = 306, Top = 8, Width = 70, DropDownStyle = ComboBoxStyle.DropDownList };
            sizeCombo.Items.AddRange(new object[] { "64", "96", "128", "160" });
            sizeCombo.SelectedIndex = 1;
            sizeCombo.SelectedIndexChanged += (s, e) =>
            {
                thumbSize = int.Parse((string)sizeCombo.SelectedItem);
                if (allFiles.Length > 0) ShowPage();   // re-decodifica solo la pagina actual al nuevo tamano
            };
            bar.Controls.Add(sizeCombo);

            var fLbl = new Label { Text = "Filtro:", Left = 386, Top = 11, Width = 42, ForeColor = Color.Gainsboro };
            bar.Controls.Add(fLbl);
            filterBox = new TextBox { Left = 428, Top = 8, Width = 220 };
            filterBox.TextChanged += (s, e) => ApplyFilter();
            bar.Controls.Add(filterBox);

            statusLbl = new Label { Left = 660, Top = 11, Width = 470, ForeColor = Color.LightGreen, Text = "Abre una carpeta con .img" };
            bar.Controls.Add(statusLbl);

            Controls.Add(bar);
        }

        void BuildPager()
        {
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 38, BackColor = Color.FromArgb(45, 45, 48) };
            firstBtn = new Button { Text = "⏮ Primera", Left = 8, Top = 6, Width = 92, Height = 26 };
            firstBtn.Click += (s, e) => GoToPage(0);
            bar.Controls.Add(firstBtn);
            prevBtn = new Button { Text = "◀ Anterior", Left = 104, Top = 6, Width = 92, Height = 26 };
            prevBtn.Click += (s, e) => GoToPage(page - 1);
            bar.Controls.Add(prevBtn);
            pageLbl = new Label { Left = 204, Top = 10, Width = 330, ForeColor = Color.Khaki, Text = "—", TextAlign = ContentAlignment.MiddleLeft };
            bar.Controls.Add(pageLbl);
            nextBtn = new Button { Text = "Siguiente ▶", Left = 540, Top = 6, Width = 98, Height = 26 };
            nextBtn.Click += (s, e) => GoToPage(page + 1);
            bar.Controls.Add(nextBtn);
            lastBtn = new Button { Text = "Ultima ⏭", Left = 642, Top = 6, Width = 92, Height = 26 };
            lastBtn.Click += (s, e) => GoToPage(int.MaxValue);
            bar.Controls.Add(lastBtn);
            var psLbl = new Label { Text = "Por pagina:", Left = 748, Top = 10, Width = 72, ForeColor = Color.Gainsboro };
            bar.Controls.Add(psLbl);
            pageSizeCombo = new ComboBox { Left = 822, Top = 7, Width = 70, DropDownStyle = ComboBoxStyle.DropDownList };
            pageSizeCombo.Items.AddRange(new object[] { "50", "100", "200", "500" });
            pageSizeCombo.SelectedIndex = 1;
            pageSizeCombo.SelectedIndexChanged += (s, e) => { pageSize = int.Parse((string)pageSizeCombo.SelectedItem); page = 0; ShowPage(); };
            bar.Controls.Add(pageSizeCombo);
            Controls.Add(bar);
        }

        void BuildDetailPanel()
        {
            var panel = new Panel { Dock = DockStyle.Right, Width = 340, BackColor = Color.FromArgb(30, 30, 30), Padding = new Padding(8) };
            var tl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 340f)); // 0 visor
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));  // 1 zoom
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));  // 2 animacion
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));  // 3 barra de frame
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));  // 4 etiqueta de frame
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));  // 5 exportar
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // 6 info

            viewer = new CheckerView { Dock = DockStyle.Fill, BackColor = Color.Black };
            tl.Controls.Add(viewer, 0, 0);

            // controles de zoom
            var zoomBar = new Panel { Dock = DockStyle.Fill };
            string[] zl = { "Fit", "1x", "2x", "3x", "4x", "6x", "8x" };
            int[] zv = { 0, 1, 2, 3, 4, 6, 8 };
            for (int i = 0; i < zl.Length; i++)
            {
                int z = zv[i];
                var zb = new Button { Text = zl[i], Left = 4 + i * 46, Top = 2, Width = 44, Height = 26 };
                zb.Click += (s, e) => { viewer.Zoom = z; viewer.Invalidate(); };
                zoomBar.Controls.Add(zb);
            }
            tl.Controls.Add(zoomBar, 0, 1);

            // controles de frame / animacion
            var animBar = new Panel { Dock = DockStyle.Fill };
            var prev = new Button { Text = "◀", Left = 4, Top = 2, Width = 40, Height = 28 };
            prev.Click += (s, e) => { StopAnim(); ShowFrame(curIndex - 1); };
            animBar.Controls.Add(prev);
            playBtn = new Button { Text = "▶ Reproducir", Left = 48, Top = 2, Width = 110, Height = 28 };
            playBtn.Click += (s, e) => TogglePlay();
            animBar.Controls.Add(playBtn);
            var next = new Button { Text = "▶", Left = 162, Top = 2, Width = 40, Height = 28 };
            next.Click += (s, e) => { StopAnim(); ShowFrame(curIndex + 1); };
            animBar.Controls.Add(next);
            var fpsLbl = new Label { Text = "FPS", Left = 210, Top = 7, Width = 30, ForeColor = Color.Gainsboro };
            animBar.Controls.Add(fpsLbl);
            fpsSpin = new NumericUpDown { Left = 242, Top = 4, Width = 54, Minimum = 1, Maximum = 60, Value = 10 };
            fpsSpin.ValueChanged += (s, e) => { anim.Interval = Math.Max(1, (int)(1000 / fpsSpin.Value)); };
            animBar.Controls.Add(fpsSpin);
            tl.Controls.Add(animBar, 0, 2);

            frameBar = new TrackBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 0, TickStyle = TickStyle.None };
            frameBar.ValueChanged += (s, e) => { if (!syncing) { StopAnim(); ShowFrame(frameBar.Value); } };
            tl.Controls.Add(frameBar, 0, 3);

            frameLbl = new Label { Dock = DockStyle.Fill, Text = "—", TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Khaki };
            tl.Controls.Add(frameLbl, 0, 4);

            // exportar el seleccionado
            var expBar = new Panel { Dock = DockStyle.Fill };
            var expPng = new Button { Text = "Exportar PNGs", Left = 4, Top = 3, Width = 150, Height = 28 };
            expPng.Click += (s, e) => ExportCurrentPngs();
            expBar.Controls.Add(expPng);
            var expSheet = new Button { Text = "Exportar hoja", Left = 160, Top = 3, Width = 140, Height = 28 };
            expSheet.Click += (s, e) => ExportCurrentSheet();
            expBar.Controls.Add(expSheet);
            tl.Controls.Add(expBar, 0, 5);

            detailInfo = new Label { Dock = DockStyle.Fill, ForeColor = Color.Gainsboro, Text = "Selecciona una imagen de la galeria.", Padding = new Padding(2, 6, 2, 2) };
            tl.Controls.Add(detailInfo, 0, 6);

            panel.Controls.Add(tl);
            Controls.Add(panel);
        }

        void BuildList()
        {
            thumbs = NewImageList();
            list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.LargeIcon,
                LargeImageList = thumbs,
                BackColor = Color.FromArgb(50, 50, 52),
                ForeColor = Color.Gainsboro,
                MultiSelect = false,
                HideSelection = false
            };
            list.SelectedIndexChanged += (s, e) =>
            {
                if (list.SelectedItems.Count == 0) return;
                SelectEntry((Entry)list.SelectedItems[0].Tag);
            };
            list.ItemActivate += (s, e) => TogglePlay();   // doble clic = reproducir
            Controls.Add(list);
        }

        ImageList NewImageList()
        {
            return new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(thumbSize, thumbSize) };
        }

        // ---------------- carga de carpeta ----------------
        string LastFolder;

        void ChooseFolder()
        {
            using (var d = new FolderBrowserDialog { Description = "Carpeta con archivos .img" })
            {
                if (!string.IsNullOrEmpty(LastFolder)) d.SelectedPath = LastFolder;
                if (d.ShowDialog() != DialogResult.OK) return;
                LoadFolder(d.SelectedPath);
            }
        }

        void LoadFolder(string path)
        {
            CancelLoad();
            LastFolder = path;
            try
            {
                allFiles = Directory.GetFiles(path, "*.img",
                    recurseChk.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) { allFiles = new string[0]; statusLbl.Text = "ERROR: " + ex.Message; }
            Array.Sort(allFiles, StringComparer.OrdinalIgnoreCase);
            page = 0;
            ApplyFilter();
        }

        // Recalcula la lista filtrada por nombre y vuelve a la pagina 1.
        void ApplyFilter()
        {
            string f = filterBox.Text;
            filtered.Clear();
            foreach (string p in allFiles)
                if (string.IsNullOrEmpty(f) || Path.GetFileName(p).IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                    filtered.Add(p);
            page = 0;
            ShowPage();
        }

        void GoToPage(int p)
        {
            int pages = Math.Max(1, (filtered.Count + pageSize - 1) / pageSize);
            if (p < 0) p = 0;
            if (p >= pages) p = pages - 1;
            page = p;
            ShowPage();
        }

        // Decodifica SOLO la pagina actual (memoria acotada a <= pageSize miniaturas). Carga en 2o plano.
        void ShowPage()
        {
            CancelLoad();
            int total = filtered.Count;
            int pages = Math.Max(1, (total + pageSize - 1) / pageSize);
            if (page >= pages) page = pages - 1;
            if (page < 0) page = 0;
            int start = page * pageSize;
            int end = Math.Min(total, start + pageSize);

            firstBtn.Enabled = prevBtn.Enabled = page > 0;
            nextBtn.Enabled = lastBtn.Enabled = page < pages - 1;
            pageLbl.Text = total == 0
                ? "sin resultados"
                : "Pagina " + (page + 1) + " / " + pages + "    (" + (start + 1) + "–" + end + " de " + total + ")";

            // reinicia lista + ImageList -> libera la pagina anterior
            StopAnim();
            list.Items.Clear();
            var oldThumbs = thumbs;
            thumbs = NewImageList();
            list.LargeImageList = thumbs;
            if (oldThumbs != null) oldThumbs.Dispose();
            if (total == 0) { statusLbl.Text = allFiles.Length + " .img (ninguno coincide con el filtro)"; return; }

            var slice = new List<string>();
            for (int i = start; i < end; i++) slice.Add(filtered[i]);
            statusLbl.Text = "cargando " + slice.Count + " miniaturas…";

            cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;
            ImageList target = thumbs;
            int sz = thumbSize;
            int loadedPage = page;

            Task.Run(() =>
            {
                foreach (string f in slice)
                {
                    if (token.IsCancellationRequested) return;
                    Bitmap thumb = null; int frames = 0, w = 0, h = 0;
                    try
                    {
                        var fr = ImgConverter.ParseImg(f);
                        frames = fr.Count;
                        if (frames > 0 && fr[0].Width > 0 && fr[0].Height > 0 && fr[0].Width <= 8192 && fr[0].Height <= 8192)
                            using (var bm = ImgConverter.DecodeFrame(fr[0])) { w = bm.Width; h = bm.Height; thumb = MakeThumb(bm, sz); }
                    }
                    catch { }
                    if (thumb == null) thumb = MakePlaceholder(sz);
                    var entry = new Entry { Path = f, Name = Path.GetFileName(f), Frames = frames, W = w, H = h };
                    Bitmap toAdd = thumb;
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            if (token.IsCancellationRequested || target != thumbs) { toAdd.Dispose(); return; }
                            int imgIndex = target.Images.Count;
                            target.Images.Add(toAdd); toAdd.Dispose();
                            var it = new ListViewItem(entry.Name) { ImageIndex = imgIndex, Tag = entry };
                            it.ToolTipText = entry.Name + "  (" + entry.Frames + " frames)";
                            list.Items.Add(it);
                        }));
                    }
                    catch { return; }   // form cerrado
                }
                try { BeginInvoke((Action)(() => statusLbl.Text = total + " .img en total  ·  pagina " + (loadedPage + 1) + "  ·  clic = ver, doble-clic = animar")); }
                catch { }
            }, token);
        }

        void CancelLoad()
        {
            if (cts != null) { try { cts.Cancel(); } catch { } cts = null; }
        }

        // ---------------- detalle / animacion ----------------
        bool syncing;

        void SelectEntry(Entry e)
        {
            StopAnim();
            DisposeCurrent();
            curPath = e.Path;
            curBitmaps = new List<Bitmap>();
            List<GunboundImg> fr = null;
            try { fr = ImgConverter.ParseImg(e.Path); } catch { }

            int pivotX = 0, pivotY = 0, ttype = 0;
            if (fr != null && fr.Count > 0)
            {
                // lienzo comun alineando por pivote (XCenter/YCenter) -> animacion estable
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                var raw = new List<Bitmap>();
                foreach (var f in fr)
                {
                    Bitmap bm = null;
                    try { if (f.Width > 0 && f.Height > 0 && f.Width <= 8192 && f.Height <= 8192) bm = ImgConverter.DecodeFrame(f); }
                    catch { }
                    raw.Add(bm);
                    if (bm != null)
                    {
                        int x0 = -f.XCenter, y0 = -f.YCenter;
                        if (x0 < minX) minX = x0; if (y0 < minY) minY = y0;
                        if (x0 + bm.Width > maxX) maxX = x0 + bm.Width;
                        if (y0 + bm.Height > maxY) maxY = y0 + bm.Height;
                    }
                }
                if (minX == int.MaxValue) { minX = 0; minY = 0; maxX = 1; maxY = 1; }
                int cw = Math.Min(4096, Math.Max(1, maxX - minX));
                int ch = Math.Min(4096, Math.Max(1, maxY - minY));
                for (int i = 0; i < fr.Count; i++)
                {
                    var canvas = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
                    if (raw[i] != null)
                    {
                        using (var g = Graphics.FromImage(canvas))
                            g.DrawImageUnscaled(raw[i], -fr[i].XCenter - minX, -fr[i].YCenter - minY);
                        raw[i].Dispose();
                    }
                    curBitmaps.Add(canvas);
                }
                pivotX = fr[0].XCenter; pivotY = fr[0].YCenter; ttype = fr[0].ImgTransparencyType & 0xFF;
            }

            syncing = true;
            frameBar.Maximum = Math.Max(0, curBitmaps.Count - 1);
            frameBar.Value = 0;
            syncing = false;
            curIndex = 0;
            ShowFrame(0);
            playBtn.Enabled = curBitmaps.Count > 1;
            detailInfo.Text =
                e.Name +
                "\r\nFrames:  " + (fr != null ? fr.Count : 0) +
                "\r\nFrame 0:  " + e.W + " x " + e.H + " px" +
                "\r\nPivote (frame 0):  " + pivotX + ", " + pivotY +
                "\r\nTipo transparencia:  " + ttype +
                "\r\n\r\nFondo a cuadros = transparente (magenta).";
        }

        void ShowFrame(int i)
        {
            if (curBitmaps == null || curBitmaps.Count == 0) { viewer.Image = null; viewer.Invalidate(); frameLbl.Text = "—"; return; }
            if (i < 0) i = curBitmaps.Count - 1;
            if (i >= curBitmaps.Count) i = 0;
            curIndex = i;
            viewer.Image = curBitmaps[i];
            viewer.Invalidate();
            frameLbl.Text = "frame " + (i + 1) + " / " + curBitmaps.Count;
            if (frameBar.Value != i && i <= frameBar.Maximum) { syncing = true; frameBar.Value = i; syncing = false; }
        }

        void StepAnim() { if (curBitmaps != null && curBitmaps.Count > 1) ShowFrame((curIndex + 1) % curBitmaps.Count); }

        void TogglePlay()
        {
            if (curBitmaps == null || curBitmaps.Count < 2) return;
            if (anim.Enabled) StopAnim();
            else { anim.Interval = Math.Max(1, (int)(1000 / fpsSpin.Value)); anim.Start(); playBtn.Text = "⏸ Pausar"; }
        }

        void StopAnim() { if (anim != null && anim.Enabled) { anim.Stop(); } if (playBtn != null) playBtn.Text = "▶ Reproducir"; }

        void DisposeCurrent()
        {
            viewer.Image = null;
            if (curBitmaps != null) { foreach (var b in curBitmaps) if (b != null) b.Dispose(); curBitmaps = null; }
        }

        // ---------------- exportar el seleccionado ----------------
        void ExportCurrentPngs()
        {
            if (curPath == null) { statusLbl.Text = "Selecciona una imagen primero."; return; }
            using (var d = new FolderBrowserDialog { Description = "Carpeta donde dejar los PNGs" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                try { int n = ImgConverter.ExportFrames(curPath, d.SelectedPath); statusLbl.Text = "Exportados " + n + " PNG en " + d.SelectedPath; }
                catch (Exception ex) { statusLbl.Text = "ERROR export: " + ex.Message; }
            }
        }

        void ExportCurrentSheet()
        {
            if (curPath == null) { statusLbl.Text = "Selecciona una imagen primero."; return; }
            using (var d = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = Path.GetFileNameWithoutExtension(curPath) + "_hoja.png" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                try { int tw, th; ImgConverter.BuildSheet(curPath, d.FileName, 6, out tw, out th); statusLbl.Text = "Hoja creada (celda " + tw + "x" + th + "): " + d.FileName; }
                catch (Exception ex) { statusLbl.Text = "ERROR hoja: " + ex.Message; }
            }
        }

        // ---------------- helpers de imagen ----------------
        static Bitmap MakeThumb(Bitmap src, int size)
        {
            var t = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(t))
            {
                g.InterpolationMode = (src.Width < size && src.Height < size) ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                float sc = Math.Min((float)size / src.Width, (float)size / src.Height);
                if (sc > 1f) sc = (float)Math.Floor(sc);
                if (sc < 0.01f) sc = 0.01f;
                int dw = Math.Max(1, (int)(src.Width * sc)), dh = Math.Max(1, (int)(src.Height * sc));
                g.DrawImage(src, (size - dw) / 2, (size - dh) / 2, dw, dh);
            }
            return t;
        }

        static Bitmap MakePlaceholder(int size)
        {
            var t = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(t))
            {
                g.Clear(Color.FromArgb(48, 48, 48));
                using (var p = new Pen(Color.DimGray)) { g.DrawLine(p, 0, 0, size, size); g.DrawLine(p, 0, size, size, 0); }
            }
            return t;
        }

        // Panel que dibuja la imagen con fondo a cuadros y escalado nitido (nearest-neighbor).
        class CheckerView : Panel
        {
            public Bitmap Image;
            public int Zoom;   // 0 = ajustar (fit), >0 = factor entero
            public CheckerView() { DoubleBuffered = true; }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Rectangle r = ClientRectangle;
                int s = 10;
                using (var b1 = new SolidBrush(Color.FromArgb(70, 70, 70))) g.FillRectangle(b1, r);
                using (var b2 = new SolidBrush(Color.FromArgb(95, 95, 95)))
                    for (int y = 0; y < r.Height; y += s)
                        for (int x = 0; x < r.Width; x += s)
                            if (((x / s) + (y / s)) % 2 == 0) g.FillRectangle(b2, r.X + x, r.Y + y, s, s);
                if (Image == null) return;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                float scale;
                if (Zoom <= 0)
                {
                    scale = Math.Min((float)r.Width / Image.Width, (float)r.Height / Image.Height);
                    if (scale > 1f) scale = (float)Math.Floor(scale);
                    if (scale < 0.01f) scale = 0.01f;
                }
                else scale = Zoom;
                int dw = Math.Max(1, (int)(Image.Width * scale)), dh = Math.Max(1, (int)(Image.Height * scale));
                int dx = (r.Width - dw) / 2, dy = (r.Height - dh) / 2;
                g.DrawImage(Image, new Rectangle(dx, dy, dw, dh));
            }
        }
    }
}
