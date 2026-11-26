namespace AudioSwitch.Platform
{
    /// <summary>Small persistent settings store (per user).</summary>
    public interface IAppSettings
    {
        bool GetFlag(string name, bool defaultValue);

        void SetFlag(string name, bool value);

        string GetString(string name, string defaultValue);

        void SetString(string name, string value);
    }
}
