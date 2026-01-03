using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Zstd.Net;

namespace SCJMapper_V2.p4kFile
{
    /// <summary>
    /// Represents a file header entry in the p4k archive.
    /// Based on Zip64 format with custom CryTek/Star Citizen extensions.
    /// </summary>
    internal sealed class p4kFileHeader : IDisposable
    {
        // ============================================================
        // Public Properties
        // ============================================================
        public bool IsValid => _itemValid;
        public string Filename => _filename;
        public long RecordOffset => _recordOffset;
        public long FileOffset => _fileOffset;
        public long FileSizeComp => _fileSizeComp;
        public long FileSizeUnComp => _fileSizeUnComp;
        public DateTime FileModifyDate => _fileDateTime;

        // ============================================================
        // Private State
        // ============================================================
        private MyRecord _item;
        private readonly bool _itemValid;
        private readonly MyZ64ExtraRecord _z64Item;
        private readonly long _recordOffset = -1;
        private readonly long _fileOffset;
        private readonly string _filename = "";
        private readonly DateTime _fileDateTime = new(1970, 1, 1);
        private readonly long _fileSizeComp;
        private readonly long _fileSizeUnComp;
        private bool _disposed;

        // ============================================================
        // Constructor
        // ============================================================
        public p4kFileHeader(p4kRecReader reader)
        {
            System.Diagnostics.Trace.Assert(
                Marshal.SizeOf<MyRecord>() == RecordLength,
                $"Record size does not match! ({Marshal.SizeOf<MyRecord>()})");
            System.Diagnostics.Trace.Assert(
                Marshal.SizeOf<MyZ64ExtraRecord>() == Z64ExtraRecordLength,
                $"Extra Record size does not match! ({Marshal.SizeOf<MyZ64ExtraRecord>()})");

            if (!reader.IsOpen()) return;

            try
            {
                long cPos;
                do
                {
                    reader.AdvancePage();
                    cPos = reader.Position;
                    _recordOffset = cPos;
                    _item = p4kRecReader.ByteToType<MyRecord>(reader.TheReader);
                    _itemValid = _item.ID.SequenceEqual(p4kSignatures.LocalFileHeaderCry);
                } while (cPos < reader.Length && !_itemValid);

                if (!_itemValid)
                {
                    _recordOffset = -1;
                    _fileOffset = -1;
                    return;
                }

                if (_item.FilenameLength > 0)
                    _filename = ReadFilename(reader);

                if (_item.ExtraFieldLength > 0)
                    _z64Item = ReadExtradata(reader);

                _fileDateTime = p4kFileTStamp.FromDos(_item.LastModDate, _item.LastModTime);

                if (_item.CompressedSize < 0xffffffff)
                {
                    _fileSizeComp = _item.CompressedSize;
                    _fileSizeUnComp = _item.UncompressedSize;
                }
                else
                {
                    _fileSizeComp = (long)_z64Item.CompressedSize;
                    _fileSizeUnComp = (long)_z64Item.UncompressedSize;
                }

                _fileOffset = reader.TheReader.BaseStream.Position;
                reader.TheReader.BaseStream.Seek(_fileSizeComp, SeekOrigin.Current);
            }
            catch
            {
                _itemValid = false;
                HandleInvalidItem();
            }
        }

        // ============================================================
        // Public Methods
        // ============================================================

        /// <summary>
        /// Extracts and decompresses the file content.
        /// </summary>
        public byte[] GetFile(p4kRecReader reader)
        {
            if (!_itemValid) return [];

            reader.Seek(_fileOffset);
            var fileBytes = reader.ReadBytes((int)_fileSizeComp);

            if (_item.CompressionMethod == 0x64)
            {
                // p4k ZStd compression
                try
                {
                    return Decompressor.Unwrap(fileBytes);
                }
                catch (ZstdException e)
                {
                    Console.WriteLine($"ZStd - Cannot decode file: {_filename}");
                    Console.WriteLine($"Error: {e.Message}");
                    return [];
                }
            }

            return fileBytes;
        }

        // ============================================================
        // Private Helpers
        // ============================================================

        private string ReadFilename(p4kRecReader reader)
        {
            var fileNameBytes = reader.ReadBytes(_item.FilenameLength);
            return Encoding.ASCII.GetString(fileNameBytes);
        }

        private MyZ64ExtraRecord ReadExtradata(p4kRecReader reader)
        {
            var z64Item = p4kRecReader.ByteToType<MyZ64ExtraRecord>(reader.TheReader);
            // Skip remaining extra data
            var remaining = _item.ExtraFieldLength - Z64ExtraRecordLength;
            if (remaining > 0)
                reader.ReadBytes(remaining);
            return z64Item;
        }

        private void HandleInvalidItem()
        {
            if (!_itemValid) return;

            if (_item.ID.SequenceEqual(p4kSignatures.CentralDirRecord))
                throw new OperationCanceledException($"EOF - found Central Directory header {_item.ID}");
            else
                throw new NotSupportedException($"Cannot process fileheader ID {_item.ID}");
        }

        // ============================================================
        // Structs
        // ============================================================

        private const int RecordLength = 30;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct MyRecord
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] ID;
            public ushort ExtractVersion;
            public ushort BitFlags;
            public ushort CompressionMethod;
            public ushort LastModTime;
            public ushort LastModDate;
            public uint CRC32;
            public uint CompressedSize;
            public uint UncompressedSize;
            public ushort FilenameLength;
            public ushort ExtraFieldLength;
        }

        private const int Z64ExtraRecordLength = 32;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct MyZ64ExtraRecord
        {
            public ushort ID;
            public ushort Size;
            public ulong UncompressedSize;
            public ulong CompressedSize;
            public ulong LocalHeaderOffset;
            public uint DiskStart;
        }

        // ============================================================
        // IDisposable
        // ============================================================

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
