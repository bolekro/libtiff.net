﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

using LibJpeg.Classic;

namespace LibJpeg
{
    /// <summary>
    /// Class for JPEG compression and decompression
    /// </summary>
#if EXPOSE_LIBJPEG
    public
#endif
    class Jpeg
    {
        private jpeg_compress_struct m_compressor = new jpeg_compress_struct(new jpeg_error_mgr());
        private jpeg_decompress_struct m_decompressor = new jpeg_decompress_struct(new jpeg_error_mgr());

        private CompressionParameters m_compressionParameters = new CompressionParameters();
        private DecompressionParameters m_decompressionParameters = new DecompressionParameters();

        /// <summary>
        /// Advanced users may set specific parameters of compression
        /// </summary>
        public CompressionParameters CompressionParameters
        {
            get
            {
                return m_compressionParameters;
            }
            set
            {
                if (value == null)
                    throw new ArgumentNullException("value");

                m_compressionParameters = value;
            }
        }

        /// <summary>
        /// Advanced users may set specific parameters of decompression
        /// </summary>
        public DecompressionParameters DecompressionParameters
        {
            get
            {
                return m_decompressionParameters;
            }
            set
            {
                if (value == null)
                    throw new ArgumentNullException("value");

                m_decompressionParameters = value;
            }
        }

        /// <summary>
        /// Compresses bitmap to JPEG
        /// </summary>
        /// <param name="input">Stream with bitmap data</param>
        /// <param name="output">Stream for output of compressed JPEG</param>
        public void CompressBitmap(Stream inputBitmap, Stream output)
        {
            BitmapSource source = new BitmapSource(inputBitmap);
            source.SetTracer(m_compressor.TRACEMS);
            Compress(source, output);
        }

        /// <summary>
        /// Compresses any image described as ICompressSource to JPEG
        /// </summary>
        /// <param name="source">Contains description of input image</param>
        /// <param name="output">Stream for output of compressed JPEG</param>
        public void Compress(INonCompressedImage source, Stream output)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            if (output == null)
                throw new ArgumentNullException("output");

            m_compressor.Image_width = source.Width;
            m_compressor.Image_height = source.Height;
            m_compressor.In_color_space = (J_COLOR_SPACE)source.Colorspace;
            m_compressor.Input_components = source.ComponentsPerPixel;
            m_compressor.Data_precision = source.DataPrecision;

            m_compressor.jpeg_set_defaults();

            //we need to set density parameters after setting of default jpeg parameters
            m_compressor.Density_unit = source.DensityUnit;
            m_compressor.X_density = (short)source.DensityX;
            m_compressor.Y_density = (short)source.DensityY;

            applyParameters(m_compressionParameters);

            // Specify data destination for compression
            m_compressor.jpeg_stdio_dest(output);

            // Start compression
            m_compressor.jpeg_start_compress(true);

            // Process  pixels
            source.Start();
            while (m_compressor.Next_scanline < m_compressor.Image_height)
            {
                byte[] row = source.GetPixelRow();
                if (row == null)
                    throw new InvalidDataException("Row of pixels is null");

                byte[][] rowForDecompressor = new byte[1][];
                rowForDecompressor[0] = row;
                m_compressor.jpeg_write_scanlines(rowForDecompressor, 1);
            }
            source.Finish();

            // Finish compression and release memory
            m_compressor.jpeg_finish_compress();
        }

        /// <summary>
        /// Decompresses JPEG image to bitmap
        /// </summary>
        /// <param name="jpeg">Stream with JPEG data</param>
        /// <param name="outputBitmap">Stream with resulted bitmap</param>
        /// <param name="bitmapFormat">Expected format of resulted bitmap</param>
        public void DecompressToBitmap(Stream jpeg, Stream outputBitmap, BitmapFormat bitmapFormat)
        {
            IDecompressDestination destination = new BitmapDestination(outputBitmap, bitmapFormat == BitmapFormat.OS2);
            Decompress(jpeg, destination);
        }

        /// <summary>
        /// Decompresses JPEG image to any image described as ICompressDestination
        /// </summary>
        /// <param name="jpeg">Stream with JPEG data</param>
        /// <param name="destination">Stream for output of compressed JPEG</param>
        public void Decompress(Stream jpeg, IDecompressDestination destination)
        {
            if (jpeg == null)
                throw new ArgumentNullException("jpeg");

            if (destination == null)
                throw new ArgumentNullException("destination");

            beforeDecompress(jpeg);

            // Start decompression
            m_decompressor.jpeg_start_decompress();

            ImageParameters parameters = getImageParametersFromDecompressor();
            destination.SetImageParameters(parameters);
            destination.Start();

            /* Process data */
            while (m_decompressor.Output_scanline < m_decompressor.Output_height)
            {
                byte[][] row = jpeg_common_struct.AllocJpegSamples(m_decompressor.Output_width * m_decompressor.Output_components, 1);
                m_decompressor.jpeg_read_scanlines(row, 1);
                destination.ProcessPixelsRow(row[0]);
            }

            destination.Finish();

            // Finish decompression and release memory.
            m_decompressor.jpeg_finish_decompress();
        }

