using System;
using NAudio.Wave;

namespace starcitizen.Audio;

/// <summary>
/// Sample provider that reads from a pre-loaded CachedSound.
/// Enables low-latency playback of cached audio data.
/// </summary>
internal sealed class CachedSoundSampleProvider(CachedSound cachedSound) : ISampleProvider
{
    private readonly CachedSound cachedSound = cachedSound ?? throw new ArgumentNullException(nameof(cachedSound));
    private long position;

    /// <summary>
    /// Gets the wave format of the audio.
    /// </summary>
    public WaveFormat WaveFormat => cachedSound.WaveFormat;

    /// <summary>
    /// Reads samples from the cached audio data.
    /// </summary>
    public int Read(float[] buffer, int offset, int count)
    {
        var availableSamples = cachedSound.AudioData.Length - position;
        var samplesToCopy = Math.Min(availableSamples, count);
        
        if (samplesToCopy > 0)
        {
            Array.Copy(cachedSound.AudioData, position, buffer, offset, samplesToCopy);
            position += samplesToCopy;
        }
        
        return (int)samplesToCopy;
    }
}
