using System;
using NAudio.Wave;

namespace starcitizen
{
    /// <summary>
    /// Sample provider wrapper that automatically disposes the underlying reader
    /// when playback completes. Useful for streaming audio files.
    /// </summary>
    internal sealed class AutoDisposeFileReader : ISampleProvider
    {
        #region Fields

        private readonly ISampleProvider reader;
        private bool isDisposed;

        #endregion

        #region Constructor

        /// <summary>
        /// Wraps a sample provider for automatic disposal on completion.
        /// </summary>
        /// <param name="reader">The underlying sample provider to wrap</param>
        public AutoDisposeFileReader(ISampleProvider reader)
        {
            this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
            WaveFormat = reader.WaveFormat;
        }

        #endregion

        #region ISampleProvider Implementation

        /// <summary>
        /// Gets the wave format of the audio.
        /// </summary>
        public WaveFormat WaveFormat { get; }

        /// <summary>
        /// Reads samples from the underlying reader.
        /// Disposes the reader when no more samples are available.
        /// </summary>
        public int Read(float[] buffer, int offset, int count)
        {
            if (isDisposed) return 0;

            int read = reader.Read(buffer, offset, count);
            
            if (read == 0)
            {
                (reader as IDisposable)?.Dispose();
                isDisposed = true;
            }
            
            return read;
        }

        #endregion
    }
}
