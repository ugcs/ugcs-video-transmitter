using System;
using System.Collections.Generic;
using System.Text;

namespace Ugcs.Video.Tools
{
    public struct Size
    {
        public static Size Empty = new Size();


        public int Width { get; private set; }
        public int Height { get; private set; }


        public Size(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Value must be greather then zero.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "Value must be greather then zero.");

            Width = width;
            Height = height;
        }
    }
}
