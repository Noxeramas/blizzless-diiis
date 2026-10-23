using DiIiS_NA.Core.Helpers.IO;
using DiIiS_NA.Core.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;


namespace DiIiS_NA.Core.MPQ
{
    public static class MPQStorage
    {
        //private static readonly Logger Logger = LogManager.CreateLogger();
        private static readonly Logger Logger = LogManager.CreateLogger("DataBaseSystem");
        public static string MPQRoot
        {
            get { return "DataBase/"; }
        }
        private readonly static string MpqRoot = MPQRoot;

        public static List<string> MPQList { get; private set; } = new List<string>();
        public static Data Data { get; private set; }
        public static bool Initialized { get; private set; }

        static MPQStorage()
        {
            Initialized = false;

            Data = OpenInstalledGame() ?? OpenBundledMpq();
            if (Data == null || !Data.Loaded)
                return;

            Logger.Info("Game data source: {0}", Data.Source);
            Data.Init();
            Initialized = true;
        }

        /// <summary>
        /// The installed game's data, so the server matches the player's client. Storage.GameDataPath picks the
        /// Diablo III folder; empty finds the installed game, and "mpq" skips this for the bundled archives.
        /// </summary>
        private static Data OpenInstalledGame()
        {
            var setting = Storage.Config.Instance.GameDataPath;
            if (setting.Equals("mpq", StringComparison.OrdinalIgnoreCase))
                return null;

            var gameDirectory = CascStorage.FindGameDirectory(setting);
            if (gameDirectory == null)
            {
                Logger.Warn("No installed Diablo III found{0}; using the bundled MPQ archives.",
                    string.IsNullOrWhiteSpace(setting) ? "" : $" at {setting}");
                return null;
            }

            try
            {
                Logger.Info("Opening game data from {0}...", gameDirectory);
                return new Data(CascStorage.Open(gameDirectory));
            }
            catch (Exception e) when (e is IOException or DllNotFoundException or BadImageFormatException)
            {
                Logger.ErrorException(e, "Could not read the installed game's data; using the bundled MPQ archives.");
                return null;
            }
        }

        private static Data OpenBundledMpq()
        {
            if (!Directory.Exists(MpqRoot))
            {
                Logger.Error("MPQ archives not found: {0}.", MpqRoot);
                return null;
            }

            Logger.Info("Initializing MPQ archives...");
            MPQList = FileHelpers.GetFilesByExtensionRecursive(MpqRoot, ".mpq");
            return new Data();
        }

        public static string GetMPQFile(string name)
        {
            return MPQList.FirstOrDefault(file => file.Contains(name));
        }
    }
}
