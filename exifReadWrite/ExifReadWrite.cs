﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ExifLibrary
{
    /// <summary>
    /// Read and Write an Exif Segment, output collect of Proprties.
    /// </summary>
    public class ExifReadWrite
    {

        private uint makerNoteOffset;
        private long exifIFDFieldOffset, gpsIFDFieldOffset, interopIFDFieldOffset, firstIFDFieldOffset;
        private long thumbOffsetLocation, thumbSizeLocation;
        private uint thumbOffsetValue, thumbSizeValue;
        public BitConverterEx.ByteOrder ByteOrder { get; set; }
        private bool makerNoteProcessed = false;

        public ExifReadWrite () {

            Properties = new ExifPropertyCollection();
            Encoding = Encoding.UTF8;
            Thumbnail = null;

                
        }



        /// <summary>
        /// Gets the collection of Exif properties contained in the Exif Segment
        /// </summary>
        public ExifPropertyCollection Properties { get; private set; }
        /// <summary>
        /// Gets or sets the embedded thumbnail image.
        /// </summary>

        /// <summary>
        /// Gets the encoding used for text metadata when the source encoding is unknown.
        /// </summary>
        public Encoding Encoding { get; protected set; }
        /// <summary>
        /// Gets or sets the embedded thumbnail image.
        /// </summary>
        public ImageFile Thumbnail { get; set; }

        /// <summary>
        /// Reads the APP1 section containing Exif metadata.
        /// </summary>
        public void ReadExifAPP1(byte[] header)
        {
            // Find the APP1 section containing Exif metadata

            SortedList<int, IFD> ifdqueue = new SortedList<int, IFD>();
            makerNoteOffset = 0;

            // TIFF header
            int tiffoffset = 6;
            if (header[tiffoffset] == 0x49 && header[tiffoffset + 1] == 0x49)
                ByteOrder = BitConverterEx.ByteOrder.LittleEndian;
            else if (header[tiffoffset] == 0x4D && header[tiffoffset + 1] == 0x4D)
                ByteOrder = BitConverterEx.ByteOrder.BigEndian;
            else
                throw new NotValidExifFileException();

            // TIFF header may have a different byte order
            BitConverterEx.ByteOrder tiffByteOrder = ByteOrder;
            if (BitConverterEx.LittleEndian.ToUInt16(header, tiffoffset + 2) == 42)
                tiffByteOrder = BitConverterEx.ByteOrder.LittleEndian;
            else if (BitConverterEx.BigEndian.ToUInt16(header, tiffoffset + 2) == 42)
                tiffByteOrder = BitConverterEx.ByteOrder.BigEndian;
            else
                throw new NotValidExifFileException();

            // Offset to 0th IFD
            int ifd0offset = (int)BitConverterEx.ToUInt32(header, tiffoffset + 4, tiffByteOrder, BitConverterEx.SystemByteOrder);
            ifdqueue.Add(ifd0offset, IFD.Zeroth);

            BitConverterEx conv = new BitConverterEx(ByteOrder, BitConverterEx.SystemByteOrder);
            int thumboffset = -1;
            int thumblength = 0;
            int thumbtype = -1;
            // Read IFDs
            while (ifdqueue.Count != 0)
            {
                int ifdoffset = tiffoffset + ifdqueue.Keys[0];
                IFD currentifd = ifdqueue.Values[0];
                ifdqueue.RemoveAt(0);

                // Field count
                ushort fieldcount = conv.ToUInt16(header, ifdoffset);
                for (short i = 0; i < fieldcount; i++)
                {
                    // Read field info
                    int fieldoffset = ifdoffset + 2 + 12 * i;
                    ushort tag = conv.ToUInt16(header, fieldoffset);
                    ushort type = conv.ToUInt16(header, fieldoffset + 2);
                    uint count = conv.ToUInt32(header, fieldoffset + 4);
                    byte[] value = new byte[4];
                    Array.Copy(header, fieldoffset + 8, value, 0, 4);

                    // Fields containing offsets to other IFDs
                    if (currentifd == IFD.Zeroth && tag == 0x8769)
                    {
                        int exififdpointer = (int)conv.ToUInt32(value, 0);
                        ifdqueue.Add(exififdpointer, IFD.EXIF);
                    }
                    else if (currentifd == IFD.Zeroth && tag == 0x8825)
                    {
                        int gpsifdpointer = (int)conv.ToUInt32(value, 0);
                        ifdqueue.Add(gpsifdpointer, IFD.GPS);
                    }
                    else if (currentifd == IFD.EXIF && tag == 0xa005)
                    {
                        int interopifdpointer = (int)conv.ToUInt32(value, 0);
                        ifdqueue.Add(interopifdpointer, IFD.Interop);
                    }

                    // Save the offset to maker note data
                    if (currentifd == IFD.EXIF && tag == 37500)
                        makerNoteOffset = conv.ToUInt32(value, 0);

                    // Calculate the bytes we need to read
                    uint baselength = 0;
                    if (type == 1 || type == 2 || type == 7)
                        baselength = 1;
                    else if (type == 3)
                        baselength = 2;
                    else if (type == 4 || type == 9)
                        baselength = 4;
                    else if (type == 5 || type == 10)
                        baselength = 8;
                    uint totallength = count * baselength;

                    // If field value does not fit in 4 bytes
                    // the value field is an offset to the actual
                    // field value
                    int fieldposition = 0;
                    if (totallength > 4)
                    {
                        fieldposition = tiffoffset + (int)conv.ToUInt32(value, 0);
                        value = new byte[totallength];
                        Array.Copy(header, fieldposition, value, 0, (int)totallength);
                    }

                    // Compressed thumbnail data
                    if (currentifd == IFD.First && tag == 0x201)
                    {
                        thumbtype = 0;
                        thumboffset = (int)conv.ToUInt32(value, 0);
                    }
                    else if (currentifd == IFD.First && tag == 0x202)
                        thumblength = (int)conv.ToUInt32(value, 0);

                    // Uncompressed thumbnail data
                    if (currentifd == IFD.First && tag == 0x111)
                    {
                        thumbtype = 1;
                        // Offset to first strip
                        if (type == 3)
                            thumboffset = (int)conv.ToUInt16(value, 0);
                        else
                            thumboffset = (int)conv.ToUInt32(value, 0);
                    }
                    else if (currentifd == IFD.First && tag == 0x117)
                    {
                        thumblength = 0;
                        for (int j = 0; j < count; j++)
                        {
                            if (type == 3)
                                thumblength += (int)conv.ToUInt16(value, 0);
                            else
                                thumblength += (int)conv.ToUInt32(value, 0);
                        }
                    }

                    // Create the exif property from the interop data
                    ExifProperty prop = ExifPropertyFactory.Get(tag, type, count, value, ByteOrder, currentifd, Encoding);
                    Properties.Add(prop);
                }

                // 1st IFD pointer
                int firstifdpointer = (int)conv.ToUInt32(header, ifdoffset + 2 + 12 * fieldcount);
                if (firstifdpointer != 0)
                    ifdqueue.Add(firstifdpointer, IFD.First);
                // Read thumbnail
                if (thumboffset != -1 && thumblength != 0 && Thumbnail == null)
                {
                    if (thumbtype == 0)
                    {
                        using (MemoryStream ts = new MemoryStream(header, tiffoffset + thumboffset, thumblength))
                        {
                            Thumbnail = ImageFile.FromStream(ts);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Replaces the contents of the APP1 section with the Exif properties.
        /// </summary>
        public byte[] WriteExifApp1(bool preserveMakerNote)
        {

            byte[] exifSegment = null;
            // Zero out IFD field offsets. We will fill those as we write the IFD sections
            exifIFDFieldOffset = 0;
            gpsIFDFieldOffset = 0;
            interopIFDFieldOffset = 0;
            firstIFDFieldOffset = 0;
            // We also do not know the location of the embedded thumbnail yet
            thumbOffsetLocation = 0;
            thumbOffsetValue = 0;
            thumbSizeLocation = 0;
            thumbSizeValue = 0;
            // Write thumbnail tags if they are missing, remove otherwise
            int indexf = -1;
            int indexl = -1;
            for (int i = 0; i < Properties.Count; i++)
            {
                ExifProperty prop = Properties[i];
                if (prop.Tag == ExifTag.ThumbnailJPEGInterchangeFormat) indexf = i;
                if (prop.Tag == ExifTag.ThumbnailJPEGInterchangeFormatLength) indexl = i;
                if (indexf != -1 && indexl != -1) break;
            }
            if (Thumbnail == null)
            {

                ExifProperty format = null;
                ExifProperty formatLength = null;

                if (indexf != -1)
                    format = Properties[indexf];
                if (indexl != -1)
                    formatLength= Properties[indexl];

                if (format != null) Properties.Remove(format);
                if (formatLength != null) Properties.Remove(formatLength);
            }
            else
            {
                if (indexf == -1)
                    Properties.Add(new ExifUInt(ExifTag.ThumbnailJPEGInterchangeFormat, 0));
                if (indexl == -1)
                    Properties.Add(new ExifUInt(ExifTag.ThumbnailJPEGInterchangeFormatLength, 0));
            }

            // Which IFD sections do we have?
            Dictionary<ExifTag, ExifProperty> ifdzeroth = new Dictionary<ExifTag, ExifProperty>();
            Dictionary<ExifTag, ExifProperty> ifdexif = new Dictionary<ExifTag, ExifProperty>();
            Dictionary<ExifTag, ExifProperty> ifdgps = new Dictionary<ExifTag, ExifProperty>();
            Dictionary<ExifTag, ExifProperty> ifdinterop = new Dictionary<ExifTag, ExifProperty>();
            Dictionary<ExifTag, ExifProperty> ifdfirst = new Dictionary<ExifTag, ExifProperty>();

            foreach (ExifProperty prop in Properties)
            {
                switch (prop.IFD)
                {
                    case IFD.Zeroth:
                        ifdzeroth.Add(prop.Tag, prop);
                        break;
                    case IFD.EXIF:
                        ifdexif.Add(prop.Tag, prop);
                        break;
                    case IFD.GPS:
                        ifdgps.Add(prop.Tag, prop);
                        break;
                    case IFD.Interop:
                        ifdinterop.Add(prop.Tag, prop);
                        break;
                    case IFD.First:
                        ifdfirst.Add(prop.Tag, prop);
                        break;
                }
            }

            // Add IFD pointers if they are missing
            // We will write the pointer values later on
            if (ifdexif.Count != 0 && !ifdzeroth.ContainsKey(ExifTag.EXIFIFDPointer))
                ifdzeroth.Add(ExifTag.EXIFIFDPointer, new ExifUInt(ExifTag.EXIFIFDPointer, 0));
            if (ifdgps.Count != 0 && !ifdzeroth.ContainsKey(ExifTag.GPSIFDPointer))
                ifdzeroth.Add(ExifTag.GPSIFDPointer, new ExifUInt(ExifTag.GPSIFDPointer, 0));
            if (ifdinterop.Count != 0 && !ifdexif.ContainsKey(ExifTag.InteroperabilityIFDPointer))
                ifdexif.Add(ExifTag.InteroperabilityIFDPointer, new ExifUInt(ExifTag.InteroperabilityIFDPointer, 0));

            // Remove IFD pointers if IFD sections are missing
            if (ifdexif.Count == 0 && ifdzeroth.ContainsKey(ExifTag.EXIFIFDPointer))
                ifdzeroth.Remove(ExifTag.EXIFIFDPointer);
            if (ifdgps.Count == 0 && ifdzeroth.ContainsKey(ExifTag.GPSIFDPointer))
                ifdzeroth.Remove(ExifTag.GPSIFDPointer);
            if (ifdinterop.Count == 0 && ifdexif.ContainsKey(ExifTag.InteroperabilityIFDPointer))
                ifdexif.Remove(ExifTag.InteroperabilityIFDPointer);

            if (ifdzeroth.Count == 0 && ifdgps.Count == 0 && ifdinterop.Count == 0 && ifdfirst.Count == 0 && Thumbnail == null)
            {
                // Nothing to write
                return null;
            }

            // We will need these bitconverter to write byte-ordered data
            BitConverterEx bceExif = new BitConverterEx(BitConverterEx.SystemByteOrder, ByteOrder);

            // Create a memory stream to write the APP1 section to
            using (MemoryStream ms = new MemoryStream())
            {

                // Exif identifer
                ms.Write(Encoding.ASCII.GetBytes("Exif\0\0"), 0, 6);

                // TIFF header
                // Byte order
                long tiffoffset = ms.Position;
                ms.Write((ByteOrder == BitConverterEx.ByteOrder.LittleEndian ? new byte[] { 0x49, 0x49 } : new byte[] { 0x4D, 0x4D }), 0, 2);
                // TIFF ID
                ms.Write(bceExif.GetBytes((ushort)42), 0, 2);
                // Offset to 0th IFD
                ms.Write(bceExif.GetBytes((uint)8), 0, 4);

                // Write IFDs
                WriteIFD(ms, ifdzeroth, IFD.Zeroth, tiffoffset, preserveMakerNote);
                uint exififdrelativeoffset = (uint)(ms.Position - tiffoffset);
                WriteIFD(ms, ifdexif, IFD.EXIF, tiffoffset, preserveMakerNote);
                uint gpsifdrelativeoffset = (uint)(ms.Position - tiffoffset);
                WriteIFD(ms, ifdgps, IFD.GPS, tiffoffset, preserveMakerNote);
                uint interopifdrelativeoffset = (uint)(ms.Position - tiffoffset);
                WriteIFD(ms, ifdinterop, IFD.Interop, tiffoffset, preserveMakerNote);
                uint firstifdrelativeoffset = (uint)(ms.Position - tiffoffset);
                WriteIFD(ms, ifdfirst, IFD.First, tiffoffset, preserveMakerNote);

                // Now that we now the location of IFDs we can go back and write IFD offsets
                if (exifIFDFieldOffset != 0)
                {
                    ms.Seek(exifIFDFieldOffset, SeekOrigin.Begin);
                    ms.Write(bceExif.GetBytes(exififdrelativeoffset), 0, 4);
                }
                if (gpsIFDFieldOffset != 0)
                {
                    ms.Seek(gpsIFDFieldOffset, SeekOrigin.Begin);
                    ms.Write(bceExif.GetBytes(gpsifdrelativeoffset), 0, 4);
                }
                if (interopIFDFieldOffset != 0)
                {
                    ms.Seek(interopIFDFieldOffset, SeekOrigin.Begin);
                    ms.Write(bceExif.GetBytes(interopifdrelativeoffset), 0, 4);
                }
                if (firstIFDFieldOffset != 0)
                {
                    ms.Seek(firstIFDFieldOffset, SeekOrigin.Begin);
                    ms.Write(bceExif.GetBytes(firstifdrelativeoffset), 0, 4);
                }
                // We can write thumbnail location now
                if (thumbOffsetLocation != 0)
                {
                    ms.Seek(thumbOffsetLocation, SeekOrigin.Begin);
                    ms.Write(bceExif.GetBytes(thumbOffsetValue), 0, 4);
                }
                if (thumbSizeLocation != 0)
                {
                    ms.Seek(thumbSizeLocation, SeekOrigin.Begin);
                    ms.Write(bceExif.GetBytes(thumbSizeValue), 0, 4);
                }

                ms.Flush();
                ms.Position = 0;
                exifSegment = ms.ToArray();

            }
            return exifSegment;
        }

        private void WriteIFD(MemoryStream stream, Dictionary<ExifTag, ExifProperty> ifd, IFD ifdtype, long tiffoffset, bool preserveMakerNote)
        {
            BitConverterEx conv = new BitConverterEx(BitConverterEx.SystemByteOrder, ByteOrder);

            // Create a queue of fields to write
            Queue<ExifProperty> fieldqueue = new Queue<ExifProperty>();
            foreach (ExifProperty prop in ifd.Values)
                if (prop.Tag != ExifTag.MakerNote)
                    fieldqueue.Enqueue(prop);
            // Push the maker note data to the end
            if (ifd.ContainsKey(ExifTag.MakerNote))
                fieldqueue.Enqueue(ifd[ExifTag.MakerNote]);

            // Offset to start of field data from start of TIFF header
            uint dataoffset = (uint)(2 + ifd.Count * 12 + 4 + stream.Position - tiffoffset);
            uint currentdataoffset = dataoffset;
            long absolutedataoffset = stream.Position + (2 + ifd.Count * 12 + 4);

            bool makernotewritten = false;
            // Field count
            stream.Write(conv.GetBytes((ushort)ifd.Count), 0, 2);
            // Fields
            while (fieldqueue.Count != 0)
            {
                ExifProperty field = fieldqueue.Dequeue();
                ExifInterOperability interop = field.Interoperability;

                uint fillerbytecount = 0;

                // Try to preserve the makernote data offset
                if (!makernotewritten &&
                    !makerNoteProcessed &&
                    makerNoteOffset != 0 &&
                    ifdtype == IFD.EXIF &&
                    field.Tag != ExifTag.MakerNote &&
                    interop.Data.Length > 4 &&
                    currentdataoffset + interop.Data.Length > makerNoteOffset &&
                    ifd.ContainsKey(ExifTag.MakerNote))
                {
                    // Delay writing this field until we write makernote data
                    fieldqueue.Enqueue(field);
                    continue;
                }
                else if (field.Tag == ExifTag.MakerNote)
                {
                    makernotewritten = true;
                    // We may need to write filler bytes to preserve maker note offset
                    if (preserveMakerNote && !makerNoteProcessed && (makerNoteOffset > currentdataoffset))
                        fillerbytecount = makerNoteOffset - currentdataoffset;
                    else
                        fillerbytecount = 0;
                }

                // Tag
                stream.Write(conv.GetBytes(interop.TagID), 0, 2);
                // Type
                stream.Write(conv.GetBytes(interop.TypeID), 0, 2);
                // Count
                stream.Write(conv.GetBytes(interop.Count), 0, 4);
                // Field data
                byte[] data = interop.Data;
                if (ByteOrder != BitConverterEx.SystemByteOrder &&
                    (interop.TypeID == 3 || interop.TypeID == 4 || interop.TypeID == 9 ||
                    interop.TypeID == 5 || interop.TypeID == 10))
                {
                    int vlen = 4;
                    if (interop.TypeID == 3) vlen = 2;
                    int n = data.Length / vlen;

                    for (int i = 0; i < n; i++)
                        Array.Reverse(data, i * vlen, vlen);
                }

                // Fields containing offsets to other IFDs
                // Just store their offets, we will write the values later on when we know the lengths of IFDs
                if (ifdtype == IFD.Zeroth && interop.TagID == 0x8769)
                    exifIFDFieldOffset = stream.Position;
                else if (ifdtype == IFD.Zeroth && interop.TagID == 0x8825)
                    gpsIFDFieldOffset = stream.Position;
                else if (ifdtype == IFD.EXIF && interop.TagID == 0xa005)
                    interopIFDFieldOffset = stream.Position;
                else if (ifdtype == IFD.First && interop.TagID == 0x201)
                    thumbOffsetLocation = stream.Position;
                else if (ifdtype == IFD.First && interop.TagID == 0x202)
                    thumbSizeLocation = stream.Position;

                // Write 4 byte field value or field data
                if (data.Length <= 4)
                {
                    stream.Write(data, 0, data.Length);
                    for (int i = data.Length; i < 4; i++)
                        stream.WriteByte(0);
                }
                else
                {
                    // Pointer to data area relative to TIFF header
                    stream.Write(conv.GetBytes(currentdataoffset + fillerbytecount), 0, 4);
                    // Actual data
                    long currentoffset = stream.Position;
                    stream.Seek(absolutedataoffset, SeekOrigin.Begin);
                    // Write filler bytes
                    for (int i = 0; i < fillerbytecount; i++)
                        stream.WriteByte(0xFF);
                    stream.Write(data, 0, data.Length);
                    stream.Seek(currentoffset, SeekOrigin.Begin);
                    // Increment pointers
                    currentdataoffset += fillerbytecount + (uint)data.Length;
                    absolutedataoffset += fillerbytecount + data.Length;
                }
            }
            // Offset to 1st IFD
            // We will write zeros for now. This will be filled after we write all IFDs
            if (ifdtype == IFD.Zeroth)
                firstIFDFieldOffset = stream.Position;
            stream.Write(new byte[] { 0, 0, 0, 0 }, 0, 4);

            // Seek to end of IFD
            stream.Seek(absolutedataoffset, SeekOrigin.Begin);

            // Write thumbnail data
            if (ifdtype == IFD.First)
            {
                if (Thumbnail != null)
                {
                    using (MemoryStream ts = new MemoryStream())
                    {
                        Thumbnail.Save(ts);
                        byte[] thumb = ts.ToArray();
                        thumbOffsetValue = (uint)(stream.Position - tiffoffset);
                        thumbSizeValue = (uint)thumb.Length;
                        stream.Write(thumb, 0, thumb.Length);
                    }
                }
                else
                {
                    thumbOffsetValue = 0;
                    thumbSizeValue = 0;
                }
            }
        }
    }
}
