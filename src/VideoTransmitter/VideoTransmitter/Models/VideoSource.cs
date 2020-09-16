using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoTransmitter.Models
{
    /// <summary>
    /// Represents a source from which video can be received.
    /// For example a video camera or a file.
    /// </summary>
    public class VideoSource : Caliburn.Micro.PropertyChangedBase
    {
        /// <summary>
        /// Name of the video source.
        /// </summary>
        public string Name { get; private set; }

        private string displayName;
        /// <summary>
        /// Display Name of the video source.
        /// </summary>
        public string DisplayName {
            get
            {
                return displayName;
            }
            set
            {
                displayName = value;
                NotifyOfPropertyChange(() => DisplayName);
            }
        }

        /// <summary>
        /// Uri to get video from the source.
        /// </summary>
        public Uri Uri { get; private set; }


        public VideoSource(string name, string displayName, Uri uri)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Uri = uri ?? throw new ArgumentNullException(nameof(uri));
        }


        public override bool Equals(object obj)
        {
            VideoSource vs = obj as VideoSource;
            if (vs == null)
                return false;

            return this.Name == vs.Name && this.Uri == vs.Uri;
        }

        public override int GetHashCode()
        {
            int hc = 700;

            if (Uri.AbsolutePath != null)
                hc += Uri.AbsolutePath.Length;

            if (Name != null)
                hc += Name.Length;

            return hc;
        }
    }
}
