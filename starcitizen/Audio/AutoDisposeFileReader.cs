using System;
using NAudio.Wave;

namespace starcitizen.Audio;

/// <summary>
/// Sample provider wrapper that automatically disposes the underlying reader
/// when playback completes. Useful for streaming audio files.
/// </summary>
internal sealed class AutoDisposeFileReader(ISampleProvider reader) : ISampleProvider
{
    private readonly ISampleProvider reader = reader ?? throw new ArgumentNullException(nameof(reader));
    private bool isDisposed;

    /// <summary>
    /// Gets the wave format of the audio.
    /// </summary>
    public WaveFormat WaveFormat { get; } = reader.WaveFormat;

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
}
