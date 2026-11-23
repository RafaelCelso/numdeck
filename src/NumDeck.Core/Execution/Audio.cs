using System.Collections.Concurrent;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NumDeck.Core.Actions;

namespace NumDeck.Core.Execution;

public sealed record AudioDeviceInfo(string Id, string Name)
{
    public override string ToString() => Name;
}

public static class AudioDevices
{
    public static List<AudioDeviceInfo> Outputs()
    {
        var list = new List<AudioDeviceInfo> { new("", "Saída padrão do Windows") };
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                list.Add(new AudioDeviceInfo(d.ID, d.FriendlyName));
        }
        catch { /* no audio service: default only */ }
        return list;
    }

    /// <summary>Toggles mute on the default microphone; returns the new mute state.</summary>
    public static bool ToggleMicrophoneMute()
    {
        using var en = new MMDeviceEnumerator();
        var mic = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        bool muted = !mic.AudioEndpointVolume.Mute;
        mic.AudioEndpointVolume.Mute = muted;
        return muted;
    }
}

/// <summary>Plays sound files; a second press can stop the same sound.</summary>
public sealed class SoundPlayer : IDisposable
{
    private readonly ConcurrentDictionary<PlaySoundAction, (IWavePlayer Out, AudioFileReader Reader)> _playing = new();

    public void Play(PlaySoundAction action)
    {
        if (!File.Exists(action.FilePath)) throw new FileNotFoundException("Arquivo de som não encontrado", action.FilePath);

        if (_playing.TryRemove(action, out var current))
        {
            current.Out.Stop(); // PlaybackStopped disposes it
            if (action.StopIfPlaying) return;
        }

        var reader = new AudioFileReader(action.FilePath) { Volume = (float)Math.Clamp(action.Volume, 0, 1) };
        IWavePlayer output;
        if (string.IsNullOrEmpty(action.OutputDeviceId))
        {
            output = new WasapiOut(AudioClientShareMode.Shared, 80);
        }
        else
        {
            using var en = new MMDeviceEnumerator();
            MMDevice device;
            try { device = en.GetDevice(action.OutputDeviceId); }
            catch { device = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
            output = new WasapiOut(device, AudioClientShareMode.Shared, true, 80);
        }

        output.PlaybackStopped += (_, _) =>
        {
            if (_playing.TryGetValue(action, out var p) && p.Out == output) _playing.TryRemove(action, out _);
            output.Dispose();
            reader.Dispose();
        };
        output.Init(reader);
        _playing[action] = (output, reader);
        output.Play();
    }

    public void StopAll()
    {
        foreach (var key in _playing.Keys.ToList())
            if (_playing.TryRemove(key, out var p)) p.Out.Stop();
    }

    public void Dispose() => StopAll();
}
