using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace starcitizen
{
    /// <summary>
    /// Pre-loaded audio buffer for low-latency sound playback.
    /// Reads the entire audio file into memory on construction.
    /// </summary>
    /// <remarks>
    /// Use this for short sounds that need to play with minimal latency,
    /// such as button click feedback. The audio data is stored as float samples.
    /// </remarks>
    public sealed class CachedSound
    {
        #region Properties

        /// <summary>
        /// The audio samples as float array.
        /// </summary>
        public float[] AudioData { get; }

        /// <summary>
        /// The wave format of the audio data.
        /// </summary>
        public WaveFormat WaveFormat { get; }

        #endregion

        #region Constructor

        /// <summary>
        /// Loads an audio file into memory.
        /// </summary>
        /// <param name="audioFileName">Path to the audio file</param>
        /// <exception cref="ArgumentNullException">If audioFileName is null or empty</exception>
        /// <exception cref="System.IO.FileNotFoundException">If file doesn't exist</exception>
        public CachedSound(string audioFileName)
        {
            if (string.IsNullOrWhiteSpace(audioFileName))
            {
                throw new ArgumentNullException(nameof(audioFileName));
            }

            using var audioFileReader = new AudioFileReader(audioFileName);
            WaveFormat = audioFileReader.WaveFormat;
            
            // Pre-allocate list with estimated capacity
            var estimatedSamples = (int)(audioFileReader.Length / sizeof(float));
            var wholeFile = new List<float>(estimatedSamples);
            
            // Read in chunks of 1 second worth of samples
            var bufferSize = audioFileReader.WaveFormat.SampleRate * audioFileReader.WaveFormat.Channels;
            var readBuffer = new float[bufferSize];
            
            int samplesRead;
            while ((samplesRead = audioFileReader.Read(readBuffer, 0, readBuffer.Length)) > 0)
            {
                // Add only the samples that were actually read
                for (int i = 0; i < samplesRead; i++)
                {
                    wholeFile.Add(readBuffer[i]);
                }
            }
            
            AudioData = wholeFile.ToArray();
        }

        #endregion
    }
}
