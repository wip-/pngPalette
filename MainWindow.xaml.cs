using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
//using System.Windows.Media;
//using System.Windows.Media.Imaging;
using System.Windows.Navigation;
//using System.Windows.Shapes;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace pngPalette
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static readonly String StartupText = "Drop PNG8 here";

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_OnDrop(object sender, DragEventArgs e)
        {
            String errorMsg = Window_OnDrop_Sub(e);
            if (errorMsg != null)
                StartupLabel.Content = errorMsg + Environment.NewLine + StartupText;
        }


        public class IHDR_Chunk
        {
            public UInt32 chunkLength;
            public byte[] chunkType;

            public UInt32 width;
            public UInt32 height;
            public byte bitdepth;
            public byte colorType;
            public byte compression;
            public byte filter;
            public byte interlace;
        }

        /// <summary>
        /// sanity check
        /// </summary>
        /// <param name="e"></param>
        /// <returns>if file can be converted: null, else: error message</returns>
        private String Window_OnDrop_Sub(DragEventArgs e)
        {
            if(!e.Data.GetDataPresent(DataFormats.FileDrop))
                return "Not a file!";

            String[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 1)
                return "Too many files!";

            String filename = files[0];

            if (!File.Exists(filename))
                return "Not a file!";

            FileStream fs = null;
            try
            {
                fs = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (IOException)
            {
                if(fs!= null)
                    fs.Close();
                return "File already in use!";
            }


        #if false   // use Bitmap.Pixelformat information

            var bitmap = new Bitmap(fs);
            if (bitmap.PixelFormat != PixelFormat.Format8bppIndexed)
            {
                bitmap.Dispose();
                return "Not an indexed PNG!";
            }
       
            int bitmapComponents = GetComponentsNumber(bitmap.PixelFormat);

        #else       // Bitmap.Pixelformat is sometimes wrong, so we read it by ourselves
                    // Also Bitmap.Palette might be wrong, so we read it by ourselves

            IHDR_Chunk ihdr = new IHDR_Chunk();
            using (BinaryReader br = new BinaryReader(fs))
            {
                byte[] header = br.ReadBytes(8);

                byte[] PngMagicNumber = new byte[]{ 0x89, (byte)'P', (byte)'N', (byte)'G', 
                     (byte)'\r', (byte)'\n', 0x1A, (byte)'\n' };
                if (!Enumerable.SequenceEqual(header, PngMagicNumber))
                    return "Not a PNG!";

                ihdr.chunkLength = BitConverter.ToUInt32(ReadBytesReverse(br, 4), 0);
                ihdr.chunkType = br.ReadBytes(4);

                byte[] IHDRMagicNumber = new byte[] { (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
                if (!Enumerable.SequenceEqual(ihdr.chunkType, IHDRMagicNumber))
                    return "PNG corrupted: IHDR chunk not found!";

                ihdr.width = BitConverter.ToUInt32(ReadBytesReverse(br, 4), 0);
                ihdr.height = BitConverter.ToUInt32(ReadBytesReverse(br, 4), 0);
                ihdr.bitdepth = br.ReadByte();
                ihdr.colorType = br.ReadByte();
                ihdr.compression = br.ReadByte();
                ihdr.filter = br.ReadByte();
                ihdr.interlace = br.ReadByte();
            }

            if (ihdr.bitdepth != 8 || ihdr.colorType != 3)
                return "Not an indexed PNG!";

            var bitmap = new Bitmap(fs);
            int bitmapComponents = 1;

        #endif


            int bitmapWidth = bitmap.Width;
            int bitmapHeight = bitmap.Height;    

            BitmapData bitmapData = bitmap.LockBits(
                Rectangle.FromLTRB(0, 0, bitmapWidth, bitmapHeight),
                ImageLockMode.ReadOnly,
                PixelFormat.Format8bppIndexed);
            int bitmapStride = bitmapData.Stride;


            var indexDictionary = new Dictionary<byte, int>();
            for (int y = 0; y < bitmapHeight; y++)
            {
                for (int x = 0; x < bitmapWidth; x++)
                {
                    byte index = Marshal.ReadByte(bitmapData.Scan0, (bitmapStride * y) + (bitmapComponents * x));
                    if (indexDictionary.ContainsKey(index))
                        indexDictionary[index] += 1;
                    else
                        indexDictionary[index] = 1;
                }
            }
            bitmap.UnlockBits(bitmapData);
            bitmap.Dispose();
            fs.Close();


            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Color index, R value, G value, B value, A value, Frequency");
            ColorPalette palette = bitmap.Palette;
            for(byte i=0; i<palette.Entries.Length; ++i)
            {
                Color col = palette.Entries[i];
                sb.AppendLine(i + "," + col.R + "," + col.G + "," + col.B + "," + col.A + "," + indexDictionary[i] );
            }

            StartupLabel.Content = sb.ToString();
            return null;
        }


        static int GetComponentsNumber(PixelFormat pixelFormat)
        {
            switch (pixelFormat)
            {
                case PixelFormat.Format8bppIndexed:
                    return 1;

                case PixelFormat.Format24bppRgb:
                    return 3;

                case PixelFormat.Format32bppArgb:
                    return 4;

                default:
                    Debug.Assert(false);
                    return 0;
            }
        }


        byte[] ReadBytesReverse(BinaryReader br, int count)
        {
            byte[] data = br.ReadBytes(count);
            Array.Reverse(data);
            return data;
        }
    }
}
