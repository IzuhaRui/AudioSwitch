namespace AudioSwitch.Audio
{
    /// <summary>Audio endpoint data flow.</summary>
    public enum AudioDataFlow
    {
        /// <summary>Speakers, headphones, HDMI, ...</summary>
        Playback = 0,

        /// <summary>Microphones, line in, ...</summary>
        Recording = 1
    }
}
