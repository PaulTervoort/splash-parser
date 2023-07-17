using System.Text;
using System.Drawing;

internal class Program
{
    // Page size used in splash images
    const int PAGE_SIZE = 512;
    // String signature at the start of a splash header
    const string SPLASH_MARKER = "SPLASH!!";

    // Information contained in a splash image header
    struct SplashHeader
    {
        public int width;
        public int height;
        public int length;
        public int mode;
    }

    private static void Main(string[] args)
    {
        // Print help if not enough arguments
        if (args.Length != 1 && args.Length != 3)
        {
            Console.WriteLine("No splash image provided");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  SPLASHPARSER <splash image>                                            Extract splash images");
            Console.WriteLine("  SPLASHPARSER <splash image> <substitute picture> <substitute index>    Substitute a splash image");
            return;
        }

        // Open the splash image
        FileStream file;
        try
        {
            file = File.OpenRead(args[0]);
        }
        catch
        {
            Console.WriteLine("Cannot open splash file or does not exist");
            return;
        }

        // Parse arguments
        bool substitute = args.Length == 3;
        Bitmap sub_bitmap = null;
        int sub_index = 0;
        string sub_path = null;
        bool sub_invalid = false;
        if (substitute)
        {
            // Load substitute image
            bool fail = false;
            try
            {
                sub_bitmap = new Bitmap(args[1]);
            } catch
            {
                fail = true;
            }

            // Check if bitmap loaded succesfully
            if (sub_bitmap == null || fail)
            {
                Console.WriteLine("Invalid substitute picture or file does not exist");
                return;
            }

            // Parse substitute index
            if (!int.TryParse(args[2], out sub_index))
            {
                Console.WriteLine("Invalid substitute index");
                return;
            }

            // Create file for modified splash image
            sub_path = Path.GetFileNameWithoutExtension(args[0]) + "_MOD" + sub_index + Path.GetExtension(args[0]);
            fail = false;
            try
            {
                FileStream sub_file = File.Create(sub_path);
                file.CopyTo(sub_file);
                file.Close();
                file.Dispose();
                file = sub_file;
            } catch { 
                fail = true; 
            };

            // Close file if not able to create file
            if (file == null || fail)
            {
                // Close modified splash image
                try
                {
                    file.Close();
                    file.Dispose();
                }
                catch { }

                // Print error
                Console.WriteLine("Cannot create modified splash image file");
                return;
            }

            // Reset position of filestream
            file.Seek(0, SeekOrigin.Begin);
        }

        // Try to parse splash images until the end of the image file
        long splash_pos = 0;
        long seek_index = 0;
        int bitmap_index = -1;
        while (splash_pos >= 0)
        {
            // Find a new splash image block
            file.Seek(seek_index, SeekOrigin.Begin);
            splash_pos = SeekSplash(file);
            if (splash_pos < 0)
            {
                continue;
            }

            // Update indices
            seek_index = splash_pos + PAGE_SIZE;
            bitmap_index++;
            Console.WriteLine();

            // Read the header page
            file.Seek(splash_pos, SeekOrigin.Begin);
            byte[] h = new byte[PAGE_SIZE];
            if (file.Read(h, 0, PAGE_SIZE) < PAGE_SIZE)
            {
                Console.WriteLine("Cannot read splash header page");
                sub_invalid = true;
                continue;
            }

            // Parse the header page
            SplashHeader header = ParseHeader(h);
            if (header.mode != 0 && header.mode != 1)
            {
                Console.WriteLine("Invalid bitmap mode in splash header");
                sub_invalid = true;
                continue;
            }

            // Print splash info
            Console.WriteLine("Splash index: " + bitmap_index.ToString());
            Console.Write("Dimensions: " + header.width.ToString());
            Console.WriteLine("x" + header.height.ToString());
            Console.WriteLine(header.mode == 0 ? "Uncompressed" : "RLE24 Compression");
            Console.WriteLine("Bitmap size: " + (header.length * PAGE_SIZE).ToString());

            // Substitute mode
            if (substitute)
            {
                // Only do something if indices match
                if (bitmap_index == sub_index)
                {
                    // Check if the dimensions match
                    if (sub_bitmap.Width != header.width || sub_bitmap.Height != header.height)
                    {
                        Console.WriteLine("Substitute picture has different dimensions");
                        sub_invalid = true;
                        continue;
                    }

                    // Encode the bitmap
                    byte[] bitmap = EncodeBitmapRLE24(sub_bitmap, header.width, header.height);
                    if (header.mode == 0 || bitmap.Length > header.width * header.height * 3)
                    {
                        // Use uncompressed bitmap as fallback
                        bitmap = EncodeBitmapRaw(sub_bitmap, header.width, header.height);
                        header.mode = 0;
                    }

                    // Write the bitmap
                    if (!WriteBitmap(file, bitmap, splash_pos + PAGE_SIZE, header.length))
                    {
                        Console.WriteLine("Substitute picture does not fit in the image");
                        sub_invalid = true;
                        continue;
                    }

                    // Write the header
                    file.Seek(splash_pos, SeekOrigin.Begin);
                    header.length = (bitmap.Length + PAGE_SIZE - 1) / PAGE_SIZE;
                    file.Write(GenerateHeader(header));

                    // Print new bitmap size
                    Console.WriteLine("Substituted - New bitmap size: " + (header.length * PAGE_SIZE).ToString());
                }
            }
            // Extract mode
            else
            {
                // Read bitmap data from file
                byte[] bitmap = ReadBitmap(file, splash_pos + PAGE_SIZE, header.length);
                if (bitmap == null)
                {
                    Console.WriteLine("Cannot read bitmap data");
                    continue;
                }

                // Decode and save bitmap if success
                Bitmap image = DecodeBitmap(bitmap, header.width, header.height, header.mode == 1);
                if (image == null)
                {
                    Console.WriteLine("Invalid bitmap encoding");
                    continue;
                }

                // Save the bitmap
                image.Save(Path.GetFileNameWithoutExtension(args[0]) + "_" + bitmap_index + ".bmp");
                Console.WriteLine("Image saved");
            }
        }

        // Flush and close file
        file.Flush();
        file.Close();
        file.Dispose();

        // Notify if no splash pictures found
        if (bitmap_index < 0)
        {
            Console.WriteLine("Image contains no splash pictures");
            sub_invalid = true;
        }
        // Notify if no substitute index found
        else if (substitute && bitmap_index < sub_index)
        {
            Console.WriteLine("Substitute index does not exist in the image");
            sub_invalid = true;
        }

        // Delete modified splash image if invalid
        if (substitute && sub_invalid)
        {
            try
            {
                File.Delete(sub_path);
            }
            catch { }
        }
        Console.WriteLine();

        // Print done
        Console.WriteLine("Done");
    }

