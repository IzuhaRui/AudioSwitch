using System;
using System.Collections.Generic;
using AudioSwitch.Audio;

namespace AudioSwitch.Host
{
    /// <summary>Demo device list used by the developer preview/screenshot modes.</summary>
    internal sealed class DemoAudioDeviceService : IAudioDeviceService
    {
        private readonly List<AudioEndpoint> _endpoints;

        public DemoAudioDeviceService()
        {
            _endpoints = CreateEndpoints();
        }

        public event EventHandler DevicesChanged;

        public IList<AudioEndpoint> GetEndpoints()
        {
            return new List<AudioEndpoint>(_endpoints);
        }

        public IList<string> GetKnownDeviceIds()
        {
            List<string> ids = new List<string>();
            foreach (AudioEndpoint endpoint in GetEndpoints())
            {
                if (!string.IsNullOrEmpty(endpoint.Id))
                {
                    ids.Add(endpoint.Id);
                }
            }

            return ids;
        }

        public string GetDefaultEndpointId(AudioDataFlow flow)
        {
            foreach (AudioEndpoint endpoint in _endpoints)
            {
                if (endpoint.Flow == flow && endpoint.IsDefault)
                {
                    return endpoint.Id;
                }
            }

            return null;
        }

        public void SetDefaultDevice(string deviceId)
        {
            foreach (AudioEndpoint endpoint in _endpoints)
            {
                endpoint.IsDefaultConsole = string.Equals(endpoint.Id, deviceId, StringComparison.Ordinal);
                endpoint.IsDefaultMultimedia = endpoint.IsDefaultConsole;
            }

            EventHandler handler = DevicesChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
        }

        private static List<AudioEndpoint> CreateEndpoints()
        {
            List<AudioEndpoint> endpoints = new List<AudioEndpoint>();

            endpoints.Add(new AudioEndpoint
            {
                Id = "demo-playback-1",
                Name = "扬声器 (Realtek High Definition Audio)",
                Flow = AudioDataFlow.Playback,
                FormFactor = EndpointFormFactor.Speakers,
                IsDefaultConsole = true,
                IsDefaultMultimedia = true
            });
            endpoints.Add(new AudioEndpoint
            {
                Id = "demo-playback-2",
                Name = "耳机 (QCY MeloBuds N65 Stereo)",
                Flow = AudioDataFlow.Playback,
                FormFactor = EndpointFormFactor.Headphones
            });
            endpoints.Add(new AudioEndpoint
            {
                Id = "demo-playback-3",
                Name = "24B1W1 (NVIDIA High Definition Audio)",
                Flow = AudioDataFlow.Playback,
                FormFactor = EndpointFormFactor.DigitalAudioDisplayDevice
            });
            endpoints.Add(new AudioEndpoint
            {
                Id = "demo-playback-4",
                Name = "数字音频 (HDMI) (High Definition Audio Device)",
                Flow = AudioDataFlow.Playback,
                FormFactor = EndpointFormFactor.SPDIF
            });
            endpoints.Add(new AudioEndpoint
            {
                Id = "demo-recording-1",
                Name = "麦克风阵列 (Realtek High Definition Audio)",
                Flow = AudioDataFlow.Recording,
                FormFactor = EndpointFormFactor.Microphone,
                IsDefaultConsole = true,
                IsDefaultMultimedia = true
            });
            endpoints.Add(new AudioEndpoint
            {
                Id = "demo-recording-2",
                Name = "麦克风 (USB Audio Device)",
                Flow = AudioDataFlow.Recording,
                FormFactor = EndpointFormFactor.Microphone
            });
            endpoints.Add(new AudioEndpoint
            {
                Id = "demo-recording-3",
                Name = "立体声混音 (Realtek High Definition Audio)",
                Flow = AudioDataFlow.Recording,
                FormFactor = EndpointFormFactor.LineLevel
            });

            return endpoints;
        }
    }
}
