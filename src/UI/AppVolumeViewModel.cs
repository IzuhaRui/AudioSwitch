using System;
using System.Collections.Generic;
using AudioSwitch.Audio;

namespace AudioSwitch.UI
{
    /// <summary>One application row in the per-app volume page.</summary>
    public sealed class AppVolumeItemViewModel
    {
        public AppVolumeItemViewModel(AudioSessionInfo session)
        {
            Session = session;
        }

        public AudioSessionInfo Session { get; private set; }

        public string Key
        {
            get { return Session.Key; }
        }

        public string Name
        {
            get { return Session.DisplayName; }
        }

        public bool IsSystemSounds
        {
            get { return Session.IsSystemSounds; }
        }

        public int Percent
        {
            get { return Session.Percent; }
        }

        public bool Muted
        {
            get { return Session.Muted; }
        }

        public string ExecutablePath
        {
            get { return Session.ExecutablePath; }
        }
    }

    /// <summary>View model of the per-application volume page; independent from the device page.</summary>
    public sealed class AppVolumeViewModel
    {
        private readonly IAudioSessionService _sessions;

        public AppVolumeViewModel(IAudioSessionService sessions)
        {
            if (sessions == null)
            {
                throw new ArgumentNullException("sessions");
            }

            _sessions = sessions;
            Items = new List<AppVolumeItemViewModel>();
        }

        public IList<AppVolumeItemViewModel> Items { get; private set; }

        public void Refresh()
        {
            IList<AudioSessionInfo> sessions = _sessions.GetSessions();
            List<AppVolumeItemViewModel> items = new List<AppVolumeItemViewModel>();
            if (sessions != null)
            {
                foreach (AudioSessionInfo session in sessions)
                {
                    if (session != null)
                    {
                        items.Add(new AppVolumeItemViewModel(session));
                    }
                }
            }

            Items = items;
        }

        public bool TrySetVolume(AppVolumeItemViewModel item, float volume)
        {
            return item != null && _sessions.TrySetVolume(item.Key, volume);
        }

        public bool TrySetMute(AppVolumeItemViewModel item, bool muted)
        {
            return item != null && _sessions.TrySetMute(item.Key, muted);
        }

        public bool TryGetPeakValue(AppVolumeItemViewModel item, out float peak)
        {
            peak = 0f;
            return item != null && _sessions.TryGetPeakValue(item.Key, out peak);
        }
    }
}
