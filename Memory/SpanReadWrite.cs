using System;
using System.Buffers;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace StoneshardMP.Memory;

public ref struct SpanReadWrite
{
    private const int MaxCharBytesSize = 128;


    private byte[]? _charBytes;

    private readonly bool expandable;
    private readonly bool _2BytesPerChar;

    private readonly bool useFastUtf8;
    /// <summary>
    /// Gets if we are using FastUTF8 optimization.
    /// </summary>
    public readonly bool IsFastUTF8 => useFastUtf8;

    private readonly Decoder decoder;
    private readonly Encoding encoding;
    /// <summary>
    /// Gets our current encoder.
    /// </summary>
    public readonly Encoding Encoding => encoding;

    private Span<byte> outBuffer;
    private ReadOnlySpan<byte> outBufferReadonly;
    /// <summary>
    /// Gets the current span buffer.
    /// </summary>
    public readonly Span<byte> Buffer => outBuffer;

    /// <summary>
    /// Gets the current span buffer if in readonly state.
    /// </summary>
    public readonly ReadOnlySpan<byte> ReadonlyBuffer => outBufferReadonly;

    /// <summary>
    /// Are we in a current state of readonly (Usually when passed readonly span into the constructor)
    /// </summary>
    public bool IsReadonly { get; private set; }

    private int length;
    /// <summary>
    /// Gets the current length, this is not the same as the capacity of the buffer.
    /// & Sets the length of the stream to a given value.  The new
    /// value must be nonnegative and less than the space remaining in
    /// the array, int.MaxValue
    /// The upper bounds prevents any situations where a stream may be created on top of an array then
    /// the buffer is made longer than the maximum possible length of the array (int.MaxValue).
    /// </summary>
    public int Length
    {
        get => length;
        set
        {
            if (IsReadonly) throw new ReadOnlyException();

            if (value < 0 || value > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value), "Out of range, SpanReadWrite length");

            bool allocatedNewArray = EnsureCapacity(value);
            if (!allocatedNewArray && value > length)
            {
                outBuffer[length..value].Clear();
            }

            length = value;
            if (position > length)
                position = length;
        }
    }

    private int position;
    /// <summary>
    /// Gets the current position within the buffer we are next writing to.
    /// </summary>
    public int Position
    {
        get => position;
        set
        {
            position = value;
        }
    }

    private int capacity;
    /// <summary>
    /// Gets & Sets the capacity (number of bytes allocated) for the span.
    /// The capacity cannot be set to a value less than the current length
    /// of the stream.
    public int Capacity
    {
        get => capacity;
        set
        {
            if (IsReadonly) throw new ReadOnlyException();

            if (!expandable)
            {
                throw new InvalidOperationException("We are not in an expandable state");
            }

            if (value != capacity)
            {
                if (value > 0)
                {
                    Span<byte> newBuffer = new byte[value];
                    if (length > 0)
                    {
                        outBuffer.CopyTo(newBuffer);
                    }
                    outBuffer = newBuffer;
                }
                else
                {
                    outBuffer = [];
                }
                capacity = value;
            }
        }
    }

    public SpanReadWrite()
    {
        encoding = Encoding.UTF8;
        decoder = encoding.GetDecoder();
        useFastUtf8 = true;
        outBuffer = [];
        length = 0;
        expandable = true;
        _2BytesPerChar = encoding is UnicodeEncoding;
    }

    public SpanReadWrite(Span<byte> buffer)
        : this(buffer, Encoding.UTF8) { }

    public SpanReadWrite(ReadOnlySpan<byte> buffer)
    {
        encoding = Encoding.UTF8;
        decoder = encoding.GetDecoder();
        useFastUtf8 = encoding.CodePage == Encoding.UTF8.CodePage && encoding.EncoderFallback.MaxCharCount <= 1;
        outBufferReadonly = buffer;
        IsReadonly = true;
        length = capacity = buffer.Length;
        expandable = false;
        _2BytesPerChar = encoding is UnicodeEncoding;
    }

    public SpanReadWrite(Span<byte> buffer, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        this.encoding = encoding;
        decoder = encoding.GetDecoder();
        useFastUtf8 = encoding.CodePage == Encoding.UTF8.CodePage && encoding.EncoderFallback.MaxCharCount <= 1;
        outBuffer = buffer;
        length = capacity = buffer.Length;
        expandable = false;
        _2BytesPerChar = encoding is UnicodeEncoding;
    }

    // returns a bool saying whether we allocated a new array.
    private bool EnsureCapacity(int value)
    {
        if (value > capacity)
        {
            int newCapacity = Math.Max(value, 256);

            // We are ok with this overflowing since the next statement will deal
            // with the cases where _capacity*2 overflows.
            if (newCapacity < capacity * 2)
            {
                newCapacity = capacity * 2;
            }

            // We want to expand the array up to Array.MaxLength.
            // And we want to give the user the value that they asked for
            if ((uint)(capacity * 2) > Array.MaxLength)
            {
                newCapacity = Math.Max(value, Array.MaxLength);
            }

            Capacity = newCapacity;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Writes an unmanaged type to the buffer.
    /// </summary>
    public void Write<T>(T value) where T : unmanaged
    {
        if (IsReadonly) throw new ReadOnlyException();

        var lengthOfValue = Unsafe.SizeOf<T>();
        // Check for overflow
        int i = position + lengthOfValue;
        if (i < 0)
        {
            throw new IOException("Overflow when attempting to write to buffer");
        }

        if (i > length)
        {
            bool mustZero = position > length;
            if (i > capacity)
            {
                bool allocatedNewArray = EnsureCapacity(i);
                if (allocatedNewArray)
                {
                    mustZero = false;
                }
            }
            if (mustZero)
            {
                outBuffer[length..i].Clear();
            }
            length = i;
        }

        MemoryMarshal.Write(outBuffer[position..], in value);
        position += lengthOfValue;
    }

    /// <summary>
    /// Writes a span to our current buffer and ensures capacity
    /// </summary>
    public void Write(ReadOnlySpan<byte> buffer)
    {
        if (IsReadonly) throw new ReadOnlyException();

        // Check for overflow
        int i = position + buffer.Length;
        if (i < 0 || buffer.Length > int.MaxValue)
        {
            throw new IOException("Overflow when attempting to write to buffer");
        }

        if (i > length)
        {
            bool mustZero = position > length;
            if (i > capacity)
            {
                bool allocatedNewArray = EnsureCapacity(i);
                if (allocatedNewArray)
                {
                    mustZero = false;
                }
            }
            if (mustZero)
            {
                outBuffer[length..i].Clear();
            }
            length = i;
        }

        buffer.CopyTo(outBuffer[position..]);
        position += buffer.Length;
    }

    /// <summary>
    /// Writes a byte to our current buffer and ensures capacity
    /// </summary>
    /// <param name="value"></param>
    public void WriteByte(byte value)
    {
        if (IsReadonly) throw new ReadOnlyException();

        if (position >= length)
        {
            int newLength = position + 1;
            bool mustZero = position > length;
            if (newLength >= capacity)
            {
                bool allocatedNewArray = EnsureCapacity(newLength);
                if (allocatedNewArray)
                {
                    mustZero = false;
                }
            }
            if (mustZero)
            {
                outBuffer[length..position].Clear();
            }
            length = newLength;
        }

        outBuffer[position++] = value;
    }

    /// <summary>
    /// Read a byte from a current position
    /// </summary>
    public byte ReadByte()
    {
        return IsReadonly ? outBufferReadonly[position++] : outBuffer[position++];
    }

    /// <summary>
    /// Read a span from a current position
    /// </summary>
    public int Read(scoped Span<byte> buffer)
    {
        int n = Math.Min(length - position, buffer.Length);

        if (n <= 0)
            return 0;

        if (IsReadonly)
        {
            outBufferReadonly[position..(position + n)].CopyTo(buffer);
        }
        else
        {
            outBuffer[position..(position + n)].CopyTo(buffer);
        }

        position += n;
        return n;
    }

    /// <summary>
    /// Read an unmanaged value
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Read<T>() where T : unmanaged
    {
        var newSpan = IsReadonly ? outBufferReadonly[position..] : outBuffer[position..];

        var result = MemoryMarshal.Read<T>(newSpan);
        position += Unsafe.SizeOf<T>();

        return result;
    }

    public int Read()
    {
        int charsRead = 0;
        int numBytes;
        int posSav = position;

        Span<byte> _charBytes = stackalloc byte[2];
        Span<char> _singleChar = stackalloc char[1];

        char singleChar = '\0';

        while (charsRead == 0)
        {
            // We really want to know what the minimum number of bytes per char
            // is for our encoding.  Otherwise for UnicodeEncoding we'd have to
            // do ~1+log(n) reads to read n characters.
            // Assume 1 byte can be 1 char unless _2BytesPerChar is true.
            numBytes = _2BytesPerChar ? 2 : 1;

            _charBytes[0] = ReadByte();
            if (position >= length)
            {
                return -1;
            }
            if (numBytes == 2)
            {
                _charBytes[1] = ReadByte();
                if (position >= length)
                {
                    numBytes = 1;
                }
            }

            Debug.Assert(numBytes == 1 || numBytes == 2, "SpanReadWrite::ReadOneChar assumes it's reading one or 2 bytes only.");

            try
            {
                charsRead = decoder.GetChars(_charBytes, _singleChar, false);
            }
            catch
            {
                // Handle surrogate char
                Position = posSav - position;
                throw;
            }

            Debug.Assert(charsRead < 2, "SpanReadWrite::ReadOneChar - assuming we only got 0 or 1 char, not 2!");
        }
        Debug.Assert(charsRead > 0);
        return singleChar;
    }

    /// <summary>
    /// Copies the buffer from the origin to the current position to a new array
    /// </summary>
    /// <returns></returns>
    public Span<byte> ToArray()
    {
        if (IsReadonly) throw new ReadOnlyException();

        return outBuffer[..position].ToArray();
    }

    /// <summary>
    /// Returns the current buffer from which the SpanReadWriter was created
    /// </summary>
    /// <returns></returns>
    public Span<byte> GetBufferToPosition()
    {
        if (IsReadonly) throw new ReadOnlyException();

        return outBuffer[..position];
    }

    /// <summary>
    /// Returns the current buffer from which the SpanReadWriter was created
    /// </summary>
    /// <returns></returns>
    public Span<byte> GetBuffer()
    {
        if (IsReadonly) throw new ReadOnlyException();

        return outBuffer;
    }


    /// <summary>
    /// Returns current position to end of readonly buffer
    /// </summary>
    /// <returns></returns>
    public ReadOnlySpan<byte> ReadToEnd()
    {
        if (!IsReadonly) throw new ReadOnlyException();

        return outBufferReadonly[position..^0];
    }

    public static implicit operator SpanReadWrite(Span<byte> span) => new SpanReadWrite(span);
    public static implicit operator SpanReadWrite(ReadOnlySpan<byte> span) => new SpanReadWrite(span);
}

public static class SpanReadWriteWriterHelpers
{
    /// <summary>
    /// Writes a character to the buffer.
    /// Note this method cannot handle surrogates properly in UTF-8.
    /// </summary>
    public static void Write(this ref SpanReadWrite spanReadWrite, char ch)
    {
        if (!Rune.TryCreate(ch, out Rune rune)) // optimistically assume UTF-8 code path (which uses Rune) will be hit
        {
            throw new ArgumentException("Surrogates not allowed as single char");
        }

        Span<byte> buffer = new byte[8]; // reasonable guess for worst-case expansion for any arbitrary encoding

        if (spanReadWrite.IsFastUTF8)
        {
            int utf8ByteCount = rune.EncodeToUtf8(buffer);
            spanReadWrite.Write(buffer[..utf8ByteCount]);
        }
        else
        {
            byte[]? rented = null;
            int maxByteCount = spanReadWrite.Encoding.GetMaxByteCount(1);

            if (maxByteCount > buffer.Length)
            {
                rented = ArrayPool<byte>.Shared.Rent(maxByteCount);
                buffer = rented;
            }

            int actualByteCount = spanReadWrite.Encoding.GetBytes(new ReadOnlySpan<char>(in ch), buffer);
            spanReadWrite.Write(buffer[..actualByteCount]);

            if (rented != null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Writes a length-prefixed string to this stream in the BinaryWriter's
    /// current Encoding. This method first writes the length of the string as
    /// an encoded unsigned integer with variable length, and then writes that many characters
    /// to the stream.
    /// </summary>
    public static void Write(this ref SpanReadWrite spanReadWrite, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // Common: UTF-8, small string, avoid 2-pass calculation
        // Less common: UTF-8, large string, avoid 2-pass calculation
        // Uncommon: not UTF-8

        if (spanReadWrite.IsFastUTF8)
        {
            Span<byte> buffer = new byte[value.Length * 3]; // max expansion: each char -> 3 bytes
            int actualByteCount = spanReadWrite.Encoding.GetBytes(value, buffer);
            spanReadWrite.Write7BitEncodedInt(actualByteCount);
            spanReadWrite.Write(buffer[..actualByteCount]);

            return;
        }

        throw new NotSupportedException("Only UTF8 encoding is supported at the moment for strings");
    }

    public static void Write7BitEncodedInt(this ref SpanReadWrite spanReadWrite, int value)
    {
        uint uValue = (uint)value;

        // Write out an int 7 bits at a time. The high bit of the byte,
        // when on, tells reader to continue reading more bytes.
        //
        // Using the constants 0x7F and ~0x7F below offers smaller
        // codegen than using the constant 0x80.

        while (uValue > 0x7Fu)
        {
            spanReadWrite.Write((byte)(uValue | ~0x7Fu));
            uValue >>= 7;
        }
        spanReadWrite.Write((byte)uValue);
    }

    public static void Write7BitEncodedInt64(this ref SpanReadWrite spanReadWrite, long value)
    {
        ulong uValue = (ulong)value;

        // Write out an int 7 bits at a time. The high bit of the byte,
        // when on, tells reader to continue reading more bytes.
        //
        // Using the constants 0x7F and ~0x7F below offers smaller
        // codegen than using the constant 0x80.

        while (uValue > 0x7Fu)
        {
            spanReadWrite.Write((byte)((uint)uValue | ~0x7Fu));
            uValue >>= 7;
        }

        spanReadWrite.Write((byte)uValue);
    }
}

public static class SpanReadWriteReaderHelpers
{
    public static sbyte ReadSByte(this ref SpanReadWrite spanReadWrite) => (sbyte)spanReadWrite.ReadByte();
    public static bool ReadBoolean(this ref SpanReadWrite spanReadWrite) => (spanReadWrite.ReadByte() != 0);
    public static char ReadChar(this ref SpanReadWrite spanReadWrite) => (char)spanReadWrite.Read();
    public static short ReadInt16(this ref SpanReadWrite spanReadWrite) => spanReadWrite.Read<short>();
    public static ushort ReadUInt16(this ref SpanReadWrite spanReadWrite) => spanReadWrite.Read<ushort>();
    public static int ReadInt32(this ref SpanReadWrite spanReadWrite) => spanReadWrite.Read<int>();
    public static uint ReadUInt32(this ref SpanReadWrite spanReadWrite) => spanReadWrite.Read<uint>();
    public static long ReadInt64(this ref SpanReadWrite spanReadWrite) => spanReadWrite.Read<long>();
    public static ulong ReadUInt64(this ref SpanReadWrite spanReadWrite) => spanReadWrite.Read<ulong>();
    public static float ReadSingle(this ref SpanReadWrite spanReadWrite) => spanReadWrite.Read<float>();
    public static double ReadDouble(this ref SpanReadWrite spanReadWrite) => spanReadWrite.Read<double>();
    public static decimal ReadDecimal(this ref SpanReadWrite spanReadWrite) => spanReadWrite.Read<decimal>();

    public static ReadOnlySpan<byte> ReadBytes(this ref SpanReadWrite spanReadWrite, int length)
    {
        var newSpan = spanReadWrite.IsReadonly ?
            spanReadWrite.ReadonlyBuffer[spanReadWrite.Position..(spanReadWrite.Position + length)] :
            spanReadWrite.Buffer[spanReadWrite.Position..(spanReadWrite.Position + length)];

        spanReadWrite.Position += length;

        return newSpan;
    }

    public static string ReadString(this ref SpanReadWrite spanReadWrite)
    {
        if (spanReadWrite.IsFastUTF8)
        {
            var stringLength = spanReadWrite.Read7BitEncodedInt();
            if (stringLength < 0 || stringLength > spanReadWrite.Length - spanReadWrite.Position)
                throw new EndOfStreamException("String exceeds remaining packet data");
            return spanReadWrite.Encoding.GetString(spanReadWrite.ReadBytes(stringLength));
        }

        throw new NotSupportedException("Only UTF8 encoding is supported at the moment for strings");
    }

    public static int Read7BitEncodedInt(this ref SpanReadWrite spanReadWrite)
    {
        // Unlike writing, we can't delegate to the 64-bit read on
        // 64-bit platforms. The reason for this is that we want to
        // stop consuming bytes if we encounter an integer overflow.

        uint result = 0;
        byte byteReadJustNow;

        // Read the integer 7 bits at a time. The high bit
        // of the byte when on means to continue reading more bytes.
        //
        // There are two failure cases: we've read more than 5 bytes,
        // or the fifth byte is about to cause integer overflow.
        // This means that we can read the first 4 bytes without
        // worrying about integer overflow.

        const int MaxBytesWithoutOverflow = 4;
        for (int shift = 0; shift < MaxBytesWithoutOverflow * 7; shift += 7)
        {
            // ReadByte handles end of stream cases for us.
            byteReadJustNow = spanReadWrite.ReadByte();
            result |= (byteReadJustNow & 0x7Fu) << shift;

            if (byteReadJustNow <= 0x7Fu)
            {
                return (int)result; // early exit
            }
        }

        // Read the 5th byte. Since we already read 28 bits,
        // the value of this byte must fit within 4 bits (32 - 28),
        // and it must not have the high bit set.

        byteReadJustNow = spanReadWrite.ReadByte();
        if (byteReadJustNow > 0b_1111u)
        {
            throw new FormatException("Bad 7bit int");
        }

        result |= (uint)byteReadJustNow << (MaxBytesWithoutOverflow * 7);
        return (int)result;
    }

    public static long Read7BitEncodedInt64(this ref SpanReadWrite spanReadWrite)
    {
        ulong result = 0;
        byte byteReadJustNow;

        // Read the integer 7 bits at a time. The high bit
        // of the byte when on means to continue reading more bytes.
        //
        // There are two failure cases: we've read more than 10 bytes,
        // or the tenth byte is about to cause integer overflow.
        // This means that we can read the first 9 bytes without
        // worrying about integer overflow.

        const int MaxBytesWithoutOverflow = 9;
        for (int shift = 0; shift < MaxBytesWithoutOverflow * 7; shift += 7)
        {
            // ReadByte handles end of stream cases for us.
            byteReadJustNow = spanReadWrite.ReadByte();
            result |= (byteReadJustNow & 0x7Ful) << shift;

            if (byteReadJustNow <= 0x7Fu)
            {
                return (long)result; // early exit
            }
        }

        // Read the 10th byte. Since we already read 63 bits,
        // the value of this byte must fit within 1 bit (64 - 63),
        // and it must not have the high bit set.

        byteReadJustNow = spanReadWrite.ReadByte();
        if (byteReadJustNow > 0b_1u)
        {
            throw new FormatException("Bad 7bit int");
        }

        result |= (ulong)byteReadJustNow << (MaxBytesWithoutOverflow * 7);
        return (long)result;
    }
}
