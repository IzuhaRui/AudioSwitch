namespace AudioSwitch.Audio
{
    /// <summary>Immutable snapshot of one active audio endpoint.</summary>
    public sealed class AudioEndpoint
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public AudioDataFlow Flow { get; set; }

        public EndpointFormFactor FormFactor { get; set; }

        public bool IsDefaultConsole { get; set; }

        public bool IsDefaultMultimedia { get; set; }

        public bool IsDefaultCommunications { get; set; }

        /// <summary>True when this endpoint is the current system default for its data flow.</summary>
        public bool IsDefault
        {
            get { return IsDefaultConsole; }
        }

        public string TypeLabel
        {
            get { return EndpointFormFactorNames.GetDisplayName(FormFactor, Flow); }
        }

        public override string ToString()
        {
            return Name ?? string.Empty;
        }
    }
}
