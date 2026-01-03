using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using starcitizen.Core;

namespace starcitizen.Audio;

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
    private static readonly Lazy<AudioPlaybackEngine> LazyInstance = 
        new(() => new AudioPlaybackEngine(44100, 2));

    /// <summary>
    /// Gets the shared AudioPlaybackEngine instance.
    /// </summary>
    public static AudioPlaybackEngine Instance => LazyInstance.Value;

    private readonly WaveOutEvent outputDevice;
    private readonly MixingSampleProvider mixer;
    private volatile bool disposed;

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
            disposed = true;
        }
    }

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

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        try
        {
            outputDevice?.Stop();
            mixer?.RemoveAllMixerInputs();
            outputDevice?.Dispose();
        }
        catch (Exception ex)
        {
            try { PluginLog.Error($"AudioPlaybackEngine.Dispose error: {ex.Message}"); } catch { }
        }
    }
}
