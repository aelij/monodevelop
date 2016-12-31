//
// PropertyService.cs
//
// Author:
//   Mike Krüger <mkrueger@novell.com>
//
// Copyright (C) 2007 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace MonoDevelop.Core
{
    /// <summary>
    /// The Property wrapper wraps a global property service value as an easy to use object.
    /// </summary>
    public abstract class ConfigurationProperty<T>
    {
        public T Value
        {
            get { return OnGetValue(); }
            set { OnSetValue(value); }
        }

        /// <summary>
        /// Set the property to the specified value.
        /// </summary>
        /// <param name='newValue'>
        /// The new value.
        /// </param>
        /// <returns>
        /// true, if the property has changed, false otherwise.
        /// </returns>
        public bool Set(T newValue)
        {
            return OnSetValue(newValue);
        }

        public static implicit operator T(ConfigurationProperty<T> watch)
        {
            return watch.Value;
        }

        protected abstract T OnGetValue();

        protected abstract bool OnSetValue(T o);

        protected void OnChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler Changed;
    }

    internal class CoreConfigurationProperty<T> : ConfigurationProperty<T>
    {
        private T value;
        private readonly string propertyName;

        public CoreConfigurationProperty(string name, T defaultValue, string oldName = null)
        {
            propertyName = name;
            if (PropertyService.HasValue(name))
            {
                value = PropertyService.Get<T>(name);
                return;
            }
            if (!string.IsNullOrEmpty(oldName))
            {
                if (PropertyService.HasValue(oldName))
                {
                    value = PropertyService.Get<T>(oldName);
                    PropertyService.Set(name, value);
                    return;
                }
            }
            value = defaultValue;
        }

        protected override T OnGetValue()
        {
            return value;
        }

        protected override bool OnSetValue(T o)
        {
            if (!Equals(value, o))
            {
                value = o;
                PropertyService.Set(propertyName, o);
                OnChanged();
                return true;
            }
            return false;
        }
    }

    public abstract class ConfigurationProperty
    {
        public static ConfigurationProperty<T> Create<T>(string propertyName, T defaultValue, string oldName = null)
        {
            return new CoreConfigurationProperty<T>(propertyName, defaultValue, oldName);
        }
    }

    public static class PropertyService
    {
        public static ConfigurationProperty<T> Wrap<T>(string property, T defaultValue)
        {
            return new CoreConfigurationProperty<T>(property, defaultValue);
        }

        //force the static class to intialize
        internal static void Initialize()
        {

        }

        private static readonly string FileName = "MonoDevelopProperties.xml";

        public static Properties GlobalInstance { get; private set; }

        public static FilePath EntryAssemblyPath
        {
            get
            {
                if (Assembly.GetEntryAssembly() != null)
                    return Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        /// <summary>
        /// Location of data files that are bundled with MonoDevelop itself.
        /// </summary>
        public static FilePath DataPath
        {
            get
            {
                string result = ConfigurationManager.AppSettings["DataDirectory"];
                if (String.IsNullOrEmpty(result))
                    result = Path.Combine(EntryAssemblyPath, Path.Combine("..", "data"));
                return result;
            }
        }

        static PropertyService()
        {
            Counters.PropertyServiceInitialization.BeginTiming();

            var prefsPath = UserProfile.Current.ConfigDir.Combine(FileName);
            if (!File.Exists(prefsPath))
            {
                string migrateVersion;
                UserProfile migratableProfile;
                if (GetMigratableProfile(out migratableProfile, out migrateVersion))
                {
                    FilePath migratePrefsPath = migratableProfile.ConfigDir.Combine(FileName);
                    try
                    {
                        var parentDir = prefsPath.ParentDirectory;
                        //can't use file service until property service is initialized
                        if (!Directory.Exists(parentDir))
                            Directory.CreateDirectory(parentDir);
                        File.Copy(migratePrefsPath, prefsPath);
                        LoggingService.LogInfo("Migrated core properties from {0}", migratePrefsPath);
                    }
                    catch (IOException ex)
                    {
                        string message = $"Failed to migrate core properties from {migratePrefsPath}";
                        LoggingService.LogError(message, ex);
                    }
                }
                else
                {
                    LoggingService.LogInfo("Did not find previous version from which to migrate data");
                }
            }

            if (!LoadProperties(prefsPath))
            {
                GlobalInstance = new Properties();
                GlobalInstance.Set("MonoDevelop.Core.FirstRun", true);
            }

            GlobalInstance.PropertyChanged += delegate (object sender, PropertyChangedEventArgs args)
            {
                Runtime.RunInMainThread(() =>
                {
                    PropertyChanged?.Invoke(sender, args);
                });
            };

            Counters.PropertyServiceInitialization.EndTiming();
        }

        internal static bool GetMigratableProfile(out UserProfile profile, out string version)
        {
            profile = null;
            version = null;

            //try versioned profiles from most recent to oldest
            //skip the last in the array, it's the current profile
            int userProfileMostRecent = UserProfile.ProfileVersions.Length - 2;
            for (int i = userProfileMostRecent; i >= 1; i--)
            {
                string v = UserProfile.ProfileVersions[i];
                var p = UserProfile.GetProfile(v);
                if (File.Exists(p.ConfigDir.Combine(FileName)))
                {
                    profile = p;
                    version = v;
                    return true;
                }
            }

            return false;
        }

        private static bool LoadProperties(string fileName)
        {
            GlobalInstance = null;
            if (File.Exists(fileName))
            {
                try
                {
                    GlobalInstance = Properties.Load(fileName);
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("Error loading properties from file '{0}':\n{1}", fileName, ex);
                }
            }

            //if it failed and a backup file exists, try that instead
            string backupFile = fileName + ".previous";
            if (GlobalInstance == null && File.Exists(backupFile))
            {
                try
                {
                    GlobalInstance = Properties.Load(backupFile);
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("Error loading properties from backup file '{0}':\n{1}", backupFile, ex);
                }
            }
            return GlobalInstance != null;
        }

        public static void SaveProperties()
        {
            Debug.Assert(GlobalInstance != null);
            var prefsPath = UserProfile.Current.ConfigDir.Combine(FileName);
            Directory.CreateDirectory(prefsPath.ParentDirectory);
            GlobalInstance.Save(prefsPath);
        }

        public static bool HasValue(string property)
        {
            return GlobalInstance.HasValue(property);
        }

        public static T Get<T>(string property, T defaultValue)
        {
            return GlobalInstance.Get(property, defaultValue);
        }

        public static T Get<T>(string property)
        {
            return GlobalInstance.Get<T>(property);
        }

        public static void Set(string key, object val)
        {
            GlobalInstance.Set(key, val);
        }

        public static void AddPropertyHandler(string propertyName, EventHandler<PropertyChangedEventArgs> handler)
        {
            GlobalInstance.AddPropertyHandler(propertyName, handler);
        }

        public static void RemovePropertyHandler(string propertyName, EventHandler<PropertyChangedEventArgs> handler)
        {
            GlobalInstance.RemovePropertyHandler(propertyName, handler);
        }

        public static event EventHandler<PropertyChangedEventArgs> PropertyChanged;
    }
}
