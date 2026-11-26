namespace AudioSwitch.Platform
{
    /// <summary>Opens the various Windows sound configuration surfaces.</summary>
    public interface IShellLauncher
    {
        void OpenSoundSettings();

        void OpenSoundControlPanel();
    }
}