        /// <summary>
        /// Tunes decompressor
        /// </summary>
        /// <param name="jpeg">Stream with input compressed JPEG data</param>
        private void beforeDecompress(Stream jpeg)
        {
            m_decompressor.jpeg_stdio_src(jpeg);
            /* Read file header, set default decompression parameters */
            m_decompressor.jpeg_read_header(true);

            applyParameters(m_decompressionParameters);
            m_decompressor.jpeg_calc_output_dimensions();
        }

        private ImageParameters getImageParametersFromDecompressor()
        {
            ImageParameters result = new ImageParameters();
            result.Colorspace = (Colorspace)m_decompressor.Out_color_space;
            result.QuantizeColors = m_decompressor.Quantize_colors;
            result.Width = m_decompressor.Output_width;
            result.Height = m_decompressor.Output_height;
            result.ComponentsPerSample = m_decompressor.Out_color_components;
            result.Components = m_decompressor.Output_components;
            result.ActualNumberOfColors = m_decompressor.Actual_number_of_colors;
            result.Colormap = m_decompressor.Colormap;
            result.DensityUnit = m_decompressor.Density_unit;
            result.DensityX = m_decompressor.X_density;
            result.DensityY = m_decompressor.Y_density;
            return result;
        }

        public jpeg_compress_struct ClassicCompressor
        {
            get
            {
                return m_compressor;
            }
        }

        public jpeg_decompress_struct ClassicDecompressor
        {
            get
            {
                return m_decompressor;
            }
        }

        /// <summary>
        /// Delegate for application-supplied marker processing methods.
        /// Need not pass marker code since it is stored in cinfo.unread_marker.
        /// </summary>
        public delegate bool MarkerParser(Jpeg decompressor);

        /* Install a special processing method for COM or APPn markers. */
        public void SetMarkerProcessor(int markerCode, MarkerParser routine)
        {
            jpeg_decompress_struct.jpeg_marker_parser_method f = delegate { return routine(this); };
            m_decompressor.jpeg_set_marker_processor(markerCode, f);
        }

        private void applyParameters(DecompressionParameters parameters)
        {
            Debug.Assert(parameters != null);

            if (parameters.OutColorspace != Colorspace.Unknown)
                m_decompressor.Out_color_space = (J_COLOR_SPACE)parameters.OutColorspace;

            m_decompressor.Scale_num = parameters.ScaleNumerator;
            m_decompressor.Scale_denom = parameters.ScaleDenominator;
            m_decompressor.Buffered_image = parameters.BufferedImage;
            m_decompressor.Raw_data_out = parameters.RawDataOut;
            m_decompressor.Dct_method = (J_DCT_METHOD)parameters.DCTMethod;
            m_decompressor.Dither_mode = (J_DITHER_MODE)parameters.DitherMode;
            m_decompressor.Do_fancy_upsampling = parameters.DoFancyUpsampling;
            m_decompressor.Do_block_smoothing = parameters.DoBlockSmoothing;
            m_decompressor.Quantize_colors = parameters.QuantizeColors;
            m_decompressor.Two_pass_quantize = parameters.TwoPassQuantize;
            m_decompressor.Desired_number_of_colors = parameters.DesiredNumberOfColors;
            m_decompressor.Enable_1pass_quant = parameters.EnableOnePassQuantizer;
            m_decompressor.Enable_external_quant = parameters.EnableExternalQuant;
            m_decompressor.Enable_2pass_quant = parameters.EnableTwoPassQuantizer;
            m_decompressor.Err.Trace_level = parameters.TraceLevel;
        }

        private void applyParameters(CompressionParameters parameters)
        {
            Debug.Assert(parameters != null);

            if (parameters.Colorspace != Colorspace.Unknown)
                m_compressor.jpeg_set_colorspace((J_COLOR_SPACE)parameters.Colorspace);

            m_compressor.Optimize_coding = parameters.OptimizeCoding;
            m_compressor.Restart_interval = parameters.RestartInterval;
            m_compressor.Restart_in_rows = parameters.RestartInRows;
            m_compressor.Smoothing_factor = parameters.SmoothingFactor;
            m_compressor.Dct_method = (J_DCT_METHOD)parameters.DCTMethod;
            m_compressor.Err.Trace_level = parameters.TraceLevel;

            m_compressor.jpeg_set_quality(parameters.Quality, parameters.ForceBaseline);

            if (parameters.SimpleProgressive)
                m_compressor.jpeg_simple_progression();
        }
    }
}
