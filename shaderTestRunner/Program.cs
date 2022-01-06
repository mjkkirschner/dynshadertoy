using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using dynshadertoy;
using System.Drawing;

namespace shaderTestRunner
{
    class Program
    {
        //can be used for brute force painting a bitmap into a window.
        [DllImport("kernel32.dll", EntryPoint = "GetConsoleWindow", SetLastError = true)]
        private static extern IntPtr GetConsoleHandle();

        static void Main(string[] args)
        {
            Console.WriteLine("Hello World");
            var x = new shadertoy();
            var image = x.Test(800,600);

            var handler = GetConsoleHandle();
            Console.SetWindowSize(150, 60);

            System.Threading.Thread.Sleep(100);

            using (var graphics = Graphics.FromHwnd(handler))
            {
                var bmp = new Bitmap(800, 600, 800 * 4, System.Drawing.Imaging.PixelFormat.Format32bppRgb,
                    Marshal.UnsafeAddrOfPinnedArrayElement(image, 0));
                graphics.DrawImage(bmp, 200, 200, 800, 600);
            }
            Console.ReadLine();
        }
    }
}
