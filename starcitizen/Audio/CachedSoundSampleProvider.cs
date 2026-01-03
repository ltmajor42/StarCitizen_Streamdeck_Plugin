using System;
using NAudio.Wave;

namespace starcitizen
{
    /// <summary>
    /// Sample provider that reads from a pre-loaded CachedSound.
    /// Enables low-latency playback of cached audio data.
    /// </summary>
    internal sealed class CachedSoundSampleProvider : ISampleProvider
    {
        #region Fields

        private readonly CachedSound cachedSound;
        private long position;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a sample provider for the specified cached sound.
        /// </summary>
        /// <param name="cachedSound">The pre-loaded sound to play</param>
        public CachedSoundSampleProvider(CachedSound cachedSound)
        {
            this.cachedSound = cachedSound ?? throw new ArgumentNullException(nameof(cachedSound));
        }

        #endregion

        #region ISampleProvider Implementation

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

        #endregion
    }
}
