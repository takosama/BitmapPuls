using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;

namespace ImageTool
{
    class BitmapPlus
    {
        private Bitmap _bmp = null;

        private BitmapData _img = null;
        int adr;
        int img_Stride;
        public BitmapPlus(Bitmap original)
        {
            _bmp = original;
        }

        public void BeginAccess()
        {
            _img = _bmp.LockBits(new Rectangle(0, 0, _bmp.Width, _bmp.Height),
                System.Drawing.Imaging.ImageLockMode.ReadWrite,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            adr = (int)_img.Scan0;
            img_Stride = _img.Stride;
        }
        public void EndAccess()
        {
            if (_img != null)
            {
                _bmp.UnlockBits(_img);
                _img = null;
            }
        }

        public void GetPixel(int x, int y,ref byte R,ref byte G,ref byte B)
        {
            unsafe
            {
                byte *adr = (byte*)this.adr;
                int pos = x * 3 + img_Stride * y;
                B= adr[pos ++];
                G = adr[pos ++];
                R = adr[pos ];
            }
        }



        public void SetPixel(int x, int y, byte R, byte G, byte B)
        {
            unsafe
            {
                byte *adr = (byte*)this.adr;
                int pos = x * 3 + img_Stride * y;
               adr[pos ++ ]= B;
               adr[pos ++] = G;
               adr[pos ] = R;
            }
        }
    }
}
