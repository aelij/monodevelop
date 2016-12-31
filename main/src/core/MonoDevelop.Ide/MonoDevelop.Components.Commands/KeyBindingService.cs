// KeyBindingService.cs
//
// Author: Jeffrey Stedfast <fejj@novell.com>
//
// Copyright (c) 2007 Novell, Inc (http://www.novell.com)
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
//

using System;
using System.IO;
using System.Xml;
using System.Collections.Generic;
using Unix = Mono.Unix.Native;

using MonoDevelop.Core;

namespace MonoDevelop.Components.Commands
{
    public class KeyBindingService
    {
        private static KeyBindingSet current;
        private static readonly SortedDictionary<string, KeyBindingScheme> SchemesMap;
        private static readonly KeyBindingSet DefaultSchemeBindings;

        static KeyBindingService()
        {
            SchemesMap = new SortedDictionary<string, KeyBindingScheme>();

            // Initialize the default scheme
            DefaultSchemeBindings = new KeyBindingSet();
            var defaultScheme = new DefaultScheme(DefaultSchemeBindings);
            SchemesMap.Add(defaultScheme.Id, defaultScheme);

            // Initialize the current bindings
            current = new KeyBindingSet(DefaultSchemeBindings);
        }

        private static string ConfigFileName
        {
            get
            {
                string file = Platform.IsMac ? "Custom.mac-kb.xml" : "Custom.kb.xml";
                return UserProfile.Current.UserDataRoot.Combine("KeyBindings", file);
            }
        }

        internal static KeyBindingSet DefaultKeyBindingSet => DefaultSchemeBindings;

        public static KeyBindingSet CurrentKeyBindingSet => current;

        public static KeyBindingScheme GetScheme(string id)
        {
            KeyBindingScheme scheme;
            if (SchemesMap.TryGetValue(id, out scheme))
                return scheme;
            else
                return null;
        }

        public static KeyBindingScheme GetSchemeByName(string name)
        {
            foreach (KeyBindingScheme scheme in SchemesMap.Values)
                if (scheme.Name == name)
                    return scheme;
            return null;
        }

        public static IEnumerable<KeyBindingScheme> Schemes => SchemesMap.Values;

        public static void LoadBindingsFromExtensionPath(string path)
        {
            // TODO-AELIJ
        }

        public static void LoadBinding(Command cmd)
        {
            current.LoadBinding(cmd);
        }

        public static void StoreDefaultBinding(Command cmd)
        {
            DefaultSchemeBindings.StoreBinding(cmd);
        }

        public static void ResetCurrent(KeyBindingSet kbset)
        {
            current = kbset.Clone();
        }

        public static void ResetCurrent()
        {
            ResetCurrent((string)null);
        }

        public static void ResetCurrent(string schemeId)
        {
            if (schemeId != null)
            {
                KeyBindingScheme scheme = GetScheme(schemeId);
                if (scheme != null)
                {
                    current = scheme.GetKeyBindingSet().Clone();
                    return;
                }
            }

            current.ClearBindings();
        }

        public static void StoreBinding(Command cmd)
        {
            current.StoreBinding(cmd);
        }

        internal static string GetCommandKey(Command cmd)
        {
            if (cmd.Id is Enum)
                return cmd.Id.GetType() + "." + cmd.Id;
            return cmd.Id.ToString();
        }

        public static void LoadCurrentBindings(string defaultSchemaId)
        {
            XmlTextReader reader = null;

            try
            {
                reader = new XmlTextReader(ConfigFileName);
                current.LoadScheme(reader, "current");
            }
            catch
            {
                ResetCurrent(defaultSchemaId);
            }
            finally
            {
                reader?.Close();
            }
        }

        public static void SaveCurrentBindings()
        {
            string dir = Path.GetDirectoryName(ConfigFileName);
            if (!Directory.Exists(dir))
            {
                // ReSharper disable once AssignNullToNotNullAttribute
                Directory.CreateDirectory(dir);
            }
            current.Save(ConfigFileName, "current");
        }
    }

    internal class DefaultScheme : KeyBindingScheme
    {
        private readonly KeyBindingSet bindings;

        public DefaultScheme(KeyBindingSet bindings)
        {
            this.bindings = bindings;
        }

        public string Id => "Default";

        public string Name => BrandingService.ApplicationName;

        public KeyBindingSet GetKeyBindingSet()
        {
            return bindings;
        }
    }
}
