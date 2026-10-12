using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;

namespace LiveBackground
{
    [DataContract]
    internal sealed class Settings
    {
        [DataMember] public int Speed;       // %
        [DataMember] public int Thickness;   // movement strength
        [DataMember] public int Wisps;
        [DataMember] public int Sway;        // treetop bend
        [DataMember] public int Fps;
        [DataMember] public bool Paused;
        [DataMember] public bool PauseWhenCovered;
        /// <summary>Folder containing index.html; relative paths resolve against the app folder.</summary>
        [DataMember] public string WallpaperFolder;

        public Settings() => SetDefaults();

        // The JSON serializer skips constructors, so fields missing from an older file keep these.
        [OnDeserializing]
        private void OnDeserializing(StreamingContext _) => SetDefaults();

        private void SetDefaults()
        {
            Speed = 100;
            Thickness = 85;
            Wisps = 60;
            Sway = 50;
            Fps = 30;
            Paused = false;
            PauseWhenCovered = true;
            WallpaperFolder = @"wallpapers\misty-ridge";
        }

        private static readonly string FilePath = Path.Combine(Log.Folder, "settings.json");
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "LiveBackground";

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var s = File.OpenRead(FilePath))
                        return (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(s);
            }
            catch (Exception e) when (e is IOException || e is SerializationException || e is UnauthorizedAccessException)
            {
                Log.Write("Settings unreadable, using defaults: " + e.Message);
            }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Log.Folder);
                using (var s = File.Create(FilePath))
                    new DataContractJsonSerializer(typeof(Settings)).WriteObject(s, this);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Log.Write("Could not save settings: " + e.Message);
            }
        }

        public string ResolveWallpaperFolder() =>
            Path.IsPathRooted(WallpaperFolder)
                ? WallpaperFolder
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, WallpaperFolder);

        public static bool StartWithWindows
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k?.GetValue(RunName) != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(RunName, "\"" + System.Windows.Forms.Application.ExecutablePath + "\"");
                    else k.DeleteValue(RunName, false);
                }
            }
        }
    }
}
