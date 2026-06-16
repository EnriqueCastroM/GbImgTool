using System;
using System.IO;
using System.Windows.Forms;

namespace GbImgTool
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Modo directo: GbImgTool.exe --gallery "C:\ruta\carpeta"  -> abre la galeria ya cargada.
            if (args.Length >= 1 && (args[0] == "--gallery" || args[0] == "-g"))
            {
                var g = new GalleryForm();
                if (args.Length >= 2 && Directory.Exists(args[1])) g.AutoLoad(args[1]);
                Application.Run(g);
                return;
            }
            Application.Run(new MainForm());
        }
    }
}