    static long SeekSplash(FileStream fs)
    {
        // Loop over all pages until the splash marker is found
        byte[] s = new byte[8];
        long pos = fs.Position;
        fs.Read(s, 0, 8);
        while (Encoding.ASCII.GetString(s) != SPLASH_MARKER)
        {
            // go to the next page
            pos += PAGE_SIZE;
            fs.Seek(pos, SeekOrigin.Begin);

            // Abort if end of file
            if (fs.Read(s, 0, 8) < 8)
            {
                return -1;
            }
        }

        // Return the splash position
        return pos;
    }

    static SplashHeader ParseHeader(byte[] header)
    {
        // Read relevant data
        SplashHeader result = new SplashHeader();
        result.width = BitConverter.ToInt32(new byte[] { header[8], header[9], header[10], header[11] }, 0);
        result.height = BitConverter.ToInt32(new byte[] { header[12], header[13], header[14], header[15] }, 0);
        result.mode = BitConverter.ToInt32(new byte[] { header[16], header[17], header[18], header[19] }, 0);
        result.length = BitConverter.ToInt32(new byte[] { header[20], header[21], header[22], header[23] }, 0);

        // Return result
        return result;
    }

    static byte[] GenerateHeader(SplashHeader header)
    {
        // Write header signature
        byte[] result = new byte[PAGE_SIZE];
        Encoding.ASCII.GetBytes("SPLASH!!").CopyTo(result, 0);

        // Write header data
        BitConverter.GetBytes(header.width).CopyTo(result, 8);
        BitConverter.GetBytes(header.height).CopyTo(result, 12);
        BitConverter.GetBytes(header.mode).CopyTo(result, 16);
        BitConverter.GetBytes(header.length).CopyTo(result, 20);

        // Return result
        return result;
    }

    static byte[] ReadBitmap(FileStream fs, long origin, int length)
    {
        // Move file stream to origin
        fs.Seek(origin, SeekOrigin.Begin);

        // Read length amount of pages
        byte[] result = new byte[length * PAGE_SIZE];
        if (fs.Read(result, 0, result.Length) < result.Length)
        {
            return null;
        }

        // Return result if read was successfull
        return result;
    }

    static bool WriteBitmap(FileStream fs, byte[] bitmap, long origin, int old_len)
    {
        // Get old length in bytes
        int old_length = old_len * PAGE_SIZE;

        // If new bitmap is bigger than the one it replaces
        if (bitmap.Length > old_length)
        {
            // Move to the end of the old bitmap
            fs.Seek(origin + old_length, SeekOrigin.Begin);

            // Check if new bitmap fits in the image
            byte[] new_pages = new byte[bitmap.Length - old_length];
            int read = fs.Read(new_pages, 0, new_pages.Length);
            if (read < new_pages.Length || new_pages.Any(o => o != 0)) {
                return false;
            }
        }
        // If new bitmap is smaller than the one it replaces
        else
        {
            // Clear the part of the old bitmap that is not overwritten by the new bitmap
            fs.Seek(origin + bitmap.Length, SeekOrigin.Begin);
            fs.Write(new byte[old_length - bitmap.Length]);
        }

        // Write the new bitmap
        fs.Seek(origin, SeekOrigin.Begin);
        fs.Write(bitmap);
        return true;
    }

