using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace SCJMapper_V2.CryXMLlib
{
    internal static class Extensions
    {
        /// <summary>
        /// Get a span slice of the array (zero-allocation).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<T> SliceSpan<T>(this T[] source, int offset, int length)
            => source.AsSpan(offset, length);

        /// <summary>
        /// Get the array slice between the two indexes.
        /// Inclusive for start index, exclusive end.
        /// </summary>
        public static T[] SliceE<T>(this T[] source, uint start, uint end)
        {
            var len = (int)(end - start);
            var res = new T[len];
            Array.Copy(source, (int)start, res, 0, len);
            return res;
        }

        /// <summary>
        /// Get the array slice with offset and length.
        /// </summary>
        public static T[] SliceL<T>(this T[] source, uint offset, uint length)
        {
            var res = new T[(int)length];
            Array.Copy(source, (int)offset, res, 0, (int)length);
            return res;
        }
    }

    internal static class Conversions
    {
        /// <summary>
        /// Converts ASCII bytes to string, stopping at null terminator.
        /// Uses Span for efficiency.
        /// </summary>
        public static string ToString(byte[] byteArr, uint size = 999)
        {
            var span = byteArr.AsSpan(0, (int)Math.Min(size, byteArr.Length));
            var nullIndex = span.IndexOf((byte)0);
            var length = nullIndex >= 0 ? nullIndex : span.Length;
            return Encoding.ASCII.GetString(span[..length]);
        }

        /// <summary>
        /// Read a struct from a byte array at the specified offset.
        /// Uses GCHandle for structs with managed fields (like byte[] arrays).
        /// </summary>
        public static T ByteToType<T>(byte[] bytes, uint offset = 0)
        {
            var size = Marshal.SizeOf(typeof(T));
            var slice = new byte[size];
            Array.Copy(bytes, (int)offset, slice, 0, size);

            var handle = GCHandle.Alloc(slice, GCHandleType.Pinned);
            try
            {
                return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
