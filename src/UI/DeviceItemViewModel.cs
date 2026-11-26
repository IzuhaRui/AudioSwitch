using AudioSwitch.Audio;

namespace AudioSwitch.UI
{
    /// <summary>Presentation wrapper around one audio endpoint.</summary>
    public sealed class DeviceItemViewModel
    {
        public DeviceItemViewModel(AudioEndpoint endpoint)
        {
            Endpoint = endpoint;
        }

        public AudioEndpoint Endpoint { get; private set; }

        public string Id
        {
            get { return Endpoint.Id; }
        }

        public string Name
        {
            get { return Endpoint.Name; }
        }

        public bool IsDefault
        {
            get { return Endpoint.IsDefault; }
        }

        public string TypeLabel
        {
            get { return Endpoint.TypeLabel; }
        }
    }
}
