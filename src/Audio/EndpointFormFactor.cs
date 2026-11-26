using System;

namespace AudioSwitch.Audio
{
    /// <summary>Endpoint form factors reported by the Windows Core Audio API.</summary>
    public enum EndpointFormFactor
    {
        RemoteNetworkDevice = 0,
        Speakers = 1,
        LineLevel = 2,
        Headphones = 3,
        Microphone = 4,
        Headset = 5,
        Handset = 6,
        UnknownDigitalPassthrough = 7,
        SPDIF = 8,
        DigitalAudioDisplayDevice = 9,
        UnknownFormFactor = 10
    }

    public static class EndpointFormFactorNames
    {
        public static string GetDisplayName(EndpointFormFactor formFactor, AudioDataFlow flow)
        {
            switch (formFactor)
            {
                case EndpointFormFactor.Speakers:
                    return "扬声器";
                case EndpointFormFactor.LineLevel:
                    return "线路";
                case EndpointFormFactor.Headphones:
                    return "耳机";
                case EndpointFormFactor.Microphone:
                    return "麦克风";
                case EndpointFormFactor.Headset:
                    return "耳麦";
                case EndpointFormFactor.Handset:
                    return "手机";
                case EndpointFormFactor.SPDIF:
                    return "数字音频 (S/PDIF)";
                case EndpointFormFactor.DigitalAudioDisplayDevice:
                    return "显示器音频";
                case EndpointFormFactor.UnknownDigitalPassthrough:
                    return "数字音频";
                default:
                    return flow == AudioDataFlow.Recording ? "录音设备" : "播放设备";
            }
        }
    }
}
