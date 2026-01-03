using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using starcitizen.Core;

namespace starcitizen
{
    /// <summary>
    /// Singleton audio playback engine using NAudio.
    /// Provides low-latency sound mixing for button click feedback.
    /// </summary>
    /// <remarks>
    /// Uses WaveOutEvent for playback with a MixingSampleProvider
    /// to allow multiple sounds to play simultaneously.
    /// </remarks>
    internal sealed class AudioPlaybackEngine : IDisposable
    {
        #region Singleton

        private static readonly Lazy<AudioPlaybackEngine> LazyInstance = 
            new(() => new AudioPlaybackEngine(44100, 2));

        /// <summary>
        /// Gets the shared AudioPlaybackEngine instance.
        /// </summary>
        public static AudioPlaybackEngine Instance => LazyInstance.Value;

        #endregion

        #region Fields

        private readonly IWavePlayer outputDevice;
        private readonly MixingSampleProvider mixer;
        private volatile bool disposed;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new audio playback engine.
        /// </summary>
        /// <param name="sampleRate">Sample rate in Hz (default: 44100)</param>
        /// <param name="channelCount">Number of channels (default: 2 for stereo)</param>
        public AudioPlaybackEngine(int sampleRate = 44100, int channelCount = 2)
        {
            try
            {
                outputDevice = new WaveOutEvent();
                mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channelCount))
                {
                    ReadFully = true
                };
                outputDevice.Init(mixer);
                outputDevice.Play();
            }
            catch (Exception ex)
            {
                PluginLog.Error($"AudioPlaybackEngine initialization failed: {ex.Message}");
                disposed = true; // Mark as disposed to prevent usage
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Plays a sound file by path.
        /// </summary>
        /// <param name="fileName">Path to the audio file</param>
        public void PlaySound(string fileName)
        {
            if (disposed || string.IsNullOrEmpty(fileName)) return;
            
            try
            {
                var input = new AudioFileReader(fileName);
                AddMixerInput(new AutoDisposeFileReader(input));
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to play sound file '{fileName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Plays a pre-loaded cached sound.
        /// </summary>
        /// <param name="sound">The CachedSound to play</param>
        public void PlaySound(CachedSound sound)
        {
            if (disposed || sound == null) return;
            
            try
            {
                AddMixerInput(new CachedSoundSampleProvider(sound));
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to play cached sound: {ex.Message}");
            }
        }

        #endregion

        #region Private Methods

        private void AddMixerInput(ISampleProvider input)
        {
            if (disposed || mixer == null) return;
            
            try
            {
                mixer.AddMixerInput(ConvertToRightChannelCount(input));
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to add mixer input: {ex.Message}");
            }
        }

        private ISampleProvider ConvertToRightChannelCount(ISampleProvider input)
        {
            if (input.WaveFormat.Channels == mixer.WaveFormat.Channels)
            {
                return input;
            }
            
            if (input.WaveFormat.Channels == 1 && mixer.WaveFormat.Channels == 2)
            {
                return new MonoToStereoSampleProvider(input);
            }
            
            throw new NotSupportedException($"Channel count conversion from {input.WaveFormat.Channels} to {mixer.WaveFormat.Channels} not implemented");
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            try
            {
                // Stop playback first
                outputDevice?.Stop();
                
                // Remove all mixer inputs to release any file handles
                mixer?.RemoveAllMixerInputs();
                
                // Dispose output device
                outputDevice?.Dispose();
            }
            catch (Exception ex)
            {
                try { PluginLog.Error($"AudioPlaybackEngine.Dispose error: {ex.Message}"); } catch { }
            }
        }

        #endregion
    }
}
