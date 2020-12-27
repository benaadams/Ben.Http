using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace Ben.Http.Templates
{
    [SkipLocalsInit]
    public ref struct BufferWriter<T> where T : IBufferWriter<byte>
    {
        private T _output;
        private Span<byte> _span;
        private int _buffered;

        public BufferWriter(T output, int sizeHint)
        {
            _buffered = 0;
            _output = output;
            _span = output.GetSpan(sizeHint);
        }

        public Span<byte> Span => _span;

        public T Output => _output;

        public int Buffered => _buffered;
        public int Remaining => _span.Length - _buffered;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Commit()
        {
            var buffered = _buffered;
            if (buffered > 0)
            {
                _buffered = 0;
                _output.Advance(buffered);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Advance(int count)
        {
            _buffered += count;
            _span = _span.Slice(count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(ReadOnlySpan<byte> source)
        {
            if (_span.Length >= source.Length)
            {
                source.CopyTo(_span);
                Advance(source.Length);
            }
            else
            {
                WriteMultiBuffer(source);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Ensure(int count = 1)
        {
            if (_span.Length < count)
            {
                EnsureMore(count);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void EnsureMore(int count = 0)
        {
            if (_buffered > 0)
            {
                Commit();
            }

            _span = _output.GetSpan(count);
        }

        private void WriteMultiBuffer(ReadOnlySpan<byte> source)
        {
            while (source.Length > 0)
            {
                if (_span.Length == 0)
                {
                    EnsureMore();
                }

                var writable = Math.Min(source.Length, _span.Length);
                source.Slice(0, writable).CopyTo(_span);
                source = source.Slice(writable);
                Advance(writable);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteNumeric(uint number)
        {
            const byte AsciiDigitStart = (byte)'0';

            var span = this.Span;

            // Fast path, try copying to the available memory directly
            var advanceBy = 0;
            if (span.Length >= 3)
            {
                if (number < 10)
                {
                    span[0] = (byte)(number + AsciiDigitStart);
                    advanceBy = 1;
                }
                else if (number < 100)
                {
                    var tens = (byte)((number * 205u) >> 11); // div10, valid to 1028

                    span[0] = (byte)(tens + AsciiDigitStart);
                    span[1] = (byte)(number - (tens * 10) + AsciiDigitStart);
                    advanceBy = 2;
                }
                else if (number < 1000)
                {
                    var digit0 = (byte)((number * 41u) >> 12); // div100, valid to 1098
                    var digits01 = (byte)((number * 205u) >> 11); // div10, valid to 1028

                    span[0] = (byte)(digit0 + AsciiDigitStart);
                    span[1] = (byte)(digits01 - (digit0 * 10) + AsciiDigitStart);
                    span[2] = (byte)(number - (digits01 * 10) + AsciiDigitStart);
                    advanceBy = 3;
                }
            }

            if (advanceBy > 0)
            {
                Advance(advanceBy);
            }
            else
            {
                BufferExtensions.WriteNumericMultiWrite(ref this, number);
            }
        }
    }

    [SkipLocalsInit]
    public static class BufferExtensions
    {
        private static HtmlEncoder HtmlEncoder { get; } = CreateHtmlEncoder();
        private static HtmlEncoder CreateHtmlEncoder()
        {
            var settings = new TextEncoderSettings(UnicodeRanges.BasicLatin, UnicodeRanges.Katakana, UnicodeRanges.Hiragana);
            settings.AllowCharacter('\u2014');  // allow EM DASH through
            return HtmlEncoder.Create(settings);
        }

        private const int StackAllocThresholdChars = 256;
        private const int MaxULongByteLength = 20;

        [ThreadStatic]
        private static byte[]? s_numericBytesScratch;

        public static void WriteUtf8String<T>(ref this BufferWriter<T> buffer, string text)
             where T : IBufferWriter<byte>
            => buffer.WriteUtf8String(text.AsSpan());

        public static void WriteUtf8String<T>(ref this BufferWriter<T> buffer, ReadOnlySpan<char> text)
             where T : IBufferWriter<byte>
        {
            const int MaxPerUtf8Char = 4;

            if (buffer.Remaining < text.Length * MaxPerUtf8Char)
            {
                var count = Encoding.UTF8.GetByteCount(text);
                buffer.Ensure(count);
            }

            var byteCount = Encoding.UTF8.GetBytes(text, buffer.Span);
            buffer.Advance(byteCount);
        }

        public static void WriteUtf8HtmlString<T>(ref this BufferWriter<T> buffer, string input)
             where T : IBufferWriter<byte>
        {
            const int MaxPerHtmlChar = 10;
            const int MaxPerUtf8Char = 4;

            char[]? array = null;
            Span<char> output = stackalloc char[StackAllocThresholdChars];
            // Need largest size, can't do multiple rounds of encoding due to https://github.com/dotnet/runtime/issues/45994
            if ((long)input.Length * MaxPerHtmlChar <= StackAllocThresholdChars)
            {
                var status = HtmlEncoder.Encode(input, output, out int charsConsumed, out int charsWritten, isFinalBlock: true);

                if (status != OperationStatus.Done)
                    throw new InvalidOperationException("Invalid Data");

                output = output.Slice(0, charsWritten);
            }
            else
            {
                array = ArrayPool<char>.Shared.Rent(input.Length * MaxPerHtmlChar);
                output = array;

                var status = HtmlEncoder.Encode(input, output, out _, out int charsWritten, isFinalBlock: true);

                if (status != OperationStatus.Done)
                    throw new InvalidOperationException("Invalid Data");

                output = output.Slice(0, charsWritten);
            }

            if (buffer.Remaining < output.Length * MaxPerUtf8Char)
            {
                var count = Encoding.UTF8.GetByteCount(output);
                buffer.Ensure(count);
            }

            var byteCount = Encoding.UTF8.GetBytes(output, buffer.Span);
            buffer.Advance(byteCount);

            if (array is not null)
            {
                ArrayPool<char>.Shared.Return(array);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void WriteNumericMultiWrite<T>(ref this BufferWriter<T> buffer, uint number)
             where T : IBufferWriter<byte>
        {
            const byte AsciiDigitStart = (byte)'0';

            var value = number;
            var position = MaxULongByteLength;
            Span<byte> byteBuffer = NumericBytesScratch;
            do
            {
                // Consider using Math.DivRem() if available
                var quotient = value / 10;
                byteBuffer[--position] = (byte)(AsciiDigitStart + (value - quotient * 10)); // 0x30 = '0'
                value = quotient;
            }
            while (value != 0);

            var length = MaxULongByteLength - position;
            buffer.Write(byteBuffer.Slice(position, length));
        }

        private static byte[] NumericBytesScratch => s_numericBytesScratch ?? CreateNumericBytesScratch();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static byte[] CreateNumericBytesScratch()
        {
            var bytes = new byte[MaxULongByteLength];
            s_numericBytesScratch = bytes;
            return bytes;
        }
    }
}
