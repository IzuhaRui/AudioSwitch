using AudioSwitch.Audio;

namespace AudioSwitch.UI
{
    /// <summary>Maps audio endpoint metadata to the Segoe MDL2 Assets glyphs used by Windows 10.</summary>
    internal static class FlyoutVisuals
    {
        public const string GlyphSpeakers = "\uE7F5";
        public const string GlyphHeadphones = "\uE7F6";
        public const string GlyphHeadset = "\uE95B";
        public const string GlyphMicrophone = "\uE720";
        public const string GlyphDisplay = "\uE7F4";
        public const string GlyphCheck = "\uE73E";

        /// <summary>Generic app glyph / system-sounds glyph for the per-app volume page.</summary>
        public static string GlyphForApp(bool systemSounds)
        {
            return systemSounds ? "\uE767" : "\uE71D";
        }

        public static string GlyphFor(AudioEndpoint endpoint)
        {
            if (endpoint == null)
            {
                return GlyphSpeakers;
            }

            switch (endpoint.FormFactor)
            {
                case EndpointFormFactor.Headphones:
                    return GlyphHeadphones;
                case EndpointFormFactor.Headset:
                case EndpointFormFactor.Handset:
                    return GlyphHeadset;
                case EndpointFormFactor.Microphone:
                    return GlyphMicrophone;
                case EndpointFormFactor.SPDIF:
                case EndpointFormFactor.DigitalAudioDisplayDevice:
                case EndpointFormFactor.UnknownDigitalPassthrough:
                    return GlyphDisplay;
                default:
                    return endpoint.Flow == AudioDataFlow.Recording ? GlyphMicrophone : GlyphSpeakers;
            }
        }
    }
}
