using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Zstd.Net;

/// <summary>
/// Exception thrown when Zstd operations fail.
/// </summary>
public class ZstdException : Exception
{
    public ZstdException(string message) : base(message) { }
    public ZstdException(Exception ex) : base("Error processing file", ex) { }
}

/// <summary>
/// Legacy exception class for backwards compatibility.
/// </summary>
public class ZStdException : ZstdException
{
    public ZStdException(string message) : base(message) { }
    public ZStdException(Exception ex) : base(ex) { }
}

/// <summary>
/// Simple decompressor that wraps the streaming API for single-shot decompression.
/// </summary>
public sealed class Decompressor : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Decompresses a Zstd-compressed byte array (static version).
    /// </summary>
    public static byte[] Unwrap(byte[] src)
    {
        if (src is null || src.Length == 0)
            return [];

        using var inputStream = new MemoryStream(src);
        using var zstdStream = new InputStream(inputStream);
        using var outputStream = new MemoryStream();
        zstdStream.CopyTo(outputStream);
        return outputStream.ToArray();
    }

    /// <summary>
    /// Instance method for backwards compatibility.
    /// </summary>
    public byte[] Decompress(byte[] src) => Unwrap(src);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Zstd decompression stream wrapper.
/// </summary>
public sealed class InputStream : Stream
{
    private readonly Stream _inputStream;
    private readonly bool _leaveOpen;
    private readonly nint _zst;
    private readonly byte[] _inputBufferArray;
    private ZstdBuffer _inputBuffer;
    private ZstdBuffer _outputBuffer;
    private bool _closed;
    private int _inputArrayPosition;
    private int _inputArraySize;
    private bool _depleted;

    /// <summary>
    /// Checks if the given buffer starts with a Zstd magic number.
    /// </summary>
    public static bool IsZstdStream(byte[] buffBytes, long buffLen) =>
        buffLen > 3 &&
        buffBytes[0] == 0x28 &&
        buffBytes[1] == 0xB5 &&
        buffBytes[2] == 0x2F &&
        buffBytes[3] == 0xFD;

    public InputStream(Stream inputStream, bool leaveOpen = false)
    {
        _inputStream = inputStream;
        _leaveOpen = leaveOpen;
        
        // Ensure native library is loaded
        ZstdNative.EnsureLoaded();
        
        _zst = ZstdNative.CreateDStream();
        ZstdNative.CheckError(ZstdNative.InitDStream(_zst));
        
        var inSize = ZstdNative.DStreamInSize();
        _inputBuffer = new ZstdBuffer { Size = inSize };
        _inputBufferArray = new byte[(int)inSize];
        _outputBuffer = new ZstdBuffer { Size = ZstdNative.DStreamOutSize() };
    }

    protected override void Dispose(bool disposing)
    {
        if (_closed) return;
        ZstdNative.CheckError(ZstdNative.FreeDStream(_zst));
        if (!_leaveOpen) _inputStream.Dispose();
        _closed = true;
        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (count == 0) return 0;

        var retVal = 0;
        var alloc1 = GCHandle.Alloc(_inputBufferArray, GCHandleType.Pinned);
        var alloc2 = GCHandle.Alloc(buffer, GCHandleType.Pinned);

        try
        {
            while (count > 0)
            {
                var left = _inputArraySize - _inputArrayPosition;
                if (left <= 0 && !_depleted)
                {
                    _inputArrayPosition = 0;
                    _inputArraySize = left = _inputStream.Read(_inputBufferArray, 0, _inputBufferArray.Length);
                    if (left <= 0)
                    {
                        left = 0;
                        _depleted = true;
                    }
                }

                _inputBuffer.Position = 0;
                if (_depleted)
                {
                    _inputBuffer.Size = 0;
                    _inputBuffer.Data = nint.Zero;
                }
                else
                {
                    _inputBuffer.Size = (nuint)left;
                    _inputBuffer.Data = Marshal.UnsafeAddrOfPinnedArrayElement(_inputBufferArray, _inputArrayPosition);
                }

                _outputBuffer.Position = 0;
                _outputBuffer.Size = (nuint)count;
                _outputBuffer.Data = Marshal.UnsafeAddrOfPinnedArrayElement(buffer, offset);

                ZstdNative.CheckError(ZstdNative.DecompressStream(_zst, ref _outputBuffer, ref _inputBuffer));

                var bytesProduced = (int)_outputBuffer.Position;
                if (bytesProduced == 0 && _depleted) break;

                retVal += bytesProduced;
                count -= bytesProduced;
                offset += bytesProduced;

                if (!_depleted)
                {
                    var bytesConsumed = (int)_inputBuffer.Position;
                    _inputArrayPosition += bytesConsumed;
                }
            }
            return retVal;
        }
        catch (Exception ex)
        {
            throw new ZstdException(ex);
        }
        finally
        {
            alloc1.Free();
            alloc2.Free();
        }
    }

    public override bool CanRead => _inputStream.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => 0;
    public override long Position { get => 0; set { } }
}

/// <summary>
/// Buffer structure for Zstd streaming operations.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ZstdBuffer
{
    public nint Data;
    public nuint Size;
    public nuint Position;
}

/// <summary>
/// Native Zstd library P/Invoke wrapper.
/// </summary>
internal static class ZstdNative
{
    private const string DllName = "libzstd";
    private static bool _loaded;
    private static readonly object _loadLock = new();

