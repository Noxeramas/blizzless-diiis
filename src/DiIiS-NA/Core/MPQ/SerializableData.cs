using CrystalMpq;
using System.IO;
using DiIiS_NA.Core.Logging;
using Gibbed.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiIiS_NA.Core.MPQ
{
    public interface ISerializableData
    {
        /// <summary>
        /// Reads serializable type.
        /// </summary>
        /// <param name="stream">The MPQFileStream to read from.</param>
        void Read(Stream stream);
    }

    public static class MpqFileStreamExtensions
    {
        private static Logger logger = new Logger("MPQ deserialization");
        private static readonly HashSet<Type> OverreadReported = new HashSet<Type>();

        /// <summary>
        /// Whether serialized data at offset/size lies inside the file. Data of an unexpected layout yields garbage
        /// offsets; reject them here instead of seeking or allocating with them.
        /// </summary>
        private static bool InRange(Stream stream, int offset, int size, string what)
        {
            if (offset >= 0 && size >= 0 && offset + 16L + size <= stream.Length)
                return true;
            logger.Error("Serialized {0} out of range (offset {1}, size {2}, file {3} bytes); the data layout doesn't match the parser.", what, offset, size, stream.Length);
            return false;
        }

        /// <summary>
        /// Reads all available items for given type.
        /// </summary>
        /// <typeparam name="T">Item type to read.</typeparam>
        /// <param name="stream">The MPQFileStream to read from.</param>
        /// <param name="recordSize">When above 0, each item starts recordSize bytes after the previous one however
        /// much its Read consumed, so fields a newer data version added can't shift the items after it.</param>
        /// <returns>List of items.</returns>
        public static List<T> ReadSerializedData<T>(this Stream stream, int recordSize = 0) where T : ISerializableData, new()
        {
            int offset = stream.ReadValueS32(); // ofset for serialized data.
            int size = stream.ReadValueS32(); // size of serialized data.

            if (offset == 0 && size != 0)
                logger.Error("Pointer error while deserializing list of {0}. Make sure you dont read too much or too few fields!", typeof(T).Name);

            var items = new List<T>(); // read-items if any.            
            if (size <= 0 || offset == 0) return items;
            if (!InRange(stream, offset, size, "list")) return items;

            var oldPos = stream.Position;
            stream.Position = offset + 16; // offset is relative to actual sno data start, so add that 16 bytes file header to get actual position. /raist

            while (stream.Position < offset + size + 16)
            {
                var start = stream.Position;
                var t = new T();
                t.Read(stream);
                items.Add(t);
                if (recordSize > 0)
                {
                    if (stream.Position - start > recordSize)
                        lock (OverreadReported)
                            if (OverreadReported.Add(typeof(T)))
                                logger.Warn("{0} reads {1} bytes but records are {2} bytes in this data version.",
                                    typeof(T).Name, stream.Position - start, recordSize);
                    stream.Position = start + recordSize;
                }
            }

            if (stream.Position != offset + size + 16)
                logger.Error("Size mismatch while deserializing list of {0}. Make sure you dont read too much or too few fields!", typeof(T).Name);

            stream.Position = oldPos;
            return items;
        }

        /// <summary>
        /// Reads a single serialized item for given type. Warning: Use with caution.
        /// </summary>
        /// <typeparam name="T">Item type to read.</typeparam>
        /// <param name="stream">The MPQFileStream to read from.</param>
        /// <returns>The read item.</returns>
        public static T ReadSerializedItem<T>(this Stream stream) where T : ISerializableData, new()
        {
            int offset = stream.ReadValueS32(); // ofset for serialized data.
            int size = stream.ReadValueS32(); // size of serialized data.

            var t = new T();
            if (size <= 0) return t;
            if (!InRange(stream, offset, size, "item")) return t;

            var oldPos = stream.Position;
            stream.Position = offset + 16; // offset is relative to actual sno data start, so add that 16 bytes file header to get actual position. /raist
            t.Read(stream);

            if (stream.Position != offset + size + 16)
                logger.Error("Size mismatch while deserializing single item of {0}. Make sure you dont read too much or too few fields!", typeof(T).Name);

            stream.Position = oldPos;
            return t;
        }

        /// <summary>
        /// Reads all available serialized ints.
        /// </summary>
        /// <param name="stream">The MPQFileStream to read from.</param>
        /// <returns>The list of read ints.</returns>
        public static List<int> ReadSerializedInts(this Stream stream)
        {
            int offset = stream.ReadValueS32(); // ofset for serialized data.
            int size = stream.ReadValueS32(); // size of serialized data.

            var items = new List<int>(); // read-items if any.
            if (size <= 0) return items;
            if (!InRange(stream, offset, size, "list")) return items;

            var oldPos = stream.Position;
            stream.Position = offset + 16; // offset is relative to actual sno data start, so add that 16 bytes file header to get actual position. /raist

            while (stream.Position < offset + size + 16)
            {
                items.Add(stream.ReadValueS32());
            }

            stream.Position = oldPos;
            return items;
        }

        public static List<int> ReadSerializedInts(this Stream stream, int SNO)
        {
            int offset = stream.ReadValueS32(); // ofset for serialized data.
            int size = stream.ReadValueS32(); // size of serialized data.

            var items = new List<int>(); // read-items if any.
            if (size <= 0) return items;
            if (!InRange(stream, offset, size, "list")) return items;

            var oldPos = stream.Position;
            stream.Position = offset + 16; // offset is relative to actual sno data start, so add that 16 bytes file header to get actual position. /raist

            while (stream.Position < offset + size + 16)
            {
                items.Add(stream.ReadValueS32());
            }

            stream.Position = oldPos;
            return items;
        }
        /// <summary>
        /// Reads all available serialized shorts.
        /// </summary>
        /// <param name="stream">The MPQFileStream to read from.</param>
        /// <returns>The list of read shorts.</returns>
        public static List<short> ReadSerializedShorts(this Stream stream)
        {
            int offset = stream.ReadValueS32(); // ofset for serialized data.
            int size = stream.ReadValueS32(); // size of serialized data.

            var items = new List<short>(); // read-items if any.
            if (size <= 0) return items;
            if (!InRange(stream, offset, size, "list")) return items;

            var oldPos = stream.Position;
            stream.Position = offset + 16; // offset is relative to actual sno data start, so add that 16 bytes file header to get actual position. /raist

            while (stream.Position < offset + size + 16)
            {
                items.Add(stream.ReadValueS16());
            }

            stream.Position = oldPos;
            return items;
        }

        /// <summary>
        /// Reads a serialized byte array
        /// </summary>
        /// <param name="stream">The MPQFileStream to read from.</param>
        /// <returns>The serialized byte array</returns>
        public static byte[] ReadSerializedByteArray(this Stream stream)
        {
            int offset = stream.ReadValueS32(); // ofset for serialized data.
            int size = stream.ReadValueS32(); // size of serialized data.

            if (size <= 0 || !InRange(stream, offset, size, "byte array")) return Array.Empty<byte>();
            byte[] buffer = new byte[size];

            var oldPos = stream.Position;
            stream.Position = offset + 16; // offset is relative to actual sno data start, so add that 16 bytes file header to get actual position. /raist
            stream.ReadExactly(buffer, 0, size);
            stream.Position = oldPos;
            return buffer;
        }

        /// <summary>
        /// Reads a serialized string.
        /// </summary>
        /// <param name="stream">The MPQFileStream to read from.</param>
        /// <returns>Read string.</returns>
        public static string ReadSerializedString(this Stream stream)
        {
            int offset = stream.ReadValueS32(); // ofset for serialized data.
            int size = stream.ReadValueS32(); // size of serialized data.

            var @string = string.Empty;
            if (size <= 0) return @string;
            if (!InRange(stream, offset, size, "string")) return @string;

            var oldPos = stream.Position;
            stream.Position = offset + 16; // offset is relative to actual sno data start, so add that 16 bytes file header to get actual position. /raist

            @string = stream.ReadString((uint)size, true);
            stream.Position = oldPos;

            return @string;
        }
    }
}
