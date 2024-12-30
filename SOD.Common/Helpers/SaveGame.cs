using System;
using System.IO;
using System.Reflection;

namespace SOD.Common.Helpers
{
    public sealed class SaveGame
    {
        internal SaveGame() { }

        /// <summary>
        /// Raised right before a savegame is saved.
        /// <br>Note: StateSaveData is unavailable in the before event.</br>
        /// </summary>
        public event EventHandler<SaveGameArgs> OnBeforeSave;
        /// <summary>
        /// Raised right after a savegame is saved.
        /// </summary>
        public event EventHandler<SaveGameArgs> OnAfterSave;
        /// <summary>
        /// Raised right before a savegame is loaded.
        /// </summary>
        public event EventHandler<SaveGameArgs> OnBeforeLoad;
        /// <summary>
        /// Raised right after a savegame is loaded.
        /// </summary>
        public event EventHandler<SaveGameArgs> OnAfterLoad;
        /// <summary>
        /// Raised right before a savegame file is removed.
        /// </summary>
        public event EventHandler<SaveGameArgs> OnBeforeDelete;
        /// <summary>
        /// Raised right after a savegame file is removed.
        /// </summary>
        public event EventHandler<SaveGameArgs> OnAfterDelete;
        /// <summary>
        /// Raised right before the game begins to create a new game, before the player clicks on the New Game button in the main menu.
        /// </summary>
        public event EventHandler OnBeforeNewGame;
        /// <summary>
        /// Raised right after the game spawns the player in and hides the loading screen. Only applies to a new game, and not a loaded save game.
        /// </summary>
        public event EventHandler OnAfterNewGame;

        // FNV prime and offset basis for 32-bit hash
        private const uint FnvPrime = 16777619;
        private const uint FnvOffsetBasis = 2166136261;

        /// <summary>
        /// Creates a unique hash from a string value that is always the same.
        /// <br>Internally it uses FNV hashing.</br>
        /// <br>Its main use is hashing the savegame filepath, so you can append the hash to your custom files, so you can find them back for a specific savegame.</br>
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public string GetUniqueString(string value)
        {
            return GetFnvHash(value).ToString();
        }

        /// <summary>
        /// Same functionality as <see cref="GetUniqueString(string)"/> but in uint format.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public uint GetUniqueNumber(string value)
        {
            return GetFnvHash(value);
        }

        private static uint GetFnvHash(string value)
        {
            // Hash the value
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            uint hash = FnvOffsetBasis;
            foreach (char c in value)
            {
                hash ^= c;
                hash *= FnvPrime;
            }
            return hash;
        }

        /// <summary>
        /// Returns a path to a folder where you can store all your data, it is combined with the filename provided.
        /// Eg. Lib.SaveGame.GetSavestoreDirectoryPath(Assembly.GetExecutingAssembly(), "moddata.json");
        /// <br>If the folder does not yet exist, it will create it for you.</br>
        /// </summary>
        /// <param name="executingAssembly"></param>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public string GetSavestoreDirectoryPath(Assembly executingAssembly, string fileName)
        {
            var path = GetSavestoreDirectoryPath(executingAssembly);
            if (!string.IsNullOrWhiteSpace(fileName))
                path = Path.Combine(path, fileName);
            return path;
        }

        /// <summary>
        /// Returns a path to a folder where you can store all your data.
        /// Eg. Lib.SaveGame.GetSavestoreDirectoryPath(Assembly.GetExecutingAssembly());
        /// <br>If the folder does not yet exist, it will create it for you.</br>
        /// </summary>
        /// <param name="executingAssembly"></param>
        /// <returns></returns>
        public string GetSavestoreDirectoryPath(Assembly executingAssembly)
        {
            var path = Path.Combine(Path.GetDirectoryName(executingAssembly.Location), "Savestore");
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Returns a path to your plugin folder.
        /// </summary>
        /// <param name="executingAssembly"></param>
        /// <returns></returns>
        public string GetPluginDirectoryPath(Assembly executingAssembly)
        {
            var path = Path.GetDirectoryName(executingAssembly.Location);
            if (!Directory.Exists(path))
                throw new Exception("There is no plugin directory for this assembly location: " + path);
            return path;
        }

        internal bool IsSaving { get; private set; }
        internal void OnSave(string path, bool after)
        {
            if (after)
                OnAfterSave?.Invoke(this, new SaveGameArgs(path));
            else
                OnBeforeSave?.Invoke(this, new SaveGameArgs(path));

            // Handle sync disk data install/upgrade
            if (after)
            {
                IsSaving = false;
                // Reset illegal action timer to max value
                if (Lib.PlayerStatus.IllegalStatusModifierDictionary != null && Lib.PlayerStatus.IllegalStatusModifierDictionary.Count > 0)
                    Player.Instance.illegalActionTimer = float.MaxValue;

                Lib.SyncDisks.CheckForSyncDiskData(false, path);
                Lib.PlayerStatus.Save(path);
                Lib.InputDetection.Save(path);
            }
            else
            {
                IsSaving = true;
                // Set illegal action timer to low number to prevent saves breaking (incase sod.common is ever uninstalled while its at maxvalue)
                if (Lib.PlayerStatus.IllegalStatusModifierDictionary != null && Lib.PlayerStatus.IllegalStatusModifierDictionary.Count > 0)
                    Player.Instance.illegalActionTimer = 1f;
            }
        }

        internal void OnLoad(string path, bool after)
        {
            if (after)
                OnAfterLoad?.Invoke(this, new SaveGameArgs(path));
            else
                OnBeforeLoad?.Invoke(this, new SaveGameArgs(path));

            // Handle sync disk data install/upgrade
            if (after)
            {
                Lib.SyncDisks.CheckForSyncDiskData(true, path);
                Lib.PlayerStatus.Load(path);
                Lib.InputDetection.Load(path);
            }
            else
            {
                Lib.PlayerStatus.ResetStatusTracking();
                Lib.InputDetection.ResetSuppressionTracking();
            }
        }

        internal void OnDelete(string path, bool after)
        {
            if (after)
                OnAfterDelete?.Invoke(this, new SaveGameArgs(path));
            else
                OnBeforeDelete?.Invoke(this, new SaveGameArgs(path));

            // Delete sync disk data for this save
            var hash = Lib.SaveGame.GetUniqueString(path);
            var syncDiskDataPath = Lib.SaveGame.GetSavestoreDirectoryPath(Assembly.GetExecutingAssembly(), $"syncdiskdata_{hash}.json");
            if (File.Exists(syncDiskDataPath))
                File.Delete(syncDiskDataPath);

            var playerStatusDataPath = Lib.SaveGame.GetSavestoreDirectoryPath(Assembly.GetExecutingAssembly(), $"playerstatus_{hash}.json");
            if (File.Exists(playerStatusDataPath))
                File.Delete(playerStatusDataPath);
        }

        internal void OnNewGame(bool after)
        {
            if (after)
                OnAfterNewGame?.Invoke(this, EventArgs.Empty);
            else
            {
                Lib.PlayerStatus.ResetStatusTracking();
                OnBeforeNewGame?.Invoke(this, EventArgs.Empty);
            }

            // Clear installed sync disks
            Lib.SyncDisks.InstalledSyncDisks.Clear();
        }
    }

    public sealed class SaveGameArgs : EventArgs
    {
        public string FilePath { get; }

        internal SaveGameArgs(string filePath)
        {
            FilePath = filePath;
        }
    }
}