    /// <summary>
    /// Ensures the native library is loaded from the correct architecture subfolder.
    /// </summary>
    internal static void EnsureLoaded()
    {
        if (_loaded) return;
        
        lock (_loadLock)
        {
            if (_loaded) return;
            
            var archFolder = Environment.Is64BitProcess ? "x64" : "x86";
            var dllName = "libzstd.dll";
            
            // Try multiple potential locations for the native DLL
            string[] searchPaths = GetSearchPaths(archFolder, dllName);
            
            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        NativeLibrary.Load(path);
                        _loaded = true;
                        return;
                    }
                    catch
                    {
                        // Try next path
                    }
                }
            }
            
            // If no explicit path worked, register a resolver and let the runtime find it
            NativeLibrary.SetDllImportResolver(typeof(ZstdNative).Assembly, DllImportResolver);
            _loaded = true;
        }
    }

    private static string[] GetSearchPaths(string archFolder, string dllName)
    {
        var paths = new System.Collections.Generic.List<string>();
        
        // 1. Assembly location (traditional deployment)
        var assemblyLocation = typeof(ZstdNative).Assembly.Location;
        if (!string.IsNullOrEmpty(assemblyLocation))
        {
            var assemblyDir = Path.GetDirectoryName(assemblyLocation);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                paths.Add(Path.Combine(assemblyDir, archFolder, dllName));
                paths.Add(Path.Combine(assemblyDir, dllName));
            }
        }
        
        // 2. AppContext.BaseDirectory (works for single-file and plugins)
        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            paths.Add(Path.Combine(baseDir, archFolder, dllName));
            paths.Add(Path.Combine(baseDir, dllName));
        }
        
        // 3. Current directory
        var currentDir = Environment.CurrentDirectory;
        paths.Add(Path.Combine(currentDir, archFolder, dllName));
        paths.Add(Path.Combine(currentDir, dllName));
        
        // 4. Entry assembly location (useful for plugins)
        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly != null)
        {
            var entryLocation = entryAssembly.Location;
            if (!string.IsNullOrEmpty(entryLocation))
            {
                var entryDir = Path.GetDirectoryName(entryLocation);
                if (!string.IsNullOrEmpty(entryDir))
                {
                    paths.Add(Path.Combine(entryDir, archFolder, dllName));
                    paths.Add(Path.Combine(entryDir, dllName));
                }
            }
        }
        
        return paths.ToArray();
    }
    
    private static nint DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName == DllName)
        {
            var archFolder = Environment.Is64BitProcess ? "x64" : "x86";
            var dllName = "libzstd.dll";
            
            foreach (var path in GetSearchPaths(archFolder, dllName))
            {
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
                {
                    return handle;
                }
            }
            
            // Last resort: let the OS find it in PATH
            if (NativeLibrary.TryLoad(dllName, assembly, searchPath, out var defaultHandle))
            {
                return defaultHandle;
            }
        }
        
        return nint.Zero;
    }

    internal static void CheckError(nuint result)
    {
        if (IsError(result) != 0)
            throw new ZstdException($"Zstd error: {result}");
    }

    // ============================================================
    // Decompression Stream API
    // ============================================================

    [DllImport(DllName, EntryPoint = "ZSTD_createDStream", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint CreateDStream();

    [DllImport(DllName, EntryPoint = "ZSTD_freeDStream", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint FreeDStream(nint zds);

    [DllImport(DllName, EntryPoint = "ZSTD_initDStream", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint InitDStream(nint zds);

    [DllImport(DllName, EntryPoint = "ZSTD_decompressStream", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint DecompressStream(nint zds, ref ZstdBuffer output, ref ZstdBuffer input);

    [DllImport(DllName, EntryPoint = "ZSTD_DStreamInSize", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint DStreamInSize();

    [DllImport(DllName, EntryPoint = "ZSTD_DStreamOutSize", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint DStreamOutSize();

    // ============================================================
    // Error Handling
    // ============================================================

    [DllImport(DllName, EntryPoint = "ZSTD_isError", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int IsError(nuint code);

    // ============================================================
    // Compression Stream API (for future use)
    // ============================================================

    [DllImport(DllName, EntryPoint = "ZSTD_createCStream", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint CreateCStream();

    [DllImport(DllName, EntryPoint = "ZSTD_freeCStream", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint FreeCStream(nint zcs);

    [DllImport(DllName, EntryPoint = "ZSTD_initCStream", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint InitCStream(nint zcs, int compressionLevel);

    [DllImport(DllName, EntryPoint = "ZSTD_compressStream", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint CompressStream(nint zcs, ref ZstdBuffer output, ref ZstdBuffer input);

    [DllImport(DllName, EntryPoint = "ZSTD_flushStream", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint FlushStream(nint zcs, ref ZstdBuffer output);

    [DllImport(DllName, EntryPoint = "ZSTD_endStream", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint EndStream(nint zcs, ref ZstdBuffer output);

    [DllImport(DllName, EntryPoint = "ZSTD_CStreamInSize", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint CStreamInSize();

    [DllImport(DllName, EntryPoint = "ZSTD_CStreamOutSize", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint CStreamOutSize();

    // ============================================================
    // Version Info
    // ============================================================

    [DllImport(DllName, EntryPoint = "ZSTD_versionNumber", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetVersionNumber();

    [DllImport(DllName, EntryPoint = "ZSTD_maxCLevel", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetMaxCompressionLevel();
}
