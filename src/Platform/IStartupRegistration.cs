namespace AudioSwitch.Platform
{
    /// <summary>Adds or removes the application from the current user's "Run" registry key.</summary>
    public interface IStartupRegistration
    {
        bool IsEnabled { get; }

        void SetEnabled(bool enabled);
    }
}