    static Bitmap DecodeBitmap(byte[] bitmap, int w, int h, bool rle)
    {
        // Create result bitmap
        Bitmap bm = new Bitmap(w, h);
        MemoryStream ms = new MemoryStream(bitmap);

        // Get the first pixel
        byte[] pixel = new byte[4];
        if (rle)
        {
            ms.Read(pixel, 0, 4);
        }
        else
        {
            ms.Read(pixel, 1, 3);
        }

        // Loop over all pixels
        for (int i = 0; i < h; i++)
        {
            for (int j = 0; j < w; j++)
            {
                // Set the current pixel color
                bm.SetPixel(j, i, Color.FromArgb(pixel[3], pixel[2], pixel[1]));

                // Load new pixel run
                if ((pixel[0] | 0x80) == 0x80 && rle)
                {
                    // Read the new pixel run
                    if(ms.Read(pixel, 0, 4) < 4)
                    {
                        return null;
                    }
                }
                // Continue pixel run
                else
                {
                    // If singular run fetch new color
                    if ((pixel[0] & 0x80) == 0 || !rle)
                    {
                        // Fetch the new color
                        if (ms.Read(pixel, 1, 3) < 3)
                        {
                            return null;
                        }
                    }

                    // Decrease current run
                    pixel[0]--;
                }
            }
        }

        // Return the image
        return bm;
    }

    static byte[] EncodeBitmapRaw(Bitmap bmp, int w, int h)
    {
        // Loop over all pixels
        byte[] result = new byte[w * h * 3];
        int pos = 0;
        for (int i = 0; i < h; i++)
        {
            for (int j = 1; j < w; j++)
            {
                // Get the current pixel color
                Color color = bmp.GetPixel(j, i);

                // Write the pixel to the result
                result[pos] = color.B;
                result[pos + 1] = color.G;
                result[pos+2] = color.R;
            }
        }

        // Return the result
        return result;
    }

    static byte[] EncodeBitmapRLE24(Bitmap bmp, int w, int h)
    {
        // Loop over all rows
        List<byte> result = new List<byte>();
        for (int i = 0; i < h; i++)
        {
            // Loop over all row pixels
            byte run_len = 0;
            Color run_color = bmp.GetPixel(0, i);
            List<Color> run_colors = new List<Color>();
            for (int j = 1; j < w; j++)
            {
                // Get the current pixel color
                Color color = bmp.GetPixel(j, i);

                // Color is same as previous
                if (color == run_color)
                {
                    // If there is a varying color run, flush it
                    if (run_colors.Count > 0)
                    {
                        result.Add((byte)(run_colors.Count - 1));
                        foreach (Color c in run_colors)
                        {
                            result.AddRange(new byte[] { c.B, c.G, c.R });
                        }
                        run_colors.Clear();
                    }

                    // Increment run length, or flush if limit reached
                    run_len++;
                    if (run_len == 0x80)
                    {
                        result.AddRange(new byte[] { 0xFF, run_color.B, run_color.G, run_color.R });
                        run_len = 0;
                    }
                }
                // Color is different from previous
                else
                {
                    // If there is a run of the last color, flush it
                    if (run_len > 0)
                    {
                        result.AddRange(new byte[] { (byte)(run_len | 0x80), run_color.B, run_color.G, run_color.R });
                        run_len = 0;
                    }
                    // Else add last color to varying color run
                    else
                    {
                        // Add color, or flush if limit reached
                        run_colors.Add(run_color);
                        if (run_colors.Count == 0x80)
                        {
                            result.Add(0x7F);
                            foreach (Color c in run_colors)
                            {
                                result.AddRange(new byte[] { c.B, c.G, c.R });
                            }
                            run_colors.Clear();
                        }
                    }

                    // Set new run color
                    run_color = color;
                }
            }

            // Flush unfinished runs
            if (run_colors.Count > 0)
            {
                run_colors.Add(run_color);
                result.Add((byte)(run_colors.Count - 1));
                foreach (Color c in run_colors)
                {
                    result.AddRange(new byte[] {c.B, c.G, c.R });
                }
            }
            else
            {
                result.AddRange(new byte[] { (byte)(run_len | 0x80), run_color.B, run_color.G, run_color.R });
            }
        }

        // Return the encoded array
        return result.ToArray();
    }
}