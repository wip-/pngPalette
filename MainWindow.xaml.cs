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

    public struct PngHeader
    {
        public void ReadPngHeader(BinaryReader br)
        {
            header = br.ReadBytes(8);
        }
        public byte[] GetMagicNumber()
        {
            return new byte[]
                { 0x89, (byte)'P', (byte)'N', (byte)'G', 
                  (byte)'\r', (byte)'\n', 0x1A, (byte)'\n' };
        }

        public byte[] header;
    }

    public struct ChunkHeader
    {
        public void ReadChunkHeader(BinaryReader br)
        {
            chunkLength = BitConverter.ToUInt32(Helpers.ReadBytesReverse(br, 4), 0);
            chunkType = br.ReadBytes(4);
        }

        public UInt32 chunkLength;
        public byte[] chunkType;
    }

    public abstract class Chunk
    {
        public Chunk()
        {
            header.chunkLength = 0;
            header.chunkType = GetMagicNumber();
            finishedReadingData = false;
        }

        public virtual byte[] GetMagicNumber()
        {
            return new byte[] { (byte)'N', (byte)'U', (byte)'L', (byte)'L' };
        }

        // override version must advance BinaryReader by an offset of length header.chunkLength
        public virtual void ReadChunkData(BinaryReader br)
        {
            finishedReadingData = true;
        }

        public void ReadChunkCRC(BinaryReader br)
        {
            crc = Helpers.ReadBytesReverse(br, 4);
        }

        public ChunkHeader header;
        public byte[] crc;

        private Boolean finishedReadingData;
        public Boolean FinishedReading
        {
            get
            {
                return finishedReadingData;
            }
        }
    }

    public class IHDR_Chunk : Chunk
    {
        public override byte[] GetMagicNumber()
        {
            return new byte[] { (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
        }

        public override void ReadChunkData(BinaryReader br) 
        {
            width = BitConverter.ToUInt32(Helpers.ReadBytesReverse(br, 4), 0);
            height = BitConverter.ToUInt32(Helpers.ReadBytesReverse(br, 4), 0);
            bitdepth = br.ReadByte();
            colorType = br.ReadByte();
            compression = br.ReadByte();
            filter = br.ReadByte();
            interlace = br.ReadByte();
            base.ReadChunkData(br);
        }
        
        public UInt32 width;
        public UInt32 height;
        public byte bitdepth;
        public byte colorType;
        public byte compression;
        public byte filter;
        public byte interlace;
    }

    public class PLTE_Chunk : Chunk
    {
        public override byte[] GetMagicNumber()
        {
            return new byte[] { (byte)'P', (byte)'L', (byte)'T', (byte)'E' };
        }

        public override void ReadChunkData(BinaryReader br)
        {
            uint colorsCount = header.chunkLength/3;    //PNG spec:"A chunk length not divisible by 3 is an error"
            colorValues = new Color[colorsCount];
            for (uint i=0; i<colorsCount; ++i)
            {
                byte[] components = Helpers.ReadBytesReverse(br, 3);
                colorValues[i] = Color.FromArgb(255, (int)components[2], (int)components[1], (int)components[0]);
            }
            base.ReadChunkData(br);
        }

        // Colors without alpha information
        public Color[] colorValues;
    }

    public class tRNS_Chunk : Chunk
    {
        public override byte[] GetMagicNumber()
        {
            return new byte[] { (byte)'t', (byte)'R', (byte)'N', (byte)'S' };
        }

        public override void ReadChunkData(BinaryReader br)
        {
            // PNG spec:"tRNS can contain fewer values than there are palette entries."
            //          "In this case, the alpha value for all remaining palette entries is assumed to be 255. "
            alphaValues = new byte[header.chunkLength];
            for (uint i = 0; i < header.chunkLength; ++i)
            {
                alphaValues[i] = br.ReadByte();
            }
            base.ReadChunkData(br);
        }

        public byte[] alphaValues;
    }

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

        private void OnDrop(object sender, DragEventArgs e)
        {
            try
            {
                String errorMsg = Window_OnDrop_Sub(e);
                if (errorMsg != null)
                    MainTextBox.Text = errorMsg + Environment.NewLine + StartupText;
            }
            catch (System.Exception ex)
            {
                var st = new StackTrace(ex, true);      // stack trace for the exception with source file information
                var frame = st.GetFrame(0);             // top stack frame
                String sourceMsg = String.Format("{0}({1})", frame.GetFileName(), frame.GetFileLineNumber());
                Console.WriteLine(sourceMsg);
                MessageBox.Show(ex.Message + Environment.NewLine + sourceMsg);
                Debugger.Break();
            }
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

            PngHeader pngHeader = new PngHeader();
            ChunkHeader chunkHeader = new ChunkHeader();
            IHDR_Chunk ihdr = new IHDR_Chunk();
            PLTE_Chunk plte = new PLTE_Chunk();
            tRNS_Chunk tRNS = new tRNS_Chunk();
            Chunk[] chunks = new Chunk[] { ihdr, plte, tRNS };

            using (BinaryReader br = new BinaryReader(fs, new UTF8Encoding(), true))
            {
                pngHeader.ReadPngHeader(br);
                if(!Enumerable.SequenceEqual(pngHeader.header, pngHeader.GetMagicNumber()))
                    return "Not a PNG!";

                while( br.BaseStream.Position!=br.BaseStream.Length )
                {
                    // Read chunk header
                    chunkHeader.ReadChunkHeader(br);

                    // Read chunk data
                    Chunk currentChunk = null;
                    foreach (Chunk chunk in chunks) // check the chunks we're interested in
                    {
                        if (!chunk.FinishedReading 
                         && Enumerable.SequenceEqual(chunkHeader.chunkType, chunk.GetMagicNumber()) )
                        {
                            chunk.header = chunkHeader;
                            chunk.ReadChunkData(br);
                            currentChunk = chunk;
                            Console.WriteLine("Filled chunk " + Encoding.ASCII.GetString(chunkHeader.chunkType));
                            break;
                        }
                    }
                    if (currentChunk==null)
                    {
                        Console.WriteLine("Discarded chunk " + Encoding.ASCII.GetString(chunkHeader.chunkType));
                        br.ReadBytes(Convert.ToInt32(chunkHeader.chunkLength)); // chunk data
                        br.ReadBytes(4);                                        // chunk crc
                    }
                    else
                    {
                        // Read chunk crc
                        currentChunk.ReadChunkCRC(br);
                    } 
                }

                if(!ihdr.FinishedReading)
                    return "PNG corrupted: IHDR chunk not found!";
            }

            if (ihdr.bitdepth != 8 || ihdr.colorType != 3 || !plte.FinishedReading)
                return "Not an indexed PNG!";

            fs.Position = 0;    // reset stream to feed it to Bitmap
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
            sb.AppendLine("Color index, R value, G value, B value, A value, Frequency (BROKEN! TODO fix)");

        #if false   // use Bitmap.Palette

            ColorPalette palette = bitmap.Palette;
            for(byte i=0; i<palette.Entries.Length; ++i)
            {
                Color col = palette.Entries[i];
                sb.AppendLine(i + "," + col.R + "," + col.G + "," + col.B + "," + col.A + "," + indexDictionary[i] );
            }

        #else       // Bitmap.Palette is sometimes wrong, so we read it by ourselves
                    // Also Bitmap.Pixelformat might be wrong, so we read it by ourselves

            int colorValuesCount = (plte.colorValues==null)? 0 : plte.colorValues.Length;
            int alphaValuesCount = (tRNS.alphaValues==null)? 0 : tRNS.alphaValues.Length;
            for(int i=0; i<colorValuesCount; ++i)
            {
                Color col = plte.colorValues[i];
                byte alpha = (i < alphaValuesCount) ? tRNS.alphaValues[i] : (byte)255;
                int frequency = indexDictionary.ContainsKey((byte)i) ? indexDictionary[(byte)i] : 0;
                sb.AppendFormat("{0}, {1}, {2}, {3}, {4}, {5} \n", i, col.R, col.G, col.B, alpha, frequency);
            }

        #endif

            MainTextBox.Text = sb.ToString();
            ResultContextMenu.IsEnabled = true;
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

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed)
                ResultContextMenu.Visibility = Visibility.Visible;
            else
                ResultContextMenu.Visibility = Visibility.Hidden;
        }

        private void SaveCSV(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.SaveFileDialog saveDialog = new Microsoft.Win32.SaveFileDialog();
            saveDialog.FileName = "palette";
            saveDialog.DefaultExt = ".csv";
            saveDialog.Filter = "comma-separated values (.csv)|*.csv";
            if (saveDialog.ShowDialog().Value)
            {
                File.WriteAllText(saveDialog.FileName, MainTextBox.Text);
                Process.Start("explorer.exe", @"/select,""" + saveDialog.FileName + "\"");
            }
        }
    }

    public static class Helpers
    {
        public static byte[] ReadBytesReverse(BinaryReader br, int count)
        {
            byte[] data = br.ReadBytes(count);
            Array.Reverse(data);
            return data;
        }
    }

    
}
