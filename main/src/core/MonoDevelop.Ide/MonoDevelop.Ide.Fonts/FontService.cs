// 
// FontService.cs
//  
// Author:
//       Mike Krüger <mkrueger@novell.com>
// 
// Copyright (c) 2010 Novell, Inc (http://www.novell.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using MonoDevelop.Core;
using Pango;

namespace MonoDevelop.Ide.Fonts
{
    public static class FontService
    {
        private static readonly Dictionary<string, FontDescription> LoadedFonts = new Dictionary<string, FontDescription>();
        private static Properties fontProperties;

        private static void LoadDefaults()
        {
            MonospaceFont?.Dispose();

#pragma warning disable 618
            MonospaceFontName = DesktopService.DefaultMonospaceFont;
            MonospaceFont = FontDescription.FromString(MonospaceFontName);
#pragma warning restore 618
        }

        internal static void Initialize()
        {
            if (fontProperties != null)
                throw new InvalidOperationException("Already initialized");

            fontProperties = PropertyService.Get("FontProperties", new Properties());

            LoadDefaults();
        }

        public static FontDescription MonospaceFont { get; private set; } = new FontDescription();

        public static FontDescription SansFont => Gui.Styles.DefaultFont;

        public static string MonospaceFontName { get; private set; } = String.Empty;

        public static string SansFontName => Gui.Styles.DefaultFontName;

        [Obsolete("Use MonospaceFont")]
        public static FontDescription DefaultMonospaceFontDescription => MonospaceFont ?? (MonospaceFont = LoadFont (DesktopService.DefaultMonospaceFont));

        private static FontDescription LoadFont(string name)
        {
            var fontName = FilterFontName(name);
            return FontDescription.FromString(fontName);
        }

        public static string FilterFontName(string name)
        {
            switch (name)
            {
                case "_DEFAULT_MONOSPACE":
                    return MonospaceFontName;
                case "_DEFAULT_SANS":
                    return SansFontName;
                default:
                    return name;
            }
        }

        public static string GetUnderlyingFontName(string name)
        {
            var result = fontProperties.Get<string>(name);

            if (result == null)
            {
                var font = GetFont(name);
                if (font == null)
                    throw new InvalidOperationException("Font " + name + " not found.");
                return font;
            }
            return result;
        }

        /// <summary>
        /// Gets the font description for the provided font id
        /// </summary>
        /// <returns>
        /// The font description.
        /// </returns>
        /// <param name='name'>
        /// Identifier of the font
        /// </param>
        /// <param name='createDefaultFont'>
        /// If set to <c>false</c> and no custom font has been set, the method will return null.
        /// </param>
        public static FontDescription GetFontDescription(string name, bool createDefaultFont = true)
        {
            if (LoadedFonts.ContainsKey(name))
                return LoadedFonts[name];
            return LoadedFonts[name] = LoadFont(GetUnderlyingFontName(name));
        }

        internal static string GetFont(string name)
        {
            // TODO-AELIJ: fonts
            LoggingService.LogError($"Font {name} not found.");
            return null;
        }

        public static void SetFont(string name, string value)
        {
            if (LoadedFonts.ContainsKey(name))
                LoadedFonts.Remove(name);

            //var font = GetFont(name);
            //fontProperties.Set(name, font == value ? null : value);
            fontProperties.Set(name, value);

            List<Action> callbacks;
            if (FontChangeCallbacks.TryGetValue(name, out callbacks))
            {
                callbacks.ForEach(c => c());
            }
        }

        internal static ConfigurationProperty<FontDescription> GetFontProperty(string name)
        {
            return new FontConfigurationProperty(name);
        }

        private static readonly Dictionary<string, List<Action>> FontChangeCallbacks = new Dictionary<string, List<Action>>();

        public static void RegisterFontChangedCallback(string fontName, Action callback)
        {
            if (!FontChangeCallbacks.ContainsKey(fontName))
                FontChangeCallbacks[fontName] = new List<Action>();
            FontChangeCallbacks[fontName].Add(callback);
        }

        public static void RemoveCallback(Action callback)
        {
            foreach (var list in FontChangeCallbacks.Values.ToList())
                list.Remove(callback);
        }
    }

    internal class FontConfigurationProperty : ConfigurationProperty<FontDescription>
    {
        private readonly string name;

        public FontConfigurationProperty(string name)
        {
            this.name = name;
            FontService.RegisterFontChangedCallback(name, OnChanged);
        }

        protected override FontDescription OnGetValue()
        {
            return FontService.GetFontDescription(name);
        }

        protected override bool OnSetValue(FontDescription value)
        {
            FontService.SetFont(name, value.ToString());
            return true;
        }
    }

    public static class FontExtensions
    {
        public static FontDescription CopyModified(this FontDescription font, double? scale = null, Weight? weight = null)
        {
            font = font.Copy();

            if (scale.HasValue)
                Scale(font, scale.Value);

            if (weight.HasValue)
                font.Weight = weight.Value;

            return font;
        }

        private static void Scale(FontDescription font, double scale)
        {
            if (font.SizeIsAbsolute)
            {
                font.AbsoluteSize = scale * font.Size;
            }
            else
            {
                var size = font.Size;
                if (size == 0)
                    size = (int)(10 * Pango.Scale.PangoScale);
                font.Size = (int)(Pango.Scale.PangoScale * (int)(scale * size / Pango.Scale.PangoScale));
            }
        }
    }
}
