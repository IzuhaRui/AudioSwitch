using System.Collections.Generic;

namespace AudioSwitch.UI
{
    /// <summary>One group of devices in the flyout (playback or recording).</summary>
    public sealed class DeviceSectionViewModel
    {
        public DeviceSectionViewModel(string title, string emptyText, IList<DeviceItemViewModel> items)
        {
            Title = title;
            EmptyText = emptyText;
            Items = items;
        }

        public string Title { get; private set; }

        public string EmptyText { get; private set; }

        public IList<DeviceItemViewModel> Items { get; private set; }
    }
}
